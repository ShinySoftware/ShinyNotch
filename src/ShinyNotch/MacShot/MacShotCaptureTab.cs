using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.MacOS;

/// <summary>The Capture tab, driving macshot.</summary>
[Singleton]
sealed class MacShotCaptureTab : ICaptureTab
{
    /// <summary>Time for the panel to fold back to the peek, so a capture doesn't have the notch in it.</summary>
    static readonly TimeSpan HandOffDelay = TimeSpan.FromMilliseconds(250);

    readonly MacShotService macshot;
    readonly IDispatcher dispatcher;

    public MacShotCaptureTab(MacShotService macshot, IDispatcher dispatcher)
    {
        this.macshot = macshot;
        this.dispatcher = dispatcher;
    }

    public string Name => "macshot";

    public bool IsAvailable => this.macshot.IsInstalled;

    public event EventHandler? Changed
    {
        add => this.macshot.Changed += value;
        remove => this.macshot.Changed -= value;
    }

    public void Opened()
    {
        this.macshot.Start();
        this.macshot.CheckRecording();
    }

    // Watching for a recording to start or end, so Stop comes and goes with it.
    public void Tick() => this.macshot.CheckRecording();

    /// <summary>
    /// Every action folds the panel away first, pinned or not: a capture would have it in the shot, and
    /// macshot's own windows (editor, history, settings) open where it was.
    /// </summary>
    public View Build(Action foldAway) => new CaptureTab(this.macshot, action =>
    {
        foldAway();
        this.dispatcher.DispatchDelayed(HandOffDelay, () => MacShotService.Run(action));
    });
}
