namespace ShinyNotch.Services;

/// <summary>
/// The teleprompter's state: whether it is running and how far it has scrolled. The notch reads the
/// offset on every frame and scrolls its text to it.
/// </summary>
public sealed class TeleprompterService
{
    readonly AppSettings settings;
    readonly IDispatcherTimer timer;
    DateTime lastTick;

    public TeleprompterService(AppSettings settings, IDispatcher dispatcher)
    {
        this.settings = settings;
        this.timer = dispatcher.CreateTimer();
        this.timer.Interval = TimeSpan.FromMilliseconds(33);
        this.timer.Tick += (_, _) => this.OnTick();
    }

    public bool IsRunning { get; private set; }

    /// <summary>Points scrolled from the top of the script.</summary>
    public double Offset { get; private set; }

    public string Script => this.settings.TeleprompterScript;
    public double FontSize => this.settings.TeleprompterFontSize;
    public double Speed => this.settings.TeleprompterSpeed;

    /// <summary>Raised ~30 times a second while running.</summary>
    public event EventHandler? Scrolled;

    /// <summary>Started, paused or restarted.</summary>
    public event EventHandler? StateChanged;

    public void Toggle()
    {
        this.IsRunning = !this.IsRunning;
        if (this.IsRunning)
        {
            this.lastTick = DateTime.UtcNow;
            this.timer.Start();
        }
        else
        {
            this.timer.Stop();
        }
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Restart()
    {
        this.Offset = 0;
        this.Scrolled?.Invoke(this, EventArgs.Empty);
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Nudge(double points)
    {
        this.Offset = Math.Max(0, this.Offset + points);
        this.Scrolled?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeSpeed(double delta) => this.settings.TeleprompterSpeed = this.Speed + delta;

    public void ChangeFontSize(double delta)
    {
        this.settings.TeleprompterFontSize = this.FontSize + delta;
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnTick()
    {
        var now = DateTime.UtcNow;
        this.Offset += this.Speed * (now - this.lastTick).TotalSeconds;
        this.lastTick = now;
        this.Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The script ran off the end; stop where it is.</summary>
    public void ReachedEnd()
    {
        if (!this.IsRunning)
            return;

        this.IsRunning = false;
        this.timer.Stop();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
