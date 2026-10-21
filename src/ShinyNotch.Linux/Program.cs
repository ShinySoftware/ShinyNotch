using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Gtk;
#endif

// The GTK head and everything in it only ever runs on Linux.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("linux")]

namespace ShinyNotch;

public class Program : GtkMauiApplication
{
    // Also the desktop entry's id, so the startup entry and the running app agree.
    protected override string ApplicationId => "net.shiny.notch";

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

#if DEBUG
    // GTK needs the agent started once the MAUI Application object exists (OnStarted fires after
    // activation); the builder registration alone doesn't spin up the HTTP listener.
    protected override void OnStarted()
    {
        base.OnStarted();
        (this.Application as Application)?.StartDevFlowAgent();
    }
#endif

    public static void Main(string[] args) => new Program().Run(args);
}
