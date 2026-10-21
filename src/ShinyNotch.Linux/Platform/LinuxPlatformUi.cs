using System.Runtime.InteropServices;
using ShinyNotch.Platform;
using ShinyNotch.Views;

namespace ShinyNotch.Linux;

/// <summary>The GTK icon theme, Pango's metrics, GTK motion controllers and GdkPixbuf.</summary>
sealed class LinuxPlatformUi : IPlatformUi
{
    /// <summary>Twice the points, as on the Mac: the Image view shows it at the box size, sharp on HiDPI.</summary>
    const int Scale = 2;

    Gtk.Label? measurer;

    static Gtk.IconTheme? Theme => Gdk.Display.GetDefault() is { } display ? Gtk.IconTheme.GetForDisplay(display) : null;

    /// <summary>
    /// The theme's icon for the symbol (see <see cref="SymbolNames"/>), tinted when it's a symbolic one, and
    /// centred in the symbol box. No icon assets ship with the app, as on the Mac.
    /// </summary>
    public ImageSource? Symbol(string name, double size, Color ink, bool multicolor, bool bold)
    {
        if (Theme is not { } theme)
            return null;

        var box = (int)(Ui.SymbolBox(size) * Scale);
        // Freedesktop icons keep a margin inside their square that SF Symbols don't, so a little larger.
        var px = Math.Min(box, (int)Math.Round(size * 1.15 * Scale));
        foreach (var icon in SymbolNames.For(name, multicolor))
        {
            if (!theme.HasIcon(icon))
                continue;

            var paintable = theme.LookupIcon(icon, null, px, 1, Gtk.TextDirection.None, Gtk.IconLookupFlags.None);
            if (Load(paintable.GetFile(), px) is not { } pixbuf)
                continue;

            var (rgba, w, h) = Rgba(pixbuf);
            if (paintable.GetIsSymbolic() || icon.EndsWith("-symbolic", StringComparison.Ordinal))
                Tint(rgba, ink);

            var png = Png.Encode(Centre(rgba, w, h, box), box, box);
            return ImageSource.FromStream(() => new MemoryStream(png));
        }
        return null;
    }

    /// <summary>Laid out by Pango with GTK's own font, as a label would be, at the size and weight asked for.</summary>
    public double TextWidth(string text, double fontSize, bool bold)
    {
        this.measurer ??= Gtk.Label.New(null);
        var layout = this.measurer.CreatePangoLayout(text);
        var font = layout.GetContext().GetFontDescription()?.Copy() ?? Pango.FontDescription.FromString("Sans");
        font.SetAbsoluteSize(fontSize * Pango.Constants.SCALE);
        if (bold)
            font.SetWeight(Pango.Weight.Bold);
        layout.SetFontDescription(font);
        layout.GetPixelSize(out var width, out _);
        return width;
    }

    /// <summary>
    /// A motion controller of our own on the native widget. GTK reports enter and leave whatever window
    /// has focus, which the notch never does.
    /// </summary>
    public void TrackHover(View view, Action<bool> changed)
    {
        var over = false;
        void Set(bool value)
        {
            if (value == over)
                return;
            over = value;
            changed(value);
        }

        view.HandlerChanged += (_, _) =>
        {
            if (view.Handler?.PlatformView is not Gtk.Widget widget)
                return;

            var motion = Gtk.EventControllerMotion.New();
            motion.OnEnter += (_, _) => Set(true);
            // Moves count as being over too: a view built under a pointer that is already there gets no enter.
            motion.OnMotion += (_, _) => Set(true);
            motion.OnLeave += (_, _) => Set(false);
            widget.AddController(motion);
        };
    }

    public byte[]? Pixels(byte[] encoded, int size)
    {
        try
        {
            var loader = GdkPixbuf.PixbufLoader.New();
            loader.Write(encoded);
            loader.Close();
            if (loader.GetPixbuf()?.ScaleSimple(size, size, GdkPixbuf.InterpType.Bilinear) is not { } scaled)
                return null;
            return Rgba(scaled).Pixels;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The theme's icon for the file's content type, as the file manager shows it.</summary>
    public byte[]? FileIcon(string path, int size)
    {
        try
        {
            if (Theme is not { } theme)
                return null;

            var info = Gio.FileHelper.NewForPath(path).QueryInfo("standard::icon", Gio.FileQueryInfoFlags.None, null);
            if (info.GetIcon() is not { } icon)
                return null;

            var paintable = theme.LookupByGicon(icon, size, 1, Gtk.TextDirection.None, Gtk.IconLookupFlags.ForceRegular);
            if (Load(paintable.GetFile(), size) is not { } pixbuf)
                return null;

            var (rgba, w, h) = Rgba(pixbuf);
            return Png.Encode(Centre(rgba, w, h, size), size, size);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// An icon file at a size. Themes installed on disk give a path; GTK's built-in icons live in a
    /// GResource and only give a resource:// URI.
    /// </summary>
    static GdkPixbuf.Pixbuf? Load(Gio.File? file, int px)
    {
        try
        {
            if (file?.GetPath() is { } path)
                return GdkPixbuf.Pixbuf.NewFromFileAtScale(path, px, px, true);

            const string resource = "resource://";
            var uri = file?.GetUri();
            return uri != null && uri.StartsWith(resource, StringComparison.Ordinal)
                ? GdkPixbuf.Pixbuf.NewFromResourceAtScale(uri[resource.Length..], px, px, true)
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A pixbuf's pixels as tightly packed RGBA, whatever its row stride and channel count.</summary>
    static (byte[] Pixels, int Width, int Height) Rgba(GdkPixbuf.Pixbuf pixbuf)
    {
        int w = pixbuf.Width, h = pixbuf.Height, stride = pixbuf.Rowstride, channels = pixbuf.NChannels;
        var source = pixbuf.ReadPixels();
        var row = new byte[stride];
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            // The last row can be shorter than the stride.
            Marshal.Copy(source + y * stride, row, 0, Math.Min(stride, w * channels));
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                rgba[o] = row[x * channels];
                rgba[o + 1] = row[x * channels + 1];
                rgba[o + 2] = row[x * channels + 2];
                rgba[o + 3] = channels == 4 ? row[x * channels + 3] : (byte)255;
            }
        }
        return (rgba, w, h);
    }

    /// <summary>A symbolic icon is a shape in one grey: keep its alpha, paint it in the ink.</summary>
    static void Tint(byte[] rgba, Color ink)
    {
        byte r = (byte)(ink.Red * 255), g = (byte)(ink.Green * 255), b = (byte)(ink.Blue * 255);
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = (byte)(rgba[i + 3] * ink.Alpha);
        }
    }

    /// <summary>Centres the pixels in a transparent square, so symbols line up whatever their shape.</summary>
    static byte[] Centre(byte[] rgba, int w, int h, int side)
    {
        var canvas = new byte[side * side * 4];
        int left = Math.Max(0, (side - w) / 2), top = Math.Max(0, (side - h) / 2);
        for (var y = 0; y < Math.Min(h, side); y++)
            Array.Copy(rgba, y * w * 4, canvas, ((top + y) * side + left) * 4, Math.Min(w, side) * 4);
        return canvas;
    }
}
