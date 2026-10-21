using ShinyNotch.Views;

namespace ShinyNotch.MacOS;

/// <summary>
/// Shortcuts into macshot: a capture or recording in one click, and the latest captures to open in its
/// editor. Every action goes through <paramref name="run"/>, which gets the notch out of the shot first.
/// </summary>
sealed class CaptureTab : VerticalStackLayout
{
    const int RecentShown = 6;

    static readonly (string Action, string Symbol, string Name, Color Tint)[] Modes =
    [
        ("capture", "rectangle.dashed", "Area", Ui.Blue),
        ("capture-fullscreen", "display", "Screen", Ui.Blue),
        ("scroll-capture", "arrow.up.and.down.text.horizontal", "Scroll", Ui.Teal),
        ("ocr", "text.viewfinder", "Text", Ui.Purple),
        ("record", "record.circle", "Record", Ui.Red),
        ("record-fullscreen", "rectangle.inset.filled.badge.record", "Rec Screen", Ui.Red)
    ];

    public CaptureTab(MacShotService macshot, Action<string> run)
    {
        this.Spacing = 10;

        var modes = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < Modes.Length; i++)
        {
            modes.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var m = Modes[i];
            modes.Children.Add(ModeTile(m.Symbol, m.Name, m.Tint, () => run(m.Action)).At(column: i));
        }
        this.Children.Add(modes);

        var shortcuts = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        Ui.Chip("Last Area", () => run("capture-last"), symbol: "arrow.counterclockwise"),
                        Ui.Chip("Quick", () => run("quick-capture"), symbol: "bolt.fill"),
                        Ui.Chip("Translate", () => run("ocr-translate"), symbol: "character.bubble")
                    }
                }.At(column: 0)
            }
        };
        // Only while macshot is recording: the service watches for it, and a change rebuilds the tab.
        if (macshot.IsRecording)
            shortcuts.Children.Add(Ui.Chip("Stop Recording", () => run("stop-recording"), selected: true, tint: Ui.Red, symbol: "stop.fill").At(column: 1));
        this.Children.Add(shortcuts);

        this.Children.Add(new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.Caption("Recent").At(column: 0),
                new HorizontalStackLayout
                {
                    Spacing = 0,
                    Children =
                    {
                        Ui.SymbolButton("clock.arrow.circlepath", () => run("history"), 12, Ui.Secondary, "All captures"),
                        Ui.SymbolButton("gearshape", () => run("settings"), 12, Ui.Secondary, "macshot Settings")
                    }
                }.At(column: 1)
            }
        });

        var recent = macshot.Recent(RecentShown);
        if (recent.Count == 0)
        {
            this.Children.Add(Ui.Card(
                Ui.Label("Your macshot captures show up here", 12, color: Ui.Secondary).Centered(),
                new Thickness(12, 20)));
            return;
        }

        var strip = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < RecentShown; i++)
            strip.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < recent.Count; i++)
        {
            var id = recent[i].Id;
            strip.Children.Add(Thumbnail(recent[i], () => run($"edit?id={Uri.EscapeDataString(id)}")).At(column: i));
        }
        this.Children.Add(strip);
    }

    static View ModeTile(string symbol, string name, Color tint, Action tapped)
    {
        var label = Ui.Label(name, 11, bold: true).Centered();
        var tile = Ui.Card(new VerticalStackLayout
        {
            Spacing = 4,
            Children = { Centre(Ui.Symbol(symbol, 20, tint)), label }
        }, new Thickness(2, 10, 2, 8), radius: 12);
        SemanticProperties.SetDescription(tile, name);
        tile.AutomationId = name;
        return tile.Pressable(tapped);
    }

    static View Thumbnail(MacShotService.Capture capture, Action tapped)
    {
        View content = capture.Thumbnail is { } png
            ? new Image { Source = ImageSource.FromStream(() => new MemoryStream(png)), Aspect = Aspect.AspectFill }
            : Centre(Ui.Symbol("photo", 16, Ui.Tertiary));
        var tile = Ui.Card(content, new Thickness(0), radius: 10);
        tile.HeightRequest = 52;
        tile.AutomationId = $"Capture {capture.Id}";
        SemanticProperties.SetDescription(tile, "Open in macshot");
        return tile.Pressable(tapped);
    }

    static View Centre(View view)
    {
        view.HorizontalOptions = LayoutOptions.Center;
        view.VerticalOptions = LayoutOptions.Center;
        return view;
    }
}
