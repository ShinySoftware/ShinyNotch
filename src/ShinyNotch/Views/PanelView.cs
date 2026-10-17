namespace ShinyNotch.Views;

public enum NotchTab
{
    Media,
    Weather,
    Tray,
    Stats,
    Calendar,
    Timer,
    Prompter
}

/// <summary>
/// The expanded notch. The tab strip sits in the band beside the camera (four tabs either side of it, counting pin and settings),
/// and the selected tab's body hangs below.
/// </summary>
sealed class PanelView : Grid, ILiveView
{
    static readonly (NotchTab Tab, string Glyph, string Name)[] Tabs =
    [
        (NotchTab.Media, "♫", "Now Playing"),
        (NotchTab.Weather, "☀︎", "Weather"),
        (NotchTab.Tray, "⇩", "File Tray"),
        (NotchTab.Stats, "◔", "System"),
        (NotchTab.Calendar, "▦", "Calendar"),
        (NotchTab.Timer, "◷", "Timer"),
        (NotchTab.Prompter, "¶", "Teleprompter")
    ];

    public View Body { get; }

    public PanelView(NotchTab selected, View body, double cutoutWidth, double bandHeight, bool pinned, Action<NotchTab> select, Action togglePin, Action openSettings)
    {
        this.Body = body;
        this.WidthRequest = Ui.PanelWidth;
        this.RowDefinitions = [new RowDefinition(bandHeight), new RowDefinition(GridLength.Auto)];
        this.ColumnDefinitions =
        [
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(Math.Max(cutoutWidth, 8)),
            new ColumnDefinition(GridLength.Star)
        ];
        this.RowSpacing = 6;

        HorizontalStackLayout Strip(IEnumerable<(NotchTab Tab, string Glyph, string Name)> tabs, LayoutOptions align)
        {
            // No spacing: four glyphs a side have to fit between the panel edge and the camera.
            var strip = new HorizontalStackLayout { Spacing = 0, HorizontalOptions = align, VerticalOptions = LayoutOptions.Center };
            foreach (var t in tabs)
            {
                var tab = t.Tab;
                strip.Children.Add(Ui.Glyph(t.Glyph, () => select(tab), 15, tab == selected ? Ui.Accent : Ui.Faint, t.Name));
            }
            return strip;
        }

        var left = Strip(Tabs.Take(4), LayoutOptions.Start);
        var right = Strip(Tabs.Skip(4), LayoutOptions.End);
        right.Children.Add(Ui.Glyph("⦿", togglePin, 12, pinned ? Ui.Warm : Ui.Faint, pinned ? "Unpin" : "Keep open"));
        right.Children.Add(Ui.Glyph("⚙︎", openSettings, 14, Ui.Faint, "Settings"));

        this.Children.Add(left.At(row: 0, column: 0));
        this.Children.Add(right.At(row: 0, column: 2));
        this.Children.Add(body.At(row: 1, columnSpan: 3));
    }

    public void Tick() => (this.Body as ILiveView)?.Tick();
}
