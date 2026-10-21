using Foundation;

namespace ShinyNotch.Services;

/// <summary>Persisted preferences, straight on NSUserDefaults.</summary>
public sealed class AppSettings
{
    static NSUserDefaults Defaults => NSUserDefaults.StandardUserDefaults;

    /// <summary>A setting the user controls changed. The argument is the setting's name.</summary>
    public event EventHandler<string>? Changed;

    public string TeleprompterScript
    {
        get => Defaults.StringForKey(nameof(this.TeleprompterScript)) ?? DefaultScript;
        set => this.Set(nameof(this.TeleprompterScript), new NSString(value));
    }

    /// <summary>Points scrolled per second.</summary>
    public double TeleprompterSpeed
    {
        get => this.Double(nameof(this.TeleprompterSpeed), 28);
        set => this.Set(nameof(this.TeleprompterSpeed), NSNumber.FromDouble(Math.Clamp(value, 5, 200)));
    }

    public double TeleprompterFontSize
    {
        get => this.Double(nameof(this.TeleprompterFontSize), 20);
        set => this.Set(nameof(this.TeleprompterFontSize), NSNumber.FromDouble(Math.Clamp(value, 12, 48)));
    }

    public bool UseHardwareNotch
    {
        get => this.Bool(nameof(this.UseHardwareNotch), true);
        set => this.Set(nameof(this.UseHardwareNotch), NSNumber.FromBoolean(value));
    }

    public const double MaxNotchWingWidth = 160;
    public const double DefaultNotchWingWidth = 84;
    public const double MaxNotchExtraHeight = 24;

    /// <summary>
    /// How far the collapsed notch reaches out left of the camera, in points; 0 is the camera's own edge.
    /// A wish, not a promise: a wing never goes narrower than what it is showing needs.
    /// </summary>
    public double NotchLeftWidth
    {
        get => this.Double(nameof(this.NotchLeftWidth), DefaultNotchWingWidth);
        set => this.Set(nameof(this.NotchLeftWidth), NSNumber.FromDouble(Math.Clamp(Math.Round(value), 0, MaxNotchWingWidth)));
    }

    /// <summary>As <see cref="NotchLeftWidth"/>, right of the camera.</summary>
    public double NotchRightWidth
    {
        get => this.Double(nameof(this.NotchRightWidth), DefaultNotchWingWidth);
        set => this.Set(nameof(this.NotchRightWidth), NSNumber.FromDouble(Math.Clamp(Math.Round(value), 0, MaxNotchWingWidth)));
    }

    /// <summary>
    /// Both wings the same width, so the camera stays centred: one slider in Settings, and the wider of
    /// the two wings' content sets both.
    /// </summary>
    public bool NotchWidthsLocked
    {
        get => this.Bool(nameof(this.NotchWidthsLocked), true);
        set => this.Set(nameof(this.NotchWidthsLocked), NSNumber.FromBoolean(value));
    }

    /// <summary>Points the collapsed notch hangs below the camera. At 0 it is exactly the camera's height.</summary>
    public double NotchExtraHeight
    {
        get => this.Double(nameof(this.NotchExtraHeight), 0);
        set => this.Set(nameof(this.NotchExtraHeight), NSNumber.FromDouble(Math.Clamp(Math.Round(value), 0, MaxNotchExtraHeight)));
    }

    public bool ShowVolumeHud
    {
        get => this.Bool(nameof(this.ShowVolumeHud), true);
        set => this.Set(nameof(this.ShowVolumeHud), NSNumber.FromBoolean(value));
    }

    public bool ShowBrightnessHud
    {
        get => this.Bool(nameof(this.ShowBrightnessHud), true);
        set => this.Set(nameof(this.ShowBrightnessHud), NSNumber.FromBoolean(value));
    }

    /// <summary>Paths in the file tray, in the order they were dropped.</summary>
    public IReadOnlyList<string> TrayFiles
    {
        get => Defaults.StringArrayForKey(nameof(this.TrayFiles)) ?? [];
        set => this.Set(nameof(this.TrayFiles), NSArray.FromStrings(value.ToArray()), notify: false);
    }

    /// <summary>"auto" (the region's units), "c" or "f".</summary>
    public string TemperatureUnit
    {
        get => Defaults.StringForKey(nameof(this.TemperatureUnit)) ?? "auto";
        set => this.Set(nameof(this.TemperatureUnit), new NSString(value));
    }

    public bool WeatherUseCurrentLocation
    {
        get => this.Bool(nameof(this.WeatherUseCurrentLocation), true);
        set => this.Set(nameof(this.WeatherUseCurrentLocation), NSNumber.FromBoolean(value));
    }

    /// <summary>Places added in Settings. Stored as "name|latitude|longitude" strings.</summary>
    public IReadOnlyList<WeatherPlace> WeatherPlaces
    {
        get => (Defaults.StringArrayForKey(nameof(this.WeatherPlaces)) ?? [])
            .Select(s => s.Split('|'))
            .Where(p => p.Length == 3)
            .Select(p => new WeatherPlace(p[0], System.Double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), System.Double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        set => this.Set(
            nameof(this.WeatherPlaces),
            NSArray.FromStrings(value.Select(p => String.Join('|', p.Name.Replace("|", " "), p.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray())
        );
    }

    /// <summary>The key of the place the Weather tab shows.</summary>
    public string? WeatherSelected
    {
        get => Defaults.StringForKey(nameof(this.WeatherSelected));
        set => this.Set(nameof(this.WeatherSelected), new NSString(value ?? ""), notify: false);
    }

    public string LastTab
    {
        get => Defaults.StringForKey(nameof(this.LastTab)) ?? "Media";
        set => this.Set(nameof(this.LastTab), new NSString(value), notify: false);
    }

    double Double(string key, double fallback)
        => Defaults[key] is NSNumber n ? n.DoubleValue : fallback;

    bool Bool(string key, bool fallback)
        => Defaults[key] is NSNumber n ? n.BoolValue : fallback;

    /// <param name="notify">
    /// False for bookkeeping the app writes itself (the last tab, the tray list), which nothing needs to
    /// react to — raising Changed for those would rebuild the notch for no reason.
    /// </param>
    void Set(string key, NSObject value, bool notify = true)
    {
        Defaults.SetValueForKey(value, new NSString(key));
        if (notify)
            this.Changed?.Invoke(this, key);
    }

    const string DefaultScript =
        "Welcome to Shiny Notch.\n\n" +
        "This is the teleprompter. Your script scrolls right under the camera, so your eyes stay on " +
        "the lens while you read.\n\n" +
        "Open Settings from the menu bar icon to paste in your own script, then press play. " +
        "Use the speed and size buttons to tune it while you talk.\n\n" +
        "Hover away and the notch stays open while the script is running. Press pause to stop.";
}
