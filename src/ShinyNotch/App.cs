using AppKit;
using Foundation;
using Shiny.Maui.Controls.Desktop.TrayIcon;

namespace ShinyNotch;

/// <summary>
/// A menu-bar agent. MAUI insists on a first window, so that window is Settings — created hidden, and
/// shown from the tray icon. Everything visible lives in the notch window the notch service opens.
/// </summary>
public sealed class App : Application
{
    readonly IServiceProvider services;
    readonly NotchController notch;
    ITrayIcon? tray;
    Window? settingsWindow;
    bool started;

    public App(IServiceProvider services, NotchController notch)
    {
        this.services = services;
        this.notch = notch;
        this.notch.OpenSettings = this.ShowSettings;
        this.UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = this.CreateSettingsWindow();

        // Hidden as soon as it exists: the app starts in the notch, not in a window.
        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView is NSWindow native && !this.started)
                native.OrderOut(null);
        };
        window.Created += async (_, _) =>
        {
            if (this.started)
                return;
            this.started = true;
            this.SetupTray();
            await this.notch.StartAsync();
#if DEBUG
            // --open <Tab>: start with the panel pinned open on a tab, for driving the UI without a mouse.
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "--open");
            if (i >= 0 && i + 1 < args.Length && Enum.TryParse<Views.NotchTab>(args[i + 1], true, out var tab))
                this.notch.OpenPinned(tab);
#endif
        };
        return window;
    }

    Window CreateSettingsWindow()
    {
        var window = new Window(this.services.GetRequiredService<SettingsPage>())
        {
            Title = "Shiny Notch Settings",
            Width = 560,
            Height = 720
        };
        window.Destroying += (_, _) =>
        {
            if (this.settingsWindow == window)
                this.settingsWindow = null;
        };
        this.settingsWindow = window;
        return window;
    }

    public void ShowSettings()
    {
        // Closing Settings destroys its window; the next open makes a fresh one.
        if (this.settingsWindow == null)
            this.OpenWindow(this.CreateSettingsWindow());

        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        if (this.settingsWindow?.Handler?.PlatformView is NSWindow native)
        {
            native.Center();
            native.MakeKeyAndOrderFront(null);
        }
    }

    void SetupTray()
    {
        this.tray = this.services.GetRequiredService<ITrayIconFactory>().Create();
        this.tray.Tooltip = "Shiny Notch";
        this.tray.IsTemplateImage = true;
        if (TrayGlyphPng() is { } png)
            this.tray.SetIcon(() => new MemoryStream(png));
        else
            this.tray.Title = "◖◗";

        this.tray.IsVisible = true;
        _ = this.RefreshTrayMenuAsync();
    }

    /// <summary>
    /// The tray menu, with "Open at Login" ticked from the login item's real state. Rebuilt after every
    /// toggle, because macOS may answer "needs approval" rather than doing what was asked.
    /// </summary>
    async Task RefreshTrayMenuAsync()
    {
        if (this.tray == null)
            return;

        var startup = this.services.GetRequiredService<Shiny.IAppStartupInstaller>();
        var menu = new TrayMenuBuilder().Item("Settings…", this.ShowSettings);

        if (startup.IsSupported)
        {
            var state = await startup.GetState();
            var on = state is Shiny.AppStartupState.Enabled or Shiny.AppStartupState.RequiresApproval;
            // An async void handler: an exception escaping it would take the app down.
            menu.Check("Open at Login", on, async enable =>
            {
                try
                {
                    var result = enable ? await startup.Register() : await startup.Unregister();
                    if (result is Shiny.AppStartupState.RequiresApproval or Shiny.AppStartupState.DisabledByUser)
                        await startup.OpenSettings();
                }
                catch
                {
                    // The menu is rebuilt from the real state below, so a failed toggle just doesn't tick.
                }
                await this.RefreshTrayMenuAsync();
            });
        }

        this.tray.SetMenu(menu
            .Separator()
            .Item("Quit Shiny Notch", () => NSApplication.SharedApplication.Terminate(null))
            .Build());
    }

    /// <summary>An SF Symbol rendered to a PNG, so the app ships no image assets.</summary>
    static byte[]? TrayGlyphPng()
    {
        var symbol = NSImage.GetSystemSymbol("rectangle.topthird.inset.filled", "Shiny Notch");
        if (symbol == null)
            return null;

        const int size = 36;
        var rect = new CoreGraphics.CGRect(0, 0, size, size);
        var cg = symbol.AsCGImage(ref rect, null, null);
        if (cg == null)
            return null;

        using var rep = new NSBitmapImageRep(cg);
        return rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)?.ToArray();
    }
}
