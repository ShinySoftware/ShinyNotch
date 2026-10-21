using Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Shiny;
using ShinyNotch.MacOS;
using ShinyNotch.Platform;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace ShinyNotch;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MigrateListSettings();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiAppMacOS<App>()
            .AddMacOSEssentials()
            .UseShinyNotch();

        builder.Services.AddCalendarStore();
        builder.Services.AddSingleton<IPlatformShell, MacPlatformShell>();
        builder.Services.AddSingleton<IPlatformUi, MacPlatformUi>();
        builder.Services.AddSingleton<IAudioControl, MacAudioControl>();
        builder.Services.AddSingleton<IBrightnessControl, MacBrightnessControl>();
        builder.Services.AddSingleton<ISystemCounters, MacSystemCounters>();
        builder.Services.AddSingleton<IFileActions, MacFileActions>();
        builder.Services.AddSingleton<ILocationProvider, MacLocationProvider>();
        builder.Services.AddSingleton<INowPlayingService, MacNowPlayingService>();
        builder.Services.AddSingleton<MacShotService>();
        builder.Services.AddSingleton<ICaptureTab, MacShotCaptureTab>();

#if DEBUG
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.AddMauiDevFlowAgent();
#endif
        return builder.Build();
    }

    /// <summary>
    /// Before settings moved to Shiny's store, the tray and the weather places were saved as NSArrays.
    /// The store keeps lists as JSON strings, so an NSArray still there is rewritten once in that form;
    /// everything else (strings, numbers, switches) was already stored the way the store reads it.
    /// </summary>
    static void MigrateListSettings()
    {
        var defaults = NSUserDefaults.StandardUserDefaults;
        foreach (var key in new[] { nameof(Services.AppSettings.TrayFiles), nameof(Services.AppSettings.WeatherPlaces) })
        {
            if (defaults[key] is NSArray array)
                Stores.Default.Set(key, Enumerable.Range(0, (int)array.Count).Select(i => array.GetItem<NSObject>((nuint)i)?.ToString() ?? "").ToArray());
        }
    }
}
