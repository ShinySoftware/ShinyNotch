using Shiny;
using Shiny.Calendar;
using ShinyNotch.Platform;
using ShinyNotch.Services;

namespace ShinyNotch;

public static class ShinyNotchHosting
{
    /// <summary>
    /// The notch, its window and everything shared behind it. The head registers the Platform
    /// implementations and an <see cref="ICalendarStore"/> (Shiny.Calendar has none for plain net10.0),
    /// and may register an <see cref="ICaptureTab"/>.
    /// </summary>
    public static MauiAppBuilder UseShinyNotch(this MauiAppBuilder builder)
    {
        builder
            .UseShinyControls()
            .UseFileDrop(o => o.AllowDirectories = true)
            .UseDesktopNotch(o =>
            {
                o.MaxWidth = 560;
                o.MaxHeight = 640;
                o.MorphDuration = 260;
            });

        // Launch at login, from Shiny.Core: a login item / startup entry for the running app.
        builder.Services.AddAppStartupInstaller(o =>
        {
            o.Identifier = "net.shiny.notch";
            o.DisplayName = "Shiny Notch";
        });

        builder.Services.AddShinyStores();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IDispatcherProvider>().GetForCurrentThread()!);
        builder.Services.AddSingleton<SystemControlsService>();
        builder.Services.AddSingleton<SystemStatsService>();
        builder.Services.AddSingleton<CalendarService>();
        builder.Services.AddSingleton<CountdownService>();
        builder.Services.AddSingleton<FileTrayService>();
        builder.Services.AddSingleton<TeleprompterService>();
        builder.Services.AddSingleton<WeatherService>();
        builder.Services.AddSingleton<NotchController>();
        builder.Services.AddTransient<SettingsPage>();
        return builder;
    }
}
