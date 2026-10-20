using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>A ring that drains as the countdown runs, the time in its middle, presets and controls beside it.</summary>
sealed class TimerTab : Grid, ILiveView
{
    const double RingSize = 124;

    readonly CountdownService countdown;
    readonly RingDrawable ring = new() { Fill = Ui.Orange, Thickness = 8 };
    readonly GraphicsView ringView;
    readonly Label remaining;
    readonly Label caption;

    public TimerTab(CountdownService countdown)
    {
        this.countdown = countdown;
        this.ColumnSpacing = 16;
        this.ColumnDefinitions = [new ColumnDefinition(RingSize), new ColumnDefinition(GridLength.Star)];

        this.ringView = Drawing.View(this.ring, RingSize, RingSize);
        this.remaining = Ui.Label("", 26, bold: true).Centered();
        this.caption = Ui.Caption("").Centered();
        var dial = new Grid
        {
            WidthRequest = RingSize,
            HeightRequest = RingSize,
            Children =
            {
                this.ringView,
                new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { this.remaining, this.caption } }
            }
        };
        this.Children.Add(dial.At(column: 0));

        var presets = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)]
        };
        var minutes = new[] { 1, 5, 10, 15, 25, 45 };
        for (var i = 0; i < minutes.Length; i++)
        {
            var m = minutes[i];
            presets.Children.Add(Ui.Chip($"{m} min", () => countdown.Start(TimeSpan.FromMinutes(m))).At(row: i / 3, column: i % 3));
        }

        var side = new VerticalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = { Ui.Caption("Start a timer"), presets } };
        if (countdown.IsActive)
        {
            side.Children.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    Ui.Chip(countdown.IsPaused ? "Resume" : "Pause", countdown.TogglePause, selected: true, tint: Ui.Orange, symbol: countdown.IsPaused ? "play.fill" : "pause.fill"),
                    Ui.Chip("1 min", () => countdown.Add(TimeSpan.FromMinutes(1)), symbol: "plus"),
                    Ui.Chip("Cancel", countdown.Cancel, tint: Ui.Red, symbol: "xmark")
                }
            });
        }
        this.Children.Add(side.At(column: 1));

        this.Tick();
    }

    public void Tick()
    {
        var active = this.countdown.IsActive;
        this.remaining.Text = active ? CountdownService.Format(this.countdown.Remaining) : "0:00";
        this.remaining.TextColor = active ? Ui.Text : Ui.Tertiary;
        this.caption.Text = !active ? "READY" : this.countdown.IsPaused ? "PAUSED" : $"OF {CountdownService.Format(this.countdown.Duration)}";

        this.ring.Value = active ? this.countdown.Fraction : 0;
        this.ring.Fill = this.countdown.IsPaused ? Ui.Secondary : Ui.Orange;
        this.ringView.Invalidate();
    }
}
