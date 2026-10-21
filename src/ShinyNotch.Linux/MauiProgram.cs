using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Shiny;
using ShinyNotch.Linux;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent.Gtk;
#endif

namespace ShinyNotch;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppLinuxGtk4<App>()
            .AddLinuxGtk4Essentials()
            .UseShinyNotch();

        // Before anything else from Shiny.Audio: PulseAudio/PipeWire capture and playback for voice notes.
        builder.Services.AddLinuxAudio();

        // Everything marked [Singleton] in this head: the Platform implementations, and a calendar store
        // that says there's no calendar (Shiny.Calendar has none for plain net10.0).
        builder.Services.AddPlatformServices();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.AddMauiDevFlowAgent();
#endif
        return builder.Build();
    }
}
