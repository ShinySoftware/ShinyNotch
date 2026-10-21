namespace ShinyNotch.Views;

public enum NotchTab
{
    Media,
    Weather,
    Tray,
    Stats,
    Capture,
    Mirror,
    Notes,
    Tasks,
    Calendar,
    Timer,
    Prompter
}

/// <summary>A tab as the strip and Settings show it.</summary>
public sealed record NotchTabInfo(NotchTab Tab, string Symbol, string Name)
{
    /// <summary>Every tab, in strip order. Whether one shows depends on the platform and on Settings.</summary>
    public static readonly IReadOnlyList<NotchTabInfo> All =
    [
        new(NotchTab.Media, "music.note", "Now Playing"),
        new(NotchTab.Weather, "cloud.sun.fill", "Weather"),
        new(NotchTab.Tray, "tray.and.arrow.down.fill", "File Tray"),
        new(NotchTab.Stats, "cpu", "System"),
        new(NotchTab.Capture, "camera.viewfinder", "Capture"),
        new(NotchTab.Mirror, "person.crop.square.fill", "Mirror"),
        new(NotchTab.Notes, "waveform", "Voice Notes"),
        new(NotchTab.Tasks, "checklist", "Tasks"),
        new(NotchTab.Calendar, "calendar", "Calendar"),
        new(NotchTab.Timer, "timer", "Timer"),
        new(NotchTab.Prompter, "text.alignleft", "Teleprompter")
    ];
}

/// <summary>
/// The expanded notch. The tab strip sits in the band beside the camera, split as evenly as it goes either
/// side of it (pin and settings close the right side), the selected tab's body hangs below, and the
/// <see cref="ControlsBar"/> closes every tab.
/// </summary>
sealed class PanelView : Grid, ILiveView, IDisposable
{
    const double TabSymbol = 16;

    public View Body { get; }

    public ControlsBar Controls { get; }

    /// <param name="tabs">The tabs to show: the ones this platform has and Settings hasn't turned off.</param>
    public PanelView(NotchTab selected, IReadOnlyList<NotchTabInfo> tabs, View body, ControlsBar controls, double cutoutWidth, double bandHeight, bool pinned, Action<NotchTab> select, Action togglePin, Action openSettings)
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

        // Half the buttons (pin and settings count) left of the camera, the left taking the odd one out.
        var leftCount = Math.Min(tabs.Count, (tabs.Count + 3) / 2);
        var leftTabs = tabs.Take(leftCount).ToList();
        var rightTabs = tabs.Skip(leftCount).ToList();

        // Buttons share the room there is between the panel's edge and the camera: the more a side holds,
        // the less padding each gets. The symbols stay full size.
        var perSide = Math.Max(leftTabs.Count, rightTabs.Count + 2);
        var room = (Ui.ContentWidth - Math.Max(cutoutWidth, 8)) / 2;
        var inset = Math.Clamp(Math.Floor((room / perSide - Ui.SymbolBox(TabSymbol)) / 2), 1, 6);

        // The selected tab sits on a pill; the pin only changes colour, since it's a switch, not a place.
        // The others are secondary grey, not tertiary: on black, tertiary was too faint to find.
        View Tab(string symbol, Action tapped, bool on, string hint, Color? onColor = null, bool pill = true)
            => Ui.SymbolButton(symbol, tapped, TabSymbol, on ? onColor ?? Ui.Text : Ui.Secondary, hint, on && pill ? Ui.Raised : null, padding: new Thickness(inset, 4));

        HorizontalStackLayout Strip(IEnumerable<NotchTabInfo> tabs, LayoutOptions align)
        {
            // No spacing: up to six a side have to fit between the panel edge and the camera.
            var strip = new HorizontalStackLayout { Spacing = 0, HorizontalOptions = align, VerticalOptions = LayoutOptions.Center };
            foreach (var t in tabs)
            {
                var tab = t.Tab;
                strip.Children.Add(Tab(t.Symbol, () => select(tab), tab == selected, t.Name));
            }
            return strip;
        }

        var left = Strip(leftTabs, LayoutOptions.Start);
        var right = Strip(rightTabs, LayoutOptions.End);
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

    public void Dispose() => (this.Body as IDisposable)?.Dispose();
}
