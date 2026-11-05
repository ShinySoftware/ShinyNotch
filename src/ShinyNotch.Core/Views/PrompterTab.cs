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
    readonly VerticalStackLayout script;
    /// <summary>One label per line of the script, so the line a word is on can be found and redrawn alone.</summary>
    readonly Label[] lines;
    readonly double viewportHeight;
    /// <summary>The words drawn read and highlighted: up to this one.</summary>
    int shownWord = -1;

    public PrompterTab(TeleprompterService prompter, Action openSettings)
    {
        this.prompter = prompter;
        this.viewportHeight = prompter.Height;
        this.Spacing = 10;

        var width = prompter.Width - Ui.PanelPadding - 8;
        this.lines = prompter.Follower.Paragraphs
            .Select(text => new Label
            {
                // A blank line still takes a line's height.
                Text = text.Length == 0 ? " " : text,
                FontSize = prompter.FontSize,
                FontAttributes = FontAttributes.Bold,
                TextColor = Ui.Text,
                LineBreakMode = LineBreakMode.WordWrap,
                LineHeight = 1.15,
                WidthRequest = width
            })
            .ToArray();
        this.script = new VerticalStackLayout { Spacing = 0 };
        foreach (var line in this.lines)
            this.script.Children.Add(line);
        // Room after the last line for it to scroll up under the camera.
        this.script.Children.Add(new BoxView { HeightRequest = this.viewportHeight, Color = Colors.Transparent, InputTransparent = true });

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
        this.ShowWord(follow: false);

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
        var transport = new HorizontalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Ui.SymbolButton(running ? "pause.fill" : "play.fill", prompter.Toggle, 16, Colors.Black, running ? "Pause" : "Play", Ui.Text, new Thickness(14, 6))
            }
        };
        // Offered when turned on in Settings: follow the voice rather than the clock.
        if (prompter.CanFollowVoice)
        {
            var listening = prompter.IsListening;
            transport.Children.Add(Ui.SymbolButton(
                "mic.fill",
                () => _ = prompter.ToggleListening(),
                15,
                listening ? Colors.White : Ui.Secondary,
                listening ? "Stop following my voice" : "Follow my voice",
                listening ? Ui.Red : Ui.Raised,
                new Thickness(12, 6)));
        }

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
                transport.At(column: 1),
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
        if (prompter.ListenProblem is { } problem)
        {
            var caption = Ui.Caption(problem, Ui.Orange);
            caption.HorizontalTextAlignment = TextAlignment.Center;
            this.Children.Add(caption);
        }
        this.Children.Add(Ui.Card(bar, new Thickness(6, 4)));
    }

    /// <summary>
    /// Draws the words read so far dimmed and the one being read highlighted, then scrolls it into view
    /// a third of the way down. Called when the reader moves on a word.
    /// </summary>
    public void ShowWord(bool follow = true)
    {
        var words = this.prompter.Follower.Words;
        var current = this.prompter.Follower.Current;
        var was = this.shownWord;
        this.shownWord = current;

        int Line(int word) => word >= 0 && word < words.Count ? words[word].Paragraph : -1;
        var fromLine = Math.Max(0, Math.Min(Line(was), Line(current)));
        var toLine = Math.Max(Line(was), Line(current));
        // Only the lines between the old word and the new one change.
        for (var i = fromLine; i <= toLine && i < this.lines.Length; i++)
            this.DrawLine(i, current);

        if (follow && current >= 0 && current < words.Count)
            this.FollowWord(words[current]);
    }

    void DrawLine(int index, int current)
    {
        var label = this.lines[index];
        var words = this.prompter.Follower.Words;
        var text = this.prompter.Follower.Paragraphs[index];
        var word = current >= 0 && current < words.Count ? words[current] : null;

        if (word == null || index > word.Paragraph)
        {
            label.FormattedText = null;
            label.TextColor = Ui.Text;
        }
        else if (index < word.Paragraph)
        {
            label.FormattedText = null;
            label.TextColor = Ui.Secondary;
        }
        else
        {
            var end = word.Start + word.Length;
            label.FormattedText = new FormattedString
            {
                Spans =
                {
                    this.Span(text[..word.Start], Ui.Secondary),
                    this.Span(text[word.Start..end], Ui.Yellow, Ui.Yellow.WithAlpha(0.2f)),
                    this.Span(text[end..], Ui.Text)
                }
            };
        }
    }

    Span Span(string text, Color color, Color? background = null) => new()
    {
        Text = text,
        TextColor = color,
        BackgroundColor = background,
        FontSize = this.prompter.FontSize,
        FontAttributes = FontAttributes.Bold,
        LineHeight = 1.15
    };

    /// <summary>
    /// Where the word is: its line's top, plus how far through the line it is — the line wraps evenly, so
    /// the share of its characters before the word is close to the share of its height.
    /// </summary>
    void FollowWord(ScriptWord word)
    {
        var line = this.lines[word.Paragraph];
        if (line.Height <= 0)
            return;

        var length = Math.Max(1, this.prompter.Follower.Paragraphs[word.Paragraph].Length);
        var y = line.Y + line.Height * word.Start / length;
        this.prompter.SeekTo(y - this.viewportHeight / 3);
    }

    static string SpeedText(double speed) => $"{speed:0} pt/s";

    /// <summary>Follows the service's offset. Called on every teleprompter frame.</summary>
    public void Scroll()
    {
        var max = Math.Max(0, this.scroller.ContentSize.Height - this.viewportHeight);
        if (max > 0 && this.prompter.Offset >= max)
            this.prompter.ReachedEnd();

        // The voice moves a line at a time: glide there. The timer moves a little every frame already.
        _ = this.scroller.ScrollToAsync(0, Math.Min(this.prompter.Offset, max > 0 ? max : this.prompter.Offset), this.prompter.IsListening);
    }
}
