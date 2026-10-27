using Shiny;
using Shiny.Calendar;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Camera;
using ShinyNotch.Platform;
using ShinyNotch.Services;

namespace ShinyNotch;

public static class ShinyNotchHosting
{
    /// <summary>
    /// The notch, its window and everything shared behind it. The head registers the Platform
    /// implementations, an <see cref="ICalendarStore"/> (Shiny.Calendar has none for plain net10.0) and
    /// Shiny.Audio's services, and may register an <see cref="ICaptureTab"/> and call <see cref="UseNotchMirror"/>.
    /// </summary>
    public static MauiAppBuilder UseShinyNotch(this MauiAppBuilder builder)
    {
        SettingsJson.Register();
        builder
            .UseShinyControls()
            .UseFileDrop(o => o.AllowDirectories = true)
            // Tasks are typed into the quick entry popup, as a window of its own over other apps.
            .UseDesktopQuickEntry()
            .UseDesktopNotch(o =>
            {
                // Room for the widest and tallest teleprompter Settings allows; every other tab is 640 wide.
                o.MaxWidth = AppSettings.MaxTeleprompterWidth + 40;
                o.MaxHeight = 760;
                o.MorphDuration = 260;
                // Without a hardware notch, hang from the top of the usable area (under the menu bar or a
                // top taskbar) instead of floating an island below it, out of the way of what's on screen.
                // A notched screen already gets this shape.
                o.TopMargin = 0;
                o.Configure = v => v.Shape = NotchShape.Notch;
            });

        // Launch at login, from Shiny.Core: a login item / startup entry for the running app.
        builder.Services.AddAppStartupInstaller(o =>
        {
            o.Identifier = "net.shiny.notch";
            o.DisplayName = "Shiny Notch";
        });

        builder.Services.AddShinyStores();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IDispatcherProvider>().GetForCurrentThread()!);
        // Everything marked [Singleton] / [Transient] in Core.
        builder.Services.AddShinyNotchServices();
        return builder;
    }

    /// <summary>
    /// The Mirror tab, on a head with a camera handler (Shiny's CameraView has none on GTK). Without this
    /// call there's no tab and no setting for it.
    /// </summary>
    public static MauiAppBuilder UseNotchMirror(this MauiAppBuilder builder)
    {
        builder.UseShinyCamera();
        builder.Services.AddSingleton<MirrorSupport>();
        return builder;
    }
}

/// <summary>Registered by <see cref="ShinyNotchHosting.UseNotchMirror"/>: this head can show the camera.</summary>
public sealed class MirrorSupport;
