namespace ShinyNotch.Linux;

/// <summary>
/// The app's SF Symbol names as freedesktop icon names, each with fallbacks for themes that lack the
/// first. Symbolic icons are tinted to the colour asked for; a weather symbol asked for in colour tries
/// the theme's full-colour icon first.
/// </summary>
static class SymbolNames
{
    static readonly Dictionary<string, string[]> Symbolic = new()
    {
        ["arrow.down.circle.fill"] = ["go-down-symbolic"],
        ["arrow.up.circle.fill"] = ["go-up-symbolic"],
        ["arrow.down.doc.fill"] = ["document-save-symbolic", "go-down-symbolic"],
        ["arrow.up.doc.fill"] = ["document-send-symbolic", "go-up-symbolic"],
        ["arrow.left.and.right"] = ["object-flip-horizontal-symbolic", "view-dual-symbolic"],
        ["arrow.left.and.right.square"] = ["object-flip-horizontal-symbolic", "view-dual-symbolic"],
        ["arrow.up.and.down.square"] = ["object-flip-vertical-symbolic"],
        ["arrow.left.square"] = ["go-previous-symbolic"],
        ["arrow.right.square"] = ["go-next-symbolic"],
        ["backward.end.fill"] = ["media-skip-backward-symbolic"],
        ["backward.fill"] = ["media-skip-backward-symbolic"],
        ["forward.fill"] = ["media-skip-forward-symbolic"],
        ["play.fill"] = ["media-playback-start-symbolic"],
        ["pause.fill"] = ["media-playback-pause-symbolic"],
        ["hare.fill"] = ["media-seek-forward-symbolic"],
        ["tortoise.fill"] = ["media-seek-backward-symbolic"],
        ["calendar"] = ["x-office-calendar-symbolic", "office-calendar-symbolic"],
        ["calendar.badge.plus"] = ["appointment-new-symbolic", "x-office-calendar-symbolic"],
        ["camera.viewfinder"] = ["camera-photo-symbolic"],
        ["checkmark.circle.fill"] = ["emblem-ok-symbolic", "object-select-symbolic"],
        ["chevron.down"] = ["pan-down-symbolic", "go-down-symbolic"],
        ["chevron.up"] = ["pan-up-symbolic", "go-up-symbolic"],
        ["chevron.left"] = ["pan-start-symbolic", "go-previous-symbolic"],
        ["chevron.right"] = ["pan-end-symbolic", "go-next-symbolic"],
        ["cpu"] = ["utilities-system-monitor-symbolic", "computer-symbolic"],
        ["doc.on.doc"] = ["edit-copy-symbolic"],
        ["doc.on.doc.fill"] = ["edit-copy-symbolic"],
        ["folder.fill"] = ["folder-symbolic"],
        ["gearshape"] = ["emblem-system-symbolic", "preferences-system-symbolic"],
        ["gearshape.fill"] = ["emblem-system-symbolic", "preferences-system-symbolic"],
        ["info.circle.fill"] = ["dialog-information-symbolic"],
        ["location.fill"] = ["find-location-symbolic", "mark-location-symbolic"],
        ["mappin.circle.fill"] = ["mark-location-symbolic", "find-location-symbolic"],
        ["magnifyingglass"] = ["system-search-symbolic", "edit-find-symbolic"],
        ["music.note"] = ["audio-x-generic-symbolic"],
        ["pin"] = ["view-pin-symbolic"],
        ["pin.fill"] = ["view-pin-symbolic"],
        ["plus"] = ["list-add-symbolic"],
        ["plus.circle.fill"] = ["list-add-symbolic"],
        ["power"] = ["system-shutdown-symbolic"],
        ["shippingbox.fill"] = ["package-x-generic-symbolic"],
        ["sparkles"] = ["starred-symbolic"],
        ["speedometer"] = ["power-profile-performance-symbolic", "preferences-system-time-symbolic"],
        ["speaker.slash.fill"] = ["audio-volume-muted-symbolic"],
        ["speaker.wave.1.fill"] = ["audio-volume-low-symbolic"],
        ["speaker.wave.2.fill"] = ["audio-volume-medium-symbolic"],
        ["speaker.wave.3.fill"] = ["audio-volume-high-symbolic"],
        ["square.and.pencil"] = ["document-edit-symbolic"],
        // The brightness slider's sun. The weather's (asked for in colour) is in Weather below.
        ["sun.max.fill"] = ["display-brightness-symbolic", "weather-clear-symbolic"],
        ["sun.min.fill"] = ["display-brightness-symbolic", "weather-clear-symbolic"],
        ["text.alignleft"] = ["format-justify-left-symbolic"],
        ["textformat.size"] = ["preferences-desktop-font-symbolic", "font-x-generic-symbolic"],
        ["textformat.size.larger"] = ["zoom-in-symbolic"],
        ["textformat.size.smaller"] = ["zoom-out-symbolic"],
        ["timer"] = ["alarm-symbolic", "preferences-system-time-symbolic"],
        ["trash.fill"] = ["user-trash-symbolic"],
        ["tray.and.arrow.down.fill"] = ["folder-download-symbolic", "document-save-symbolic"],
        ["xmark"] = ["window-close-symbolic"],
        ["photo"] = ["image-x-generic-symbolic"],
        ["thermometer.medium"] = ["temperature-symbolic", "weather-severe-alert-symbolic"]
    };

    /// <summary>The weather codes' symbols, by their freedesktop weather icon (the colour and the symbolic one).</summary>
    static readonly Dictionary<string, string> Weather = new()
    {
        ["sun.max.fill"] = "weather-clear",
        ["moon.fill"] = "weather-clear-night",
        ["moon.stars.fill"] = "weather-clear-night",
        ["cloud.sun.fill"] = "weather-few-clouds",
        ["cloud.moon.fill"] = "weather-few-clouds-night",
        ["cloud.fill"] = "weather-overcast",
        ["cloud.fog.fill"] = "weather-fog",
        ["cloud.drizzle.fill"] = "weather-showers-scattered",
        ["cloud.sun.rain.fill"] = "weather-showers-scattered",
        ["cloud.moon.rain.fill"] = "weather-showers-scattered",
        ["cloud.heavyrain.fill"] = "weather-showers",
        ["cloud.sleet.fill"] = "weather-freezing-rain",
        ["cloud.snow.fill"] = "weather-snow",
        ["cloud.bolt.rain.fill"] = "weather-storm",
        ["cloud.hail.fill"] = "weather-storm"
    };

    /// <summary>Candidate icon names, best first; a name ending in -symbolic is to be tinted.</summary>
    public static IEnumerable<string> For(string symbol, bool multicolor)
    {
        if (Weather.TryGetValue(symbol, out var weather) && (multicolor || !Symbolic.ContainsKey(symbol)))
        {
            if (multicolor)
                yield return weather;
            yield return weather + "-symbolic";
            // Sleet and night variants are missing from some themes.
            if (weather == "weather-freezing-rain")
                yield return "weather-showers-symbolic";
            if (weather.EndsWith("-night", StringComparison.Ordinal))
                yield return weather[..^"-night".Length] + "-symbolic";
        }

        if (Symbolic.TryGetValue(symbol, out var names))
        {
            foreach (var name in names)
                yield return name;
        }
    }
}
