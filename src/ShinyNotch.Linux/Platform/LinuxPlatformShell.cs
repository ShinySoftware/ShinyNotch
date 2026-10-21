using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Linux;

/// <summary>GTK windows, the GDK monitor list, and the freedesktop sound theme.</summary>
[Singleton]
sealed class LinuxPlatformShell : IPlatformShell
{
    Gio.ListModel? monitors;
    EventHandler? screensChanged;

    public void Hide(Window window)
    {
        if (window.Handler?.PlatformView is Gtk.Window native)
            native.SetVisible(false);
    }

    /// <summary>
    /// Shown and presented. Not centred: GTK 4 has no say over where a window goes (Wayland never lets a
    /// client place its toplevel), so it lands wherever the window manager puts it.
    /// </summary>
    public void Present(Window window)
    {
        if (window.Handler?.PlatformView is Gtk.Window native)
        {
            native.SetVisible(true);
            native.Present();
        }
    }

    public void Quit()
    {
        // The GtkApplication behind the app's windows, rather than MAUI's: quitting it ends the main loop.
        var gtk = Application.Current?.Windows
            .Select(w => (w.Handler?.PlatformView as Gtk.Window)?.GetApplication())
            .FirstOrDefault(a => a != null);
        if (gtk != null)
            gtk.Quit();
        else
            Application.Current?.Quit();
    }

    public event EventHandler? ScreensChanged
    {
        add
        {
            this.screensChanged += value;
            if (this.monitors == null && Gdk.Display.GetDefault() is { } display)
            {
                // Kept: the list model only reports changes while something holds it.
                this.monitors = display.GetMonitors();
                this.monitors.OnItemsChanged += (_, _) => this.screensChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        remove => this.screensChanged -= value;
    }

    /// <summary>The sound theme's "complete" through libcanberra's player, or the display's bell without it.</summary>
    public void PlayAlert()
    {
        if (!Command.Start("canberra-gtk-play", "-i", "complete", "-d", "Shiny Notch"))
            Gdk.Display.GetDefault()?.Beep();
    }

    // No portal or settings panel to deep-link into, and no calendar to ask for (see UnsupportedCalendarStore).
    public bool OpenCalendarPrivacySettings() => false;

    public string LoginItemsLocation => "your desktop's Startup Applications";
}
