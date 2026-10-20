using ShinyNotch.Native;

namespace ShinyNotch.Services;

public enum HudKind
{
    Volume,
    Brightness
}

/// <summary>
/// Volume and brightness. Observed, not polled: CoreAudio listeners report the output device, its volume
/// and its mute, and DisplayServices reports brightness. Nothing runs until something changes. If the
/// brightness notification is ever unavailable, brightness alone falls back to a once-a-second poll.
/// </summary>
public sealed class SystemControlsService : IDisposable
{
    /// <summary>
    /// Brightness has to move this far within <see cref="BrightnessWindow"/> to count as a key press. Just
    /// under the ⌥⇧ fine step (1/64): a slow auto-brightness drift never gets there, a key press always does.
    /// </summary>
    const float BrightnessStep = 0.014f;
    static readonly TimeSpan BrightnessWindow = TimeSpan.FromSeconds(1);

    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    readonly Queue<(DateTimeOffset At, float? Value)> recentBrightness = new();
    IDispatcherTimer? brightnessPoll;
    float? lastVolume;
    bool lastMuted;
    float? lastBrightness;
    DateTimeOffset suppressUntil;
    bool started;

    public SystemControlsService(IDispatcher dispatcher, TimeProvider time)
    {
        this.dispatcher = dispatcher;
        this.time = time;
        this.lastVolume = CoreAudio.GetVolume();
        this.lastMuted = CoreAudio.IsMuted();
        this.lastBrightness = Brightness.Get();
    }

    /// <summary>
    /// Volume or brightness changed from outside the app (keys, Control Centre). Changes the app makes
    /// itself are not reported, so dragging a slider in the notch doesn't also pop the HUD.
    /// </summary>
    public event EventHandler<HudKind>? ChangedExternally;

    public float? Volume => this.lastVolume;
    public bool IsMuted => this.lastMuted;
    public float? DisplayBrightness => this.lastBrightness;

    public void Start()
    {
        if (this.started)
            return;
        this.started = true;

        CoreAudio.Observe(() => this.dispatcher.Dispatch(this.OnAudioChanged));
        if (!Brightness.Observe(() => this.dispatcher.Dispatch(this.OnBrightnessChanged)))
        {
            this.brightnessPoll = this.dispatcher.CreateTimer();
            this.brightnessPoll.Interval = TimeSpan.FromSeconds(1);
            this.brightnessPoll.Tick += (_, _) => this.OnBrightnessChanged();
            this.brightnessPoll.Start();
        }
    }

    public void SetVolume(float value)
    {
        this.SuppressExternal();
        CoreAudio.SetVolume(value);
        this.lastVolume = CoreAudio.GetVolume();
        this.lastMuted = CoreAudio.IsMuted();
    }

    public void ToggleMute()
    {
        this.SuppressExternal();
        CoreAudio.SetMuted(!CoreAudio.IsMuted());
        this.lastMuted = CoreAudio.IsMuted();
    }

    public void SetBrightness(float value)
    {
        this.SuppressExternal();
        Brightness.Set(value);
        this.lastBrightness = Brightness.Get();
    }

    void SuppressExternal() => this.suppressUntil = this.time.GetUtcNow().AddMilliseconds(600);

    bool IsExternal => this.time.GetUtcNow() >= this.suppressUntil;

    void OnAudioChanged()
    {
        // A new output device (headphones in, AirPods connected) moves the listeners with it.
        CoreAudio.FollowDefaultDevice();

        var volume = CoreAudio.GetVolume();
        var muted = CoreAudio.IsMuted();

        // Nothing but a person moves the volume, so any change counts — including the ⌥⇧ fine steps (1/64).
        var volumeChanged = Changed(volume, this.lastVolume, 0.001f) || muted != this.lastMuted;
        this.lastVolume = volume;
        this.lastMuted = muted;
        if (volumeChanged && this.IsExternal)
            this.ChangedExternally?.Invoke(this, HudKind.Volume);
    }

    void OnBrightnessChanged()
    {
        var now = this.time.GetUtcNow();
        var brightness = Brightness.Get();

        // Auto-brightness drifts by tiny steps all day. Kept current on every change, so the drift never
        // adds up to a HUD; only a step within the last second is one. After a quiet spell the window is
        // empty, and the step is measured from the reading before this one.
        var previous = this.lastBrightness;
        this.lastBrightness = brightness;
        while (this.recentBrightness.Count > 0 && now - this.recentBrightness.Peek().At > BrightnessWindow)
            this.recentBrightness.Dequeue();
        var before = this.recentBrightness.Count > 0 ? this.recentBrightness.Peek().Value : previous;
        this.recentBrightness.Enqueue((now, brightness));

        if (!Changed(brightness, before, BrightnessStep))
            return;

        // Start the window over, so one step is one HUD and a held key is a run of them.
        this.recentBrightness.Clear();
        this.recentBrightness.Enqueue((now, brightness));
        if (this.IsExternal)
            this.ChangedExternally?.Invoke(this, HudKind.Brightness);
    }

    static bool Changed(float? a, float? b, float threshold)
        => a.HasValue != b.HasValue || (a.HasValue && Math.Abs(a.Value - b!.Value) >= threshold);

    public void Dispose() => this.brightnessPoll?.Stop();
}
