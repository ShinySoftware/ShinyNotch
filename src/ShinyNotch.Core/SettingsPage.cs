using Shiny;
using ShinyNotch.Platform;
using ShinyNotch.Services;
using ShinyNotch.Views;
using ButtonCell = Shiny.Maui.Controls.Cells.ButtonCell;
using CommandCell = Shiny.Maui.Controls.Cells.CommandCell;
using CustomCell = Shiny.Maui.Controls.Cells.CustomCell;
using EntryCell = Shiny.Maui.Controls.Cells.EntryCell;
using LabelCell = Shiny.Maui.Controls.Cells.LabelCell;
using RadioCell = Shiny.Maui.Controls.Cells.RadioCell;
using SwitchCell = Shiny.Maui.Controls.Cells.SwitchCell;
using TableSection = Shiny.Maui.Controls.Sections.TableSection;
using Slider = Shiny.Maui.Controls.Slider;
using TableView = Shiny.Maui.Controls.TableView;

namespace ShinyNotch;

/// <summary>
/// The one ordinary window: things that need typing (the teleprompter script) or a decision you make once.
/// The notch window can never take keyboard focus, so text entry lives here.
/// </summary>
/// <remarks>
/// A grouped <see cref="TableView"/>, one section per area, styled with the notch's own dark palette.
/// Only cells that edit in place: the picker and number cells open a dialog, and dialogs don't paint on
/// the AppKit head.
/// </remarks>
[Transient]
public sealed class SettingsPage : ContentPage
{
    // Kept across rebuilds of the page (see Rebuild): the AppKit head never realises rows added to a page
    // that is already on screen, so a changed list is a fresh page, and the search must survive that.
    static string searchText = "";
    static IReadOnlyList<WeatherPlace> searchResults = [];

    /// <summary>Saves a script edit still waiting on its debounce.</summary>
    Action? flushScript;

    public SettingsPage(AppSettings settings, CalendarService calendar, WeatherService weather, NotchController notch, IPlatformShell shell, IAppStartupInstaller startup)
    {
        this.Title = "Shiny Notch";
        this.BackgroundColor = Ui.Surface;

        var table = new TableView
        {
            BackgroundColor = Ui.Surface,
            Margin = new Thickness(16, 0, 16, 16),
            CellBackgroundColor = Ui.Raised,
            CellSelectedColor = Ui.Separator,
            CellTitleColor = Ui.Text,
            CellDescriptionColor = Ui.Secondary,
            CellValueTextColor = Ui.Secondary,
            CellHintTextColor = Ui.Secondary,
            CellAccentColor = Ui.Blue,
            CellIconSize = 18,
            HeaderBackgroundColor = Ui.Surface,
            FooterBackgroundColor = Ui.Surface,
            HeaderTextColor = Ui.Secondary,
            HeaderFontSize = 12,
            FooterTextColor = Ui.Tertiary,
            FooterFontSize = 11,
            SeparatorColor = Ui.Separator,
            SectionSeparatorHeight = 18,
            SectionSeparatorColor = Ui.Surface
        };

        var sections = table.Root.Sections;
        sections.Add(this.NotchSection(settings));
        sections.Add(TabsSection(settings, notch.AvailableTabs));
        sections.Add(this.TeleprompterSection(settings));
        // Nothing to ask for where the platform can't locate: the places are the whole story.
        if (!weather.IsLocationUnavailable)
            sections.Add(this.WeatherSection(settings, weather));
        sections.Add(this.TemperatureSection(settings, weather));
        sections.Add(this.PlacesSection(settings, weather));
        if (calendar.IsSupported)
            sections.Add(this.PermissionsSection(calendar));
        sections.Add(GeneralSection(startup, shell));
        sections.Add(AboutSection(shell));

        // The table is the page's one scroller.
        this.Content = table;
        // The table renders its rows on the dispatcher, after the page has loaded (the window is made
        // hidden at startup). The AppKit head sizes the scroller from the first layout and doesn't grow it
        // when the rows arrive, so everything past the first few sections was cut off. Measuring again
        // once they're in, table and page both, fixes the scroll height.
        table.ModelChanged += (_, _) => this.Dispatcher.Dispatch(() =>
        {
            ((IView)table).InvalidateMeasure();
            ((IView)this).InvalidateMeasure();
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Sections
    // ---------------------------------------------------------------------------------------------

    TableSection NotchSection(AppSettings settings)
    {
        var section = Section("Notch", "The size of the closed notch, either side of the camera. Each side still grows to fit what it's showing: what's playing on the left; the weather, a countdown or a meeting on the right. At 0 and 0 with nothing to show, it's just the camera.");
        section.HeaderView = Intro();

        var sliders = new List<Slider>();
        section.Cells.Add(Switch("Same width both sides", "arrow.left.and.right", Ui.Blue, settings.NotchWidthsLocked, locked =>
        {
            // Locking evens the sides at the left's width; unlocking starts both from where they are.
            if (locked)
                settings.NotchRightWidth = settings.NotchLeftWidth;
            settings.NotchWidthsLocked = locked;
            this.Rebuild();
        }));

        if (settings.NotchWidthsLocked)
        {
            section.Cells.Add(SliderCell("Width each side", "arrow.left.and.right.square", Ui.Teal, settings.NotchLeftWidth, AppSettings.MaxNotchWingWidth, sliders, v =>
            {
                settings.NotchLeftWidth = v;
                settings.NotchRightWidth = v;
            }));
        }
        else
        {
            section.Cells.Add(SliderCell("Left of camera", "arrow.left.square", Ui.Teal, settings.NotchLeftWidth, AppSettings.MaxNotchWingWidth, sliders, v => settings.NotchLeftWidth = v));
            section.Cells.Add(SliderCell("Right of camera", "arrow.right.square", Ui.Teal, settings.NotchRightWidth, AppSettings.MaxNotchWingWidth, sliders, v => settings.NotchRightWidth = v));
        }

        var heights = new List<Slider>();
        section.Cells.Add(SliderCell("Extra height", "arrow.up.and.down.square", Ui.Indigo, settings.NotchExtraHeight, AppSettings.MaxNotchExtraHeight, heights, v => settings.NotchExtraHeight = v));

        section.Cells.Add(Button("Reset size", () =>
        {
            foreach (var slider in sliders)
                slider.Value = AppSettings.DefaultNotchWingWidth;
            foreach (var slider in heights)
                slider.Value = 0;
        }));

        section.Cells.Add(Switch("Show the new song when it changes", "music.note", Ui.Red, settings.ShowTrackChanges, v => settings.ShowTrackChanges = v));
        section.Cells.Add(Switch("Show volume changes", "speaker.wave.2.fill", Ui.Pink, settings.ShowVolumeHud, v => settings.ShowVolumeHud = v));
        section.Cells.Add(Switch("Show brightness changes", "sun.max.fill", Ui.Yellow, settings.ShowBrightnessHud, v => settings.ShowBrightnessHud = v));
        return section;
    }

    /// <summary>
    /// A switch for every tab this platform has. The strip spreads whatever is left across the room beside
    /// the camera. The last tab on can't be turned off: an empty strip would leave nothing to open.
    /// </summary>
    static TableSection TabsSection(AppSettings settings, IReadOnlyList<NotchTabInfo> tabs)
    {
        var section = Section("Tabs", "Turn off the tabs you don't use. The rest spread out to fill the strip.");
        foreach (var t in tabs)
        {
            var name = t.Tab.ToString();
            var cell = new SwitchCell { Title = t.Name, On = !settings.HiddenTabs.Contains(name), IconSource = Ui.SymbolSource(t.Symbol, 16, Ui.Blue), IsSelectable = false };
            cell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != SwitchCell.OnProperty.PropertyName)
                    return;

                var hidden = settings.HiddenTabs.Where(h => h != name).ToList();
                if (!cell.On)
                {
                    if (tabs.All(other => other.Tab == t.Tab || hidden.Contains(other.Tab.ToString())))
                    {
                        cell.On = true;
                        return;
                    }
                    hidden.Add(name);
                }
                settings.HiddenTabs = hidden;
            };
            section.Cells.Add(cell);
        }
        return section;
    }

    TableSection TeleprompterSection(AppSettings settings)
    {
        var section = Section("Teleprompter", "Your script scrolls right under the camera, so you read while looking at the lens.");

        var script = new Editor
        {
            Text = settings.TeleprompterScript,
            HeightRequest = 200,
            FontSize = 14,
            TextColor = Ui.Text,
            BackgroundColor = Ui.Surface,
            AutoSize = EditorAutoSizeOption.Disabled
        };
        // Saved once typing pauses, not per keystroke: each save rebuilds an open teleprompter. Leaving the
        // editor saves straight away, so closing the window can't lose the last few keys.
        var scriptEdits = 0;
        var saved = settings.TeleprompterScript;
        void SaveScript()
        {
            if (script.Text is { } text && text != saved)
                settings.TeleprompterScript = saved = text;
        }
        this.flushScript = SaveScript;
        script.TextChanged += (_, _) =>
        {
            var edit = ++scriptEdits;
            this.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), () =>
            {
                if (edit == scriptEdits)
                    SaveScript();
            });
        };
        script.Unfocused += (_, _) =>
        {
            scriptEdits++;
            SaveScript();
        };

        section.Cells.Add(new CustomCell { UseFullSize = true, IsSelectable = false, CustomContent = new ContentView { Padding = new Thickness(12, 10), Content = script } });
        section.Cells.Add(StepperCell("Text size", "textformat.size", Ui.Orange, settings.TeleprompterFontSize, 12, 48, 2, "pt", v => settings.TeleprompterFontSize = v));
        section.Cells.Add(StepperCell("Speed", "speedometer", Ui.Green, settings.TeleprompterSpeed, 5, 200, 4, "pt/s", v => settings.TeleprompterSpeed = v));
        return section;
    }

    TableSection WeatherSection(AppSettings settings, WeatherService weather)
    {
        var section = Section("Weather", null);

        var here = Switch("Show the weather where I am", "location.fill", Ui.Blue, settings.WeatherUseCurrentLocation, v =>
        {
            settings.WeatherUseCurrentLocation = v;
            if (v)
                weather.RequestLocationAccess();
            _ = weather.RefreshAsync(force: true);
            // Shows or hides the location access note and button that go with the switch.
            this.Rebuild();
        });
        section.Cells.Add(here);

        if (settings.WeatherUseCurrentLocation)
        {
            if (weather.IsLocationDenied)
                here.Description = "Location access is off: allow it in your system's privacy settings.";
            else if (!weather.IsLocationAuthorized)
                section.Cells.Add(Button("Allow location access", weather.RequestLocationAccess));
        }
        return section;
    }

    TableSection TemperatureSection(AppSettings settings, WeatherService weather)
    {
        var section = Section("Temperature", "Automatic follows your region. You can also tap the temperature in the notch to switch.");
        foreach (var (key, text) in new[] { ("auto", "Automatic"), ("c", "Celsius (°C)"), ("f", "Fahrenheit (°F)") })
            section.Cells.Add(new RadioCell { Title = text, Value = key });

        RadioCell.SetSelectedValue(section, settings.TemperatureUnit);
        section.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == RadioCell.SelectedValueProperty.PropertyName && RadioCell.GetSelectedValue(section) is string key && key != settings.TemperatureUnit)
                weather.SetUnit(key);
        };
        return section;
    }

    TableSection PlacesSection(AppSettings settings, WeatherService weather)
    {
        var places = settings.WeatherPlaces;
        var section = Section("Places", places.Count == 0 ? "No other places yet." : "Use ‹ › in the notch to switch between them.");

        foreach (var place in places)
        {
            var remove = new Button { Text = "Remove", FontSize = 12, Padding = new Thickness(10, 2), VerticalOptions = LayoutOptions.Center };
            remove.Clicked += (_, _) =>
            {
                weather.Remove(place);
                this.Rebuild();
            };
            section.Cells.Add(new CustomCell { Title = place.Name, IconSource = Ui.SymbolSource("mappin.circle.fill", 16, Ui.Red), IsSelectable = false, CustomContent = remove });
        }

        var search = new EntryCell { Title = "Add a city", Placeholder = "e.g. Toronto", ValueText = searchText, IconSource = Ui.SymbolSource("magnifyingglass", 16, Ui.Secondary) };
        async void Search()
        {
            searchText = search.ValueText ?? "";
            if (searchText.Trim().Length == 0)
                return;
            searchResults = await weather.SearchAsync(searchText);
            if (searchResults.Count == 0)
                searchResults = [new WeatherPlace("", 0, 0)]; // a "nothing found" marker
            this.Rebuild();
        }
        search.Completed += (_, _) => Search();
        section.Cells.Add(search);

        foreach (var result in searchResults)
        {
            if (result.Name.Length == 0)
            {
                section.Cells.Add(new LabelCell { Title = $"Nothing found for \"{searchText}\"", IsSelectable = false, TitleColor = Ui.Secondary });
                continue;
            }

            section.Cells.Add(new CommandCell
            {
                Title = result.Name,
                IconSource = Ui.SymbolSource("plus.circle.fill", 16, Ui.Green),
                ShowArrow = false,
                Command = new Command(() =>
                {
                    weather.Add(result);
                    searchText = "";
                    searchResults = [];
                    this.Rebuild();
                })
            });
        }
        return section;
    }

    TableSection PermissionsSection(CalendarService calendar)
    {
        var section = Section("Permissions", "Now Playing asks once for permission to talk to Music and Spotify, the first time either is open.");

        section.Cells.Add(new LabelCell
        {
            Title = "Calendar",
            IconSource = Ui.SymbolSource("calendar", 16, Ui.Red),
            IsSelectable = false,
            ValueText = calendar.IsAuthorized ? "Allowed" : calendar.IsDenied ? "Turned off" : "Not asked yet",
            Description = calendar.IsDenied ? "Allow it in your system's privacy settings." : ""
        });

        if (!calendar.IsAuthorized && !calendar.IsDenied)
        {
            section.Cells.Add(Button("Allow calendar access", async () =>
            {
                await calendar.RequestAccessAsync();
                this.Rebuild();
            }));
        }
        return section;
    }

    /// <summary>
    /// The launch-at-login switch. It shows what macOS actually decided, not what was asked: the first
    /// registration usually needs approving in System Settings, and the user can turn the login item off
    /// there at any time. So the state is read fresh each time the page is built, never cached.
    /// </summary>
    static TableSection GeneralSection(IAppStartupInstaller startup, IPlatformShell shell)
    {
        var section = Section("General", null);
        if (!startup.IsSupported)
        {
            section.Cells.Add(new LabelCell { Title = "Open at login", Description = "Not available on this system.", IsSelectable = false });
            return section;
        }

        var syncing = false;
        var login = new SwitchCell { Title = "Open Shiny Notch when you log in", IconSource = Ui.SymbolSource("power", 16, Ui.Green), IsSelectable = false };
        var openSettings = Button("Open Login Items", async () => await startup.OpenSettings());

        void Show(AppStartupState state)
        {
            syncing = true;
            login.On = state is AppStartupState.Enabled or AppStartupState.RequiresApproval;
            syncing = false;

            login.Description = state switch
            {
                AppStartupState.RequiresApproval => $"Almost: allow Shiny Notch in {shell.LoginItemsLocation}.",
                AppStartupState.DisabledByUser => $"Turned off in {shell.LoginItemsLocation}.",
                AppStartupState.DisabledByPolicy => "Your organisation's settings don't allow this.",
                _ => ""
            };
            openSettings.IsEnabled = state is AppStartupState.RequiresApproval or AppStartupState.DisabledByUser;
        }

        login.PropertyChanged += async (_, e) =>
        {
            if (syncing || e.PropertyName != SwitchCell.OnProperty.PropertyName)
                return;

            try
            {
                Show(login.On ? await startup.Register() : await startup.Unregister());
            }
            catch (Exception ex)
            {
                login.Description = $"Couldn't change the login item: {ex.Message}";
            }
        };

        // Always present (the AppKit head won't add it later); enabled only when it has something to do.
        openSettings.IsEnabled = false;
        _ = Load();
        async Task Load() => Show(await startup.GetState());

        section.Cells.Add(login);
        section.Cells.Add(openSettings);
        return section;
    }

    static TableSection AboutSection(IPlatformShell shell)
    {
        var section = Section("About", "Shiny Notch is built on the open-source Shiny .NET libraries.");
        section.Cells.Add(new LabelCell { Title = "Version", ValueText = AppInfo.VersionString, IconSource = Ui.SymbolSource("info.circle.fill", 16, Ui.Secondary), IsSelectable = false });
        section.Cells.Add(new CommandCell
        {
            Title = "ShinySoft Technologies",
            ValueText = "shinysoft.biz",
            IconSource = Ui.SymbolSource("sparkles", 16, Ui.Yellow),
            Command = new Command(() => _ = Launcher.OpenAsync("https://shinysoft.biz"))
        });
        section.Cells.Add(new CommandCell
        {
            Title = "Shiny .NET",
            ValueText = "shinylib.net",
            IconSource = Ui.SymbolSource("shippingbox.fill", 16, Ui.Purple),
            Command = new Command(() => _ = Launcher.OpenAsync("https://www.shinylib.net"))
        });

        var quit = Button("Quit Shiny Notch", shell.Quit);
        quit.ButtonTextColor = Ui.Red;
        section.Cells.Add(quit);
        return section;
    }

    // ---------------------------------------------------------------------------------------------
    // Cells
    // ---------------------------------------------------------------------------------------------

    static View Intro() => new VerticalStackLayout
    {
        Spacing = 4,
        Padding = new Thickness(4, 20, 4, 14),
        Children =
        {
            new Label { Text = "Shiny Notch", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Ui.Text },
            new Label { Text = "Hover the notch to open it. Drop files on it to keep them in the tray.", FontSize = 12, TextColor = Ui.Secondary },
            new Label { Text = "NOTCH", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Ui.Secondary, Margin = new Thickness(0, 18, 0, 0) }
        }
    };

    static TableSection Section(string title, string? footer)
    {
        var section = new TableSection(title.ToUpperInvariant());
        if (footer != null)
            section.FooterText = footer;
        return section;
    }

    static SwitchCell Switch(string title, string symbol, Color tint, bool value, Action<bool> changed)
    {
        var cell = new SwitchCell { Title = title, On = value, IconSource = Ui.SymbolSource(symbol, 16, tint), IsSelectable = false };
        cell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == SwitchCell.OnProperty.PropertyName)
                changed(cell.On);
        };
        return cell;
    }

    static ButtonCell Button(string title, Action tapped)
        => new() { Title = title, ButtonTextColor = Ui.Blue, Command = new Command(tapped) };

    /// <summary>Whole points only, and only on a change: every save re-renders the notch, and a drag sends many.</summary>
    static CustomCell SliderCell(string title, string symbol, Color tint, double value, double max, List<Slider> sliders, Action<double> changed)
    {
        var shown = new Label { Text = $"{value:0} pt", TextColor = Ui.Secondary, FontSize = 12, WidthRequest = 44, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };
        var slider = Ui.Slider(0, max, value, 1, tint, Ui.Purple);
        slider.WidthRequest = 220;
        var last = Math.Round(value);
        slider.ValueChangedEvent += (_, v) =>
        {
            var rounded = Math.Round(v);
            if (rounded == last)
                return;
            last = rounded;
            shown.Text = $"{rounded:0} pt";
            changed(rounded);
        };
        sliders.Add(slider);

        return new CustomCell
        {
            Title = title,
            IconSource = Ui.SymbolSource(symbol, 16, tint),
            IsSelectable = false,
            CustomContent = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { slider, shown } }
        };
    }

    static CustomCell StepperCell(string title, string symbol, Color tint, double value, double min, double max, double step, string unit, Action<double> changed)
    {
        var shown = new Label { Text = $"{value:0} {unit}", TextColor = Ui.Secondary, FontSize = 12, VerticalOptions = LayoutOptions.Center };
        var stepper = new Stepper { Minimum = min, Maximum = max, Increment = step, Value = value, VerticalOptions = LayoutOptions.Center };
        stepper.ValueChanged += (_, e) =>
        {
            shown.Text = $"{e.NewValue:0} {unit}";
            changed(e.NewValue);
        };

        return new CustomCell
        {
            Title = title,
            IconSource = Ui.SymbolSource(symbol, 16, tint),
            IsSelectable = false,
            CustomContent = new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.End, Children = { shown, stepper } }
        };
    }

    void Rebuild()
    {
        // The fresh page reads the script from settings, so an edit still in its debounce goes there first.
        this.flushScript?.Invoke();
        if (this.Window is { } window && IPlatformApplication.Current?.Services.GetService<SettingsPage>() is { } fresh)
            window.Page = fresh;
    }
}
