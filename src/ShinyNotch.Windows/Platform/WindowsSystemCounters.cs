using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Windows;

/// <summary>
/// GetSystemTimes and GlobalMemoryStatusEx, the disks' own running byte totals (IOCTL_DISK_PERFORMANCE),
/// the network interfaces' statistics, and the "GPU Engine" counters for the GPU.
/// </summary>
[Singleton]
sealed partial class WindowsSystemCounters : ISystemCounters
{
    const uint IoctlDiskPerformance = 0x00070020;
    const int MaxDisks = 16;

    readonly Dictionary<string, PerformanceCounter> gpuEngines = new();
    bool gpuUnavailable;

    public (ulong Busy, ulong Total) CpuTicks()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return (0, 0);

        // Kernel time includes idle time.
        var total = kernel + user;
        return (total - idle, total);
    }

    public (ulong Used, ulong Total) Memory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (status.TotalPhys - status.AvailPhys, status.TotalPhys) : (0, 0);
    }

    /// <summary>
    /// The busiest kind of engine across every process's 3D engines, as Task Manager shows it. The
    /// instances come and go with processes, so they're re-listed each reading; a counter's first reading
    /// is always 0, which only matters for a process that just started.
    /// </summary>
    public double? GpuUtilisation()
    {
        if (this.gpuUnavailable)
            return null;

        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var names = category.GetInstanceNames().Where(n => n.EndsWith("engtype_3D", StringComparison.Ordinal)).ToHashSet();

            foreach (var gone in this.gpuEngines.Keys.Where(k => !names.Contains(k)).ToList())
            {
                this.gpuEngines[gone].Dispose();
                this.gpuEngines.Remove(gone);
            }

            double total = 0;
            foreach (var name in names)
            {
                if (!this.gpuEngines.TryGetValue(name, out var counter))
                {
                    counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true);
                    this.gpuEngines[name] = counter;
                }
                try
                {
                    total += counter.NextValue();
                }
                catch (InvalidOperationException)
                {
                    // The process ended between listing and reading.
                }
            }
            return Math.Min(100, total);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            // No GPU counters (an old driver, a VM): stop asking.
            this.gpuUnavailable = true;
            return null;
        }
    }

    /// <summary>Every physical disk's running totals. Opening a disk with no access rights needs no elevation.</summary>
    public (ulong Read, ulong Written) DiskBytes()
    {
        ulong read = 0, written = 0;
        for (var i = 0; i < MaxDisks; i++)
        {
            using var disk = CreateFile($@"\\.\PhysicalDrive{i}", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (disk.IsInvalid)
                continue;

            if (DeviceIoControl(disk, IoctlDiskPerformance, IntPtr.Zero, 0, out var performance, (uint)Marshal.SizeOf<DiskPerformance>(), out _, IntPtr.Zero))
            {
                read += (ulong)performance.BytesRead;
                written += (ulong)performance.BytesWritten;
            }
        }
        return (read, written);
    }

    public (ulong In, ulong Out) NetworkBytes()
    {
        ulong received = 0, sent = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.OperationalStatus != OperationalStatus.Up)
                continue;

            try
            {
                var stats = nic.GetIPStatistics();
                received += (ulong)stats.BytesReceived;
                sent += (ulong)stats.BytesSent;
            }
            catch (NetworkInformationException) { }
        }
        return (received, sent);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DiskPerformance
    {
        public long BytesRead;
        public long BytesWritten;
        public long ReadTime;
        public long WriteTime;
        public long IdleTime;
        public uint ReadCount;
        public uint WriteCount;
        public uint QueueDepth;
        public uint SplitCount;
        public long QueryTime;
        public uint StorageDeviceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string StorageManagerName;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, uint inputSize, out DiskPerformance output, uint outputSize, out uint returned, IntPtr overlapped);
}
