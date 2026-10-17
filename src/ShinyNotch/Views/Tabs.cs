using Microsoft.Maui.Controls.Shapes;
using ShinyNotch.Services;

namespace ShinyNotch.Views;

sealed class MediaTab : VerticalStackLayout, ILiveView
{
    readonly NowPlayingService media;
    readonly Label? position;
    readonly Label? playGlyph;

    public MediaTab(NowPlayingService media, SystemControlsService controls)
    {
        this.media = media;
        this.Spacing = 12;

        if (media.Current is { } now)
        {
            this.position = Ui.Label("", 11, color: Ui.Dim, width: 200);
            var info = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    Ui.Label(now.Title, 16, bold: true, width: 330),
                    Ui.Label(now.Artist, 13, color: Ui.Dim, width: 330),
                    Ui.Label(now.Album, 11, color: Ui.Faint, width: 330),
                    this.position
                }
            };

            var art = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = Ui.Track,
                WidthRequest = 84,
                HeightRequest = 84,
                Content = media.Artwork != null ? Ui.Artwork(media.Artwork, 84) : Ui.Label("♫", 32, color: Ui.Faint)
            };

            this.playGlyph = (Label)Ui.Glyph("", media.PlayPause, 24);
            var transport = new HorizontalStackLayout
            {
                Spacing = 18,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    Ui.Glyph("⏮︎", media.Previous, 20, hint: "Previous"),
                    this.playGlyph,
                    Ui.Glyph("⏭︎", media.Next, 20, hint: "Next")
                }
            };

            this.Children.Add(new HorizontalStackLayout { Spacing = 14, Children = { art, info } });
            this.Children.Add(transport);
        }
        else
        {
            this.Children.Add(Ui.Label("Nothing playing in Music or Spotify", 13, color: Ui.Dim));
        }

        this.Children.Add(SliderRow("🔊", controls.Volume ?? 0, v => controls.SetVolume((float)v), controls.ToggleMute));
        if (controls.DisplayBrightness is { } brightness)
            this.Children.Add(SliderRow("☀︎", brightness, v => controls.SetBrightness((float)v), null));

        this.Tick();
    }

    static View SliderRow(string glyph, double value, Action<double> changed, Action? glyphTapped)
    {
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = value,
            WidthRequest = 400,
            MinimumTrackColor = Ui.Accent,
            MaximumTrackColor = Ui.Track,
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
        slider.ValueChanged += (_, e) => changed(e.NewValue);

        var icon = glyphTapped != null ? Ui.Glyph(glyph, glyphTapped, 15) : Ui.Label(glyph, 15);
        return new HorizontalStackLayout { Spacing = 10, Children = { icon, slider } };
    }

    public void Tick()
    {
        if (this.media.Current is not { } now)
            return;

        if (this.position != null)
            this.position.Text = $"{Time(now.Position)} / {Time(now.Duration)}";
        if (this.playGlyph != null)
            this.playGlyph.Text = now.IsPlaying ? "⏸︎" : "▶︎";
    }

    static string Time(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }
}

sealed class TrayTab : VerticalStackLayout
{
    const int Columns = 6;
    const int MaxShown = 12;

    public TrayTab(FileTrayService tray)
    {
        this.Spacing = 12;
        var files = tray.Files;

        if (files.Count == 0)
        {
            this.Children.Add(new Border
            {
                Stroke = Ui.Faint,
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                HeightRequest = 96,
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        Centered(Ui.Label("⇩", 24, color: Ui.Accent)),
                        Centered(Ui.Label("Drop files on the notch", 13, color: Ui.Dim)),
                        Centered(Ui.Label("then copy, AirDrop or reveal them from here", 11, color: Ui.Faint))
                    }
                }
            });
            return;
        }

        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 10 };
        for (var c = 0; c < Columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var shown = files.Take(MaxShown).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            if (i % Columns == 0)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Children.Add(Tile(tray, shown[i]).At(row: i / Columns, column: i % Columns));
        }
        this.Children.Add(grid);

        if (files.Count > MaxShown)
            this.Children.Add(Ui.Label($"+{files.Count - MaxShown} more", 11, color: Ui.Dim));

        this.Children.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Ui.Chip("Copy all", () => FileTrayService.Copy(tray.Files), selected: true),
                Ui.Chip("AirDrop", () => FileTrayService.AirDrop(tray.Files)),
                Ui.Chip("Reveal", () => FileTrayService.Reveal(tray.Files)),
                Ui.Chip("Clear", tray.Clear, tint: Ui.Danger)
            }
        });
    }

    static View Centered(Label label)
    {
        label.HorizontalOptions = LayoutOptions.Center;
        label.HorizontalTextAlignment = TextAlignment.Center;
        return label;
    }

    static View Tile(FileTrayService tray, string path)
    {
        var icon = FileTrayService.IconPng(path);
        var image = Ui.Artwork(icon, 44);
        image.Aspect = Aspect.AspectFit;
        image.HorizontalOptions = LayoutOptions.Center;
        image.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => FileTrayService.Open(path)) });

        var name = Ui.Label(System.IO.Path.GetFileName(path), 10, color: Ui.Dim, width: 72);
        name.HorizontalTextAlignment = TextAlignment.Center;
        name.LineBreakMode = LineBreakMode.MiddleTruncation;

        var actions = new HorizontalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                Ui.Glyph("⧉", () => FileTrayService.Copy([path]), 12, Ui.Accent, "Copy"),
                Ui.Glyph("⌕", () => FileTrayService.Reveal([path]), 12, Ui.Dim, "Reveal in Finder"),
                Ui.Glyph("✕", () => tray.Remove(path), 11, Ui.Faint, "Remove")
            }
        };

        return new VerticalStackLayout { Spacing = 2, Children = { image, name, actions } };
    }
}

sealed class StatsTab : VerticalStackLayout, ILiveView
{
    readonly SystemStatsService stats;
    readonly Label cpu, cpuSpark, gpu, memory, disk, net;

    public StatsTab(SystemStatsService stats)
    {
        this.stats = stats;
        this.Spacing = 8;

        Label Row(string name, Color color)
        {
            var value = Ui.Label("", 12, color: color, width: 330);
            this.Children.Add(new HorizontalStackLayout { Spacing = 10, Children = { Ui.Label(name, 12, bold: true, color: Ui.Dim, width: 56), value } });
            return value;
        }

        this.cpu = Row("CPU", Ui.Accent);
        this.cpuSpark = Row("", Ui.Faint);
        this.gpu = Row("GPU", Color.FromArgb("#A78BFA"));
        this.memory = Row("Memory", Color.FromArgb("#34D399"));
        this.disk = Row("Disk", Ui.Warm);
        this.net = Row("Network", Color.FromArgb("#F472B6"));

        this.Tick();
    }

    public void Tick()
    {
        var s = this.stats.Sample();
        this.cpu.Text = $"{Ui.Meter(s.CpuPercent / 100)}  {s.CpuPercent:0}%";
        this.cpuSpark.Text = Ui.Sparkline(this.stats.CpuHistory);
        this.gpu.Text = s.GpuPercent is { } g ? $"{Ui.Meter(g / 100)}  {g:0}%" : "not reported";
        this.memory.Text = $"{Ui.Meter(s.MemoryPercent / 100)}  {SystemStatsService.Bytes(s.MemoryUsed)} of {SystemStatsService.Bytes(s.MemoryTotal)}";
        this.disk.Text = $"read {SystemStatsService.Bytes(s.DiskReadPerSec)}/s   write {SystemStatsService.Bytes(s.DiskWritePerSec)}/s";
        this.net.Text = $"↓ {SystemStatsService.Bytes(s.NetInPerSec)}/s   ↑ {SystemStatsService.Bytes(s.NetOutPerSec)}/s";
    }
}

sealed class CalendarTab : VerticalStackLayout
{
    public CalendarTab(CalendarService calendar, Action rebuild)
    {
        this.Spacing = 8;

        if (!calendar.IsAuthorized)
        {
            if (calendar.IsDenied)
            {
                this.Children.Add(Ui.Label("Calendar access is turned off.", 13, color: Ui.Dim));
                this.Children.Add(Ui.Chip("Open Privacy Settings", () =>
                    AppKit.NSWorkspace.SharedWorkspace.OpenUrl(new Foundation.NSUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_Calendars")), selected: true));
            }
            else
            {
                this.Children.Add(Ui.Label("See your next events right in the notch.", 13, color: Ui.Dim));
                this.Children.Add(Ui.Chip("Allow calendar access", async () =>
                {
                    await calendar.RequestAccessAsync();
                    rebuild();
                }, selected: true));
            }
            return;
        }

        if (calendar.Upcoming.Count == 0)
        {
            this.Children.Add(Ui.Label("Nothing else today or tomorrow.", 13, color: Ui.Dim));
            return;
        }

        DateTime? day = null;
        foreach (var e in calendar.Upcoming.Take(6))
        {
            if (day != e.Start.Date)
            {
                day = e.Start.Date;
                var header = e.Start.Date == DateTime.Today ? "Today" : e.Start.Date == DateTime.Today.AddDays(1) ? "Tomorrow" : e.Start.ToString("dddd");
                this.Children.Add(Ui.Label(header.ToUpperInvariant(), 10, bold: true, color: Ui.Faint));
            }

            var when = e.AllDay ? "all day" : e.Start <= DateTime.Now ? "now" : e.Start.ToString("t");
            this.Children.Add(new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new BoxView { Color = e.Color, WidthRequest = 4, HeightRequest = 18, CornerRadius = 2, VerticalOptions = LayoutOptions.Center },
                    Ui.Label(when, 12, bold: true, color: e.Start <= DateTime.Now && !e.AllDay ? Ui.Accent : Ui.Dim, width: 64),
                    Ui.Label(e.Location == null ? e.Title : $"{e.Title}  ·  {e.Location}", 13, width: 380)
                }
            });
        }
    }
}

sealed class TimerTab : VerticalStackLayout, ILiveView
{
    readonly CountdownService countdown;
    readonly Label remaining;

    public TimerTab(CountdownService countdown)
    {
        this.countdown = countdown;
        this.Spacing = 12;

        this.remaining = Ui.Label("", 40, bold: true, width: Ui.PanelWidth - 28);
        this.remaining.HorizontalTextAlignment = TextAlignment.Center;
        this.Children.Add(this.remaining);

        var presets = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
        foreach (var minutes in new[] { 1, 5, 10, 15, 25, 45 })
            presets.Children.Add(Ui.Chip($"{minutes}m", () => countdown.Start(TimeSpan.FromMinutes(minutes))));
        this.Children.Add(presets);

        if (countdown.IsActive)
        {
            this.Children.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    Ui.Chip("+1m", () => countdown.Add(TimeSpan.FromMinutes(1))),
                    Ui.Chip(countdown.IsPaused ? "Resume" : "Pause", countdown.TogglePause, selected: true, tint: Ui.Warm),
                    Ui.Chip("Cancel", countdown.Cancel, tint: Ui.Danger)
                }
            });
        }

        this.Tick();
    }

    public void Tick()
    {
        this.remaining.Text = this.countdown.IsActive ? CountdownService.Format(this.countdown.Remaining) : "0:00";
        this.remaining.TextColor = this.countdown.IsPaused ? Ui.Dim : this.countdown.IsActive ? Ui.Warm : Ui.Faint;
    }
}

sealed class PrompterTab : VerticalStackLayout
{
    const double ViewportHeight = 150;

    readonly TeleprompterService prompter;
    readonly ScrollView scroller;
    readonly Label script;

    public PrompterTab(TeleprompterService prompter, Action openSettings)
    {
        this.prompter = prompter;
        this.Spacing = 10;

        this.script = new Label
        {
            Text = prompter.Script + "\n\n\n\n\n",
            FontSize = prompter.FontSize,
            TextColor = Ui.Text,
            LineBreakMode = LineBreakMode.WordWrap,
            WidthRequest = Ui.PanelWidth - 36
        };
        this.scroller = new ScrollView
        {
            HeightRequest = ViewportHeight,
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = this.script
        };
        this.Children.Add(this.scroller);

        this.Children.Add(new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                Ui.Glyph("⏮︎", prompter.Restart, 17, hint: "Restart"),
                Ui.Glyph(prompter.IsRunning ? "⏸︎" : "▶︎", prompter.Toggle, 22, Ui.Accent, prompter.IsRunning ? "Pause" : "Play"),
                Ui.Glyph("▲", () => prompter.Nudge(-40), 12, Ui.Dim, "Back"),
                Ui.Glyph("▼", () => prompter.Nudge(40), 12, Ui.Dim, "Forward"),
                Ui.Label("  speed", 11, color: Ui.Faint),
                Ui.Glyph("−", () => prompter.ChangeSpeed(-4), 16, hint: "Slower"),
                Ui.Glyph("+", () => prompter.ChangeSpeed(4), 16, hint: "Faster"),
                Ui.Label("  size", 11, color: Ui.Faint),
                Ui.Glyph("A", () => prompter.ChangeFontSize(-2), 12, hint: "Smaller text"),
                Ui.Glyph("A", () => prompter.ChangeFontSize(2), 19, hint: "Larger text"),
                Ui.Glyph("✎", openSettings, 15, Ui.Dim, "Edit script")
            }
        });
    }

    /// <summary>Follows the service's offset. Called on every teleprompter frame.</summary>
    public void Scroll()
    {
        var max = Math.Max(0, this.scroller.ContentSize.Height - ViewportHeight);
        if (max > 0 && this.prompter.Offset >= max)
            this.prompter.ReachedEnd();

        _ = this.scroller.ScrollToAsync(0, Math.Min(this.prompter.Offset, max > 0 ? max : this.prompter.Offset), false);
    }
}
