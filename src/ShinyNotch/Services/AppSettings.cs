using Foundation;

namespace ShinyNotch.Services;

/// <summary>Persisted preferences, straight on NSUserDefaults.</summary>
public sealed class AppSettings
{
    static NSUserDefaults Defaults => NSUserDefaults.StandardUserDefaults;

    /// <summary>A setting the user controls changed.</summary>
    public event EventHandler? Changed;

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
            this.Changed?.Invoke(this, EventArgs.Empty);
    }

    const string DefaultScript =
        "Welcome to Shiny Notch.\n\n" +
        "This is the teleprompter. Your script scrolls right under the camera, so your eyes stay on " +
        "the lens while you read.\n\n" +
        "Open Settings from the menu bar icon to paste in your own script, then press play. " +
        "Use the speed and size buttons to tune it while you talk.\n\n" +
        "Hover away and the notch stays open while the script is running. Press pause to stop.";
}
