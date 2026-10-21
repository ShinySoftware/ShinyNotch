using Shiny.Controls.MotionIcons;
using Shiny.Maui.Controls;

namespace ShinyNotch.Views;

/// <summary>
/// The notch's custom shapes, drawn with Maui.Graphics. Each is hosted by a <see cref="GraphicsView"/>:
/// change a value and invalidate, and only that view repaints - no rebuild, nothing re-laid out.
/// </summary>
static class Drawing
{
    public static GraphicsView View(IDrawable drawable, double width, double height)
        => new()
        {
            Drawable = drawable,
            WidthRequest = width,
            HeightRequest = height,
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
            VerticalOptions = LayoutOptions.Center
        };
}

/// <summary>
/// A colour fading to clear from the top-left corner, behind a card's content. Drawn rather than set as a
/// background: on the AppKit head neither a Border nor a layout paints a gradient brush.
/// </summary>
sealed class WashDrawable(Color color, float reach = 0.8f) : IDrawable
{
    public void Draw(ICanvas canvas, RectF rect)
    {
        canvas.SetFillPaint(new LinearGradientPaint([new PaintGradientStop(0, color), new PaintGradientStop(reach, color.WithAlpha(0))], new Point(0, 0), new Point(0.6, 1)), rect);
        canvas.FillRectangle(rect);
    }
}

/// <summary>A rounded bar filled to <see cref="Value"/>: progress, meters, the HUD.</summary>
sealed class CapsuleDrawable : IDrawable
{
    public double Value { get; set; }
    public Color Fill { get; set; } = Ui.Text;
    public Color Track { get; set; } = Ui.Raised;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var radius = rect.Height / 2;
        canvas.FillColor = this.Track;
        canvas.FillRoundedRectangle(rect, radius);

        var width = (float)(rect.Width * Math.Clamp(this.Value, 0, 1));
        if (width <= 0)
            return;

        canvas.FillColor = this.Fill;
        canvas.FillRoundedRectangle(rect.X, rect.Y, Math.Max(width, rect.Height), rect.Height, radius);
    }
}

/// <summary>A ring filled clockwise from the top to <see cref="Value"/>.</summary>
sealed class RingDrawable : IDrawable
{
    public double Value { get; set; }
    public Color Fill { get; set; } = Ui.Teal;
    public Color Track { get; set; } = Ui.Raised;
    public float Thickness { get; set; } = 6;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var inset = this.Thickness / 2;
        var ring = rect.Inflate(-inset, -inset);
        var size = Math.Min(ring.Width, ring.Height);
        ring = new RectF(ring.Center.X - size / 2, ring.Center.Y - size / 2, size, size);

        canvas.StrokeSize = this.Thickness;
        canvas.StrokeColor = this.Track;
        canvas.DrawEllipse(ring);

        var value = Math.Clamp(this.Value, 0, 1);
        if (value <= 0.001)
            return;

        canvas.StrokeColor = this.Fill;
        canvas.StrokeLineCap = LineCap.Round;
        if (value >= 0.999)
            canvas.DrawEllipse(ring);
        else
            canvas.DrawArc(ring.X, ring.Y, ring.Width, ring.Height, 90, (float)(90 - value * 360), true, false);
    }
}

/// <summary>
/// The music bars beside the artwork. Playing, they dance: a Shiny <see cref="MotionIconView"/>, whose
/// one frame timer per window runs only while an icon is animating and stops when the view leaves the
/// screen. A pause rebuilds the view, so paused bars are a static <see cref="PausedBarsDrawable"/> and cost
/// nothing. Each track's beat comes from its key, so every song dances its own way.
/// </summary>
static class MusicBars
{
    const int Count = 4;
    const float BarWidth = 4f;
    const float Gap = 2f;

    public static View Create(string trackKey, bool isPlaying, Color fill, double width = 16, double height = 14)
    {
        if (!isPlaying)
            return Drawing.View(new PausedBarsDrawable { Fill = fill }, width, height);

        return new MotionIconView
        {
            Source = Dancing(trackKey),
            Trigger = MotionTrigger.Loop,
            Color = fill,
            WidthRequest = width,
            HeightRequest = height,
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
            VerticalOptions = LayoutOptions.Center
        };
    }

    // Four pills in the 24-unit box, each squashed toward the middle on its own keys. A cycle starts and
    // ends on the same height, so the loop has no seam.
    static MotionIconDefinition Dancing(string trackKey)
    {
        var random = new Random(StableHash(trackKey));
        var parts = new MotionIconPart[Count];
        var spec = new MotionSpecBuilder(1100);

        for (var i = 0; i < Count; i++)
        {
            var left = 1f + i * (BarWidth + Gap);
            var right = left + BarWidth;
            var id = $"bar{i}";
            parts[i] = MotionIconPart.Filled(id, $"M{left} 6A2 2 0 0 1 {right} 6V18A2 2 0 0 1 {left} 18z") with
            {
                Origin = new MotionPoint(left + BarWidth / 2, 12f)
            };

            var steps = Enumerable.Range(0, 4).Select(_ => 0.25 + random.NextDouble() * 0.75).ToList();
            steps.Add(steps[0]);
            spec.ScaleY(id, k => k.Evenly(MotionEase.SinInOut, [.. steps]));
        }

        return new MotionIconDefinition("music-bars", parts, spec.Build());
    }

    // string.GetHashCode is randomised per process; a track should dance the same way every launch.
    static int StableHash(string s)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in s)
                hash = hash * 31 + c;
            return hash;
        }
    }
}

/// <summary>The music bars while paused: a flat, dimmed line.</summary>
sealed class PausedBarsDrawable : IDrawable
{
    const int Count = 4;

    public Color Fill { get; set; } = Ui.Text;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var gap = rect.Width * 0.12f;
        var bar = (rect.Width - gap * (Count - 1)) / Count;
        var h = rect.Height * 0.18f;

        canvas.FillColor = this.Fill.WithAlpha(0.45f);
        for (var i = 0; i < Count; i++)
            canvas.FillRoundedRectangle(rect.X + i * (bar + gap), rect.Center.Y - h / 2, bar, h, bar / 2);
    }
}

/// <summary>
/// An area chart of recent percentages, newest at the right. Scaled to what has happened lately (but
/// never closer than 0-20%), so an idle Mac's few percent is a shape rather than a line on the floor.
/// </summary>
sealed class SparkDrawable : IDrawable
{
    public IReadOnlyCollection<double> Values { get; set; } = [];
    public int Capacity { get; set; } = 24;
    public Color Stroke { get; set; } = Ui.Teal;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var values = this.Values.ToArray();
        if (values.Length < 2)
            return;

        var top = Math.Clamp(values.Max() * 1.25, 20, 100);
        var step = rect.Width / (this.Capacity - 1);
        var x0 = rect.Right - step * (values.Length - 1);
        PointF At(int i) => new(x0 + i * step, rect.Bottom - (float)(Math.Clamp(values[i], 0, top) / top * rect.Height));

        var line = new PathF();
        line.MoveTo(At(0));
        for (var i = 1; i < values.Length; i++)
            line.LineTo(At(i));

        var area = new PathF(line);
        area.LineTo(At(values.Length - 1).X, rect.Bottom);
        area.LineTo(x0, rect.Bottom);
        area.Close();

        canvas.SetFillPaint(new LinearGradientPaint([new PaintGradientStop(0, this.Stroke.WithAlpha(0.35f)), new PaintGradientStop(1, this.Stroke.WithAlpha(0))], new Point(0, 0), new Point(0, 1)), rect);
        canvas.FillPath(area);

        canvas.StrokeColor = this.Stroke;
        canvas.StrokeSize = 1.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(line);
    }
}

/// <summary>
/// A day's low-to-high on a track spanning the week's range, shaded from the low's colour to the
/// high's, with a dot at the temperature now on today's - the iOS weather widget's bar.
/// </summary>
sealed class RangeDrawable(double low, double high, double weekLow, double weekSpan, double? now, Func<double, Color> colorOf) : IDrawable
{
    public void Draw(ICanvas canvas, RectF rect)
    {
        var radius = rect.Height / 2;
        canvas.FillColor = Ui.Raised;
        canvas.FillRoundedRectangle(rect, radius);

        var start = rect.X + (float)((low - weekLow) / weekSpan) * rect.Width;
        var end = rect.X + (float)((high - weekLow) / weekSpan) * rect.Width;
        var bar = new RectF(start, rect.Y, Math.Max(end - start, rect.Height), rect.Height);

        canvas.SetFillPaint(new LinearGradientPaint([new PaintGradientStop(0, colorOf(low)), new PaintGradientStop(1, colorOf(high))], new Point(0, 0.5), new Point(1, 0.5)), bar);
        canvas.FillRoundedRectangle(bar, radius);

        if (now is { } t && high > low)
        {
            var x = start + (float)(Math.Clamp((t - low) / (high - low), 0, 1)) * (bar.Width - rect.Height);
            canvas.FillColor = Colors.Black;
            canvas.FillEllipse(x - 1, rect.Y - 1, rect.Height + 2, rect.Height + 2);
            canvas.FillColor = Colors.White;
            canvas.FillEllipse(x, rect.Y, rect.Height, rect.Height);
        }
    }
}
