using AppKit;
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace ShinyNotch;

[Register("Program")]
public class Program : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // A menu-bar agent lives in the notch and the tray: closing Settings must not quit it.
    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => false;

    public static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new Program();
        NSApplication.Main(args);
    }
}
