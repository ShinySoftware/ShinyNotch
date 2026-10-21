using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The volume / brightness flash: the symbol on one side of the camera, a meter and percentage on the
/// other. Everything updates in place, so a held key never rebuilds the notch.
/// </summary>
sealed class HudView : Grid, ILiveView
{
    const double IconSize = 14;
    /// <summary>The meter and percentage, and the room either side of them.</summary>
    const double MeterWingWidth = 132;

    readonly SystemControlsService controls;
    readonly Image icon;
    readonly CapsuleDrawable bar = new();
    readonly GraphicsView meter;
    readonly Label percent;

    public HudKind Kind { get; private set; }

    /// <param name="leftWish">The peek's wing widths, so the notch keeps its size; the meter side may need more.</param>
    public HudView(HudKind kind, double cutoutWidth, double leftWish, double rightWish, bool locked, double height, SystemControlsService controls)
    {
        this.controls = controls;
        this.HeightRequest = height;
        var (left, right) = PeekView.Widths(leftWish, rightWish, locked, PeekView.EdgeGap + IconSize + 6, MeterWingWidth);
        var (leftInset, rightInset) = PeekView.Lay(this, cutoutWidth, left, right);

        this.icon = Ui.Symbol("speaker.wave.2.fill", IconSize);
        this.icon.HorizontalOptions = LayoutOptions.Start;
        this.icon.Margin = new Thickness(leftInset + PeekView.EdgeGap, 0, 0, 0);
        this.meter = Drawing.View(this.bar, 84, 5);
        this.percent = Ui.Label("", 11, bold: true, width: 28);
        this.percent.HorizontalTextAlignment = TextAlignment.End;

        this.Children.Add(this.icon.At(column: 0));
        this.Children.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, rightInset + PeekView.EdgeGap, 0),
            VerticalOptions = LayoutOptions.Center,
            Children = { this.meter, this.percent }
        }.At(column: 2));
        this.Show(kind);
    }

    public void Show(HudKind kind)
    {
        this.Kind = kind;
        this.Tick();
    }

    public void Tick()
    {
        string symbol;
        double value;
        Color color;
        if (this.Kind == HudKind.Volume)
        {
            value = this.controls.Volume ?? 0;
            var muted = this.controls.IsMuted || value <= 0.001;
            symbol = muted ? "speaker.slash.fill" : value < 0.34 ? "speaker.wave.1.fill" : value < 0.67 ? "speaker.wave.2.fill" : "speaker.wave.3.fill";
            color = muted ? Ui.Tertiary : Ui.Text;
            if (muted)
                value = 0;
        }
        else
        {
            value = this.controls.DisplayBrightness ?? 0;
            symbol = value < 0.5 ? "sun.min.fill" : "sun.max.fill";
            color = Ui.Yellow;
        }

        this.icon.SetSymbol(symbol, IconSize, color);
        this.bar.Value = value;
        this.bar.Fill = color;
        this.meter.Invalidate();
        this.percent.Text = $"{value * 100:0}";
        this.percent.TextColor = color;
    }
}
