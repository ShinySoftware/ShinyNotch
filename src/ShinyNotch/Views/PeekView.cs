using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The collapsed notch: a "wing" either side of the camera, at the camera's height. Left is what's
/// playing; right is the countdown, then a meeting that's about to start.
/// </summary>
sealed class PeekView : Grid, ILiveView
{
    readonly CountdownService countdown;
    readonly CalendarService calendar;
    readonly Label? right;

    public const double WingWidth = 64;

    PeekView(double cutoutWidth, double height, View? left, Label? right, CountdownService countdown, CalendarService calendar)
    {
        this.countdown = countdown;
        this.calendar = calendar;
        this.right = right;

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
    public static PeekView? Create(double cutoutWidth, double height, NowPlayingService media, CountdownService countdown, CalendarService calendar)
    {
        View? left = null;
        if (media.Current is { } now)
        {
            var art = media.Artwork != null
                ? (View)new Border
                {
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 5 },
                    WidthRequest = 22,
                    HeightRequest = 22,
                    Content = Ui.Artwork(media.Artwork, 22)
                }
                : Ui.Label("♫", 15, color: Ui.Accent);

            left = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start,
                Children = { art, Ui.Label(now.IsPlaying ? "▶︎" : "❚❚", 10, color: now.IsPlaying ? Ui.Accent : Ui.Dim) }
            };
        }

        var rightText = RightText(countdown, calendar);
        Label? right = null;
        if (rightText != null)
        {
            right = Ui.Label(rightText.Value.Text, 12, bold: true, color: rightText.Value.Color, width: WingWidth);
            right.HorizontalTextAlignment = TextAlignment.End;
            right.VerticalOptions = LayoutOptions.Center;
        }

        return left == null && right == null
            ? null
            : new PeekView(cutoutWidth, height, left, right, countdown, calendar);
    }

    static (string Text, Color Color)? RightText(CountdownService countdown, CalendarService calendar)
    {
        if (countdown.IsActive)
            return (CountdownService.Format(countdown.Remaining), countdown.IsPaused ? Ui.Dim : Ui.Warm);

        if (calendar.StartingWithin(TimeSpan.FromMinutes(15)) is { } next)
            return ($"in {Math.Max(1, (int)Math.Ceiling((next.Start - DateTime.Now).TotalMinutes))}m", next.Color);

        return null;
    }

    public void Tick()
    {
        if (this.right != null && RightText(this.countdown, this.calendar) is { } text)
            this.right.Text = text.Text;
    }
}
