namespace ShinyNotch.Windows;

/// <summary>
/// The SF Symbol names the app draws with, and the nearest Segoe Fluent Icons / MDL2 glyph for each.
/// A name missing here draws nothing, so a new symbol in the views needs a line here too.
/// </summary>
static class SymbolGlyphs
{
    const string Play = "";
    const string Pause = "";
    const string Previous = "";
    const string Next = "";
    const string Sun = "";
    const string Moon = "";
    const string Cloud = "";
    const string Lightning = "";
    const string Download = "";
    const string Upload = "";
    const string Gauge = "";
    const string Add = "";
    const string Remove = "";

    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        // Media
        ["music.note"] = "",
        ["play.fill"] = Play,
        ["pause.fill"] = Pause,
        ["backward.fill"] = Previous,
        ["backward.end.fill"] = Previous,
        ["forward.fill"] = Next,
        ["speaker.slash.fill"] = "",
        ["speaker.wave.1.fill"] = "",
        ["speaker.wave.2.fill"] = "",
        ["speaker.wave.3.fill"] = "",

        // Display
        ["sun.max.fill"] = Sun,
        ["sun.min.fill"] = "",

        // Tabs and panel
        ["cloud.sun.fill"] = Cloud,
        ["tray.and.arrow.down.fill"] = Download,
        ["cpu"] = Gauge,
        ["camera.viewfinder"] = "",
        ["calendar"] = "",
        ["calendar.badge.plus"] = "",
        ["timer"] = "",
        ["text.alignleft"] = "",
        ["pin"] = "",
        ["pin.fill"] = "",
        ["gearshape.fill"] = "",

        // Weather
        ["cloud.fill"] = Cloud,
        ["cloud.moon.fill"] = Cloud,
        ["cloud.fog.fill"] = Cloud,
        ["cloud.drizzle.fill"] = Cloud,
        ["cloud.rain.fill"] = Cloud,
        ["cloud.heavyrain.fill"] = Cloud,
        ["cloud.sun.rain.fill"] = Cloud,
        ["cloud.moon.rain.fill"] = Cloud,
        ["cloud.sleet.fill"] = Cloud,
        ["cloud.snow.fill"] = Cloud,
        ["cloud.hail.fill"] = Cloud,
        ["cloud.bolt.rain.fill"] = Lightning,
        ["moon.fill"] = Moon,
        ["moon.stars.fill"] = Moon,
        ["thermometer.medium"] = Gauge,
        ["location.fill"] = "",
        ["mappin.circle.fill"] = "",

        // Stats
        ["arrow.down.circle.fill"] = Download,
        ["arrow.up.circle.fill"] = Upload,
        ["arrow.down.doc.fill"] = Download,
        ["arrow.up.doc.fill"] = Upload,
        ["speedometer"] = Gauge,

        // Tray
        ["doc.on.doc"] = "",
        ["doc.on.doc.fill"] = "",
        ["folder.fill"] = "",
        ["trash.fill"] = "",
        ["trash"] = "\uE74D",
        ["tray.and.arrow.down"] = "\uE896",

        // Voice notes and the mirror
        ["waveform"] = "\uE720",
        ["mic"] = "\uE720",
        ["mic.fill"] = "\uE720",
        ["stop.fill"] = "\uE71A",
        ["person.crop.square.fill"] = "\uE77B",
        ["camera.fill"] = "\uE722",
        ["arrow.triangle.2.circlepath.camera"] = "\uE89E",
        ["magnifyingglass"] = "",
        ["square.and.arrow.up"] = "",

        // Teleprompter
        ["tortoise.fill"] = Remove,
        ["hare.fill"] = Add,
        ["textformat.size"] = "",
        ["textformat.size.larger"] = "",
        ["textformat.size.smaller"] = "",
        ["square.and.pencil"] = "",

        // Settings and the rest
        ["arrow.left.and.right"] = "",
        ["arrow.left.and.right.square"] = "",
        ["arrow.left.square"] = "",
        ["arrow.right.square"] = "",
        ["arrow.up.and.down.square"] = "",
        ["chevron.up"] = "",
        ["chevron.down"] = "",
        ["chevron.left"] = "",
        ["chevron.right"] = "",
        ["checkmark.circle.fill"] = "",
        ["info.circle.fill"] = "",
        ["plus"] = Add,
        ["plus.circle.fill"] = Add,
        ["xmark"] = "",
        ["power"] = "",
        ["sparkles"] = "",
        ["shippingbox.fill"] = ""
    };
}
