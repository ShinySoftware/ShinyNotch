using ShinyNotch.Platform;
using ShinyNotch.Services;
using Slider = Shiny.Maui.Controls.Slider;

namespace ShinyNotch.Views;

/// <summary>
/// The controls under every tab: play/pause, volume and brightness. Kept current in place - the keys
/// and Control Centre move volume and brightness too.
/// </summary>
sealed class ControlsBar : Grid, ILiveView
{
    const double PlaySize = 13;

    readonly INowPlayingService media;
    readonly SystemControlsService controls;
    readonly Border? play;
    readonly Border mute;
    readonly Slider volume;
    readonly Slider? brightness;
    bool syncing;

    /// <param name="showPlay">False on the Media tab, which has its own transport right above.</param>
    public ControlsBar(INowPlayingService media, SystemControlsService controls, bool showPlay)
    {
        this.media = media;
        this.controls = controls;
        this.ColumnSpacing = 8;
        this.ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)];

        if (showPlay && media.Current is { } now)
        {
            this.play = Ui.SymbolButton(now.IsPlaying ? "pause.fill" : "play.fill", media.PlayPause, PlaySize, hint: "Play or pause", background: Ui.Surface, padding: new Thickness(9));
            this.Children.Add(this.play.At(column: 0));
        }

        this.mute = Ui.SymbolButton(this.VolumeSymbol(), controls.ToggleMute, 13, Ui.Secondary, "Mute", padding: new Thickness(2));
        var volumeCard = this.SliderCard(this.mute, controls.Volume ?? 0, Ui.Teal, Ui.Purple, v => controls.SetVolume((float)v), out this.volume);
        this.Children.Add(volumeCard.At(column: 1));

        if (controls.DisplayBrightness is { } level)
            this.Children.Add(this.SliderCard(Ui.Symbol("sun.max.fill", 13, Ui.Secondary), level, Ui.Red, Ui.Yellow, v => controls.SetBrightness((float)v), out this.brightness).At(column: 2));
        else
            Grid.SetColumnSpan(volumeCard, 2);
    }

    View SliderCard(View icon, double value, Color cold, Color hot, Action<double> changed, out Slider slider)
    {
        // A fine step: the keys move volume and brightness in sixteenths, and finer still with ⌥⇧.
        slider = Ui.Slider(0, 1, value, 0.01, cold, hot);
        slider.ValueChangedEvent += (_, v) =>
        {
            if (!this.syncing)
                changed(v);
        };

        return Ui.Card(new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children = { icon.At(column: 0), slider.At(column: 1) }
        }, new Thickness(10, 4));
    }

    string VolumeSymbol()
    {
        var v = this.controls.Volume ?? 0;
        return this.controls.IsMuted || v <= 0.001 ? "speaker.slash.fill" : v < 0.34 ? "speaker.wave.1.fill" : v < 0.67 ? "speaker.wave.2.fill" : "speaker.wave.3.fill";
    }

    public void Tick()
    {
        // Only a real difference is pushed, so a drag (which sets the value itself) isn't fought by the
        // reading that comes back.
        this.syncing = true;
        Sync(this.volume, this.controls.Volume);
        if (this.brightness != null)
            Sync(this.brightness, this.controls.DisplayBrightness);
        this.syncing = false;

        this.mute.SetSymbol(this.VolumeSymbol(), 13, Ui.Secondary);
        if (this.play != null && this.media.Current is { } now)
            this.play.SetSymbol(now.IsPlaying ? "pause.fill" : "play.fill", PlaySize);
    }

    static void Sync(Slider slider, float? value)
    {
        if (value is { } v && Math.Abs(slider.Value - v) > 0.005)
            slider.Value = v;
    }
}
