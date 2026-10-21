namespace ShinyNotch.Platform;

/// <summary>
/// The Capture tab: shortcuts into a screenshot tool. Optional - a head registers one only where it has
/// a tool to drive (macshot, on macOS), and without one there is no tab and no setting for it.
/// </summary>
public interface ICaptureTab
{
    /// <summary>The tool's name, for the tab's tooltip and the Settings switch.</summary>
    string Name { get; }

    /// <summary>The tool is installed, so the tab has something to drive.</summary>
    bool IsAvailable { get; }

    /// <summary>Something the tab shows changed (a new capture, a recording starting); rebuild it.</summary>
    event EventHandler? Changed;

    /// <summary>The tab was opened.</summary>
    void Opened();

    /// <summary>Once a second while the tab is showing.</summary>
    void Tick();

    /// <param name="foldAway">Folds the panel back to the peek - before a capture, so the notch isn't in the shot.</param>
    View Build(Action foldAway);
}
