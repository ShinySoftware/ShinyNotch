using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The collapsed notch: a "wing" either side of the camera, at the camera's height. Left is what's
/// playing - the artwork and its bars, in the artwork's colour. Right is the countdown, then a meeting
/// that's about to start, then the weather.
/// </summary>
sealed class PeekView : Grid, ILiveView
{
    readonly CountdownService countdown;
    readonly CalendarService calendar;
    readonly WeatherService weather;
    readonly TimeProvider time;
    readonly Label? right;

    public const double WingWidth = 72;

    PeekView(double cutoutWidth, double height, View? left, View? right, Label? rightText, CountdownService countdown, CalendarService calendar, WeatherService weather, TimeProvider time)
    {
        this.countdown = countdown;
        this.calendar = calendar;
        this.weather = weather;
        this.time = time;
        this.right = rightText;

        this.HeightRequest = height;
        this.ColumnDefinitions =
        [
            new ColumnDefinition(WingWidth),
            new ColumnDefinition(Math.Max(cutoutWidth, 8)),
            new ColumnDefinition(WingWidth)
        ];

        if (left != null)
            this.Children.Add(left.At(column: 0));
        if (right != null)
            this.Children.Add(right.At(column: 2));
    }

    /// <summary>Null when there is nothing worth peeking at, so the notch can disappear into the camera.</summary>
    public static PeekView? Create(double cutoutWidth, double height, NowPlayingService media, CountdownService countdown, CalendarService calendar, WeatherService weather, TimeProvider time)
    {
        View? left = null;
        if (media.Current is { } now)
        {
            var bars = new BarsDrawable(now.TrackKey) { IsPlaying = now.IsPlaying, Fill = media.ArtworkColor ?? Ui.Text };
            left = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start,
                Children = { Ui.Artwork(media.Artwork, 22, 6), Drawing.View(bars, 16, 14) }
            };
        }

        View? right = null;
        Label? rightText = null;
        if (Right(countdown, calendar, weather, time) is { } r)
        {
            rightText = Ui.Label(r.Text, 12, bold: true, color: r.Color);
            rightText.HorizontalOptions = LayoutOptions.End;
            right = new HorizontalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Children = { r.Multicolor ? Ui.Symbol(r.Symbol, 12, multicolor: true) : Ui.Symbol(r.Symbol, 11, r.Color), rightText }
            };
        }

        return left == null && right == null
            ? null
            : new PeekView(cutoutWidth, height, left, right, rightText, countdown, calendar, weather, time);
    }

    static (string Symbol, bool Multicolor, string Text, Color Color)? Right(CountdownService countdown, CalendarService calendar, WeatherService weather, TimeProvider time)
    {
        if (countdown.IsActive)
            return (countdown.IsPaused ? "pause.fill" : "timer", false, CountdownService.Format(countdown.Remaining), countdown.IsPaused ? Ui.Secondary : Ui.Orange);

        if (calendar.StartingWithin(TimeSpan.FromMinutes(15)) is { } next)
            return ("calendar", false, $"in {Math.Max(1, (int)Math.Ceiling((next.Start - time.GetLocalNow().DateTime).TotalMinutes))}m", next.Color);

        // Nothing more urgent: the weather where you are, the way the iPhone's island idles.
        if (weather.Selected is { } report)
            return (WeatherService.Symbol(report.Code, report.IsDay), true, WeatherService.Degrees(report.Temperature), Ui.Text);

        return null;
    }

    public void Tick()
    {
        if (this.right != null && Right(this.countdown, this.calendar, this.weather, this.time) is { } r)
            this.right.Text = r.Text;
    }
}
