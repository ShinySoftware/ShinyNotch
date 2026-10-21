namespace ShinyNotch.Platform;

/// <summary>The colour the Now Playing views tint with, picked out of a track's artwork.</summary>
public static class ArtworkAccent
{
    /// <summary>The pixels the artwork is shrunk to, a side, before averaging.</summary>
    public const int SampleSize = 12;

    /// <summary>
    /// The artwork's colour: shrunk to a few pixels and averaged, weighted towards the saturated ones so a
    /// grey border or a dark background doesn't wash it out, then lifted to read on the black notch.
    /// </summary>
    public static Color? From(IPlatformUi ui, byte[] artwork)
    {
        try
        {
            return ui.Pixels(artwork, SampleSize) is { } pixels ? FromPixels(pixels) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <param name="rgba">RGBA, 8 bits a channel, in any order of pixels.</param>
    public static Color? FromPixels(byte[] rgba)
    {
        double r = 0, g = 0, b = 0, total = 0;
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            double pr = rgba[i] / 255d, pg = rgba[i + 1] / 255d, pb = rgba[i + 2] / 255d;
            var max = Math.Max(pr, Math.Max(pg, pb));
            var min = Math.Min(pr, Math.Min(pg, pb));
            var saturation = max == 0 ? 0 : (max - min) / max;
            var weight = saturation * saturation * max + 0.02;
            r += pr * weight;
            g += pg * weight;
            b += pb * weight;
            total += weight;
        }
        if (total == 0)
            return null;

        Color.FromRgb(r / total, g / total, b / total).ToHsl(out var h, out var s, out var l);
        return Color.FromHsla(h, Math.Min(1, s * 1.15), Math.Clamp(l, 0.6, 0.75));
    }
}
