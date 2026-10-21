using Shiny;
using ShinyNotch.Platform;
using ShinyNotch.Windows;

namespace ShinyNotch;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseShinyNotch();

        builder.Services.AddCalendarStore();
        builder.Services.AddSingleton<IPlatformShell, WindowsPlatformShell>();
        builder.Services.AddSingleton<IPlatformUi, WindowsPlatformUi>();
        builder.Services.AddSingleton<IAudioControl, WindowsAudioControl>();
        builder.Services.AddSingleton<IBrightnessControl, WindowsBrightnessControl>();
        builder.Services.AddSingleton<ISystemCounters, WindowsSystemCounters>();
        builder.Services.AddSingleton<IFileActions, WindowsFileActions>();
        builder.Services.AddSingleton<ILocationProvider, WindowsLocationProvider>();
        builder.Services.AddSingleton<INowPlayingService, WindowsNowPlayingService>();
        // No ICaptureTab: macshot is macOS only, so Windows has no Capture tab.

        return builder.Build();
    }
}
