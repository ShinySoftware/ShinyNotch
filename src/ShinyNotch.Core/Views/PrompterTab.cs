using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// The script, scrolling right under the camera and fading out at the edges, over a bar of controls.
/// </summary>
sealed class PrompterTab : VerticalStackLayout
{
    const double FadeHeight = 18;

    readonly TeleprompterService prompter;
    readonly ScrollView scroller;
    readonly Label script;
    readonly double viewportHeight;

    public PrompterTab(TeleprompterService prompter, Action openSettings)
    {
        this.prompter = prompter;
        this.viewportHeight = prompter.Height;
        this.Spacing = 10;

        this.script = new Label
        {
            Text = prompter.Script + "\n\n\n\n\n",
            FontSize = prompter.FontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = Ui.Text,
            LineBreakMode = LineBreakMode.WordWrap,
            LineHeight = 1.15,
            WidthRequest = prompter.Width - Ui.PanelPadding - 8
        };
        this.scroller = new ScrollView
        {
            HeightRequest = this.viewportHeight,
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = this.script
        };
        // A rebuilt tab (pause, end of script, a new text size) starts at the top. Put it back where the
        // service says once the text has a size to scroll — the timer won't while paused.
        this.script.SizeChanged += (_, _) => this.Scroll();

        // Black fading to clear over the top and bottom, so lines slide in and out rather than being cut.
        static BoxView Fade(bool top) => new()
        {
            HeightRequest = FadeHeight,
            InputTransparent = true,
            VerticalOptions = top ? LayoutOptions.Start : LayoutOptions.End,
            Background = new LinearGradientBrush(
                [new GradientStop(Colors.Black, 0), new GradientStop(Colors.Black.WithAlpha(0), 1)],
                new Point(0, top ? 0 : 1),
                new Point(0, top ? 1 : 0))
        };
        this.Children.Add(new Grid { Children = { this.scroller, Fade(true), Fade(false) } });

        var speed = Ui.Label(SpeedText(prompter.Speed), 10, bold: true, color: Ui.Secondary, width: 54);
        speed.HorizontalTextAlignment = TextAlignment.Center;
        speed.VerticalOptions = LayoutOptions.Center;
        void ChangeSpeed(double delta)
        {
            // The speed isn't worth a rebuild: the label is updated here, in place.
            prompter.ChangeSpeed(delta);
            speed.Text = SpeedText(prompter.Speed);
        }

        var running = prompter.IsRunning;
        var bar = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 0,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        Ui.SymbolButton("backward.end.fill", prompter.Restart, 13, Ui.Secondary, "Restart"),
                        Ui.SymbolButton("chevron.up", () => prompter.Nudge(-40), 13, Ui.Secondary, "Back"),
                        Ui.SymbolButton("chevron.down", () => prompter.Nudge(40), 13, Ui.Secondary, "Forward")
                    }
                }.At(column: 0),
                Ui.SymbolButton(running ? "pause.fill" : "play.fill", prompter.Toggle, 16, Colors.Black, running ? "Pause" : "Play", Ui.Text, new Thickness(14, 6)).At(column: 1),
                new HorizontalStackLayout
                {
                    Spacing = 0,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        Ui.SymbolButton("tortoise.fill", () => ChangeSpeed(-4), 13, Ui.Secondary, "Slower"),
                        speed,
                        Ui.SymbolButton("hare.fill", () => ChangeSpeed(4), 13, Ui.Secondary, "Faster"),
                        Ui.SymbolButton("textformat.size.smaller", () => prompter.ChangeFontSize(-2), 13, Ui.Secondary, "Smaller text"),
                        Ui.SymbolButton("textformat.size.larger", () => prompter.ChangeFontSize(2), 13, Ui.Secondary, "Larger text"),
                        Ui.SymbolButton("square.and.pencil", openSettings, 13, Ui.Secondary, "Edit script")
                    }
                }.At(column: 2)
            }
        };
        this.Children.Add(Ui.Card(bar, new Thickness(6, 4)));
    }

    static string SpeedText(double speed) => $"{speed:0} pt/s";

    /// <summary>Follows the service's offset. Called on every teleprompter frame.</summary>
    public void Scroll()
    {
        var max = Math.Max(0, this.scroller.ContentSize.Height - this.viewportHeight);
        if (max > 0 && this.prompter.Offset >= max)
            this.prompter.ReachedEnd();

        _ = this.scroller.ScrollToAsync(0, Math.Min(this.prompter.Offset, max > 0 ? max : this.prompter.Offset), false);
    }
}
