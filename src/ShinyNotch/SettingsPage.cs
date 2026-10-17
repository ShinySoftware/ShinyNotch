using ServiceManagement;
using ShinyNotch.Services;

namespace ShinyNotch;

/// <summary>
/// The one ordinary window: things that need typing (the teleprompter script) or a decision you make once.
/// The notch window can never take keyboard focus, so text entry lives here.
/// </summary>
public sealed class SettingsPage : ContentPage
{
    // Kept across rebuilds of the page (see Rebuild): the AppKit head never realises rows added to a page
    // that is already on screen, so a changed list is a fresh page, and the search must survive that.
    static string searchText = "";
    static IReadOnlyList<WeatherPlace> searchResults = [];

    public SettingsPage(AppSettings settings, CalendarService calendar, WeatherService weather)
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

        var weatherSection = this.WeatherSection(settings, weather, Toggle, Note);

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

                    Heading("Weather"),
                    weatherSection,

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

    View WeatherSection(AppSettings settings, WeatherService weather, Func<string, bool, Action<bool>, View> toggle, Func<string, Label> note)
    {
        var section = new VerticalStackLayout { Spacing = 8 };

        section.Children.Add(toggle("Show the weather where I am", settings.WeatherUseCurrentLocation, v =>
        {
            settings.WeatherUseCurrentLocation = v;
            if (v)
                weather.RequestLocationAccess();
            _ = weather.RefreshAsync(force: true);
        }));

        if (settings.WeatherUseCurrentLocation)
        {
            if (weather.IsLocationDenied)
                section.Children.Add(note("Location access is off — allow it in System Settings ▸ Privacy & Security ▸ Location Services."));
            else if (!weather.IsLocationAuthorized)
            {
                var allow = new Button { Text = "Allow location access", HorizontalOptions = LayoutOptions.Start };
                allow.Clicked += (_, _) => weather.RequestLocationAccess();
                section.Children.Add(allow);
            }
        }

        // Units: three buttons, the chosen one ticked. Rebuilding the page moves the tick.
        var units = new HorizontalStackLayout { Spacing = 8 };
        units.Children.Add(new Label { Text = "Temperature", TextColor = Colors.White, VerticalOptions = LayoutOptions.Center, WidthRequest = 110 });
        foreach (var (key, text) in new[] { ("auto", "Automatic"), ("c", "°C"), ("f", "°F") })
        {
            var button = new Button { Text = (settings.TemperatureUnit == key ? "✓ " : "") + text, VerticalOptions = LayoutOptions.Center };
            button.Clicked += (_, _) =>
            {
                weather.SetUnit(key);
                this.Rebuild();
            };
            units.Children.Add(button);
        }
        section.Children.Add(units);
        section.Children.Add(note("Automatic follows your region. You can also tap the temperature in the notch to switch."));

        var places = settings.WeatherPlaces;
        section.Children.Add(note(places.Count == 0 ? "No other places yet." : "Your places — use ‹ › in the notch to switch between them."));
        foreach (var place in places)
        {
            var remove = new Button { Text = "Remove", VerticalOptions = LayoutOptions.Center };
            remove.Clicked += (_, _) =>
            {
                weather.Remove(place);
                this.Rebuild();
            };
            section.Children.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                Children = { new Label { Text = place.Name, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center, WidthRequest = 380 }, remove }
            });
        }

        var search = new Entry { Placeholder = "Add a city — e.g. Toronto", Text = searchText, WidthRequest = 380, ReturnType = ReturnType.Search };
        var go = new Button { Text = "Search", VerticalOptions = LayoutOptions.Center };
        async void Search()
        {
            searchText = search.Text ?? "";
            searchResults = await weather.SearchAsync(searchText);
            if (searchResults.Count == 0)
                searchResults = [new WeatherPlace("", 0, 0)]; // a "nothing found" marker
            this.Rebuild();
        }
        go.Clicked += (_, _) => Search();
        search.Completed += (_, _) => Search();
        section.Children.Add(new HorizontalStackLayout { Spacing = 10, Children = { search, go } });

        foreach (var result in searchResults)
        {
            if (result.Name.Length == 0)
            {
                section.Children.Add(note($"Nothing found for \"{searchText}\"."));
                continue;
            }

            var add = new Button { Text = $"+ {result.Name}", HorizontalOptions = LayoutOptions.Start };
            add.Clicked += (_, _) =>
            {
                weather.Add(result);
                searchText = "";
                searchResults = [];
                this.Rebuild();
            };
            section.Children.Add(add);
        }

        return section;
    }

    void Rebuild()
    {
        if (this.Window is { } window && IPlatformApplication.Current?.Services.GetService<SettingsPage>() is { } fresh)
            window.Page = fresh;
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
