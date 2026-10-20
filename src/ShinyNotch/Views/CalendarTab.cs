using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>The week across the top with today ringed, then what's left of today and tomorrow.</summary>
sealed class CalendarTab : VerticalStackLayout
{
    const int MaxShown = 5;

    public CalendarTab(CalendarService calendar, TimeProvider time, Action rebuild)
    {
        this.Spacing = 8;
        var now = time.GetLocalNow().DateTime;

        this.Children.Add(Week(now));

        if (!calendar.IsAuthorized)
        {
            var message = new VerticalStackLayout { Spacing = 8 };
            if (calendar.IsDenied)
            {
                message.Children.Add(Ui.Label("Calendar access is turned off.", 13, color: Ui.Secondary));
                message.Children.Add(Ui.Chip("Open Privacy Settings", () =>
                    AppKit.NSWorkspace.SharedWorkspace.OpenUrl(new Foundation.NSUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_Calendars")), selected: true, symbol: "gearshape.fill"));
            }
            else
            {
                message.Children.Add(Ui.Label("See your next events right in the notch.", 13, color: Ui.Secondary));
                message.Children.Add(Ui.Chip("Allow calendar access", async () =>
                {
                    await calendar.RequestAccessAsync();
                    rebuild();
                }, selected: true, symbol: "calendar.badge.plus"));
            }
            this.Children.Add(Ui.Card(message, new Thickness(14, 12)));
            return;
        }

        var events = calendar.Upcoming;
        if (events.Count == 0)
        {
            this.Children.Add(Ui.Card(new HorizontalStackLayout
            {
                Spacing = 10,
                Children = { Ui.Symbol("checkmark.circle.fill", 16, Ui.Green), Ui.Label("Nothing else today or tomorrow", 13, color: Ui.Secondary) }
            }, new Thickness(14, 12)));
            return;
        }

        DateTime? day = null;
        foreach (var e in events.Take(MaxShown))
        {
            if (day != e.Start.Date)
            {
                day = e.Start.Date;
                var header = e.Start.Date <= now.Date ? "Today" : e.Start.Date == now.Date.AddDays(1) ? "Tomorrow" : e.Start.ToString("dddd");
                var caption = Ui.Caption(header);
                caption.Margin = new Thickness(4, 4, 0, 0);
                this.Children.Add(caption);
            }
            this.Children.Add(Event(e, now));
        }
    }

    /// <summary>Seven days from Sunday (or the culture's first day), today's date in a filled circle.</summary>
    static View Week(DateTime now)
    {
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var start = now.Date.AddDays(-(((int)now.DayOfWeek - (int)first + 7) % 7));

        var grid = new Grid { RowSpacing = 2 };
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var date = start.AddDays(i);
            var today = date == now.Date;

            grid.Children.Add(Ui.Label(date.ToString("ddd")[..1].ToUpperInvariant(), 10, bold: true, color: today ? Ui.Red : Ui.Tertiary).Centered().At(row: 0, column: i));
            grid.Children.Add(new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 13 },
                BackgroundColor = today ? Ui.Red : Colors.Transparent,
                WidthRequest = 26,
                HeightRequest = 26,
                HorizontalOptions = LayoutOptions.Center,
                Content = Ui.Label(date.Day.ToString(), 13, bold: true, color: today ? Ui.Text : date < now.Date ? Ui.Tertiary : Ui.Text).Centered()
            }.At(row: 1, column: i));
        }
        grid.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];

        var month = Ui.Caption(now.ToString("MMMM yyyy"), Ui.Red);
        return Ui.Card(new VerticalStackLayout { Spacing = 6, Children = { month, grid } }, new Thickness(12, 10));
    }

    static View Event(CalendarItem e, DateTime now)
    {
        var happening = !e.AllDay && e.Start <= now;
        var when = e.AllDay ? "All day" : $"{e.Start:t} – {e.End:t}";
        var detail = e.Location == null ? when : $"{when}  ·  {e.Location}";

        var row = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = [new ColumnDefinition(4), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                new BoxView { Color = e.Color, CornerRadius = 2, HeightRequest = 30, VerticalOptions = LayoutOptions.Center }.At(column: 0),
                new VerticalStackLayout
                {
                    Spacing = 1,
                    VerticalOptions = LayoutOptions.Center,
                    Children = { Ui.Label(e.Title, 13, bold: true), Ui.Label(detail, 11, color: Ui.Secondary) }
                }.At(column: 1)
            }
        };

        if (!e.AllDay && (happening || e.Start - now <= TimeSpan.FromHours(1)))
        {
            var text = happening ? "Now" : $"in {Math.Max(1, (int)Math.Ceiling((e.Start - now).TotalMinutes))}m";
            row.Children.Add(new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 9 },
                BackgroundColor = e.Color.WithAlpha(0.22f),
                Padding = new Thickness(8, 2),
                HeightRequest = 18,
                VerticalOptions = LayoutOptions.Center,
                Content = Ui.Label(text, 10, bold: true, color: e.Color)
            }.At(column: 2));
        }

        return Ui.Card(row, new Thickness(10, 8));
    }
}
