using System.Globalization;
using System.Text.Json;
using ShinyNotch.Platform;

namespace ShinyNotch.Services;

public sealed record WeatherPlace(string Name, double Latitude, double Longitude, bool IsCurrent = false)
{
    public string Key => this.IsCurrent ? "current" : $"{this.Latitude:F3},{this.Longitude:F3}";
}

public sealed record HourForecast(DateTime Time, double Temperature, int Code, bool IsDay, int? PrecipitationChance);

public sealed record DayForecast(DateTime Date, double Low, double High, int Code, int? PrecipitationChance);

public sealed record WeatherReport(
    WeatherPlace Place,
    double Temperature,
    int Code,
    bool IsDay,
    double High,
    double Low,
    IReadOnlyList<HourForecast> Hours,
    IReadOnlyList<DayForecast> Days,
    DateTime FetchedUtc
);

/// <summary>
/// Forecasts for where you are and any places you add, from Open-Meteo (free, no API key).
/// </summary>
/// <remarks>
/// "Where you are" comes from CoreLocation, asked once per refresh and named with the system's reverse
/// geocoder. Temperatures follow the region's units: Fahrenheit where the region isn't metric.
/// </remarks>
public sealed class WeatherService
{
    static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(20);

    readonly AppSettings settings;
    readonly TimeProvider time;
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    readonly Dictionary<string, WeatherReport> reports = new();
    readonly ILocationProvider location;
    WeatherPlace? current;
    bool refreshing;
    bool refreshQueued;
    bool queuedForce;
    int unitVersion;

    public WeatherService(AppSettings settings, TimeProvider time, ILocationProvider location)
    {
        this.settings = settings;
        this.time = time;
        this.location = location;
        this.location.AccessChanged += (_, _) =>
        {
            if (this.IsLocationAuthorized && this.settings.WeatherUseCurrentLocation)
                _ = this.RefreshAsync(force: true);
        };
    }

    /// <summary>Fahrenheit when chosen in Settings, or on Automatic where the region isn't metric.</summary>
    public bool UsesFahrenheit => this.settings.TemperatureUnit switch
    {
        "f" => true,
        "c" => false,
        _ => !RegionInfo.CurrentRegion.IsMetric
    };

    /// <summary>
    /// Switches units and refetches. Open-Meteo converts server-side, so the cached reports (in the old
    /// unit) are dropped rather than converted.
    /// </summary>
    public void SetUnit(string unit)
    {
        this.settings.TemperatureUnit = unit;
        this.unitVersion++;
        this.reports.Clear();
        this.Changed?.Invoke(this, EventArgs.Empty);
        _ = this.RefreshAsync(force: true);
    }

    /// <summary>Flips between °C and °F (from whichever is showing now).</summary>
    public void ToggleUnit() => this.SetUnit(this.UsesFahrenheit ? "c" : "f");

    public bool IsLocationAuthorized => this.location.Access == LocationAccess.Granted;

    public bool IsLocationDenied => this.location.Access == LocationAccess.Denied;

    /// <summary>The platform can't locate at all: no "where I am", only places added by hand.</summary>
    public bool IsLocationUnavailable => this.location.Access == LocationAccess.Unavailable;

    /// <summary>Where you are (when allowed and turned on), then the places you added.</summary>
    public IReadOnlyList<WeatherPlace> Places
    {
        get
        {
            var places = new List<WeatherPlace>();
            if (this.settings.WeatherUseCurrentLocation && this.current != null)
                places.Add(this.current);
            places.AddRange(this.settings.WeatherPlaces);
            return places;
        }
    }

    public WeatherPlace? SelectedPlace
    {
        get
        {
            var places = this.Places;
            if (places.Count == 0)
                return null;
            return places.FirstOrDefault(p => p.Key == this.settings.WeatherSelected) ?? places[0];
        }
    }

    public WeatherReport? Selected => this.SelectedPlace is { } p && this.reports.TryGetValue(p.Key, out var r) ? r : null;

    /// <summary>A report arrived or the selection moved.</summary>
    public event EventHandler? Changed;

    public void Select(int delta)
    {
        var places = this.Places;
        if (places.Count < 2 || this.SelectedPlace is not { } selected)
            return;

        var i = places.ToList().FindIndex(p => p.Key == selected.Key);
        var next = places[((i + delta) % places.Count + places.Count) % places.Count];
        this.settings.WeatherSelected = next.Key;
        this.Changed?.Invoke(this, EventArgs.Empty);
        if (!this.reports.ContainsKey(next.Key))
            _ = this.RefreshAsync(force: true);
    }

    public void RequestLocationAccess() => this.location.RequestAccess();

    public void Add(WeatherPlace place)
    {
        var places = this.settings.WeatherPlaces.ToList();
        if (places.Any(p => p.Key == place.Key))
            return;

        places.Add(place with { IsCurrent = false });
        this.settings.WeatherPlaces = places;
        this.settings.WeatherSelected = place.Key;
        _ = this.RefreshAsync(force: true);
    }

    public void Remove(WeatherPlace place)
    {
        this.settings.WeatherPlaces = this.settings.WeatherPlaces.Where(p => p.Key != place.Key).ToList();
        this.reports.Remove(place.Key);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cities matching <paramref name="query"/>, from Open-Meteo's geocoder.</summary>
    public async Task<IReadOnlyList<WeatherPlace>> SearchAsync(string query)
    {
        if (String.IsNullOrWhiteSpace(query))
            return [];

        var url = $"https://geocoding-api.open-meteo.com/v1/search?count=6&language=en&format=json&name={Uri.EscapeDataString(query.Trim())}";
        try
        {
            using var doc = JsonDocument.Parse(await this.http.GetStringAsync(url).ConfigureAwait(true));
            if (!doc.RootElement.TryGetProperty("results", out var results))
                return [];

            return results.EnumerateArray()
                .Select(r =>
                {
                    var name = r.GetProperty("name").GetString() ?? "?";
                    var admin = r.TryGetProperty("admin1", out var a) ? a.GetString() : null;
                    var country = r.TryGetProperty("country", out var c) ? c.GetString() : null;
                    var label = String.Join(", ", new[] { name, admin, country }.Where(s => !String.IsNullOrWhiteSpace(s)).Distinct());
                    return new WeatherPlace(label, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble());
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Refreshes the selected place (and finds where you are) when its report is stale.</summary>
    /// <remarks>
    /// One refresh at a time. Asking while one is running queues another for when it finishes (forced, if
    /// any of the asks was), so a new place, a new selection or a new unit is never just dropped.
    /// </remarks>
    public async Task RefreshAsync(bool force = false)
    {
        if (this.refreshing)
        {
            this.refreshQueued = true;
            this.queuedForce |= force;
            return;
        }

        this.refreshing = true;
        try
        {
            while (true)
            {
                await this.RefreshOnceAsync(force).ConfigureAwait(true);
                if (!this.refreshQueued)
                    break;

                force = this.queuedForce;
                this.refreshQueued = false;
                this.queuedForce = false;
            }
        }
        finally
        {
            this.refreshing = false;
        }
    }

    async Task RefreshOnceAsync(bool force)
    {
        if (this.settings.WeatherUseCurrentLocation)
        {
            // First run: ask. The answer arrives through AuthorizationChanged, which refreshes again.
            if (this.location.Access == LocationAccess.NotDetermined)
                this.RequestLocationAccess();
            else if (this.IsLocationAuthorized)
                await this.UpdateCurrentPlaceAsync().ConfigureAwait(true);
        }

        if (this.SelectedPlace is not { } place)
            return;

        if (!force && this.reports.TryGetValue(place.Key, out var existing) && this.time.GetUtcNow().UtcDateTime - existing.FetchedUtc < StaleAfter)
            return;

        var unitVersion = this.unitVersion;
        if (await this.FetchAsync(place).ConfigureAwait(true) is not { } report)
            return;

        // While it was fetching, the unit may have switched (the report is in the old one) or the place been removed.
        if (unitVersion != this.unitVersion || this.Places.All(p => p.Key != place.Key))
            return;

        this.reports[place.Key] = report;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    async Task UpdateCurrentPlaceAsync()
    {
        if (await this.location.GetCurrentAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true) is not { } fix)
            return;

        // A fix the platform couldn't name keeps the last name.
        var name = fix.Name ?? this.current?.Name ?? "My Location";
        this.current = new WeatherPlace(name, fix.Latitude, fix.Longitude, IsCurrent: true);
    }

    async Task<WeatherReport?> FetchAsync(WeatherPlace place)
    {
        var ci = CultureInfo.InvariantCulture;
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={place.Latitude.ToString(ci)}&longitude={place.Longitude.ToString(ci)}" +
                  "&current=temperature_2m,weather_code,is_day" +
                  "&hourly=temperature_2m,weather_code,is_day,precipitation_probability" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                  "&timezone=auto&forecast_days=7" +
                  (this.UsesFahrenheit ? "&temperature_unit=fahrenheit" : "");
        try
        {
            using var doc = JsonDocument.Parse(await this.http.GetStringAsync(url).ConfigureAwait(true));
            var root = doc.RootElement;
            var offset = TimeSpan.FromSeconds(root.GetProperty("utc_offset_seconds").GetInt32());
            var localNow = this.time.GetUtcNow().UtcDateTime + offset;

            var cur = root.GetProperty("current");
            var hourly = root.GetProperty("hourly");
            var daily = root.GetProperty("daily");

            var times = hourly.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, ci)).ToList();
            var hours = new List<HourForecast>();
            for (var i = 0; i < times.Count && hours.Count < 24; i++)
            {
                if (times[i] < localNow.AddMinutes(-59))
                    continue;
                hours.Add(new HourForecast(
                    times[i],
                    Number(hourly, "temperature_2m", i),
                    (int)Number(hourly, "weather_code", i),
                    Number(hourly, "is_day", i) > 0,
                    NullableInt(hourly, "precipitation_probability", i)
                ));
            }

            var dates = daily.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, ci)).ToList();
            var days = dates.Select((d, i) => new DayForecast(
                d,
                Number(daily, "temperature_2m_min", i),
                Number(daily, "temperature_2m_max", i),
                (int)Number(daily, "weather_code", i),
                NullableInt(daily, "precipitation_probability_max", i)
            )).ToList();

            return new WeatherReport(
                place,
                cur.GetProperty("temperature_2m").GetDouble(),
                cur.GetProperty("weather_code").GetInt32(),
                cur.GetProperty("is_day").GetInt32() > 0,
                days.Count > 0 ? days[0].High : 0,
                days.Count > 0 ? days[0].Low : 0,
                hours,
                days,
                this.time.GetUtcNow().UtcDateTime
            );
        }
        catch
        {
            return null;
        }
    }

    static double Number(JsonElement series, string name, int i)
        => series.GetProperty(name)[i] is { ValueKind: JsonValueKind.Number } n ? n.GetDouble() : 0;

    static int? NullableInt(JsonElement series, string name, int i)
        => series.TryGetProperty(name, out var s) && s[i] is { ValueKind: JsonValueKind.Number } n ? (int)Math.Round(n.GetDouble()) : null;

    // ---------------------------------------------------------------------------------------------
    // WMO weather codes
    // ---------------------------------------------------------------------------------------------

    /// <summary>The SF Symbol for a WMO code, drawn multicolour - the same set the iOS weather widget uses.</summary>
    public static string Symbol(int code, bool isDay = true) => code switch
    {
        0 => isDay ? "sun.max.fill" : "moon.stars.fill",
        1 => isDay ? "sun.max.fill" : "moon.fill",
        2 => isDay ? "cloud.sun.fill" : "cloud.moon.fill",
        3 => "cloud.fill",
        45 or 48 => "cloud.fog.fill",
        51 or 53 or 55 => "cloud.drizzle.fill",
        56 or 57 or 66 or 67 => "cloud.sleet.fill",
        61 or 63 or 80 or 81 => isDay ? "cloud.sun.rain.fill" : "cloud.moon.rain.fill",
        65 or 82 => "cloud.heavyrain.fill",
        71 or 73 or 75 or 77 or 85 or 86 => "cloud.snow.fill",
        95 => "cloud.bolt.rain.fill",
        96 or 99 => "cloud.hail.fill",
        _ => "thermometer.medium"
    };

    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly Clear",
        2 => "Partly Cloudy",
        3 => "Cloudy",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        56 or 57 => "Freezing Drizzle",
        61 or 63 => "Rain",
        65 => "Heavy Rain",
        66 or 67 => "Freezing Rain",
        71 or 73 => "Snow",
        75 => "Heavy Snow",
        77 => "Snow Grains",
        80 or 81 => "Showers",
        82 => "Heavy Showers",
        85 or 86 => "Snow Showers",
        95 => "Thunderstorms",
        96 or 99 => "Thunderstorms, Hail",
        _ => "—"
    };

    public static string Degrees(double t) => $"{Math.Round(t):0}°";
}
