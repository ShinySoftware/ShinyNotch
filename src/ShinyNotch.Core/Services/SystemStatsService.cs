using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Services;

public sealed record SystemStats(
    double CpuPercent,
    double? GpuPercent,
    ulong MemoryUsed,
    ulong MemoryTotal,
    double DiskReadPerSec,
    double DiskWritePerSec,
    double NetInPerSec,
    double NetOutPerSec
)
{
    public double MemoryPercent => this.MemoryTotal == 0 ? 0 : 100d * this.MemoryUsed / this.MemoryTotal;
}

/// <summary>
/// Turns the system counters into rates. It does nothing on its own: no timer, no sampling at startup. The
/// Stats tab calls <see cref="Begin"/> when it opens and <see cref="Sample"/> once a second while it is on
/// screen, and the closed notch does the same while it shows a reading. The two are never on screen
/// together, and nothing else reads the counters, so with neither showing the work stops entirely.
/// </summary>
[Singleton]
public sealed class SystemStatsService
{
    /// <summary>Showing the readings again within this long carries on, rather than starting them over.</summary>
    static readonly TimeSpan ResumeWindow = TimeSpan.FromSeconds(3);

    readonly TimeProvider time;
    readonly ISystemCounters counters;
    (ulong Busy, ulong Total) lastCpu;
    (ulong Read, ulong Written) lastDisk;
    (ulong In, ulong Out) lastNet;
    DateTimeOffset lastSample;

    public SystemStatsService(TimeProvider time, ISystemCounters counters)
    {
        this.time = time;
        this.counters = counters;
    }

    /// <summary>Recent CPU readings, newest last, for the sparkline.</summary>
    public Queue<double> CpuHistory { get; } = new();

    /// <summary>The last reading, or null before the first one since <see cref="Begin"/>.</summary>
    public SystemStats? Current { get; private set; }

    /// <summary>
    /// The Stats tab opened, or a closed notch with readings in it was built. Takes a baseline for the rates —
    /// unless readings were showing a moment ago (a rebuild, the closed notch opening onto the Stats tab), in
    /// which case the readings and the sparkline carry on.
    /// </summary>
    public void Begin()
    {
        if (this.Current != null && this.time.GetUtcNow() - this.lastSample < ResumeWindow)
            return;

        this.Current = null;
        this.CpuHistory.Clear();
        this.Prime();
    }

    void Prime()
    {
        this.lastCpu = this.counters.CpuTicks();
        this.lastDisk = this.counters.DiskBytes();
        this.lastNet = this.counters.NetworkBytes();
        this.lastSample = this.time.GetUtcNow();
    }

    public SystemStats Sample()
    {
        var now = this.time.GetUtcNow();
        var seconds = Math.Max(0.2, (now - this.lastSample).TotalSeconds);

        var cpu = this.counters.CpuTicks();
        var busy = cpu.Busy - this.lastCpu.Busy;
        var total = cpu.Total - this.lastCpu.Total;
        var cpuPercent = total == 0 ? 0 : 100d * busy / total;

        var disk = this.counters.DiskBytes();
        var net = this.counters.NetworkBytes();
        var (used, memTotal) = this.counters.Memory();

        this.Current = new SystemStats(
            cpuPercent,
            this.counters.GpuUtilisation(),
            used,
            memTotal,
            Rate(disk.Read, this.lastDisk.Read, seconds),
            Rate(disk.Written, this.lastDisk.Written, seconds),
            Rate(net.In, this.lastNet.In, seconds),
            Rate(net.Out, this.lastNet.Out, seconds)
        );

        this.lastCpu = cpu;
        this.lastDisk = disk;
        this.lastNet = net;
        this.lastSample = now;

        this.CpuHistory.Enqueue(cpuPercent);
        while (this.CpuHistory.Count > 24)
            this.CpuHistory.Dequeue();

        return this.Current;
    }

    // The network counters are 32-bit and wrap; a sample that went backwards is dropped, not negative.
    static double Rate(ulong now, ulong before, double seconds)
        => now >= before ? (now - before) / seconds : 0;

    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }
        return i == 0 ? $"{bytes:0} {units[i]}" : $"{bytes:0.0} {units[i]}";
    }

    /// <summary>
    /// A rate in four characters at most, for the closed notch: "0K" to "999K", then "1.0M", "12M" and so on.
    /// Never wider than <see cref="CompactWidest"/>, so a reading ticking in place keeps its width.
    /// </summary>
    public static string Compact(double bytes)
    {
        string[] units = ["K", "M", "G", "T"];
        var value = bytes / 1024;
        var i = 0;
        while (value >= 999.5 && i < units.Length - 1)
        {
            value /= 1024;
            i++;
        }
        return i > 0 && value < 9.95 ? $"{value:0.0}{units[i]}" : $"{value:0}{units[i]}";
    }

    /// <summary>The widest <see cref="Compact"/> can be, to measure by.</summary>
    public const string CompactWidest = "888M";
}
