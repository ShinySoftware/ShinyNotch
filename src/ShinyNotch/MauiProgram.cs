using Foundation;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Shiny;
using ShinyNotch.MacOS;
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
            .UseShinyNotch()
            .UseNotchMirror();

        builder.Services.AddCalendarStore();
        builder.Services.AddAudioServices();
        // Everything marked [Singleton] in this head: the Platform implementations and macshot.
        builder.Services.AddPlatformServices();

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
        // This runs before anything else of Core's has been touched, so its JSON types aren't in yet.
        Services.SettingsJson.Register();
        var defaults = NSUserDefaults.StandardUserDefaults;
        foreach (var key in new[] { nameof(Services.AppSettings.TrayFiles), nameof(Services.AppSettings.WeatherPlaces) })
        {
            if (defaults[key] is NSArray array)
                Stores.Default.Set(key, Enumerable.Range(0, (int)array.Count).Select(i => array.GetItem<NSObject>((nuint)i)?.ToString() ?? "").ToArray());
        }
    }
}
