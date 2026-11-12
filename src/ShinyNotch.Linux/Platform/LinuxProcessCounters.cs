using System.Globalization;
using System.Runtime.InteropServices;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Linux;

/// <summary>
/// Each process's <c>/proc/[pid]/stat</c> and <c>io</c>. The io file is readable only for the user's own
/// processes, which is also the set it can kill, so that's what's listed. Linux keeps no per-process
/// network counters (that takes eBPF), so there is no network column.
/// </summary>
[Singleton]
sealed partial class LinuxProcessCounters : IProcessCounters
{
    const int ScClockTicks = 2;

    static readonly long ClockTicks = Math.Max(1, sysconf(ScClockTicks));
    static readonly IReadOnlyDictionary<int, (ulong, ulong)> NoNetwork = new Dictionary<int, (ulong, ulong)>();

    public bool HasNetwork => false;

    public IReadOnlyDictionary<int, (ulong In, ulong Out)> NetworkBytes() => NoNetwork;

    public IReadOnlyList<ProcessReading> Processes()
    {
        var list = new List<ProcessReading>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!Int32.TryParse(Path.GetFileName(dir), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
                continue;

            try
            {
                ulong read = 0, written = 0;
                foreach (var line in File.ReadLines(Path.Combine(dir, "io")))
                {
                    if (line.StartsWith("read_bytes:", StringComparison.Ordinal))
                        read = Number(line[11..]);
                    else if (line.StartsWith("write_bytes:", StringComparison.Ordinal))
                        written = Number(line[12..]);
                }

                // "pid (comm) state ppid ..." - the name can hold spaces and brackets, so split after the last ')'.
                var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                var open = stat.IndexOf('(');
                var close = stat.LastIndexOf(')');
                if (open < 0 || close < open)
                    continue;

                // From the state (field 3): utime is field 14, stime 15, starttime 22, rss (in pages) 24.
                var fields = stat[(close + 2)..].Split(' ');
                if (fields.Length < 22)
                    continue;

                var ticks = Number(fields[11]) + Number(fields[12]);
                list.Add(new ProcessReading(
                    pid,
                    Number(fields[19]),
                    stat[(open + 1)..close],
                    TimeSpan.FromSeconds((double)ticks / ClockTicks),
                    Number(fields[21]) * (ulong)Environment.SystemPageSize,
                    read,
                    written
                ));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Someone else's process, or one that ended while it was being read.
            }
        }
        return list;
    }

    static ulong Number(string text) => UInt64.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    [LibraryImport("libc")]
    private static partial long sysconf(int name);
}
