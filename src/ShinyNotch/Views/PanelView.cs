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
/// The expanded notch. The tab strip sits in the band beside the camera (four either side of it, counting
/// pin and settings), the selected tab's body hangs below, and the <see cref="ControlsBar"/> closes every tab.
/// </summary>
sealed class PanelView : Grid, ILiveView
{
    const double TabSymbol = 14;

    static readonly (NotchTab Tab, string Symbol, string Name)[] Tabs =
    [
        (NotchTab.Media, "music.note", "Now Playing"),
        (NotchTab.Weather, "cloud.sun.fill", "Weather"),
        (NotchTab.Tray, "tray.and.arrow.down.fill", "File Tray"),
        (NotchTab.Stats, "cpu", "System"),
        (NotchTab.Calendar, "calendar", "Calendar"),
        (NotchTab.Timer, "timer", "Timer"),
        (NotchTab.Prompter, "text.alignleft", "Teleprompter")
    ];

    public View Body { get; }

    public ControlsBar Controls { get; }

    public PanelView(NotchTab selected, View body, ControlsBar controls, double cutoutWidth, double bandHeight, bool pinned, Action<NotchTab> select, Action togglePin, Action openSettings)
    {
        this.Body = body;
        this.Controls = controls;
        this.WidthRequest = Ui.PanelWidth;
        this.RowDefinitions = [new RowDefinition(bandHeight), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];
        this.ColumnDefinitions =
        [
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(Math.Max(cutoutWidth, 8)),
            new ColumnDefinition(GridLength.Star)
        ];
        this.RowSpacing = 10;

        // The selected tab sits on a pill; the pin only changes colour, since it's a switch, not a place.
        // The others are secondary grey, not tertiary: on black, tertiary was too faint to find.
        static View Tab(string symbol, Action tapped, bool on, string hint, Color? onColor = null, bool pill = true)
            => Ui.SymbolButton(symbol, tapped, TabSymbol, on ? onColor ?? Ui.Text : Ui.Secondary, hint, on && pill ? Ui.Raised : null, padding: new Thickness(5, 4));

        HorizontalStackLayout Strip(IEnumerable<(NotchTab Tab, string Symbol, string Name)> tabs, LayoutOptions align)
        {
            // No spacing: four tabs a side have to fit between the panel edge and the camera.
            var strip = new HorizontalStackLayout { Spacing = 0, HorizontalOptions = align, VerticalOptions = LayoutOptions.Center };
            foreach (var t in tabs)
            {
                var tab = t.Tab;
                strip.Children.Add(Tab(t.Symbol, () => select(tab), tab == selected, t.Name));
            }
            return strip;
        }

        var left = Strip(Tabs.Take(4), LayoutOptions.Start);
        var right = Strip(Tabs.Skip(4), LayoutOptions.End);
        right.Children.Add(Tab(pinned ? "pin.fill" : "pin", togglePin, pinned, pinned ? "Unpin" : "Keep open", Ui.Orange, pill: false));
        right.Children.Add(Tab("gearshape.fill", openSettings, false, "Settings"));

        this.Children.Add(left.At(row: 0, column: 0));
        this.Children.Add(right.At(row: 0, column: 2));
        this.Children.Add(body.At(row: 1, columnSpan: 3));
        this.Children.Add(controls.At(row: 2, columnSpan: 3));
    }

    public void Tick()
    {
        (this.Body as ILiveView)?.Tick();
        this.Controls.Tick();
    }
}
