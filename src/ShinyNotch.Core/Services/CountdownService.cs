using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Services;

/// <summary>A single countdown timer, with pause and a chime when it ends.</summary>
/// <remarks>
/// Nothing ticks here: one timer is set for the moment the countdown ends, and moved when it is paused,
/// extended or cancelled. The notch draws the seconds on its own clock while it shows them.
/// </remarks>
[Singleton]
public sealed class CountdownService : IDisposable
{
    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    readonly IPlatformShell shell;
    readonly ITimer finish;
    // UTC, so a DST switch or a time zone change doesn't move a countdown that's running.
    DateTimeOffset endsAt;
    TimeSpan pausedRemaining;

    public CountdownService(IDispatcher dispatcher, TimeProvider time, IPlatformShell shell)
    {
        this.dispatcher = dispatcher;
        this.time = time;
        this.shell = shell;
        // The callback comes on a thread-pool thread.
        this.finish = time.CreateTimer(_ => this.dispatcher.Dispatch(this.OnFinish), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsActive => this.IsRunning || this.IsPaused;
    public TimeSpan Duration { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            if (!this.IsRunning)
                return this.pausedRemaining;

            var left = this.endsAt - this.time.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>How much of the countdown is left, 1 to 0.</summary>
    public double Fraction => this.Duration > TimeSpan.Zero ? this.Remaining / this.Duration : 0;

    /// <summary>Started, paused, resumed, extended, cancelled or finished.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The countdown reached zero.</summary>
    public event EventHandler? Finished;

    public void Start(TimeSpan duration)
    {
        this.Duration = duration;
        this.endsAt = this.time.GetUtcNow() + duration;
        this.IsRunning = true;
        this.IsPaused = false;
        this.Arm();
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
        this.Arm();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePause()
    {
        if (this.IsRunning)
        {
            this.pausedRemaining = this.Remaining;
            this.IsRunning = false;
            this.IsPaused = true;
        }
        else if (this.IsPaused)
        {
            this.endsAt = this.time.GetUtcNow() + this.pausedRemaining;
            this.IsRunning = true;
            this.IsPaused = false;
        }
        this.Arm();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        this.IsRunning = false;
        this.IsPaused = false;
        this.Arm();
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sets the one timer for the end, or stops it when nothing is running.</summary>
    void Arm() => this.finish.Change(this.IsRunning ? this.Remaining : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    void OnFinish()
    {
        if (!this.IsRunning)
            return;

        // Woken early (the timer and the wall clock can disagree slightly, or the Mac slept): set it again.
        if (this.Remaining > TimeSpan.Zero)
        {
            this.Arm();
            return;
        }

        this.Cancel();
        this.shell.PlayAlert();
        this.Finished?.Invoke(this, EventArgs.Empty);
    }

    public static string Format(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    public void Dispose() => this.finish.Dispose();
}
