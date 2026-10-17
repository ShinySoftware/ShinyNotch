using AppKit;

namespace ShinyNotch.Services;

/// <summary>A single countdown timer, with pause and a chime when it ends.</summary>
public sealed class CountdownService
{
    readonly IDispatcherTimer timer;
    DateTime endsAt;
    TimeSpan pausedRemaining;

    public CountdownService(IDispatcher dispatcher)
    {
        this.timer = dispatcher.CreateTimer();
        this.timer.Interval = TimeSpan.FromMilliseconds(250);
        this.timer.Tick += (_, _) => this.OnTick();
    }

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsActive => this.IsRunning || this.IsPaused;
    public TimeSpan Duration { get; private set; }

    public TimeSpan Remaining => this.IsRunning
        ? (this.endsAt > DateTime.Now ? this.endsAt - DateTime.Now : TimeSpan.Zero)
        : this.pausedRemaining;

    /// <summary>Started, paused, resumed, cancelled or finished.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The countdown reached zero.</summary>
    public event EventHandler? Finished;

    public void Start(TimeSpan duration)
    {
        this.Duration = duration;
        this.endsAt = DateTime.Now + duration;
        this.IsRunning = true;
        this.IsPaused = false;
        this.timer.Start();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Add(TimeSpan amount)
    {
        if (this.IsRunning)
            this.endsAt += amount;
        else if (this.IsPaused)
            this.pausedRemaining += amount;
        else
        {
            this.Start(amount);
            return;
        }
        this.Duration += amount;
    }

    public void TogglePause()
    {
        if (this.IsRunning)
        {
            this.pausedRemaining = this.Remaining;
            this.IsRunning = false;
            this.IsPaused = true;
            this.timer.Stop();
        }
        else if (this.IsPaused)
        {
            this.endsAt = DateTime.Now + this.pausedRemaining;
            this.IsRunning = true;
            this.IsPaused = false;
            this.timer.Start();
        }
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        this.IsRunning = false;
        this.IsPaused = false;
        this.timer.Stop();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnTick()
    {
        if (!this.IsRunning || this.Remaining > TimeSpan.Zero)
            return;

        this.Cancel();
        NSSound.FromName("Glass")?.Play();
        this.Finished?.Invoke(this, EventArgs.Empty);
    }

    public static string Format(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
}
