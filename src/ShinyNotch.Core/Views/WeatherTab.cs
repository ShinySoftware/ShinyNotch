using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The iOS weather widget, in a notch: the place and the conditions now over the next twelve hours, on a
/// card tinted like the sky, then the week, each day with its low-to-high bar placed on the week's range.
/// </summary>
sealed class WeatherTab : VerticalStackLayout
{
    public WeatherTab(WeatherService weather, Action openSettings)
    {
        this.Spacing = 10;

        if (weather.Places.Count == 0)
        {
            this.Children.Add(Empty(weather, openSettings));
            return;
        }

        var report = weather.Selected;
        var now = new VerticalStackLayout { Spacing = 12, Children = { Header(weather, report) } };

        if (report == null)
        {
            now.Children.Add(Ui.Label("Loading the forecast…", 12, color: Ui.Secondary));
            this.Children.Add(Ui.Card(now, new Thickness(14, 12)));
            _ = weather.RefreshAsync();
            return;
        }

        now.Children.Add(Hourly(report));
        this.Children.Add(Ui.Card(now, new Thickness(14, 12), wash: Sky(report.Code, report.IsDay).WithAlpha(0.85f)));

        // Week takes the week's range, which an empty forecast doesn't have.
        if (report.Days.Count > 0)
            this.Children.Add(Ui.Card(Week(report, weather.UsesFahrenheit), new Thickness(14, 10)));
    }

    static View Empty(WeatherService weather, Action openSettings)
    {
        var content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new HorizontalStackLayout { Spacing = 8, Children = { Ui.Symbol("cloud.sun.fill", 18, multicolor: true), Ui.Label("Weather", 16, bold: true) } }
            }
        };

        var actions = new HorizontalStackLayout { Spacing = 8 };
        if (weather.IsLocationUnavailable)
        {
            content.Children.Add(Ui.Label("Add any city in Settings to see its weather here.", 12, color: Ui.Secondary));
        }
        else if (weather.IsLocationDenied)
        {
            content.Children.Add(Ui.Label("Location access is off. Add a city in Settings, or allow it in your system's privacy settings.", 12, color: Ui.Secondary));
        }
        else
        {
            content.Children.Add(Ui.Label("Show the weather where you are, or for any city you add.", 12, color: Ui.Secondary));
            actions.Children.Add(Ui.Chip("Use my location", weather.RequestLocationAccess, selected: true, symbol: "location.fill"));
        }
        actions.Children.Add(Ui.Chip("Add a city", openSettings, symbol: "plus"));
        content.Children.Add(actions);
        return Ui.Card(content, new Thickness(14, 12));
    }

    static View Header(WeatherService weather, WeatherReport? report)
    {
        var place = weather.SelectedPlace!;
        // Just the city, as on iOS; Settings shows the full "City, Region, Country" to tell places apart.
        var city = place.Name.Split(',')[0].Trim();

        var title = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        if (weather.Places.Count > 1)
            title.Children.Add(Ui.SymbolButton("chevron.left", () => weather.Select(-1), 11, Ui.Secondary, "Previous place", padding: new Thickness(2, 4)));
        if (place.IsCurrent)
            title.Children.Add(Ui.Symbol("location.fill", 10, Ui.Text));
        title.Children.Add(Ui.Label(city, 16, bold: true));
        if (weather.Places.Count > 1)
            title.Children.Add(Ui.SymbolButton("chevron.right", () => weather.Select(1), 11, Ui.Secondary, "Next place", padding: new Thickness(2, 4)));

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)],
            Children = { title.At(row: 0, column: 0) }
        };

        if (report != null)
        {
            var condition = Ui.Label(WeatherService.Describe(report.Code), 12, bold: true, color: Ui.Secondary);
            condition.VerticalOptions = LayoutOptions.Center;
            grid.Children.Add(new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Ui.Symbol(WeatherService.Symbol(report.Code, report.IsDay), 14, multicolor: true),
                    condition
                }
            }.At(row: 1, column: 0));

            var temperature = Ui.Label(WeatherService.Degrees(report.Temperature), 38);
            temperature.HorizontalTextAlignment = TextAlignment.End;
            // Tap the temperature to flip °C / °F.
            temperature.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(weather.ToggleUnit) });
            SemanticProperties.SetDescription(temperature, weather.UsesFahrenheit ? "Show Celsius" : "Show Fahrenheit");

            var range = Ui.Label($"H:{WeatherService.Degrees(report.High)}  L:{WeatherService.Degrees(report.Low)}", 11, bold: true, color: Ui.Secondary);
            range.HorizontalTextAlignment = TextAlignment.End;

            grid.Children.Add(new VerticalStackLayout { Spacing = -4, Children = { temperature, range } }.At(row: 0, column: 1, rowSpan: 2));
        }
        return grid;
    }

    static View Hourly(WeatherReport report)
    {
        const int count = 12;
        var grid = new Grid { RowSpacing = 6 };
        grid.RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(28), new RowDefinition(GridLength.Auto)];

        var hours = report.Hours.Take(count).ToList();
        for (var i = 0; i < hours.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var h = hours[i];

            grid.Children.Add(Ui.Label(i == 0 ? "Now" : h.Time.ToString(HourFormat), 10, bold: true, color: i == 0 ? Ui.Text : Ui.Secondary).Centered().At(row: 0, column: i));

            // The chance of rain sits under the symbol only when it is worth mentioning, as on iOS.
            var symbol = new VerticalStackLayout
            {
                Spacing = 0,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { Centre(Ui.Symbol(WeatherService.Symbol(h.Code, h.IsDay), 15, multicolor: true)) }
            };
            if (h.PrecipitationChance is >= 30)
                symbol.Children.Add(Ui.Label($"{h.PrecipitationChance}%", 9, bold: true, color: Ui.Teal).Centered());
            grid.Children.Add(symbol.At(row: 1, column: i));

            grid.Children.Add(Ui.Label(WeatherService.Degrees(h.Temperature), 13, bold: true).Centered().At(row: 2, column: i));
        }
        return grid;
    }

    static View Week(WeatherReport report, bool fahrenheit)
    {
        var days = report.Days.Take(7).ToList();
        var weekLow = days.Min(d => d.Low);
        var weekHigh = days.Max(d => d.High);
        var span = Math.Max(1, weekHigh - weekLow);

        var list = new VerticalStackLayout { Spacing = 4 };
        list.Children.Add(Ui.Caption("7-day forecast"));
        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var row = new Grid
            {
                ColumnSpacing = 8,
                HeightRequest = 26,
                ColumnDefinitions =
                [
                    new ColumnDefinition(52),   // day
                    new ColumnDefinition(24),   // symbol
                    new ColumnDefinition(32),   // chance of rain
                    new ColumnDefinition(34),   // low
                    new ColumnDefinition(GridLength.Star), // range bar
                    new ColumnDefinition(34)    // high
                ]
            };

            row.Children.Add(Ui.Label(i == 0 ? "Today" : d.Date.ToString("ddd"), 13, bold: true).At(column: 0));
            row.Children.Add(Centre(Ui.Symbol(WeatherService.Symbol(d.Code), 14, multicolor: true)).At(column: 1));
            if (d.PrecipitationChance is >= 30)
                row.Children.Add(Ui.Label($"{d.PrecipitationChance}%", 10, bold: true, color: Ui.Teal).At(column: 2));

            var low = Ui.Label(WeatherService.Degrees(d.Low), 13, bold: true, color: Ui.Tertiary);
            low.HorizontalTextAlignment = TextAlignment.End;
            row.Children.Add(low.At(column: 3));

            var bar = Drawing.View(new RangeDrawable(d.Low, d.High, weekLow, span, i == 0 ? report.Temperature : null, t => TemperatureColor(t, fahrenheit)), 200, 5);
            bar.HorizontalOptions = LayoutOptions.Fill;
            row.Children.Add(bar.At(column: 4));

            var high = Ui.Label(WeatherService.Degrees(d.High), 13, bold: true);
            high.HorizontalTextAlignment = TextAlignment.End;
            row.Children.Add(high.At(column: 5));

            list.Children.Add(row);
        }
        return list;
    }

    static View Centre(View view)
    {
        view.HorizontalOptions = LayoutOptions.Center;
        return view;
    }

    /// <summary>The card's tint: the sky the conditions describe.</summary>
    static Color Sky(int code, bool isDay) => code switch
    {
        _ when !isDay => Color.FromArgb("#27306B"),
        0 or 1 => Color.FromArgb("#2F7BE0"),
        2 => Color.FromArgb("#3C6FB0"),
        3 or 45 or 48 => Color.FromArgb("#56657A"),
        >= 71 and <= 77 or 85 or 86 => Color.FromArgb("#6D84A3"),
        >= 95 => Color.FromArgb("#433B6B"),
        _ => Color.FromArgb("#3D5068")
    };

    static Color TemperatureColor(double t, bool fahrenheit)
    {
        var c = fahrenheit ? (t - 32) * 5 / 9 : t;
        return c switch
        {
            < 0 => Color.FromArgb("#5E9CFF"),
            < 10 => Color.FromArgb("#64D2FF"),
            < 18 => Color.FromArgb("#5CE1A0"),
            < 26 => Color.FromArgb("#FFD60A"),
            _ => Color.FromArgb("#FF8A3D")
        };
    }

    // "10AM" without the space, as the iOS widget does; "10 AM" doesn't fit a twelfth of the panel.
    static string HourFormat => System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H') ? "HH" : "htt";
}
