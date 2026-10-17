using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The iOS weather widget, in a notch: the place and the conditions now, the next twelve hours across, and
/// the week down, each day with its low-to-high bar placed on the week's range.
/// </summary>
sealed class WeatherTab : VerticalStackLayout
{
    const double Width = Ui.PanelWidth - 28;

    public WeatherTab(WeatherService weather, Action openSettings)
    {
        this.Spacing = 10;

        if (weather.Places.Count == 0)
        {
            this.Empty(weather, openSettings);
            return;
        }

        var report = weather.Selected;
        this.Children.Add(Header(weather, report));

        if (report == null)
        {
            this.Children.Add(Ui.Label("Loading the forecast…", 13, color: Ui.Dim));
            _ = weather.RefreshAsync();
            return;
        }

        this.Children.Add(Hourly(report));
        this.Children.Add(new BoxView { Color = Ui.Track, HeightRequest = 1, WidthRequest = Width });
        this.Children.Add(Week(report));
    }

    void Empty(WeatherService weather, Action openSettings)
    {
        this.Children.Add(Ui.Label("☀️  Weather", 16, bold: true));
        if (weather.IsLocationDenied)
        {
            this.Children.Add(Ui.Label("Location access is off. Add a city in Settings, or allow it in System Settings.", 12, color: Ui.Dim, width: Width));
        }
        else
        {
            this.Children.Add(Ui.Label("Show the weather where you are, or for any city you add.", 12, color: Ui.Dim, width: Width));
            this.Children.Add(Ui.Chip("Use my location", weather.RequestLocationAccess, selected: true));
        }
        this.Children.Add(Ui.Chip("Add a city in Settings", openSettings));
    }

    static View Header(WeatherService weather, WeatherReport? report)
    {
        var place = weather.SelectedPlace!;
        // Just the city, as on iOS; Settings shows the full "City, Region, Country" to tell places apart.
        var city = place.Name.Split(',')[0].Trim();
        var name = Ui.Label((place.IsCurrent ? "➤ " : "") + city, 15, bold: true, width: 260);

        var grid = new Grid
        {
            WidthRequest = Width,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)]
        };

        var title = new HorizontalStackLayout { Spacing = 4, Children = { name } };
        if (weather.Places.Count > 1)
        {
            title.Children.Insert(0, Ui.Glyph("‹", () => weather.Select(-1), 18, Ui.Dim, "Previous place"));
            title.Children.Add(Ui.Glyph("›", () => weather.Select(1), 18, Ui.Dim, "Next place"));
        }
        grid.Children.Add(title.At(row: 0, column: 0));

        if (report != null)
        {
            var now = Ui.Label(WeatherService.Degrees(report.Temperature), 34, bold: false);
            now.HorizontalTextAlignment = TextAlignment.End;
            // Tap the temperature to flip °C / °F.
            now.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(weather.ToggleUnit) });
            SemanticProperties.SetDescription(now, WeatherService.UsesFahrenheit ? "Show Celsius" : "Show Fahrenheit");
            grid.Children.Add(now.At(row: 0, column: 1, rowSpan: 2));

            grid.Children.Add(Ui.Label(
                $"{WeatherService.Symbol(report.Code, report.IsDay)}  {WeatherService.Describe(report.Code)}   H:{WeatherService.Degrees(report.High)}  L:{WeatherService.Degrees(report.Low)}",
                12, color: Ui.Dim, width: 330).At(row: 1, column: 0));
        }
        return grid;
    }

    static View Hourly(WeatherReport report)
    {
        const int count = 12;
        var grid = new Grid { WidthRequest = Width, RowSpacing = 4 };
        grid.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)];

        var hours = report.Hours.Take(count).ToList();
        for (var i = 0; i < hours.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var h = hours[i];

            grid.Children.Add(Centered(Ui.Label(i == 0 ? "Now" : h.Time.ToString(HourFormat), 11, bold: i == 0, color: Ui.Dim)).At(row: 0, column: i));

            // The chance of rain sits under the symbol only when it is worth mentioning, as on iOS.
            var symbol = h.PrecipitationChance is >= 30
                ? new VerticalStackLayout
                {
                    Spacing = 0,
                    Children =
                    {
                        Centered(Ui.Label(WeatherService.Symbol(h.Code, h.IsDay), 17)),
                        Centered(Ui.Label($"{h.PrecipitationChance}%", 9, bold: true, color: Color.FromArgb("#7DD3FC")))
                    }
                }
                : (View)Centered(Ui.Label(WeatherService.Symbol(h.Code, h.IsDay), 17));
            grid.Children.Add(symbol.At(row: 1, column: i));

            grid.Children.Add(Centered(Ui.Label(WeatherService.Degrees(h.Temperature), 13, bold: true)).At(row: 2, column: i));
        }
        return grid;
    }

    static View Week(WeatherReport report)
    {
        var days = report.Days.Take(7).ToList();
        var weekLow = days.Min(d => d.Low);
        var weekHigh = days.Max(d => d.High);
        var span = Math.Max(1, weekHigh - weekLow);

        var list = new VerticalStackLayout { Spacing = 6, WidthRequest = Width };
        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var row = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                [
                    new ColumnDefinition(56),   // day
                    new ColumnDefinition(28),   // symbol
                    new ColumnDefinition(34),   // chance of rain
                    new ColumnDefinition(36),   // low
                    new ColumnDefinition(GridLength.Star), // range bar
                    new ColumnDefinition(36)    // high
                ]
            };

            row.Children.Add(Ui.Label(i == 0 ? "Today" : d.Date.ToString("ddd"), 13, bold: true, width: 56).At(column: 0));
            row.Children.Add(Centered(Ui.Label(WeatherService.Symbol(d.Code), 16)).At(column: 1));
            if (d.PrecipitationChance is >= 30)
                row.Children.Add(Ui.Label($"{d.PrecipitationChance}%", 10, bold: true, color: Color.FromArgb("#7DD3FC")).At(column: 2));

            var low = Ui.Label(WeatherService.Degrees(d.Low), 13, color: Ui.Dim);
            low.HorizontalTextAlignment = TextAlignment.End;
            row.Children.Add(low.At(column: 3));
            row.Children.Add(RangeBar(d, weekLow, span, i == 0 ? report.Temperature : null).At(column: 4));
            var high = Ui.Label(WeatherService.Degrees(d.High), 13, bold: true);
            high.HorizontalTextAlignment = TextAlignment.End;
            row.Children.Add(high.At(column: 5));

            list.Children.Add(row);
        }
        return list;
    }

    /// <summary>
    /// The day's low-to-high on a track spanning the week's range, so the days line up against each other.
    /// Built from star columns rather than measured widths: it lays out once and never needs to know its size.
    /// </summary>
    static View RangeBar(DayForecast day, double weekLow, double span, double? now)
    {
        var before = Math.Max(0.001, (day.Low - weekLow) / span);
        var inside = Math.Max(0.02, (day.High - day.Low) / span);
        var after = Math.Max(0.001, 1 - before - inside);

        var bar = new Grid
        {
            HeightRequest = 6,
            VerticalOptions = LayoutOptions.Center,
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(before, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(inside, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(after, GridUnitType.Star))
            ]
        };
        bar.Children.Add(new BoxView { Color = Ui.Track, CornerRadius = 3 }.At(columnSpan: 3));
        bar.Children.Add(new BoxView { Color = TemperatureColor((day.Low + day.High) / 2), CornerRadius = 3 }.At(column: 1));

        // Today's bar carries a dot at the current temperature, as on iOS.
        if (now is { } t && day.High > day.Low)
        {
            var at = Math.Clamp((t - day.Low) / (day.High - day.Low), 0, 1);
            var inner = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(new GridLength(Math.Max(0.001, at), GridUnitType.Star)),
                    new ColumnDefinition(6),
                    new ColumnDefinition(new GridLength(Math.Max(0.001, 1 - at), GridUnitType.Star))
                ]
            };
            inner.Children.Add(new BoxView { Color = Colors.White, CornerRadius = 3, WidthRequest = 6, HeightRequest = 6 }.At(column: 1));
            bar.Children.Add(inner.At(column: 1));
        }
        return bar;
    }

    static Color TemperatureColor(double t)
    {
        var c = WeatherService.UsesFahrenheit ? (t - 32) * 5 / 9 : t;
        return c switch
        {
            < 0 => Color.FromArgb("#60A5FA"),
            < 10 => Color.FromArgb("#22D3EE"),
            < 18 => Color.FromArgb("#4ADE80"),
            < 26 => Color.FromArgb("#FBBF24"),
            _ => Color.FromArgb("#F97316")
        };
    }

    // "10AM" without the space, as the iOS widget does; "10 AM" doesn't fit a twelfth of the panel.
    static string HourFormat => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H') ? "HH" : "htt";

    static Label Centered(Label label)
    {
        label.HorizontalOptions = LayoutOptions.Center;
        label.HorizontalTextAlignment = TextAlignment.Center;
        return label;
    }
}
