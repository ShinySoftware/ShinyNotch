using ShinyNotch.Native;

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

/// <summary>Samples the counters once a second while something is watching, and turns them into rates.</summary>
public sealed class SystemStatsService
{
    (ulong Busy, ulong Total) lastCpu;
    (ulong Read, ulong Written) lastDisk;
    (ulong In, ulong Out) lastNet;
    DateTime lastSample;

    public SystemStatsService() => this.Prime();

    /// <summary>Recent CPU readings, newest last, for the sparkline.</summary>
    public Queue<double> CpuHistory { get; } = new();

    public SystemStats Current { get; private set; } = new(0, null, 0, 0, 0, 0, 0, 0);

    void Prime()
    {
        this.lastCpu = SystemCounters.CpuTicks();
        this.lastDisk = SystemCounters.DiskBytes();
        this.lastNet = SystemCounters.NetworkBytes();
        this.lastSample = DateTime.UtcNow;
    }

    public SystemStats Sample()
    {
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0.2, (now - this.lastSample).TotalSeconds);

        var cpu = SystemCounters.CpuTicks();
        var busy = cpu.Busy - this.lastCpu.Busy;
        var total = cpu.Total - this.lastCpu.Total;
        var cpuPercent = total == 0 ? 0 : 100d * busy / total;

        var disk = SystemCounters.DiskBytes();
        var net = SystemCounters.NetworkBytes();
        var (used, memTotal) = SystemCounters.Memory();

        this.Current = new SystemStats(
            cpuPercent,
            SystemCounters.GpuUtilisation(),
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
}
