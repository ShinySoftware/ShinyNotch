using ShinyNotch.Native;

namespace ShinyNotch.Services;

public enum HudKind
{
    Volume,
    Brightness
}

/// <summary>
/// Volume and brightness. Polled rather than observed: brightness has no change notification at all, and
/// one 4Hz timer covering both is simpler than a CoreAudio listener plus a poll. The calls are cheap.
/// </summary>
public sealed class SystemControlsService : IDisposable
{
    readonly IDispatcherTimer timer;
    float? lastVolume;
    bool lastMuted;
    float? lastBrightness;
    DateTime suppressUntil;

    public SystemControlsService(IDispatcher dispatcher)
    {
        this.lastVolume = CoreAudio.GetVolume();
        this.lastMuted = CoreAudio.IsMuted();
        this.lastBrightness = Brightness.Get();

        this.timer = dispatcher.CreateTimer();
        this.timer.Interval = TimeSpan.FromMilliseconds(250);
        this.timer.Tick += (_, _) => this.Poll();
    }

    /// <summary>
    /// Volume or brightness changed from outside the app (keys, Control Centre). Changes the app makes
    /// itself are not reported, so dragging a slider in the notch doesn't also pop the HUD.
    /// </summary>
    public event EventHandler<HudKind>? ChangedExternally;

    public float? Volume => this.lastVolume;
    public bool IsMuted => this.lastMuted;
    public float? DisplayBrightness => this.lastBrightness;

    public void Start() => this.timer.Start();

    public void SetVolume(float value)
    {
        this.suppressUntil = DateTime.UtcNow.AddMilliseconds(600);
        CoreAudio.SetVolume(value);
        this.lastVolume = CoreAudio.GetVolume();
        this.lastMuted = CoreAudio.IsMuted();
    }

    public void ToggleMute()
    {
        this.suppressUntil = DateTime.UtcNow.AddMilliseconds(600);
        CoreAudio.SetMuted(!CoreAudio.IsMuted());
        this.lastMuted = CoreAudio.IsMuted();
    }

    public void SetBrightness(float value)
    {
        this.suppressUntil = DateTime.UtcNow.AddMilliseconds(600);
        Brightness.Set(value);
        this.lastBrightness = Brightness.Get();
    }

    void Poll()
    {
        var volume = CoreAudio.GetVolume();
        var muted = CoreAudio.IsMuted();
        var brightness = Brightness.Get();
        var external = DateTime.UtcNow >= this.suppressUntil;

        if (Changed(volume, this.lastVolume) || muted != this.lastMuted)
        {
            this.lastVolume = volume;
            this.lastMuted = muted;
            if (external)
                this.ChangedExternally?.Invoke(this, HudKind.Volume);
        }

        if (Changed(brightness, this.lastBrightness))
        {
            this.lastBrightness = brightness;
            // Auto-brightness drifts by tiny steps all day; only a real change is a HUD.
            if (external)
                this.ChangedExternally?.Invoke(this, HudKind.Brightness);
        }
    }

    static bool Changed(float? a, float? b)
        => a.HasValue != b.HasValue || (a.HasValue && Math.Abs(a.Value - b!.Value) > 0.02f);

    public void Dispose() => this.timer.Stop();
}
