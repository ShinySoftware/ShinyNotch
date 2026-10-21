namespace ShinyNotch.Platform;

/// <summary>The default output device's volume and mute.</summary>
public interface IAudioControl
{
    /// <summary>0..1, or null when the output device has no volume control (HDMI, some interfaces).</summary>
    float? Volume { get; }

    bool IsMuted { get; }

    /// <summary>Sets the volume, unmuting when it is above zero.</summary>
    void SetVolume(float value);

    void SetMuted(bool muted);

    /// <summary>
    /// Calls <paramref name="onChange"/> when the default output device, its volume or its mute changes.
    /// Called once. It may arrive on any thread; the caller hops to the main thread before reading.
    /// </summary>
    void Observe(Action onChange);
}

/// <summary>The built-in (or main) display's brightness.</summary>
public interface IBrightnessControl
{
    /// <summary>0..1, or null when the display's brightness can't be read (most external monitors).</summary>
    float? Brightness { get; }

    void SetBrightness(float value);

    /// <summary>
    /// Calls <paramref name="onChange"/> whenever the brightness changes, on any thread. False when the
    /// system can't say, in which case the caller polls instead.
    /// </summary>
    bool Observe(Action onChange);
}

/// <summary>The raw, ever-growing counters behind the Stats tab. The service turns them into rates.</summary>
public interface ISystemCounters
{
    /// <summary>CPU time spent busy, and in all, across every core, in any consistent unit.</summary>
    (ulong Busy, ulong Total) CpuTicks();

    /// <summary>Memory in use and installed, in bytes.</summary>
    (ulong Used, ulong Total) Memory();

    /// <summary>GPU busy, 0..100, or null when it can't be read.</summary>
    double? GpuUtilisation();

    /// <summary>Bytes read from and written to disk since boot.</summary>
    (ulong Read, ulong Written) DiskBytes();

    /// <summary>Bytes received and sent over every interface but loopback since boot.</summary>
    (ulong In, ulong Out) NetworkBytes();
}
