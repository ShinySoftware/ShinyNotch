using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The volume / brightness flash: icon on one side of the camera, a meter and percentage on the other.
/// Text only, so a held key updates it in place without rebuilding the notch.
/// </summary>
sealed class HudView : Grid, ILiveView
{
    readonly SystemControlsService controls;
    readonly Label icon;
    readonly Label meter;

    public HudKind Kind { get; private set; }

    public HudView(HudKind kind, double cutoutWidth, double height, SystemControlsService controls)
    {
        this.controls = controls;
        this.HeightRequest = height;
        this.ColumnDefinitions =
        [
            new ColumnDefinition(56),
            new ColumnDefinition(Math.Max(cutoutWidth, 8)),
            new ColumnDefinition(150)
        ];

        this.icon = Ui.Label("", 15);
        this.icon.HorizontalOptions = LayoutOptions.Start;
        this.meter = Ui.Label("", 11, color: Ui.Accent, width: 150);
        this.meter.HorizontalTextAlignment = TextAlignment.End;

        this.Children.Add(this.icon.At(column: 0));
        this.Children.Add(this.meter.At(column: 2));
        this.Show(kind);
    }

    public void Show(HudKind kind)
    {
        this.Kind = kind;
        this.Tick();
    }

    public void Tick()
    {
        if (this.Kind == HudKind.Volume)
        {
            var volume = this.controls.Volume ?? 0;
            var muted = this.controls.IsMuted || volume <= 0.001f;
            this.icon.Text = muted ? "🔇" : volume < 0.34 ? "🔈" : volume < 0.67 ? "🔉" : "🔊";
            this.meter.Text = muted ? "Muted" : $"{Ui.Meter(volume, 12)} {volume * 100:0}%";
            this.meter.TextColor = muted ? Ui.Dim : Ui.Accent;
        }
        else
        {
            var b = this.controls.DisplayBrightness ?? 0;
            this.icon.Text = "☀︎";
            this.meter.Text = $"{Ui.Meter(b, 12)} {b * 100:0}%";
            this.meter.TextColor = Ui.Warm;
        }
    }
}
