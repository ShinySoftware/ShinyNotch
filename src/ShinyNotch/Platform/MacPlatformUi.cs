using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Foundation;
using ShinyNotch.Platform;
using ShinyNotch.Views;

namespace ShinyNotch.MacOS;

/// <summary>SF Symbols, AppKit's text metrics, tracking-area hover and Core Graphics pixels.</summary>
sealed class MacPlatformUi : IPlatformUi
{
    /// <summary>An SF Symbol drawn into a PNG: no icon assets ship with the app.</summary>
    public ImageSource? Symbol(string name, double size, Color ink, bool multicolor, bool bold)
        => RenderSymbol(name, size, ink, multicolor, bold) is { } png ? ImageSource.FromStream(() => new MemoryStream(png)) : null;

    public double TextWidth(string text, double fontSize, bool bold)
    {
        var font = bold ? NSFont.BoldSystemFontOfSize((nfloat)fontSize) : NSFont.SystemFontOfSize((nfloat)fontSize);
        using var measured = new NSAttributedString(text, new NSStringAttributes { Font = font });
        return measured.Size.Width;
    }

    /// <summary>
    /// A tracking area of our own on the native view, not a PointerGestureRecognizer: the AppKit head only
    /// forwards pointer events from its container views (a Border's view isn't one), and only in the key
    /// window, which the notch never is (dotnet/maui-labs#744).
    /// </summary>
    public void TrackHover(View view, Action<bool> changed)
    {
        // Captured by the view's HandlerChanged handler, so it lives as long as the view: a tracking area
        // doesn't retain its owner.
        var tracker = new HoverTracker(changed);
        view.HandlerChanged += (_, _) =>
        {
            if (view.Handler?.PlatformView is NSView native)
                native.AddTrackingArea(new NSTrackingArea(
                    CGRect.Empty,
                    NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved |
                    NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.InVisibleRect,
                    tracker,
                    null));
        };
    }

    public byte[]? Pixels(byte[] encoded, int size)
    {
        using var data = NSData.FromArray(encoded);
        using var image = new NSImage(data);
        var rect = new CGRect(0, 0, image.Size.Width, image.Size.Height);
        using var cg = image.AsCGImage(ref rect, null, null);
        if (cg == null)
            return null;

        var pixels = new byte[size * size * 4];
        using var space = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(pixels, size, size, 8, size * 4, space, CGImageAlphaInfo.PremultipliedLast);
        context.InterpolationQuality = CGInterpolationQuality.Medium;
        context.DrawImage(new CGRect(0, 0, size, size), cg);
        return pixels;
    }

    /// <summary>The Finder icon.</summary>
    public byte[]? FileIcon(string path, int size)
    {
        // Disposed rather than left to the GC: a tray rebuild renders a dozen of these, and the
        // managed wrappers are too small for the GC to hurry over the native images behind them.
        using var icon = NSWorkspace.SharedWorkspace.IconForFile(path);
        icon.Size = new CGSize(size, size);
        var rect = new CGRect(0, 0, size, size);
        using var cg = icon.AsCGImage(ref rect, null, null);
        if (cg == null)
            return null;

        using var rep = new NSBitmapImageRep(cg);
        return rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)?.ToArray();
    }

    static byte[]? RenderSymbol(string name, double size, Color ink, bool multicolor, bool bold)
    {
        using var symbol = NSImage.GetSystemSymbol(name, null);
        if (symbol == null)
            return null;

        using var shape = NSImageSymbolConfiguration.Create((NFloat)size, bold ? NSFontWeight.Bold : NSFontWeight.Semibold);
        // Hierarchical rather than one flat colour: a two-layer symbol (an arrow in a circle) keeps its
        // inner shape, drawn at full strength over a fainter outer one.
        using var colour = multicolor
            ? NSImageSymbolConfiguration.Create()
            : NSImageSymbolConfiguration.Create(NSColor.FromSrgb((NFloat)ink.Red, (NFloat)ink.Green, (NFloat)ink.Blue, (NFloat)ink.Alpha));
        using var config = shape.Create(colour);
        using var image = symbol.GetImage(config);
        if (image == null)
            return null;

        // Twice the points, for Retina; the Image view shows it at the box size.
        const int scale = 2;
        var box = Ui.SymbolBox(size);
        var pixels = (nint)(box * scale);
        using var rep = new NSBitmapImageRep(IntPtr.Zero, pixels, pixels, 8, 4, true, false, NSColorSpace.DeviceRGB, 0, 0);
        rep.Size = new CGSize(box, box);

        var fit = Math.Min(1, box / Math.Max(image.Size.Width, image.Size.Height));
        var w = image.Size.Width * fit;
        var h = image.Size.Height * fit;

        NSGraphicsContext.GlobalSaveGraphicsState();
        try
        {
            NSGraphicsContext.CurrentContext = NSGraphicsContext.FromBitmap(rep);
            image.Draw(new CGRect((box - w) / 2, (box - h) / 2, w, h));
            NSGraphicsContext.CurrentContext?.FlushGraphics();
        }
        finally
        {
            NSGraphicsContext.GlobalRestoreGraphicsState();
        }

        using var data = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png);
        return data?.ToArray();
    }
}

/// <summary>
/// Receives a tracking area's enter and exit. Moves count as being over too: a view built under a pointer
/// that is already there gets no enter, which is every rebuild after a click.
/// </summary>
sealed class HoverTracker(Action<bool> changed) : NSResponder
{
    bool over;

    public override void MouseEntered(NSEvent theEvent) => this.Set(true);

    public override void MouseExited(NSEvent theEvent) => this.Set(false);

    public override void MouseMoved(NSEvent theEvent) => this.Set(true);

    void Set(bool value)
    {
        if (value == this.over)
            return;
        this.over = value;
        changed(value);
    }
}
