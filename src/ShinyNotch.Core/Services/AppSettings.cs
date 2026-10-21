using System.Globalization;
using Shiny;
using Shiny.Extensions.Stores;

namespace ShinyNotch.Services;

/// <summary>
/// Persisted preferences, on Shiny's default store: NSUserDefaults on macOS, a settings file elsewhere.
/// </summary>
[Singleton]
public sealed class AppSettings
{
    readonly IKeyValueStore store;

    public AppSettings(IKeyValueStore store) => this.store = store;

    /// <summary>A setting the user controls changed. The argument is the setting's name.</summary>
    public event EventHandler<string>? Changed;

    public string TeleprompterScript
    {
        get => this.Text(nameof(this.TeleprompterScript)) ?? DefaultScript;
        set => this.Set(nameof(this.TeleprompterScript), value);
    }

    /// <summary>Points scrolled per second.</summary>
    public double TeleprompterSpeed
    {
        get => this.Double(nameof(this.TeleprompterSpeed), 28);
        set => this.Set(nameof(this.TeleprompterSpeed), Math.Clamp(value, 5, 200));
    }

    public double TeleprompterFontSize
    {
        get => this.Double(nameof(this.TeleprompterFontSize), 20);
        set => this.Set(nameof(this.TeleprompterFontSize), Math.Clamp(value, 12, 48));
    }

    public bool UseHardwareNotch
    {
        get => this.Bool(nameof(this.UseHardwareNotch), true);
        set => this.Set(nameof(this.UseHardwareNotch), value);
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
        set => this.Set(nameof(this.NotchLeftWidth), Math.Clamp(Math.Round(value), 0, MaxNotchWingWidth));
    }

    /// <summary>As <see cref="NotchLeftWidth"/>, right of the camera.</summary>
    public double NotchRightWidth
    {
        get => this.Double(nameof(this.NotchRightWidth), DefaultNotchWingWidth);
        set => this.Set(nameof(this.NotchRightWidth), Math.Clamp(Math.Round(value), 0, MaxNotchWingWidth));
    }

    /// <summary>
    /// Both wings the same width, so the camera stays centred: one slider in Settings, and the wider of
    /// the two wings' content sets both.
    /// </summary>
    public bool NotchWidthsLocked
    {
        get => this.Bool(nameof(this.NotchWidthsLocked), true);
        set => this.Set(nameof(this.NotchWidthsLocked), value);
    }

    /// <summary>Points the collapsed notch hangs below the camera. At 0 it is exactly the camera's height.</summary>
    public double NotchExtraHeight
    {
        get => this.Double(nameof(this.NotchExtraHeight), 0);
        set => this.Set(nameof(this.NotchExtraHeight), Math.Clamp(Math.Round(value), 0, MaxNotchExtraHeight));
    }

    public bool ShowVolumeHud
    {
        get => this.Bool(nameof(this.ShowVolumeHud), true);
        set => this.Set(nameof(this.ShowVolumeHud), value);
    }

    public bool ShowBrightnessHud
    {
        get => this.Bool(nameof(this.ShowBrightnessHud), true);
        set => this.Set(nameof(this.ShowBrightnessHud), value);
    }

    /// <summary>Briefly open Now Playing when the song changes, unless the panel is already open.</summary>
    public bool ShowTrackChanges
    {
        get => this.Bool(nameof(this.ShowTrackChanges), true);
        set => this.Set(nameof(this.ShowTrackChanges), value);
    }

    /// <summary>
    /// Tabs turned off in Settings, by name. Everything else shows (if the platform has it), so a tab
    /// added in an update shows up on its own.
    /// </summary>
    public IReadOnlyList<string> HiddenTabs
    {
        // Before tabs could be hidden, only the macshot tab could be turned off, under its own switch.
        get => this.List(nameof(this.HiddenTabs)) ?? (this.Bool(LegacyShowMacShotTab, true) ? [] : ["Capture"]);
        set => this.Set(nameof(this.HiddenTabs), value.Distinct().ToArray());
    }

    const string LegacyShowMacShotTab = "ShowMacShotTab";

    /// <summary>The task list, as JSON (see <see cref="TasksService"/>).</summary>
    public string? Tasks
    {
        get => this.Text(nameof(this.Tasks));
        set => this.Set(nameof(this.Tasks), value ?? "", notify: false);
    }

    /// <summary>The microphone voice notes record from, by its device id; null is the system default.</summary>
    public string? VoiceNoteInput
    {
        get => this.Text(nameof(this.VoiceNoteInput)) is { Length: > 0 } id ? id : null;
        set => this.Set(nameof(this.VoiceNoteInput), value ?? "", notify: false);
    }

    /// <summary>The camera the mirror shows, by its id; null is the default camera.</summary>
    public string? MirrorCamera
    {
        get => this.Text(nameof(this.MirrorCamera)) is { Length: > 0 } id ? id : null;
        set => this.Set(nameof(this.MirrorCamera), value ?? "", notify: false);
    }

    /// <summary>The mirror's look, a <c>CameraFilter</c> name.</summary>
    public string MirrorFilter
    {
        get => this.Text(nameof(this.MirrorFilter)) ?? "None";
        set => this.Set(nameof(this.MirrorFilter), value, notify: false);
    }

    /// <summary>Paths in the file tray, in the order they were dropped.</summary>
    public IReadOnlyList<string> TrayFiles
    {
        get => this.List(nameof(this.TrayFiles)) ?? [];
        set => this.Set(nameof(this.TrayFiles), value.ToArray(), notify: false);
    }

    /// <summary>"auto" (the region's units), "c" or "f".</summary>
    public string TemperatureUnit
    {
        get => this.Text(nameof(this.TemperatureUnit)) ?? "auto";
        set => this.Set(nameof(this.TemperatureUnit), value);
    }

    public bool WeatherUseCurrentLocation
    {
        get => this.Bool(nameof(this.WeatherUseCurrentLocation), true);
        set => this.Set(nameof(this.WeatherUseCurrentLocation), value);
    }

    /// <summary>Places added in Settings. Stored as "name|latitude|longitude" strings.</summary>
    public IReadOnlyList<WeatherPlace> WeatherPlaces
    {
        get => (this.List(nameof(this.WeatherPlaces)) ?? [])
            .Select(s => s.Split('|'))
            .Where(p => p.Length == 3)
            .Select(p => new WeatherPlace(p[0], System.Double.Parse(p[1], CultureInfo.InvariantCulture), System.Double.Parse(p[2], CultureInfo.InvariantCulture)))
            .ToList();
        set => this.Set(
            nameof(this.WeatherPlaces),
            value.Select(p => String.Join('|', p.Name.Replace("|", " "), p.Latitude.ToString(CultureInfo.InvariantCulture), p.Longitude.ToString(CultureInfo.InvariantCulture))).ToArray()
        );
    }

    /// <summary>The key of the place the Weather tab shows.</summary>
    public string? WeatherSelected
    {
        get => this.Text(nameof(this.WeatherSelected));
        set => this.Set(nameof(this.WeatherSelected), value ?? "", notify: false);
    }

    /// <summary>Open at login has been set up on a first launch. It only happens once.</summary>
    public bool LoginItemSetUp
    {
        get => this.Bool(nameof(this.LoginItemSetUp), false);
        set => this.Set(nameof(this.LoginItemSetUp), value, notify: false);
    }

    public string LastTab
    {
        get => this.Text(nameof(this.LastTab)) ?? "Media";
        set => this.Set(nameof(this.LastTab), value, notify: false);
    }

    string? Text(string key) => this.store.Get<string?>(key, null);

    double Double(string key, double fallback) => this.store.Get(key, fallback);

    bool Bool(string key, bool fallback) => this.store.Get(key, fallback);

    /// <summary>A list, or null when unset - or unreadable, which is no worse than unset for a tray or a place list.</summary>
    string[]? List(string key)
    {
        try
        {
            return this.store.Get<string[]?>(key, null);
        }
        catch
        {
            return null;
        }
    }

    /// <param name="notify">
    /// False for bookkeeping the app writes itself (the last tab, the tray list), which nothing needs to
    /// react to — raising Changed for those would rebuild the notch for no reason.
    /// </param>
    void Set<T>(string key, T value, bool notify = true)
    {
        this.store.Set(key, value);
        if (notify)
            this.Changed?.Invoke(this, key);
    }

    const string DefaultScript =
        "Welcome to Shiny Notch.\n\n" +
        "This is the teleprompter. Your script scrolls right under the camera, so your eyes stay on " +
        "the lens while you read.\n\n" +
        "Open Settings (the ⚙ in the notch) to paste in your own script, then press play. " +
        "Use the speed and size buttons to tune it while you talk.\n\n" +
        "Hover away and the notch stays open while the script is running. Press pause to stop.";
}
