using System.ComponentModel;
using System.Diagnostics;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Services;

public enum ProcessSort
{
    Name,
    Cpu,
    Memory,
    Disk,
    Network
}

/// <param name="CpuPercent">Of one core, as Activity Monitor shows it, so a busy process on many cores goes past 100.</param>
/// <param name="DiskPerSec">Read and written together.</param>
/// <param name="NetPerSec">Received and sent together.</param>
public sealed record ProcessUsage(int Pid, string Name, double CpuPercent, ulong Memory, double DiskPerSec, double NetPerSec);

/// <summary>
/// Turns each process's running totals into rates, for the Processes tab. Like <see cref="SystemStatsService"/>
/// it does nothing on its own: the tab samples once a second while it is on screen, and the reading happens
/// off the main thread, since listing every process (and asking nettop, on macOS) takes a few milliseconds.
/// </summary>
[Singleton]
public sealed class ProcessListService
{
    /// <summary>Opening the tab again within this long carries on, rather than starting over.</summary>
    static readonly TimeSpan ResumeWindow = TimeSpan.FromSeconds(3);

    readonly TimeProvider time;
    readonly IProcessCounters counters;
    Dictionary<(int Pid, ulong Started), ProcessReading> last = new();
    IReadOnlyDictionary<int, (ulong In, ulong Out)> lastNet = new Dictionary<int, (ulong, ulong)>();
    DateTimeOffset lastSample;
    bool sampling;

    public ProcessListService(TimeProvider time, IProcessCounters counters)
    {
        this.time = time;
        this.counters = counters;
    }

    /// <summary>What the list is ordered by. Kept here, so it survives the tab being rebuilt.</summary>
    public ProcessSort Sort { get; set; } = ProcessSort.Cpu;

    /// <summary>Flipped from the column's natural order: A to Z for names, biggest first for the rest.</summary>
    public bool Reversed { get; set; }

    /// <summary>Whether the list runs smallest (or A) first, for the heading's arrow.</summary>
    public bool Ascending => (this.Sort == ProcessSort.Name) != this.Reversed;

    public bool HasNetwork => this.counters.HasNetwork;

    /// <summary>The last reading, or null until there are two samples to take rates from.</summary>
    public IReadOnlyList<ProcessUsage>? Current { get; private set; }

    /// <summary>
    /// Takes a reading, and says whether there are rates to show. The first one in a while is only a
    /// baseline. A call while one is still reading is dropped.
    /// </summary>
    public async Task<bool> SampleAsync()
    {
        if (!this.sampling)
        {
            this.sampling = true;
            try
            {
                await Task.Run(this.Read);
            }
            finally
            {
                this.sampling = false;
            }
        }
        return this.Current != null;
    }

    void Read()
    {
        var now = this.time.GetUtcNow();
        var processes = this.counters.Processes();
        var net = this.counters.NetworkBytes();
        var resumed = this.last.Count > 0 && now - this.lastSample < ResumeWindow;
        var seconds = Math.Max(0.2, (now - this.lastSample).TotalSeconds);

        var next = new Dictionary<(int, ulong), ProcessReading>(processes.Count);
        var usage = new List<ProcessUsage>(processes.Count);
        foreach (var p in processes)
        {
            next[(p.Pid, p.Started)] = p;
            if (!resumed)
                continue;

            // A process that started since the last sample has no rates yet, only its memory.
            if (!this.last.TryGetValue((p.Pid, p.Started), out var before))
            {
                usage.Add(new ProcessUsage(p.Pid, p.Name, 0, p.Memory, 0, 0));
                continue;
            }

            var cpu = p.CpuTime > before.CpuTime ? 100 * (p.CpuTime - before.CpuTime).TotalSeconds / seconds : 0;
            var disk = Rate(p.DiskRead, before.DiskRead, seconds) + Rate(p.DiskWritten, before.DiskWritten, seconds);
            double network = 0;
            if (net.TryGetValue(p.Pid, out var bytes) && this.lastNet.TryGetValue(p.Pid, out var bytesBefore))
                network = Rate(bytes.In, bytesBefore.In, seconds) + Rate(bytes.Out, bytesBefore.Out, seconds);

            usage.Add(new ProcessUsage(p.Pid, p.Name, cpu, p.Memory, disk, network));
        }

        this.last = next;
        this.lastNet = net;
        this.lastSample = now;
        this.Current = resumed ? usage : null;
    }

    // A socket closing takes its bytes out of the process's total; that sample is dropped, not negative.
    static double Rate(ulong now, ulong before, double seconds)
        => now >= before ? (now - before) / seconds : 0;

    /// <summary>The first <paramref name="count"/> by <see cref="Sort"/>, ties going to the bigger footprint.</summary>
    public IReadOnlyList<ProcessUsage> Top(int count)
    {
        if (this.Current is not { } current)
            return [];

        IOrderedEnumerable<ProcessUsage> sorted;
        if (this.Sort == ProcessSort.Name)
        {
            sorted = this.Ascending
                ? current.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                : current.OrderByDescending(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
        }
        else
        {
            Func<ProcessUsage, double> key = this.Sort switch
            {
                ProcessSort.Memory => p => p.Memory,
                ProcessSort.Disk => p => p.DiskPerSec,
                ProcessSort.Network => p => p.NetPerSec,
                _ => p => p.CpuPercent
            };
            sorted = this.Ascending ? current.OrderBy(key) : current.OrderByDescending(key);
        }
        return sorted.ThenByDescending(p => p.Memory).Take(count).ToList();
    }

    /// <summary>Kills the process outright, as Force Quit does. False if it had already gone, or isn't ours to kill.</summary>
    public bool ForceQuit(int pid)
    {
        if (pid == Environment.ProcessId)
            return false;

        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }

        // Gone from the list now, rather than on the next sample.
        if (this.Current is { } current)
            this.Current = current.Where(p => p.Pid != pid).ToList();
        return true;
    }
}
