using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Platform;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>Something the closed notch can show, turned on or off in Settings.</summary>
public enum PeekItem
{
    Music,
    Weather,
    Cpu,
    Gpu,
    Memory,
    NetworkIn,
    NetworkOut,
    DiskRead,
    DiskWrite
}

/// <summary>A closed-notch item as Settings and the notch show it.</summary>
/// <param name="Tag">
/// The short name a percentage goes by in the notch ("CPU 23%"). Null for a rate, which goes by its symbol.
/// </param>
public sealed record PeekItemInfo(PeekItem Item, string Name, string Symbol, Color Color, string? Tag = null)
{
    /// <summary>A system reading, sampled every second while it shows.</summary>
    public bool IsReading => this.Item is not (PeekItem.Music or PeekItem.Weather);

    /// <summary>Every item, in Settings' order - and, for the readings, the notch's.</summary>
    public static readonly IReadOnlyList<PeekItemInfo> All =
    [
        new(PeekItem.Music, "What's playing", "music.note", Ui.Red),
        new(PeekItem.Weather, "Weather", "cloud.sun.fill", Ui.Blue),
        new(PeekItem.Cpu, "CPU load", "cpu", Ui.Teal, "CPU"),
        new(PeekItem.Gpu, "GPU load", "cpu", Ui.Purple, "GPU"),
        new(PeekItem.Memory, "Memory used", "memorychip", Ui.Green, "MEM"),
        new(PeekItem.NetworkIn, "Network in", "arrow.down.circle.fill", Ui.Blue),
        new(PeekItem.NetworkOut, "Network out", "arrow.up.circle.fill", Ui.Pink),
        new(PeekItem.DiskRead, "Disk reads", "arrow.down.doc.fill", Ui.Orange),
        new(PeekItem.DiskWrite, "Disk writes", "arrow.up.doc.fill", Ui.Yellow)
    ];

    /// <summary>The items turned on, from the names Settings keeps. A name no longer known is skipped.</summary>
    public static IReadOnlySet<PeekItem> Shown(IEnumerable<string> names)
        => names.Select(n => Enum.TryParse<PeekItem>(n, out var item) ? item : (PeekItem?)null).OfType<PeekItem>().ToHashSet();
}

/// <summary>
/// The collapsed notch: a "wing" either side of the camera, at the camera's height. Left is what's
/// playing - the artwork and its bars, in the artwork's colour - then any system readings turned on in
/// Settings. Right is the countdown, then a meeting that's about to start, then the weather.
/// </summary>
/// <remarks>
/// Each wing is the width asked for in Settings or what its content needs, whichever is more - so a wing
/// asked for at 0 with nothing in it is just the camera. With the widths locked, both wings take the
/// wider of the two, keeping the camera centred.
/// <para>
/// The notch window is always centred on the camera, so uneven wings can't come from the window's width.
/// Instead the window is as wide as the wider wing on both sides, the notch's own surface is clear (see
/// <c>ConfigurePeek</c>), and the black is drawn here, over just the wings asked for.
/// </para>
/// </remarks>
sealed class PeekView : Grid, ILiveView
{
    readonly CountdownService countdown;
    readonly CalendarService calendar;
    readonly WeatherService weather;
    readonly TimeProvider time;
    readonly SystemStatsService stats;
    readonly bool showWeather;
    readonly Label? right;
    readonly IReadOnlyList<(PeekItem Item, Label Value)> readings;

    const double ArtworkSize = 22;
    const double BarsWidth = 16;
    const double WingSpacing = 8;
    const double RightSpacing = 4;
    const double RightTextSize = 12;
    const double ReadingTagSize = 9;
    const double ReadingSymbolSize = 10;
    const double ReadingSpacing = 3;
    /// <summary>Kept clear between a wing's content and the camera.</summary>
    const double CameraGap = 6;
    /// <summary>Between a wing's content and the notch's outer edge (the notch itself has no padding).</summary>
    public const double EdgeGap = 12;
    const double CornerRadius = 12;

    PeekView(double cutoutWidth, double leftWidth, double rightWidth, double height, View? left, View? right, Label? rightText, IReadOnlyList<(PeekItem, Label)> readings, bool showWeather, CountdownService countdown, CalendarService calendar, WeatherService weather, SystemStatsService stats, TimeProvider time)
    {
        this.countdown = countdown;
        this.calendar = calendar;
        this.weather = weather;
        this.stats = stats;
        this.time = time;
        this.showWeather = showWeather;
        this.right = rightText;
        this.readings = readings;

        this.HeightRequest = height;
        var (leftInset, rightInset) = Lay(this, cutoutWidth, leftWidth, rightWidth);

        if (left != null)
        {
            left.Margin = new Thickness(leftInset + EdgeGap, 0, 0, 0);
            this.Children.Add(left.At(column: 0));
        }
        if (right != null)
        {
            right.Margin = new Thickness(0, 0, rightInset + EdgeGap, 0);
            this.Children.Add(right.At(column: 2));
        }
    }

    /// <summary>
    /// Lays out a grid of wings either side of the camera: both columns as wide as the wider wing, so the
    /// window stays centred on the camera, and the notch's black drawn over only the width each wing wants.
    /// Returns how far in from each column's outer edge its wing's black starts - where content should begin.
    /// </summary>
    public static (double LeftInset, double RightInset) Lay(Grid grid, double cutoutWidth, double leftWidth, double rightWidth)
    {
        var wing = Math.Max(leftWidth, rightWidth);
        grid.ColumnDefinitions =
        [
            new ColumnDefinition(wing),
            new ColumnDefinition(Math.Max(cutoutWidth, 8)),
            new ColumnDefinition(wing)
        ];

        var leftInset = wing - leftWidth;
        var rightInset = wing - rightWidth;
        grid.Children.Add(new Border
        {
            BackgroundColor = Colors.Black,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, CornerRadius, CornerRadius) },
            Margin = new Thickness(leftInset, 0, rightInset, 0),
            InputTransparent = true
        }.At(columnSpan: 3));
        return (leftInset, rightInset);
    }

    /// <summary>Shows system readings, so it wants ticking every second.</summary>
    public bool ShowsReadings => this.readings.Count > 0;

    /// <summary>
    /// Null when there is nothing worth peeking at, so the notch can disappear into the camera - unless
    /// <paramref name="preview"/>, when an empty one is shown: so a size being changed in Settings can be seen,
    /// or on a display without a camera notch, where the black stand-in is all there is to hover.
    /// </summary>
    public static PeekView? Create(double cutoutWidth, double leftWish, double rightWish, bool locked, double height, bool preview, IReadOnlySet<PeekItem> items, INowPlayingService media, CountdownService countdown, CalendarService calendar, WeatherService weather, SystemStatsService stats, TimeProvider time)
    {
        double leftNeeds = 0, rightNeeds = 0;

        // Each part of the left wing with its width, which together (and the gaps) are what the wing needs.
        var parts = new HorizontalStackLayout
        {
            Spacing = WingSpacing,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            Margin = new Thickness(EdgeGap, 0, 0, 0)
        };
        double partsWidth = 0;
        void Add(View part, double width)
        {
            partsWidth += (parts.Count > 0 ? WingSpacing : 0) + width;
            parts.Add(part);
        }

        if (items.Contains(PeekItem.Music) && media.Current is { } now)
        {
            Add(Ui.Artwork(media.Artwork, ArtworkSize, 6), ArtworkSize);
            Add(MusicBars.Create(now.TrackKey, now.IsPlaying, media.ArtworkColor ?? Ui.Text, BarsWidth, 14), BarsWidth);
        }

        var readings = new List<(PeekItem, Label)>();
        var shownReadings = PeekItemInfo.All.Where(i => i.IsReading && items.Contains(i.Item)).ToList();
        if (shownReadings.Count > 0)
        {
            // Carries on from the Stats tab or the last peek when that was a moment ago; else "—" for a second.
            stats.Begin();
            foreach (var info in shownReadings)
            {
                Add(Reading(info, stats.Current, out var value, out var width), width);
                readings.Add((info.Item, value));
            }
        }

        View? left = null;
        if (parts.Count > 0)
        {
            leftNeeds = Math.Ceiling(EdgeGap + partsWidth + CameraGap);
            left = parts;
        }

        var showWeather = items.Contains(PeekItem.Weather);
        View? right = null;
        Label? rightText = null;
        if (Right(countdown, calendar, weather, showWeather, time) is { } r)
        {
            rightNeeds = Math.Ceiling(CameraGap + RightTextSize + RightSpacing + TextWidth(r.Text) + EdgeGap);
            rightText = Ui.Label(r.Text, RightTextSize, bold: true, color: r.Color);
            rightText.HorizontalOptions = LayoutOptions.End;
            right = new HorizontalStackLayout
            {
                Spacing = RightSpacing,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, EdgeGap, 0),
                Children = { r.Multicolor ? Ui.Symbol(r.Symbol, 12, multicolor: true) : Ui.Symbol(r.Symbol, 11, r.Color), rightText }
            };
        }

        if (left == null && right == null && !preview)
            return null;

        var (leftWidth, rightWidth) = Widths(leftWish, rightWish, locked, leftNeeds, rightNeeds);
        return new PeekView(cutoutWidth, leftWidth, rightWidth, height, left, right, rightText, readings, showWeather, countdown, calendar, weather, stats, time);
    }

    /// <summary>
    /// A reading: its tag ("CPU") or symbol, then the value. The value is as wide as the widest it can be,
    /// so ticking in place (which doesn't rebuild the wing) never outgrows the room it was given.
    /// </summary>
    static View Reading(PeekItemInfo info, SystemStats? current, out Label value, out double width)
    {
        View label;
        double labelWidth;
        if (info.Tag is { } tag)
        {
            labelWidth = Math.Ceiling(Ui.Platform.TextWidth(tag, ReadingTagSize, bold: true));
            label = Ui.Label(tag, ReadingTagSize, bold: true, color: info.Color, width: labelWidth);
        }
        else
        {
            labelWidth = Ui.SymbolBox(ReadingSymbolSize);
            label = Ui.Symbol(info.Symbol, ReadingSymbolSize, info.Color);
        }

        var widest = info.Tag != null ? "888%" : SystemStatsService.CompactWidest;
        var valueWidth = Math.Ceiling(Ui.Platform.TextWidth(widest, RightTextSize, bold: true));
        value = Ui.Label(Value(info.Item, current), RightTextSize, bold: true, width: valueWidth);
        value.HorizontalTextAlignment = TextAlignment.End;

        width = labelWidth + ReadingSpacing + valueWidth;
        return new HorizontalStackLayout { Spacing = ReadingSpacing, VerticalOptions = LayoutOptions.Center, Children = { label, value } };
    }

    static string Value(PeekItem item, SystemStats? s)
    {
        static string Percent(double? percent) => percent is { } p ? $"{Math.Clamp(p, 0, 100):0}%" : "—";

        if (s == null)
            return "—";

        return item switch
        {
            PeekItem.Cpu => Percent(s.CpuPercent),
            PeekItem.Gpu => Percent(s.GpuPercent),
            PeekItem.Memory => Percent(s.MemoryPercent),
            PeekItem.NetworkIn => SystemStatsService.Compact(s.NetInPerSec),
            PeekItem.NetworkOut => SystemStatsService.Compact(s.NetOutPerSec),
            PeekItem.DiskRead => SystemStatsService.Compact(s.DiskReadPerSec),
            PeekItem.DiskWrite => SystemStatsService.Compact(s.DiskWritePerSec),
            _ => ""
        };
    }

    /// <summary>The wings' widths: each the wish or its content's need, whichever is more; locked, the wider for both.</summary>
    public static (double Left, double Right) Widths(double leftWish, double rightWish, bool locked, double leftNeeds, double rightNeeds)
    {
        var left = Math.Max(leftWish, leftNeeds);
        var right = Math.Max(rightWish, rightNeeds);
        return locked ? (Math.Max(left, right), Math.Max(left, right)) : (left, right);
    }

    /// <summary>
    /// The right wing's text as the platform will draw it. Every digit is measured as an 8, the widest, so a
    /// countdown ticking in place (which doesn't rebuild the wing) never outgrows the width it was given.
    /// </summary>
    static double TextWidth(string text)
    {
        var widest = new string(text.Select(c => char.IsAsciiDigit(c) ? '8' : c).ToArray());
        return Ui.Platform.TextWidth(widest, RightTextSize, bold: true);
    }

    static (string Symbol, bool Multicolor, string Text, Color Color)? Right(CountdownService countdown, CalendarService calendar, WeatherService weather, bool showWeather, TimeProvider time)
    {
        if (countdown.IsActive)
            return (countdown.IsPaused ? "pause.fill" : "timer", false, CountdownService.Format(countdown.Remaining), countdown.IsPaused ? Ui.Secondary : Ui.Orange);

        if (calendar.StartingWithin(TimeSpan.FromMinutes(15)) is { } next)
            return ("calendar", false, $"in {Math.Max(1, (int)Math.Ceiling((next.Start - time.GetLocalNow().DateTime).TotalMinutes))}m", next.Color);

        // Nothing more urgent: the weather where you are, the way the iPhone's island idles.
        if (showWeather && weather.Selected is { } report)
            return (WeatherService.Symbol(report.Code, report.IsDay), true, WeatherService.Degrees(report.Temperature), Ui.Text);

        return null;
    }

    public void Tick()
    {
        if (this.right != null && Right(this.countdown, this.calendar, this.weather, this.showWeather, this.time) is { } r)
            this.right.Text = r.Text;

        if (this.readings.Count > 0)
        {
            var s = this.stats.Sample();
            foreach (var (item, value) in this.readings)
                value.Text = Value(item, s);
        }
    }
}
