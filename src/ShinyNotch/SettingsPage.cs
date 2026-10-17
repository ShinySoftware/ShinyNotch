using ServiceManagement;
using ShinyNotch.Services;

namespace ShinyNotch;

/// <summary>
/// The one ordinary window: things that need typing (the teleprompter script) or a decision you make once.
/// The notch window can never take keyboard focus, so text entry lives here.
/// </summary>
public sealed class SettingsPage : ContentPage
{
    public SettingsPage(AppSettings settings, CalendarService calendar)
    {
        this.Title = "Shiny Notch";
        this.BackgroundColor = Color.FromArgb("#18181B");

        static Label Heading(string text) => new() { Text = text, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, Margin = new Thickness(0, 12, 0, 0) };
        static Label Note(string text) => new() { Text = text, FontSize = 12, TextColor = Color.FromArgb("#A1A1AA") };

        View Toggle(string text, bool value, Action<bool> changed)
        {
            var toggle = new Switch { IsToggled = value, VerticalOptions = LayoutOptions.Center };
            toggle.Toggled += (_, e) => changed(e.Value);
            return new HorizontalStackLayout
            {
                Spacing = 10,
                Children = { toggle, new Label { Text = text, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center } }
            };
        }

        View Stepper(string text, double value, double min, double max, double step, Action<double> changed)
        {
            var label = new Label { Text = $"{text}: {value:0}", TextColor = Colors.White, VerticalOptions = LayoutOptions.Center, WidthRequest = 220 };
            var stepper = new Stepper { Minimum = min, Maximum = max, Increment = step, Value = value, VerticalOptions = LayoutOptions.Center };
            stepper.ValueChanged += (_, e) =>
            {
                label.Text = $"{text}: {e.NewValue:0}";
                changed(e.NewValue);
            };
            return new HorizontalStackLayout { Spacing = 10, Children = { label, stepper } };
        }

        var script = new Editor
        {
            Text = settings.TeleprompterScript,
            HeightRequest = 240,
            FontSize = 14,
            AutoSize = EditorAutoSizeOption.Disabled
        };
        script.TextChanged += (_, e) => settings.TeleprompterScript = e.NewTextValue ?? "";

        var calendarStatus = Note(CalendarText(calendar));
        var calendarButton = new Button { Text = "Allow calendar access", HorizontalOptions = LayoutOptions.Start, IsVisible = !calendar.IsAuthorized && !calendar.IsDenied };
        calendarButton.Clicked += async (_, _) =>
        {
            await calendar.RequestAccessAsync();
            calendarStatus.Text = CalendarText(calendar);
            calendarButton.IsVisible = !calendar.IsAuthorized && !calendar.IsDenied;
        };

        var launchAtLogin = Toggle("Open Shiny Notch when you log in", IsLaunchAtLogin(), SetLaunchAtLogin);

        var quit = new Button { Text = "Quit Shiny Notch", HorizontalOptions = LayoutOptions.Start };
        quit.Clicked += (_, _) => AppKit.NSApplication.SharedApplication.Terminate(null);

        // One scroller for the page; nothing inside it scrolls on its own except the script editor,
        // which has a fixed height.
        this.Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24),
                Spacing = 10,
                Children =
                {
                    new Label { Text = "Shiny Notch", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    Note("Hover the notch to open it. Drop files on it to keep them in the tray."),

                    Heading("Teleprompter"),
                    Note("Your script scrolls right under the camera, so you read while looking at the lens."),
                    script,
                    Stepper("Text size", settings.TeleprompterFontSize, 12, 48, 2, v => settings.TeleprompterFontSize = v),
                    Stepper("Speed (points per second)", settings.TeleprompterSpeed, 5, 200, 4, v => settings.TeleprompterSpeed = v),

                    Heading("Notch"),
                    Toggle("Show volume changes in the notch", settings.ShowVolumeHud, v => settings.ShowVolumeHud = v),
                    Toggle("Show brightness changes in the notch", settings.ShowBrightnessHud, v => settings.ShowBrightnessHud = v),

                    Heading("Permissions"),
                    calendarStatus,
                    calendarButton,
                    Note("Now Playing asks once for permission to talk to Music and Spotify, the first time either is open."),

                    Heading("General"),
                    launchAtLogin,
                    quit
                }
            }
        };
    }

    static string CalendarText(CalendarService calendar)
        => calendar.IsAuthorized ? "Calendar: allowed"
            : calendar.IsDenied ? "Calendar: turned off — allow it in System Settings ▸ Privacy & Security ▸ Calendars"
            : "Calendar: not asked yet";

    static bool IsLaunchAtLogin()
        => SMAppService.MainApp.Status == SMAppServiceStatus.Enabled;

    static void SetLaunchAtLogin(bool enabled)
    {
        try
        {
            if (enabled)
                SMAppService.MainApp.Register(out _);
            else
                SMAppService.MainApp.Unregister(out _);
        }
        catch
        {
            // Registration only works for an app in /Applications that is signed; a dev build may refuse.
        }
    }
}
