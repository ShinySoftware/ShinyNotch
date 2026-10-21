using System.Runtime.InteropServices;
using AppKit;
using CoreGraphics;
using Microsoft.Maui.Controls.Shapes;

namespace ShinyNotch.Views;

/// <summary>
/// Small building blocks for the notch's views. Everything in the notch is built in code and rebuilt
/// whole when its structure changes: the notch window swaps in a fresh page per content, because the
/// AppKit head never creates native views for children added to a layout after it is on screen.
/// Values that only change (a clock, a percentage, a meter) are updated in place instead: text on a
/// label, a symbol's image source, or a <see cref="GraphicsView"/> redrawn.
/// </summary>
static class Ui
{
    // Apple's dark-mode system colours, so the notch sits with the rest of macOS.
    public static readonly Color Text = Colors.White;
    public static readonly Color Secondary = Color.FromRgba(235, 235, 245, 153);
    public static readonly Color Tertiary = Color.FromRgba(235, 235, 245, 77);
    public static readonly Color Surface = Color.FromArgb("#1C1C1E");
    public static readonly Color Raised = Color.FromArgb("#2C2C2E");
    public static readonly Color Separator = Color.FromArgb("#38383A");

    public static readonly Color Blue = Color.FromArgb("#0A84FF");
    public static readonly Color Teal = Color.FromArgb("#64D2FF");
    public static readonly Color Green = Color.FromArgb("#30D158");
    public static readonly Color Yellow = Color.FromArgb("#FFD60A");
    public static readonly Color Orange = Color.FromArgb("#FF9F0A");
    public static readonly Color Red = Color.FromArgb("#FF453A");
    public static readonly Color Pink = Color.FromArgb("#FF375F");
    public static readonly Color Purple = Color.FromArgb("#BF5AF2");
    public static readonly Color Indigo = Color.FromArgb("#5E5CE6");

    /// <summary>The expanded panel's width. Fixed, so switching tabs only ever changes the height.</summary>
    public const double PanelWidth = 500;

    /// <summary>The width inside the panel's padding.</summary>
    public const double ContentWidth = PanelWidth - 28;

    public const double CardRadius = 14;

    public static Label Label(string text, double size = 13, bool bold = false, Color? color = null, double width = -1)
        => new()
        {
            Text = text,
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
            TextColor = color ?? Text,
            WidthRequest = width,
            // A fixed-width label in a wider cell would otherwise be centred in it, out of line with its neighbours.
            HorizontalOptions = width > 0 ? LayoutOptions.Start : LayoutOptions.Fill,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

    /// <summary>A small all-caps heading over a group, as in Control Centre.</summary>
    public static Label Caption(string text, Color? color = null)
        => Label(text.ToUpperInvariant(), 10, bold: true, color: color ?? Tertiary);

    public static T Centered<T>(this T label) where T : Label
    {
        label.HorizontalOptions = LayoutOptions.Center;
        label.HorizontalTextAlignment = TextAlignment.Center;
        return label;
    }

    /// <summary>A rounded, raised surface: the card everything in the panel sits on.</summary>
    /// <param name="wash">
    /// A colour washed in from the top-left corner. Drawn by a <see cref="WashDrawable"/> under the content
    /// (the AppKit head paints no gradient brushes), and clipped to the rounded corners by the Border.
    /// </param>
    public static Border Card(View content, Thickness? padding = null, Color? background = null, double radius = CardRadius, Color? wash = null)
    {
        var inset = padding ?? new Thickness(12, 10);
        View body = content;
        if (wash != null)
        {
            content.Margin = inset;
            body = new Grid
            {
                Children =
                {
                    new GraphicsView { Drawable = new WashDrawable(wash), InputTransparent = true },
                    content
                }
            };
        }

        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = radius },
            BackgroundColor = background ?? Surface,
            Padding = wash == null ? inset : 0,
            Content = body
        };
    }

    /// <summary>A pill button: text, an optional symbol before it, filled when selected.</summary>
    public static View Chip(string text, Action tapped, bool selected = false, Color? tint = null, string? symbol = null)
    {
        var fill = selected ? tint ?? Text : Raised;
        var ink = selected ? Colors.Black : tint ?? Text;

        var row = new HorizontalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
        if (symbol != null)
            row.Children.Add(Symbol(symbol, 11, ink));
        row.HorizontalOptions = LayoutOptions.Center;
        row.Children.Add(new Label
        {
            Text = text,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = ink,
            VerticalTextAlignment = TextAlignment.Center
        });

        var chip = new Border
        {
            AutomationId = text,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            BackgroundColor = fill,
            HeightRequest = 26,
            Padding = new Thickness(12, 0),
            Content = row
        };
        return chip.Pressable(tapped);
    }

    /// <summary>
    /// Makes a surface a button: it lightens while the pointer is over it, and dims for a moment when
    /// clicked. Nothing reports the press itself, so the click shows as a flash rather than a held-down
    /// state. Both only change colour and opacity, in place.
    /// </summary>
    /// <remarks>
    /// Hover comes from a tracking area of our own on the native view, not a PointerGestureRecognizer:
    /// the AppKit head only forwards pointer events from its container views (a Border's view isn't one),
    /// and only in the key window, which the notch never is.
    /// </remarks>
    public static T Pressable<T>(this T button, Action tapped) where T : Border
    {
        var rest = button.BackgroundColor ?? Colors.Transparent;
        var hover = rest.Alpha < 0.05f
            ? Colors.White.WithAlpha(0.1f)
            : new Color(rest.Red + (1 - rest.Red) * 0.12f, rest.Green + (1 - rest.Green) * 0.12f, rest.Blue + (1 - rest.Blue) * 0.12f, rest.Alpha);

        // Captured by the button's HandlerChanged handler, so it lives as long as the button: a tracking
        // area doesn't retain its owner.
        var tracker = new HoverTracker(over => button.BackgroundColor = over ? hover : rest);
        button.HandlerChanged += (_, _) =>
        {
            if (button.Handler?.PlatformView is NSView view)
                view.AddTrackingArea(new NSTrackingArea(
                    CGRect.Empty,
                    NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved |
                    NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.InVisibleRect,
                    tracker,
                    null));
        };

        button.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                button.Opacity = 0.55;
                button.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(130), () => button.Opacity = 1);
                tapped();
            })
        });
        return button;
    }

    // ---------------------------------------------------------------------------------------------
    // SF Symbols
    // ---------------------------------------------------------------------------------------------

    static readonly Dictionary<string, byte[]> SymbolCache = new();

    /// <summary>
    /// An SF Symbol, drawn once into a PNG and cached: no icon assets ship with the app, and a rebuild
    /// re-uses the bytes. Centred in a square of <see cref="SymbolBox"/> points so symbols line up.
    /// </summary>
    public static ImageSource SymbolSource(string name, double size, Color? color = null, bool multicolor = false, bool bold = false)
    {
        var ink = color ?? Text;
        var key = $"{name}|{size}|{ink.ToArgbHex(true)}|{multicolor}|{bold}";
        if (!SymbolCache.TryGetValue(key, out var png))
        {
            png = RenderSymbol(name, size, ink, multicolor, bold) ?? [];
            SymbolCache[key] = png;
        }
        return ImageSource.FromStream(() => new MemoryStream(png));
    }

    public static double SymbolBox(double size) => Math.Ceiling(size * 1.3);

    public static Image Symbol(string name, double size, Color? color = null, bool multicolor = false, bool bold = false)
    {
        var box = SymbolBox(size);
        var image = new Image
        {
            WidthRequest = box,
            HeightRequest = box,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center
        };
        if (bold)
            image.Source = SymbolSource(name, size, color, multicolor, bold);
        else
            image.SetSymbol(name, size, color, multicolor);
        return image;
    }

    /// <summary>A tappable symbol, with room around it to hit. Native buttons draw their own bezel on AppKit.</summary>
    public static Border SymbolButton(string name, Action tapped, double size = 15, Color? color = null, string? hint = null, Color? background = null, Thickness? padding = null)
    {
        var button = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = background ?? Colors.Transparent,
            Padding = padding ?? new Thickness(5),
            VerticalOptions = LayoutOptions.Center,
            Content = Symbol(name, size, color)
        };
        if (hint != null)
        {
            SemanticProperties.SetDescription(button, hint);
            button.AutomationId = hint;
        }
        return button.Pressable(tapped);
    }

    /// <summary>Swaps the symbol inside a <see cref="SymbolButton"/> in place.</summary>
    public static void SetSymbol(this Border button, string name, double size, Color? color = null)
    {
        if (button.Content is Image image)
            image.SetSymbol(name, size, color);
    }

    /// <summary>
    /// Swaps an image's symbol in place - and does nothing when it is already showing that one, so a
    /// view that ticks can call this every time without the image decoding again.
    /// </summary>
    public static void SetSymbol(this Image image, string name, double size, Color? color = null, bool multicolor = false)
    {
        var key = $"{name}|{size}|{(color ?? Text).ToArgbHex(true)}|{multicolor}";
        if (image.StyleId == key)
            return;
        image.StyleId = key;
        image.Source = SymbolSource(name, size, color, multicolor);
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
        var box = SymbolBox(size);
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

    // ---------------------------------------------------------------------------------------------
    // Artwork
    // ---------------------------------------------------------------------------------------------

    /// <summary>Album art (or a placeholder note) in a rounded square.</summary>
    public static Border Artwork(byte[]? bytes, double size, double radius)
    {
        View content = bytes != null
            ? new Image
            {
                Source = ImageSource.FromStream(() => new MemoryStream(bytes)),
                WidthRequest = size,
                HeightRequest = size,
                Aspect = Aspect.AspectFill
            }
            : Symbol("music.note", size * 0.4, Tertiary);

        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = radius },
            BackgroundColor = Raised,
            WidthRequest = size,
            HeightRequest = size,
            Content = content
        };
    }

    public static T At<T>(this T view, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : View
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetRowSpan(view, rowSpan);
        Grid.SetColumnSpan(view, columnSpan);
        return view;
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

/// <summary>A view that refreshes in place while it is in the notch.</summary>
interface ILiveView
{
    void Tick();
}
