using ShinyNotch.Platform;

namespace ShinyNotch;

/// <summary>
/// An app with no Dock, taskbar or menu-bar presence of its own. MAUI insists on a first window, so that window is
/// Settings: created hidden, and shown from the notch's gear or by launching the app again. Everything
/// visible lives in the notch window the notch service opens.
/// </summary>
public sealed class App : Application
{
    readonly IServiceProvider services;
    readonly NotchController notch;
    readonly IPlatformShell shell;
    readonly Services.UpdateService updates;
    Window? settingsWindow;
    Window? updateWindow;
    bool started;

    public App(IServiceProvider services, NotchController notch, IPlatformShell shell, IPlatformUi ui, Services.UpdateService updates)
    {
        this.services = services;
        this.notch = notch;
        this.shell = shell;
        this.updates = updates;
        this.updates.Found += (_, release) => this.ShowUpdate(release);
        this.updates.ShowRelease = this.ShowUpdate;
        Views.Ui.Platform = ui;
        this.notch.OpenSettings = this.ShowSettings;
        this.UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = this.CreateSettingsWindow();

        // Hidden as soon as it exists: the app starts in the notch, not in a window.
        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView != null && !this.started)
                this.shell.Hide(window);
        };
        window.Created += async (_, _) =>
        {
            if (this.started)
                return;
            this.started = true;
            await this.notch.StartAsync();
            _ = this.SetUpLoginItemOnceAsync();
            this.updates.Start();
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
        // The open panel sits over the top of the screen, where Settings would be read; it folds away,
        // pinned or not, and opens again on hover.
        this.notch.FoldAway();

        // Closing Settings destroys its window; the next open makes a fresh one.
        if (this.settingsWindow == null)
            this.OpenWindow(this.CreateSettingsWindow());

        if (this.settingsWindow != null)
            this.shell.Present(this.settingsWindow);
    }

    /// <summary>
    /// The release notes and Install, in a window of their own: found by a check, or asked for from
    /// Settings. One at a time - a newer release replaces the one showing.
    /// </summary>
    public void ShowUpdate(Services.AppRelease release)
    {
        this.notch.FoldAway();
        if (this.updateWindow != null)
            this.CloseWindow(this.updateWindow);

        var window = new Window(new UpdatePage(this.updates, release))
        {
            Title = "Shiny Notch Update",
            Width = 560,
            Height = 620
        };
        window.Destroying += (_, _) =>
        {
            if (this.updateWindow == window)
                this.updateWindow = null;
        };
        this.updateWindow = window;
        this.OpenWindow(window);
        this.shell.Present(window);
    }

    /// <summary>
    /// The first launch turns on open at login, once. After that it's the user's call (Settings, or
    /// the system's login items), and turning it off has to stick, so this never runs again.
    /// </summary>
    async Task SetUpLoginItemOnceAsync()
    {
        var settings = this.services.GetRequiredService<Services.AppSettings>();
        var startup = this.services.GetRequiredService<Shiny.IAppStartupInstaller>();
        if (settings.LoginItemSetUp || !startup.IsSupported)
            return;
        settings.LoginItemSetUp = true;

        try
        {
            // Only from nothing: a login item the user turned off, or one already there, is left alone.
            if (await startup.GetState() == Shiny.AppStartupState.NotRegistered)
                await startup.Register();
        }
        catch
        {
            // Not fatal: Settings shows the real state and offers the switch.
        }
    }
}
