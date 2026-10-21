using AppKit;
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace ShinyNotch;

[Register("Program")]
public class Program : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // The app lives in the notch: closing Settings must not quit it.
    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => false;

    // There's no Dock or menu-bar icon, so launching the app again while it's running (Finder,
    // Spotlight, Launchpad) is the way back to Settings if the notch can't be reached.
    public override bool ApplicationShouldHandleReopen(NSApplication sender, bool hasVisibleWindows)
    {
        (Microsoft.Maui.Controls.Application.Current as App)?.ShowSettings();
        return false;
    }

    public static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new Program();
        NSApplication.Main(args);
    }
}
