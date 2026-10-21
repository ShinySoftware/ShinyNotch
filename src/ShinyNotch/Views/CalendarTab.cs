using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The week across the top with today in red and the picked day ringed, then what's left of today and
/// tomorrow - or, with another day picked, everything on that day.
/// </summary>
sealed class CalendarTab : VerticalStackLayout
{
    const int MaxShown = 5;

    public CalendarTab(CalendarService calendar, TimeProvider time, Action rebuild)
    {
        this.Spacing = 8;
        var now = time.GetLocalNow().DateTime;

        var selected = calendar.SelectedDate ?? now.Date;
        this.Children.Add(Week(now, selected, date => _ = calendar.SelectAsync(date)));

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

        if (calendar.SelectedDate is { } date)
        {
            this.Children.Add(Day(calendar.SelectedEvents, date, now));
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

    /// <summary>Everything on a day other than today, under its date.</summary>
    static View Day(IReadOnlyList<CalendarItem> events, DateTime date, DateTime now)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        var caption = Ui.Caption(date == now.Date.AddDays(1) ? "Tomorrow" : date == now.Date.AddDays(-1) ? "Yesterday" : date.ToString("dddd, MMMM d"));
        caption.Margin = new Thickness(4, 4, 0, 0);
        stack.Children.Add(caption);

        if (events.Count == 0)
        {
            stack.Children.Add(Ui.Card(new HorizontalStackLayout
            {
                Spacing = 10,
                Children = { Ui.Symbol("calendar", 16, Ui.Tertiary), Ui.Label("No events", 13, color: Ui.Secondary) }
            }, new Thickness(14, 12)));
            return stack;
        }

        foreach (var e in events.Take(MaxShown))
            stack.Children.Add(Event(e, now));
        return stack;
    }

    /// <summary>
    /// Seven days from Sunday (or the culture's first day) around the picked day. Today's date is red,
    /// the picked one sits in a filled circle; arrows step a week either way.
    /// </summary>
    static View Week(DateTime now, DateTime selected, Action<DateTime> select)
    {
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var start = selected.AddDays(-(((int)selected.DayOfWeek - (int)first + 7) % 7));

        var grid = new Grid { RowSpacing = 2 };
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var date = start.AddDays(i);
            var today = date == now.Date;
            var picked = date == selected;

            grid.Children.Add(Ui.Label(date.ToString("ddd")[..1].ToUpperInvariant(), 10, bold: true, color: today ? Ui.Red : Ui.Tertiary).Centered().At(row: 0, column: i));

            // The circle and the number are siblings, each centred in the cell: a label filling a Border
            // draws its text at the top on AppKit, which left the number sitting high in the circle.
            var number = Ui.Label(date.Day.ToString(), 13, bold: true, color: picked ? (today ? Ui.Text : Colors.Black) : today ? Ui.Red : date < now.Date ? Ui.Tertiary : Ui.Text).Centered();
            number.VerticalOptions = LayoutOptions.Center;
            number.InputTransparent = true;
            var cell = new Grid
            {
                HeightRequest = 28,
                BackgroundColor = Colors.Transparent,
                Children =
                {
                    new Border
                    {
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = 14 },
                        BackgroundColor = picked ? (today ? Ui.Red : Ui.Text) : Colors.Transparent,
                        WidthRequest = 28,
                        HeightRequest = 28,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        InputTransparent = true
                    },
                    number
                }
            };
            cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => select(date)) });
            grid.Children.Add(cell.At(row: 1, column: i));
        }
        grid.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];

        var header = new Grid
        {
            ColumnSpacing = 2,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                Ui.Caption(selected.ToString("MMMM yyyy"), Ui.Red).At(column: 0),
                Ui.SymbolButton("chevron.left", () => select(selected.AddDays(-7)), 11, Ui.Secondary, "Previous week", padding: new Thickness(6, 2)).At(column: 2),
                Ui.SymbolButton("chevron.right", () => select(selected.AddDays(7)), 11, Ui.Secondary, "Next week", padding: new Thickness(6, 2)).At(column: 3)
            }
        };
        if (selected != now.Date)
        {
            var back = Ui.Caption("Today", Ui.Red);
            back.Margin = new Thickness(0, 0, 6, 0);
            back.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => select(now.Date)) });
            header.Children.Add(back.At(column: 1));
        }

        return Ui.Card(new VerticalStackLayout { Spacing = 6, Children = { header, grid } }, new Thickness(12, 10));
    }

    static View Event(CalendarItem e, DateTime now)
    {
        var happening = !e.AllDay && e.Start <= now && e.End > now;
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

        if (!e.AllDay && (happening || (e.Start > now && e.Start - now <= TimeSpan.FromHours(1))))
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
