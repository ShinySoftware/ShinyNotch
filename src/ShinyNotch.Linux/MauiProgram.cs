using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Hosting;
using Microsoft.Maui.Platforms.Linux.Gtk4.Hosting;
using Shiny.Calendar;
using ShinyNotch.Linux;
using ShinyNotch.Platform;
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

        // Shiny.Calendar has no store for plain net10.0; this one says so, and the calendar steps aside.
        builder.Services.AddSingleton<ICalendarStore, UnsupportedCalendarStore>();
        builder.Services.AddSingleton<IPlatformShell, LinuxPlatformShell>();
        builder.Services.AddSingleton<IPlatformUi, LinuxPlatformUi>();
        builder.Services.AddSingleton<IAudioControl, LinuxAudioControl>();
        builder.Services.AddSingleton<IBrightnessControl, LinuxBrightnessControl>();
        builder.Services.AddSingleton<ISystemCounters, LinuxSystemCounters>();
        builder.Services.AddSingleton<IFileActions, LinuxFileActions>();
        builder.Services.AddSingleton<ILocationProvider, LinuxLocationProvider>();
        builder.Services.AddSingleton<INowPlayingService, LinuxNowPlayingService>();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.AddMauiDevFlowAgent();
#endif
        return builder.Build();
    }
}
