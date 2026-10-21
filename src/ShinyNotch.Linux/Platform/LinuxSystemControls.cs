using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Shiny;
using ShinyNotch.Platform;
using Tmds.DBus.Protocol;

namespace ShinyNotch.Linux;

/// <summary>
/// The default sink through <c>pactl</c>, which PulseAudio and PipeWire (pipewire-pulse) both answer,
/// and <c>pactl subscribe</c> for word of changes.
/// </summary>
[Singleton]
sealed partial class LinuxAudioControl : IAudioControl
{
    const string Sink = "@DEFAULT_SINK@";

    /// <summary>The first channel's percentage; PulseAudio allows over 100%, which reads as full.</summary>
    public float? Volume
        => Command.Read("pactl", "get-sink-volume", Sink) is { } output && Percent().Match(output) is { Success: true } m
            ? Math.Clamp(Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 100f, 0f, 1f)
            : null;

    public bool IsMuted => Command.Read("pactl", "get-sink-mute", Sink)?.Contains("yes", StringComparison.Ordinal) == true;

    public void SetVolume(float value)
    {
        var percent = (int)Math.Round(Math.Clamp(value, 0f, 1f) * 100);
        Command.Read("pactl", "set-sink-volume", Sink, $"{percent}%");
        if (percent > 0 && this.IsMuted)
            this.SetMuted(false);
    }

    public void SetMuted(bool muted) => Command.Read("pactl", "set-sink-mute", Sink, muted ? "1" : "0");

    /// <summary>
    /// A long-running <c>pactl subscribe</c>, read on a thread of its own. Only sink and server events
    /// count: the server's is the default sink changing (headphones in), and sink-input events - every
    /// app's own stream - would fire constantly. Restarted if the sound server restarts.
    /// </summary>
    public void Observe(Action onChange)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var process = Process.Start(Command.Info("pactl", true, "subscribe"));
                    if (process == null)
                        return;

                    while (process.StandardOutput.ReadLine() is { } line)
                    {
                        if (line.Contains(" on sink #", StringComparison.Ordinal) || line.Contains(" on server", StringComparison.Ordinal))
                            onChange();
                    }
                }
                catch
                {
                    // No pactl: nothing to observe, and nothing to show anyway.
                    return;
                }
                Thread.Sleep(5000);
            }
        })
        {
            IsBackground = true,
            Name = "pactl subscribe"
        };
        thread.Start();
    }

    [GeneratedRegex(@"(\d+)%")]
    private static partial Regex Percent();
}

/// <summary>
/// The panel's backlight from <c>/sys/class/backlight</c>, set through logind - which lets the session's
/// own user change it without root or a udev rule. Not observable, so Core polls.
/// </summary>
[Singleton]
sealed class LinuxBrightnessControl : IBrightnessControl
{
    const string Root = "/sys/class/backlight";

    readonly string? device = FindDevice();

    public float? Brightness
    {
        get
        {
            if (this.device == null)
                return null;

            var value = ReadNumber(Path.Combine(Root, this.device, "brightness"));
            var max = ReadNumber(Path.Combine(Root, this.device, "max_brightness"));
            return value is { } v && max is > 0 and var m ? Math.Clamp((float)v / m, 0f, 1f) : null;
        }
    }

    public void SetBrightness(float value)
    {
        if (this.device == null || ReadNumber(Path.Combine(Root, this.device, "max_brightness")) is not { } max || max <= 0)
            return;

        // Never quite to 0: on most panels that is the backlight off, not dim.
        var raw = (uint)Math.Max(1, Math.Round(Math.Clamp(value, 0f, 1f) * max));
        try
        {
            var connection = DBusConnection.System;
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.login1",
                path: "/org/freedesktop/login1/session/auto",
                @interface: "org.freedesktop.login1.Session",
                member: "SetBrightness",
                signature: "ssu"
            );
            writer.WriteString("backlight");
            writer.WriteString(this.device);
            writer.WriteUInt32(raw);

            // Waited on (briefly), so the reading that follows a set is the new value. The connection
            // completes on its own threads, so this can't deadlock the main loop.
            connection.CallMethodAsync(writer.CreateMessage()).Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // No logind (or not a local session): the slider just doesn't move the panel.
        }
    }

    public bool Observe(Action onChange) => false;

    /// <summary>The kernel's own preference: firmware, then platform, then a raw driver interface.</summary>
    static string? FindDevice()
    {
        try
        {
            return Directory.Exists(Root)
                ? Directory.GetDirectories(Root)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .OrderBy(d => ReadText(Path.Combine(Root, d, "type")) switch
                    {
                        "firmware" => 0,
                        "platform" => 1,
                        _ => 2
                    })
                    .FirstOrDefault()
                : null;
        }
        catch
        {
            return null;
        }
    }

    static long? ReadNumber(string path)
        => Int64.TryParse(ReadText(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>The kernel's own counters under <c>/proc</c> and <c>/sys</c>.</summary>
[Singleton]
sealed class LinuxSystemCounters : ISystemCounters
{
    /// <summary>The <c>cpu</c> line of /proc/stat, in jiffies: busy is everything but idle and iowait.</summary>
    public (ulong Busy, ulong Total) CpuTicks()
    {
        var line = ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (line == null)
            return (0, 0);

        var fields = Numbers(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1));
        // user nice system idle iowait irq softirq steal - guest time is already inside user and nice.
        var total = fields.Take(8).Aggregate(0UL, (a, b) => a + b);
        var idle = fields.ElementAtOrDefault(3) + fields.ElementAtOrDefault(4);
        return (total - idle, total);
    }

    /// <summary>In use is what isn't available: MemAvailable counts reclaimable cache as free, as the system monitor does.</summary>
    public (ulong Used, ulong Total) Memory()
    {
        ulong total = 0, available = 0;
        foreach (var line in ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                total = Kilobytes(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                available = Kilobytes(line);
        }
        return (total > available ? total - available : 0, total);
    }

    /// <summary>AMD (and some Intel Xe) drivers publish a busy percentage; NVIDIA's proprietary driver doesn't.</summary>
    public double? GpuUtilisation()
    {
        try
        {
            foreach (var card in Directory.GetDirectories("/sys/class/drm", "card*"))
            {
                var path = Path.Combine(card, "device", "gpu_busy_percent");
                if (File.Exists(path) && Double.TryParse(File.ReadAllText(path).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var busy))
                    return busy;
            }
        }
        catch
        {
            // No DRM sysfs: no reading.
        }
        return null;
    }

    /// <summary>
    /// Sectors read and written on whole disks, from /proc/diskstats. Partitions would count twice, and
    /// loop, RAM, compressed-swap and device-mapper devices are views onto disks already counted.
    /// </summary>
    public (ulong Read, ulong Written) DiskBytes()
    {
        ulong read = 0, written = 0;
        foreach (var line in ReadLines("/proc/diskstats"))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10 || !IsWholeDisk(fields[2]))
                continue;

            // Sectors are always 512 bytes here, whatever the device's own sector size.
            read += Number(fields[5]) * 512;
            written += Number(fields[9]) * 512;
        }
        return (read, written);
    }

    /// <summary>Every interface but loopback, from /proc/net/dev.</summary>
    public (ulong In, ulong Out) NetworkBytes()
    {
        ulong received = 0, sent = 0;
        foreach (var line in ReadLines("/proc/net/dev").Skip(2))
        {
            var colon = line.IndexOf(':');
            if (colon < 0 || line[..colon].Trim() == "lo")
                continue;

            var fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 9)
                continue;
            received += Number(fields[0]);
            sent += Number(fields[8]);
        }
        return (received, sent);
    }

    static bool IsWholeDisk(string name)
        => Directory.Exists(Path.Combine("/sys/block", name)) &&
           !name.StartsWith("loop", StringComparison.Ordinal) &&
           !name.StartsWith("ram", StringComparison.Ordinal) &&
           !name.StartsWith("zram", StringComparison.Ordinal) &&
           !name.StartsWith("dm-", StringComparison.Ordinal) &&
           !name.StartsWith("sr", StringComparison.Ordinal);

    static ulong Kilobytes(string line)
        => Number(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "0") * 1024;

    static List<ulong> Numbers(IEnumerable<string> fields) => fields.Select(Number).ToList();

    static ulong Number(string text) => UInt64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    static IEnumerable<string> ReadLines(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch
        {
            return [];
        }
    }
}
