using ShinyNotch.Services;

namespace ShinyNotch.Views;

/// <summary>
/// Now Playing: the artwork, the track and a scrubber on a card washed in the artwork's colour, the
/// transport under it. Volume and brightness are in the <see cref="ControlsBar"/> under every tab.
/// </summary>
sealed class MediaTab : VerticalStackLayout, ILiveView
{
    const double ArtSize = 96;
    const double PlaySize = 22;

    readonly NowPlayingService media;
    readonly Label? elapsed;
    readonly Label? remaining;
    readonly CapsuleDrawable? progress;
    readonly GraphicsView? progressView;
    readonly Border? play;

    public MediaTab(NowPlayingService media)
    {
        this.media = media;
        this.Spacing = 10;

        if (media.Current is { } now)
        {
            var tint = media.ArtworkColor ?? Ui.Text;

            this.elapsed = Ui.Label("", 10, bold: true, color: Ui.Tertiary, width: 34);
            this.remaining = Ui.Label("", 10, bold: true, color: Ui.Tertiary, width: 40);
            this.remaining.HorizontalTextAlignment = TextAlignment.End;
            this.progress = new CapsuleDrawable { Fill = tint, Track = Colors.White.WithAlpha(0.14f) };
            this.progressView = Drawing.View(this.progress, 260, 4);
            this.progressView.HorizontalOptions = LayoutOptions.Fill;

            var scrubber = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                Children = { this.elapsed.At(column: 0), this.progressView.At(column: 1), this.remaining.At(column: 2) }
            };

            this.play = Ui.SymbolButton(now.IsPlaying ? "pause.fill" : "play.fill", media.PlayPause, PlaySize, hint: "Play or pause", padding: new Thickness(10, 2));
            var transport = new HorizontalStackLayout
            {
                Spacing = 14,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    Ui.SymbolButton("backward.fill", media.Previous, 16, Ui.Secondary, "Previous"),
                    this.play,
                    Ui.SymbolButton("forward.fill", media.Next, 16, Ui.Secondary, "Next")
                }
            };

            var title = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                Children =
                {
                    Ui.Label(now.Title, 15, bold: true).At(column: 0),
                    Drawing.View(new BarsDrawable(now.TrackKey) { IsPlaying = now.IsPlaying, Fill = tint }, 16, 14).At(column: 1)
                }
            };

            var info = new VerticalStackLayout
            {
                Spacing = 3,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    title,
                    Ui.Label(String.IsNullOrWhiteSpace(now.Album) ? now.Artist : $"{now.Artist} — {now.Album}", 12, color: Ui.Secondary),
                    new BoxView { HeightRequest = 4, Color = Colors.Transparent },
                    scrubber,
                    transport
                }
            };

            var body = new Grid
            {
                ColumnSpacing = 16,
                ColumnDefinitions = [new ColumnDefinition(ArtSize), new ColumnDefinition(GridLength.Star)],
                Children = { Ui.Artwork(media.Artwork, ArtSize, 12).At(column: 0), info.At(column: 1) }
            };

            // The artwork's colour bleeds into the card from the corner, as in the Music app's player.
            this.Children.Add(Ui.Card(body, new Thickness(12), wash: tint.WithAlpha(0.3f)));
        }
        else
        {
            this.Children.Add(Ui.Card(new HorizontalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    Ui.Artwork(null, 56, 10),
                    new VerticalStackLayout
                    {
                        Spacing = 2,
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            Ui.Label("Nothing playing", 15, bold: true),
                            Ui.Label("Play something in Music or Spotify", 12, color: Ui.Secondary)
                        }
                    }
                }
            }, new Thickness(12)));
        }

        this.Tick();
    }

    public void Tick()
    {
        if (this.media.Current is not { } now)
            return;

        var position = this.media.Position;
        if (this.elapsed != null)
            this.elapsed.Text = Time(position);
        if (this.remaining != null)
            this.remaining.Text = now.Duration > 0 ? $"-{Time(now.Duration - position)}" : "LIVE";
        if (this.progress != null && this.progressView != null)
        {
            this.progress.Value = now.Duration > 0 ? position / now.Duration : 0;
            this.progressView.Invalidate();
        }
        this.play?.SetSymbol(now.IsPlaying ? "pause.fill" : "play.fill", PlaySize);
    }

    static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}
