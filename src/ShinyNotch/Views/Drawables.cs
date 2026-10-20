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
/// The music bars beside the artwork. Static, not animated: a dancing visualizer means redrawing many
/// times a second for as long as music plays, which is exactly the CPU this app doesn't spend. The
/// heights come from the track, so each song has its own shape; paused, they sink to a flat line.
/// </summary>
sealed class BarsDrawable : IDrawable
{
    readonly double[] heights;

    public BarsDrawable(string seed, int count = 4)
    {
        var random = new Random(StableHash(seed));
        this.heights = Enumerable.Range(0, count).Select(_ => 0.35 + random.NextDouble() * 0.65).ToArray();
    }

    public bool IsPlaying { get; set; }
    public Color Fill { get; set; } = Ui.Text;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var count = this.heights.Length;
        var gap = rect.Width * 0.12f;
        var bar = (rect.Width - gap * (count - 1)) / count;

        canvas.FillColor = this.IsPlaying ? this.Fill : this.Fill.WithAlpha(0.45f);
        for (var i = 0; i < count; i++)
        {
            var h = (float)(rect.Height * (this.IsPlaying ? this.heights[i] : 0.18));
            var x = rect.X + i * (bar + gap);
            canvas.FillRoundedRectangle(x, rect.Bottom - h - (rect.Height - h) / 2, bar, h, bar / 2);
        }
    }

    // string.GetHashCode is randomised per process; the bars should look the same every launch.
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
