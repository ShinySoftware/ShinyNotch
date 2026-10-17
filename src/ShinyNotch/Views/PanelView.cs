namespace ShinyNotch.Views;

public enum NotchTab
{
    Media,
    Tray,
    Stats,
    Calendar,
    Timer,
    Prompter
}

/// <summary>
/// The expanded notch. The tab strip sits in the band beside the camera (three tabs either side of it),
/// and the selected tab's body hangs below.
/// </summary>
sealed class PanelView : Grid, ILiveView
{
    static readonly (NotchTab Tab, string Glyph, string Name)[] Tabs =
    [
        (NotchTab.Media, "♫", "Now Playing"),
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
            var strip = new HorizontalStackLayout { Spacing = 2, HorizontalOptions = align, VerticalOptions = LayoutOptions.Center };
            foreach (var t in tabs)
            {
                var tab = t.Tab;
                strip.Children.Add(Ui.Glyph(t.Glyph, () => select(tab), 16, tab == selected ? Ui.Accent : Ui.Faint, t.Name));
            }
            return strip;
        }

        var left = Strip(Tabs.Take(3), LayoutOptions.Start);
        var right = Strip(Tabs.Skip(3), LayoutOptions.End);
        right.Children.Add(Ui.Glyph("⦿", togglePin, 13, pinned ? Ui.Warm : Ui.Faint, pinned ? "Unpin" : "Keep open"));
        right.Children.Add(Ui.Glyph("⚙︎", openSettings, 15, Ui.Faint, "Settings"));

        this.Children.Add(left.At(row: 0, column: 0));
        this.Children.Add(right.At(row: 0, column: 2));
        this.Children.Add(body.At(row: 1, columnSpan: 3));
    }

    public void Tick() => (this.Body as ILiveView)?.Tick();
}
