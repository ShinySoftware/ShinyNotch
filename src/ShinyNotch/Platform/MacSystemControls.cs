using System.Diagnostics;
using System.Globalization;
using Shiny;
using ShinyNotch.Native;
using ShinyNotch.Platform;

namespace ShinyNotch.MacOS;

/// <summary>CoreAudio listeners on the default output device.</summary>
[Singleton]
sealed class MacAudioControl : IAudioControl
{
    public float? Volume
    {
        get
        {
            // A new output device (headphones in, AirPods connected) moves the listeners with it. Read
            // after every change, on the main thread, so this is where the move happens.
            CoreAudio.FollowDefaultDevice();
            return CoreAudio.GetVolume();
        }
    }

    public bool IsMuted => CoreAudio.IsMuted();

    public void SetVolume(float value) => CoreAudio.SetVolume(value);

    public void SetMuted(bool muted) => CoreAudio.SetMuted(muted);

    public void Observe(Action onChange) => CoreAudio.Observe(onChange);
}

/// <summary>The built-in display, through DisplayServices.</summary>
[Singleton]
sealed class MacBrightnessControl : IBrightnessControl
{
    public float? Brightness => Native.Brightness.Get();

    public void SetBrightness(float value) => Native.Brightness.Set(value);

    public bool Observe(Action onChange) => Native.Brightness.Observe(onChange);
}

/// <summary>Mach host statistics, sysctl, IOKit and getifaddrs.</summary>
[Singleton]
sealed class MacSystemCounters : ISystemCounters
{
    public (ulong Busy, ulong Total) CpuTicks() => SystemCounters.CpuTicks();

    public (ulong Used, ulong Total) Memory() => SystemCounters.Memory();

    public double? GpuUtilisation() => SystemCounters.GpuUtilisation();

    public (ulong Read, ulong Written) DiskBytes() => SystemCounters.DiskBytes();

    public (ulong In, ulong Out) NetworkBytes() => SystemCounters.NetworkBytes();
}

/// <summary>
/// libproc for CPU, memory and disk, and nettop for network: macOS has no public per-process network
/// counters, and nettop reads the kernel's own. It takes about 15ms, off the main thread.
/// </summary>
[Singleton]
sealed class MacProcessCounters : IProcessCounters
{
    public IReadOnlyList<ProcessReading> Processes() => ProcessTable.Read();

    public bool HasNetwork => true;

    /// <summary>One line per process with sockets, "name.pid,bytes_in,bytes_out,", under a header.</summary>
    public IReadOnlyDictionary<int, (ulong In, ulong Out)> NetworkBytes()
    {
        var result = new Dictionary<int, (ulong, ulong)>();
        try
        {
            using var nettop = Process.Start(new ProcessStartInfo("/usr/bin/nettop", "-P -L 1 -n -x -J bytes_in,bytes_out")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (nettop == null)
                return result;

            var output = nettop.StandardOutput.ReadToEnd();
            nettop.WaitForExit(2000);

            foreach (var line in output.Split('\n'))
            {
                var fields = line.Split(',');
                if (fields.Length < 3)
                    continue;

                // The name can have dots of its own; the pid is after the last one.
                var dot = fields[0].LastIndexOf('.');
                if (dot < 0 ||
                    !Int32.TryParse(fields[0].AsSpan(dot + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ||
                    !UInt64.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var received) ||
                    !UInt64.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sent))
                    continue;

                result[pid] = (received, sent);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No nettop: no network column readings, the rest carries on.
        }
        return result;
    }
}
