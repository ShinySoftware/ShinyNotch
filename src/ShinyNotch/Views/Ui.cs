using Microsoft.Maui.Controls.Shapes;

namespace ShinyNotch.Views;

/// <summary>
/// Small building blocks for the notch's views. Everything in the notch is built in code and rebuilt
/// whole when its structure changes: the notch window swaps in a fresh page per content, because the
/// AppKit head never creates native views for children added to a layout after it is on screen.
/// Values that only change their text (a clock, a percentage) are updated in place instead.
/// </summary>
static class Ui
{
    public static readonly Color Text = Colors.White;
    public static readonly Color Dim = Color.FromArgb("#A1A1AA");
    public static readonly Color Faint = Color.FromArgb("#52525B");
    public static readonly Color Track = Color.FromArgb("#27272A");
    public static readonly Color Accent = Color.FromArgb("#22D3EE");
    public static readonly Color Warm = Color.FromArgb("#F59E0B");
    public static readonly Color Danger = Color.FromArgb("#F43F5E");

    /// <summary>The expanded panel's width. Fixed, so switching tabs only ever changes the height.</summary>
    public const double PanelWidth = 500;

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

    /// <summary>A tappable glyph. A Label with a tap recogniser, not a Button: native buttons draw their own bezel on AppKit.</summary>
    public static View Glyph(string glyph, Action tapped, double size = 18, Color? color = null, string? hint = null)
    {
        var label = new Label
        {
            Text = glyph,
            FontSize = size,
            TextColor = color ?? Text,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MinimumWidthRequest = size + 12,
            HeightRequest = size + 12
        };
        if (hint != null)
        {
            SemanticProperties.SetDescription(label, hint);
            label.AutomationId = hint;
        }
        label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(tapped) });
        return label;
    }

    /// <summary>A rounded, tinted button with a text label.</summary>
    public static View Chip(string text, Action tapped, bool selected = false, Color? tint = null)
    {
        var chip = new Border
        {
            AutomationId = text,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = selected ? (tint ?? Accent) : Track,
            Padding = new Thickness(12, 5),
            Content = new Label
            {
                Text = text,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = selected ? Colors.Black : Text,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(tapped) });
        return chip;
    }

    /// <summary>A bar of filled and empty blocks — text, so it can update in place.</summary>
    public static string Meter(double fraction, int cells = 16)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        var filled = (int)Math.Round(fraction * cells);
        return new string('▰', filled) + new string('▱', cells - filled);
    }

    /// <summary>A sparkline from block characters — also text, for the same reason.</summary>
    public static string Sparkline(IEnumerable<double> percents)
    {
        const string blocks = "▁▂▃▄▅▆▇█";
        return new string(percents.Select(p => blocks[Math.Clamp((int)(p / 100 * (blocks.Length - 1)), 0, blocks.Length - 1)]).ToArray());
    }

    public static Image Artwork(byte[]? bytes, double size)
        => new()
        {
            Source = bytes == null ? null : ImageSource.FromStream(() => new MemoryStream(bytes)),
            WidthRequest = size,
            HeightRequest = size,
            Aspect = Aspect.AspectFill
        };

    public static T At<T>(this T view, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : View
    {
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetRowSpan(view, rowSpan);
        Grid.SetColumnSpan(view, columnSpan);
        return view;
    }
}

/// <summary>A view that refreshes its text in place once a second (or faster) while it is in the notch.</summary>
interface ILiveView
{
    void Tick();
}
