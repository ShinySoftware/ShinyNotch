using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Shiny;
using ShinyNotch.Services;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace ShinyNotch;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppMacOS<App>()
            .AddMacOSEssentials()
            .UseShinyControls()
            .UseTrayIcon()
            .UseFileDrop(o => o.AllowDirectories = true)
            .UseDesktopNotch(o =>
            {
                o.MaxWidth = 560;
                o.MaxHeight = 640;
                o.MorphDuration = 260;
            });

        // Launch at login, from Shiny.Core: an SMAppService login item for the running app bundle.
        builder.Services.AddAppStartupInstaller(o =>
        {
            o.Identifier = "net.shiny.notch";
            o.DisplayName = "Shiny Notch";
        });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IDispatcherProvider>().GetForCurrentThread()!);
        builder.Services.AddSingleton<NowPlayingService>();
        builder.Services.AddSingleton<SystemControlsService>();
        builder.Services.AddSingleton<SystemStatsService>();
        builder.Services.AddCalendarStore();
        builder.Services.AddSingleton<CalendarService>();
        builder.Services.AddSingleton<CountdownService>();
        builder.Services.AddSingleton<FileTrayService>();
        builder.Services.AddSingleton<TeleprompterService>();
        builder.Services.AddSingleton<WeatherService>();
        builder.Services.AddSingleton<NotchController>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.AddMauiDevFlowAgent();
#endif
        return builder.Build();
    }
}
