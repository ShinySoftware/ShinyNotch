using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Desktop.FileDrop;
using Shiny.Maui.Controls.Desktop.Notch;
using ShinyNotch.Services;
using ShinyNotch.Views;

namespace ShinyNotch;

/// <summary>
/// Decides what the notch shows, and when.
/// </summary>
/// <remarks>
/// <para>
/// Three modes. <b>Peek</b> is the resting state: a wing either side of the camera with what's playing and
/// the countdown — or nothing, in which case the notch vanishes into the camera housing. <b>Panel</b> is
/// the expanded notch with tabs, opened by hovering. <b>Hud</b> flashes for a moment when the volume or
/// brightness keys are used.
/// </para>
/// <para>
/// The content is rebuilt whole when its structure changes (a new mode, a new tab, a new track, a file
/// dropped) and ticked in place otherwise, because the AppKit head will not realise views added to a
/// layout that is already on screen. A rebuild that would produce the same thing is skipped.
/// </para>
/// </remarks>
public sealed class NotchController
{
    enum Mode
    {
        Peek,
        Panel,
        Hud
    }

    static readonly TimeSpan HoverOpenDelay = TimeSpan.FromMilliseconds(140);
    static readonly TimeSpan HoverCloseDelay = TimeSpan.FromMilliseconds(380);
    static readonly TimeSpan HudDuration = TimeSpan.FromSeconds(1.6);

    readonly INotchWindowService notch;
    readonly IFileDropService fileDrop;
    readonly AppSettings settings;
    readonly NowPlayingService media;
    readonly SystemControlsService controls;
    readonly SystemStatsService stats;
    readonly CalendarService calendar;
    readonly CountdownService countdown;
    readonly FileTrayService tray;
    readonly TeleprompterService prompter;
    readonly IDispatcher dispatcher;

    Mode mode = Mode.Peek;
    NotchTab tab;
    HudKind hudKind;
    ILiveView? live;
    string? shownKey;
    bool dragging;
    bool pinnedByUser;
    int hoverToken;
    int hudToken;
    IDispatcherTimer? ticker;

    public NotchController(
        INotchWindowService notch,
        IFileDropService fileDrop,
        AppSettings settings,
        NowPlayingService media,
        SystemControlsService controls,
        SystemStatsService stats,
        CalendarService calendar,
        CountdownService countdown,
        FileTrayService tray,
        TeleprompterService prompter,
        IDispatcher dispatcher)
    {
        this.notch = notch;
        this.fileDrop = fileDrop;
        this.settings = settings;
        this.media = media;
        this.controls = controls;
        this.stats = stats;
        this.calendar = calendar;
        this.countdown = countdown;
        this.tray = tray;
        this.prompter = prompter;
        this.dispatcher = dispatcher;
        this.tab = Enum.TryParse<NotchTab>(settings.LastTab, out var t) ? t : NotchTab.Media;
    }

    /// <summary>Opens Settings. Set by the app, which owns the window.</summary>
    public Action? OpenSettings { get; set; }

    /// <summary>The notch should stay open even when the pointer leaves.</summary>
    bool IsPinned => this.pinnedByUser || this.dragging || this.prompter.IsRunning;

    public async Task StartAsync()
    {
        this.notch.PointerEntered += (_, _) => this.OnPointer(true);
        this.notch.PointerExited += (_, _) => this.OnPointer(false);

        this.fileDrop.DragEnter += (_, e) =>
        {
            if (!IsNotchWindow(e.Window))
                return;
            this.dragging = true;
            this.Open(NotchTab.Tray);
        };
        this.fileDrop.DragLeave += (_, e) =>
        {
            if (!IsNotchWindow(e.Window))
                return;
            this.dragging = false;
            this.ScheduleClose();
        };
        this.fileDrop.Dropped += (_, e) =>
        {
            if (!IsNotchWindow(e.Window))
                return;
            this.dragging = false;
            this.tray.Add(e.Files.Select(f => f.FullPath).OfType<string>());
            this.Open(NotchTab.Tray);
            this.ScheduleClose();
        };

        this.media.Changed += (_, _) => this.Rebuild(NotchTab.Media);
        this.countdown.StateChanged += (_, _) => this.Rebuild(NotchTab.Timer);
        this.countdown.Finished += (_, _) => this.Open(NotchTab.Timer);
        this.calendar.Changed += (_, _) => this.Rebuild(NotchTab.Calendar);
        this.tray.Changed += (_, _) => this.Rebuild(NotchTab.Tray);
        this.prompter.StateChanged += (_, _) =>
        {
            this.Rebuild(NotchTab.Prompter);
            if (!this.prompter.IsRunning)
                this.ScheduleClose();
        };
        this.prompter.Scrolled += (_, _) =>
        {
            if (this.live is PanelView { Body: PrompterTab prompterTab })
                prompterTab.Scroll();
        };
        this.controls.ChangedExternally += (_, kind) => this.ShowHud(kind);
        this.settings.Changed += (_, _) => this.Rebuild(NotchTab.Prompter);

        this.media.Start();
        this.controls.Start();
        this.calendar.Refresh();

        this.ticker = this.dispatcher.CreateTimer();
        this.ticker.Interval = TimeSpan.FromSeconds(1);
        this.ticker.Tick += (_, _) => this.OnTick();
        this.ticker.Start();

        await this.notch.ShowAsync(this.BuildPeek(), this.ConfigurePeek);
        this.shownKey = this.Key();
    }

    static bool IsNotchWindow(Window? window) => window?.Title == "Notch";

    // ---------------------------------------------------------------------------------------------
    // Hover
    // ---------------------------------------------------------------------------------------------

    void OnPointer(bool over)
    {
        var token = ++this.hoverToken;
        if (over)
        {
            this.dispatcher.DispatchDelayed(HoverOpenDelay, () =>
            {
                if (token == this.hoverToken && this.notch.IsPointerOver && this.mode != Mode.Panel)
                    this.Open(this.tab);
            });
        }
        else
        {
            this.ScheduleClose();
        }
    }

    void ScheduleClose()
    {
        var token = ++this.hoverToken;
        this.dispatcher.DispatchDelayed(HoverCloseDelay, () =>
        {
            if (token == this.hoverToken && !this.notch.IsPointerOver && !this.IsPinned && this.mode == Mode.Panel)
                this.Show(Mode.Peek);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Modes
    // ---------------------------------------------------------------------------------------------

    void TogglePin()
    {
        this.pinnedByUser = !this.pinnedByUser;
        _ = this.RenderAsync();
        if (!this.pinnedByUser)
            this.ScheduleClose();
    }

    /// <summary>Opens the panel on a tab and keeps it open until unpinned.</summary>
    public void OpenPinned(NotchTab tab)
    {
        this.pinnedByUser = true;
        this.Open(tab);
    }

    void Open(NotchTab tab)
    {
        this.tab = tab;
        this.settings.LastTab = tab.ToString();
        this.Show(Mode.Panel);
    }

    void ShowHud(HudKind kind)
    {
        if (this.mode == Mode.Panel)
        {
            // The panel has the sliders on its Media tab already; just keep them current.
            this.live?.Tick();
            return;
        }

        if (kind == HudKind.Volume && !this.settings.ShowVolumeHud)
            return;
        if (kind == HudKind.Brightness && !this.settings.ShowBrightnessHud)
            return;

        var token = ++this.hudToken;
        if (this.mode == Mode.Hud && this.live is HudView hud)
        {
            // A held key: update in place, and push the dismissal back.
            hud.Show(kind);
            this.hudKind = kind;
        }
        else
        {
            this.hudKind = kind;
            this.Show(Mode.Hud);
        }

        this.dispatcher.DispatchDelayed(HudDuration, () =>
        {
            if (token == this.hudToken && this.mode == Mode.Hud)
                this.Show(Mode.Peek);
        });
    }

    void Show(Mode next)
    {
        this.mode = next;
        _ = this.RenderAsync();
    }

    /// <summary>
    /// Something behind <paramref name="tab"/> changed structurally. The peek shows a little of every tab,
    /// so it is always re-rendered (and skipped if it would come out the same); the panel only when it is
    /// showing that tab. A HUD is left alone - it goes back to the peek on its own.
    /// </summary>
    void Rebuild(NotchTab tab)
    {
        if (this.mode == Mode.Peek || (this.mode == Mode.Panel && this.tab == tab))
            _ = this.RenderAsync();
    }

    string Key() => this.mode switch
    {
        Mode.Peek => $"peek|{this.media.Current?.TrackKey}|{this.media.Current?.IsPlaying}|{this.media.Artwork?.Length}|{this.countdown.IsActive}|{this.countdown.IsPaused}|{this.calendar.StartingWithin(TimeSpan.FromMinutes(15))?.Title}",
        Mode.Hud => "hud",
        // Panels are rebuilt on every structural change, and those are rare.
        _ => $"panel|{this.tab}|{Guid.NewGuid()}"
    };

    async Task RenderAsync()
    {
        var key = this.Key();
        if (key == this.shownKey)
            return;
        this.shownKey = key;

        switch (this.mode)
        {
            case Mode.Peek:
                var peek = this.BuildPeek();
                this.live = peek;
                await this.notch.SetContentAsync(peek, this.ConfigurePeek);
                break;

            case Mode.Hud:
                var cutout = this.notch.HardwareNotchSize;
                var hud = new HudView(this.hudKind, cutout.Width, Math.Max(cutout.Height, 32), this.controls);
                this.live = hud;
                await this.notch.SetContentAsync(hud, this.ConfigurePeek);
                break;

            default:
                var panel = this.BuildPanel();
                this.live = panel;
                await this.notch.SetContentAsync(panel, this.ConfigurePanel);
                break;
        }
    }

    PeekView? BuildPeek()
    {
        var cutout = this.notch.HardwareNotchSize;
        return PeekView.Create(cutout.Width, Math.Max(cutout.Height, 32), this.media, this.countdown, this.calendar);
    }

    PanelView BuildPanel()
    {
        View body = this.tab switch
        {
            NotchTab.Media => new MediaTab(this.media, this.controls),
            NotchTab.Tray => this.BuildTray(),
            NotchTab.Stats => new StatsTab(this.stats),
            NotchTab.Calendar => new CalendarTab(this.calendar, () => _ = this.RenderAsync()),
            NotchTab.Timer => new TimerTab(this.countdown),
            _ => new PrompterTab(this.prompter, () => this.OpenSettings?.Invoke())
        };

        var cutout = this.notch.HardwareNotchSize;
        return new PanelView(this.tab, body, cutout.Width, Math.Max(cutout.Height, 32), this.pinnedByUser, this.Open, this.TogglePin, () => this.OpenSettings?.Invoke());
    }

    TrayTab BuildTray()
    {
        this.tray.Prune();
        return new TrayTab(this.tray);
    }

    /// <summary>Wings beside the camera: no padding above, the camera's own height.</summary>
    void ConfigurePeek(NotchView view)
    {
        var cutout = this.notch.HardwareNotchSize;
        view.ContentPadding = new Thickness(12, 0);
        view.MinNotchHeight = Math.Max(cutout.Height, 32);
        view.CornerRadius = 12;
    }

    /// <summary>The panel starts in the camera band too (its tab strip lives there).</summary>
    void ConfigurePanel(NotchView view)
    {
        view.ContentPadding = new Thickness(14, 0, 14, 16);
        view.CornerRadius = 22;
    }

    void OnTick()
    {
        // Calendar entries and "in 5m" peeks move on their own.
        if (DateTime.Now.Second == 0)
            this.calendar.Refresh();

        this.live?.Tick();

        if (this.mode == Mode.Peek && this.Key() != this.shownKey)
            _ = this.RenderAsync();
    }
}
