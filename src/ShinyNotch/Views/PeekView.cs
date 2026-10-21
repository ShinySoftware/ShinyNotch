using AppKit;
using Foundation;
using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The collapsed notch: a "wing" either side of the camera, at the camera's height. Left is what's
/// playing - the artwork and its bars, in the artwork's colour. Right is the countdown, then a meeting
/// that's about to start, then the weather.
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
    readonly Label? right;

    const double ArtworkSize = 22;
    const double BarsWidth = 16;
    const double WingSpacing = 8;
    const double RightSpacing = 4;
    const double RightTextSize = 12;
    /// <summary>Kept clear between a wing's content and the camera.</summary>
    const double CameraGap = 6;
    /// <summary>Between a wing's content and the notch's outer edge (the notch itself has no padding).</summary>
    public const double EdgeGap = 12;
    const double CornerRadius = 12;

    PeekView(double cutoutWidth, double leftWidth, double rightWidth, double height, View? left, View? right, Label? rightText, CountdownService countdown, CalendarService calendar, WeatherService weather, TimeProvider time)
    {
        this.countdown = countdown;
        this.calendar = calendar;
        this.weather = weather;
        this.time = time;
        this.right = rightText;

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

    /// <summary>
    /// Null when there is nothing worth peeking at, so the notch can disappear into the camera - unless
    /// <paramref name="preview"/>, when an empty one is shown so a size being changed in Settings can be seen.
    /// </summary>
    public static PeekView? Create(double cutoutWidth, double leftWish, double rightWish, bool locked, double height, bool preview, NowPlayingService media, CountdownService countdown, CalendarService calendar, WeatherService weather, TimeProvider time)
    {
        double leftNeeds = 0, rightNeeds = 0;

        View? left = null;
        if (media.Current is { } now)
        {
            leftNeeds = EdgeGap + ArtworkSize + WingSpacing + BarsWidth + CameraGap;
            left = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start,
                Margin = new Thickness(EdgeGap, 0, 0, 0),
                Children = { Ui.Artwork(media.Artwork, ArtworkSize, 6), MusicBars.Create(now.TrackKey, now.IsPlaying, media.ArtworkColor ?? Ui.Text, BarsWidth, 14) }
            };
        }

        View? right = null;
        Label? rightText = null;
        if (Right(countdown, calendar, weather, time) is { } r)
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
        return new PeekView(cutoutWidth, leftWidth, rightWidth, height, left, right, rightText, countdown, calendar, weather, time);
    }

    /// <summary>The wings' widths: each the wish or its content's need, whichever is more; locked, the wider for both.</summary>
    public static (double Left, double Right) Widths(double leftWish, double rightWish, bool locked, double leftNeeds, double rightNeeds)
    {
        var left = Math.Max(leftWish, leftNeeds);
        var right = Math.Max(rightWish, rightNeeds);
        return locked ? (Math.Max(left, right), Math.Max(left, right)) : (left, right);
    }

    /// <summary>
    /// The right wing's text as AppKit will draw it. Every digit is measured as an 8, the widest, so a
    /// countdown ticking in place (which doesn't rebuild the wing) never outgrows the width it was given.
    /// </summary>
    static double TextWidth(string text)
    {
        var widest = new string(text.Select(c => char.IsAsciiDigit(c) ? '8' : c).ToArray());
        var font = NSFont.BoldSystemFontOfSize((nfloat)RightTextSize);
        using var measured = new NSAttributedString(widest, new NSStringAttributes { Font = font });
        return measured.Size.Width;
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
