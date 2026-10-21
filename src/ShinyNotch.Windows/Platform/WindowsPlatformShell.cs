using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.Win32;
using ShinyNotch.Platform;
using Launcher = Windows.System.Launcher;
using MauiWindow = Microsoft.Maui.Controls.Window;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace ShinyNotch.Windows;

/// <summary>AppWindow for showing and hiding, SystemEvents for displays, ms-settings: for the rest.</summary>
sealed partial class WindowsPlatformShell : IPlatformShell
{
    const uint MB_ICONASTERISK = 0x40;

    EventHandler? screensChanged;
    bool observingScreens;

    public void Hide(MauiWindow window)
    {
        if (window.Handler?.PlatformView is WinUIWindow native)
            native.AppWindow.Hide();
    }

    public void Present(MauiWindow window)
    {
        if (window.Handler?.PlatformView is not WinUIWindow native)
            return;

        var app = native.AppWindow;
        app.Show();

        // Centred on the display it's on, in that display's work area (clear of the taskbar).
        var area = DisplayArea.GetFromWindowId(app.Id, DisplayAreaFallback.Nearest).WorkArea;
        app.Move(new global::Windows.Graphics.PointInt32(
            area.X + (area.Width - app.Size.Width) / 2,
            area.Y + (area.Height - app.Size.Height) / 2
        ));

        native.Activate();
        // Activate alone doesn't take the foreground from another app; a hidden app has to ask.
        SetForegroundWindow(Win32Interop.GetWindowFromWindowId(app.Id));
    }

    public void Quit() => Microsoft.Maui.Controls.Application.Current?.Quit();

    public event EventHandler? ScreensChanged
    {
        add
        {
            this.screensChanged += value;
            if (this.observingScreens)
                return;
            this.observingScreens = true;
            // Raised on SystemEvents' own thread: hop back before anyone touches the notch.
            SystemEvents.DisplaySettingsChanged += (_, _) =>
                MainThread.BeginInvokeOnMainThread(() => this.screensChanged?.Invoke(this, EventArgs.Empty));
        }
        remove => this.screensChanged -= value;
    }

    public void PlayAlert() => MessageBeep(MB_ICONASTERISK);

    public bool OpenCalendarPrivacySettings()
    {
        _ = Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-calendar"));
        return true;
    }

    public string LoginItemsLocation => "Settings ▸ Apps ▸ Startup";

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MessageBeep(uint type);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);
}
