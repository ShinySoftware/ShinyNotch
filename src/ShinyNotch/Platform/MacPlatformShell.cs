using AppKit;
using Foundation;
using ShinyNotch.Platform;

namespace ShinyNotch.MacOS;

sealed class MacPlatformShell : IPlatformShell
{
    NSObject? screensObserver;
    EventHandler? screensChanged;

    public void Hide(Window window)
    {
        if (window.Handler?.PlatformView is NSWindow native)
            native.OrderOut(null);
    }

    public void Present(Window window)
    {
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        if (window.Handler?.PlatformView is NSWindow native)
        {
            native.Center();
            native.MakeKeyAndOrderFront(null);
        }
    }

    public void Quit() => NSApplication.SharedApplication.Terminate(null);

    public event EventHandler? ScreensChanged
    {
        add
        {
            this.screensChanged += value;
            this.screensObserver ??= NSNotificationCenter.DefaultCenter.AddObserver(
                NSApplication.DidChangeScreenParametersNotification,
                _ => this.screensChanged?.Invoke(this, EventArgs.Empty)
            );
        }
        remove => this.screensChanged -= value;
    }

    public void PlayAlert() => NSSound.FromName("Glass")?.Play();

    public bool OpenCalendarPrivacySettings()
        => NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_Calendars"));

    public string LoginItemsLocation => "System Settings ▸ General ▸ Login Items";
}
