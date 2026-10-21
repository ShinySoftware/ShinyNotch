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
