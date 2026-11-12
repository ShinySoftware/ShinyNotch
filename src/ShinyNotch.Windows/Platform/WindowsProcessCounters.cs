using System.Diagnostics;
using System.Runtime.InteropServices;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Windows;

/// <summary>
/// The processes in this sign-in session, each opened for limited query (no elevation needed) for its
/// times, working set and I/O counters. Windows' I/O counters take in every read and write the process
/// makes, files on network shares and devices included, as Task Manager's own I/O columns do. There are
/// no per-process network counters without ETW, so there is no network column.
/// </summary>
[Singleton]
sealed partial class WindowsProcessCounters : IProcessCounters
{
    const uint ProcessQueryLimitedInformation = 0x1000;

    static readonly IReadOnlyDictionary<int, (ulong, ulong)> NoNetwork = new Dictionary<int, (ulong, ulong)>();
    readonly int session = Process.GetCurrentProcess().SessionId;

    public bool HasNetwork => false;

    public IReadOnlyDictionary<int, (ulong In, ulong Out)> NetworkBytes() => NoNetwork;

    public IReadOnlyList<ProcessReading> Processes()
    {
        var list = new List<ProcessReading>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == 0 || process.SessionId != this.session)
                    continue;

                var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)process.Id);
                if (handle == IntPtr.Zero)
                    continue;

                try
                {
                    if (!GetProcessTimes(handle, out var created, out _, out var kernel, out var user))
                        continue;

                    GetProcessIoCounters(handle, out var io);
                    var memory = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
                    GetProcessMemoryInfo(handle, ref memory, memory.Size);

                    list.Add(new ProcessReading(
                        process.Id,
                        created,
                        process.ProcessName,
                        TimeSpan.FromTicks((long)(kernel + user)),
                        memory.WorkingSetSize,
                        io.ReadTransferCount,
                        io.WriteTransferCount
                    ));
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    // FILETIMEs, read as the 100ns counts they are.
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(IntPtr process, out ulong creation, out ulong exit, out ulong kernel, out ulong user);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);
}
