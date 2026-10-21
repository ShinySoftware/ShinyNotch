namespace ShinyNotch.Platform;

/// <summary>
/// Where the app meets the desktop it runs on: its windows, quitting, the system's own settings, and
/// the displays. One per head.
/// </summary>
public interface IPlatformShell
{
    /// <summary>
    /// Takes a window off screen without closing it. The app starts in the notch, so the first window
    /// MAUI insists on (Settings) is hidden as soon as it exists.
    /// </summary>
    void Hide(Window window);

    /// <summary>Brings a window to the front, centred, and the app forward with it.</summary>
    void Present(Window window);

    void Quit();

    /// <summary>Displays were plugged in, removed or rescaled. May be raised in a burst.</summary>
    event EventHandler? ScreensChanged;

    /// <summary>A short sound for a countdown ending.</summary>
    void PlayAlert();

    /// <summary>Opens the system's calendar privacy settings, or false when it has none to open.</summary>
    bool OpenCalendarPrivacySettings();

    /// <summary>Where the user manages login items, for Settings to point at ("System Settings ▸ General ▸ Login Items").</summary>
    string LoginItemsLocation { get; }
}
