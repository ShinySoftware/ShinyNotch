using Shiny;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Desktop.FileDrop;
using Shiny.Maui.Controls.Desktop.Notch;
using ShinyNotch.Platform;
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
[Singleton]
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
    /// <summary>How long a finished countdown holds the panel open when nobody is hovering it.</summary>
    static readonly TimeSpan FinishedHold = TimeSpan.FromSeconds(6);
    /// <summary>How long a new song holds the panel open when nobody is hovering it.</summary>
    static readonly TimeSpan TrackChangeHold = TimeSpan.FromSeconds(4);
    static readonly TimeSpan WeatherCheckEvery = TimeSpan.FromMinutes(5);
    /// <summary>The store says when events change; this only catches the day moving on.</summary>
    static readonly TimeSpan CalendarRefreshEvery = TimeSpan.FromMinutes(15);
    /// <summary>A display change arrives as a burst of notifications; act once it settles.</summary>
    static readonly TimeSpan ScreensSettleDelay = TimeSpan.FromMilliseconds(400);
    /// <summary>How long the notch stays out, even with nothing to show, after its size is changed in Settings.</summary>
    static readonly TimeSpan SizePreviewHold = TimeSpan.FromSeconds(2.5);

    readonly INotchWindowService notch;
    readonly IFileDropService fileDrop;
    readonly AppSettings settings;
    readonly INowPlayingService media;
    readonly SystemControlsService controls;
    readonly SystemStatsService stats;
    readonly CalendarService calendar;
    readonly CountdownService countdown;
    readonly FileTrayService tray;
    readonly TeleprompterService prompter;
    readonly WeatherService weather;
    readonly VoiceNotesService notes;
    readonly TasksService tasks;
    readonly TaskEntry taskEntry;
    readonly bool hasMirror;
    readonly ICameraControls? cameraControls;
    readonly ICaptureTab? capture;
    readonly IPlatformShell shell;
    readonly IDispatcher dispatcher;
    readonly TimeProvider time;

    Mode mode = Mode.Peek;
    NotchTab tab;
    HudKind hudKind;
    ILiveView? live;
    string? shownKey;
    bool dragging;
    /// <summary>The drag carries an app the platform can trust, so the tray shows a target for that too.</summary>
    bool draggingApp;
    bool pinnedByUser;
    /// <summary>The last song seen, so a new one can be told from a play/pause or a refresh.</summary>
    string? lastTrackKey;
    /// <summary>The tab to go back to once a song-change pop-open folds away, so it doesn't change the user's tab.</summary>
    NotchTab? tabBeforePop;
    int hoverToken;
    int hudToken;
    bool rendering;
    bool renderQueued;
    DateTimeOffset nextCalendarRefresh;
    DateTimeOffset nextWeatherCheck;
    int tickToken;
    int screensToken;
    int previewToken;
    bool previewingSize;

    public NotchController(
        INotchWindowService notch,
        IFileDropService fileDrop,
        AppSettings settings,
        INowPlayingService media,
        SystemControlsService controls,
        SystemStatsService stats,
        CalendarService calendar,
        CountdownService countdown,
        FileTrayService tray,
        TeleprompterService prompter,
        WeatherService weather,
        VoiceNotesService notes,
        TasksService tasks,
        TaskEntry taskEntry,
        IEnumerable<MirrorSupport> mirror,
        IEnumerable<ICameraControls> cameraControls,
        IEnumerable<ICaptureTab> captures,
        IPlatformShell shell,
        IDispatcher dispatcher,
        TimeProvider time)
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
        this.weather = weather;
        this.notes = notes;
        this.tasks = tasks;
        this.taskEntry = taskEntry;
        this.hasMirror = mirror.Any();
        this.cameraControls = cameraControls.FirstOrDefault();
        this.capture = captures.FirstOrDefault();
        this.shell = shell;
        this.dispatcher = dispatcher;
        this.time = time;
        this.tab = Enum.TryParse<NotchTab>(settings.LastTab, out var t) ? t : NotchTab.Media;
    }

    /// <summary>Opens Settings. Set by the app, which owns the window.</summary>
    public Action? OpenSettings { get; set; }

    /// <summary>The tabs this platform has, with the capture tool's own name on the Capture tab.</summary>
    public IReadOnlyList<NotchTabInfo> AvailableTabs => NotchTabInfo.All
        .Where(t => t.Tab switch
        {
            NotchTab.Capture => this.capture is { IsAvailable: true },
            NotchTab.Mirror => this.hasMirror,
            _ => true
        })
        .Select(t => t.Tab == NotchTab.Capture ? t with { Name = this.capture!.Name } : t)
        .ToList();

    /// <summary>The tabs in the strip: the ones this platform has, less those turned off in Settings.</summary>
    IReadOnlyList<NotchTabInfo> ShownTabs
    {
        get
        {
            var hidden = this.settings.HiddenTabs;
            var shown = this.AvailableTabs.Where(t => !hidden.Contains(t.Tab.ToString())).ToList();
            // Settings never turns off the last one, but a tab the platform lost (macshot uninstalled) can.
            return shown.Count > 0 ? shown : [NotchTabInfo.All[0]];
        }
    }

    /// <summary>The notch should stay open even when the pointer leaves.</summary>
    bool IsPinned => this.pinnedByUser || this.dragging || this.prompter.IsRunning || this.notes.IsRecording || this.taskEntry.IsOpen;

    public async Task StartAsync()
    {
        this.notch.PointerEntered += (_, _) => this.OnPointer(true);
        this.notch.PointerExited += (_, _) => this.OnPointer(false);
        this.notch.Tapped += (_, _) => this.OnTapped();

        this.fileDrop.DragEnter += (_, e) =>
        {
            if (!IsNotchWindow(e.Window))
                return;
            this.dragging = true;
            this.draggingApp = e.Files.Any(f => f.FullPath is { } path && this.tray.Actions.CanRemoveQuarantine(path));
            this.Open(NotchTab.Tray);
        };
        this.fileDrop.DragOver += (_, e) =>
        {
            if (this.draggingApp && IsNotchWindow(e.Window))
                this.LiveTray?.DragMoved(e.Position);
        };
        this.fileDrop.DragLeave += (_, e) =>
        {
            if (!IsNotchWindow(e.Window))
                return;
            this.dragging = false;
            this.EndAppDrag();
            this.ScheduleClose();
        };
        this.fileDrop.Dropped += (_, e) =>
        {
            if (!IsNotchWindow(e.Window))
                return;
            this.dragging = false;
            var paths = e.Files.Select(f => f.FullPath).OfType<string>().ToList();
            // Dropped on the trust target: the apps are trusted, not kept. Anything else in the drag still goes in the tray.
            var trust = this.draggingApp && this.LiveTray?.IsOverTrust(e.Position) == true;
            this.draggingApp = false;
            var apps = trust ? paths.Where(this.tray.Actions.CanRemoveQuarantine).ToList() : [];
            this.tray.Add(paths.Except(apps));
            foreach (var app in apps)
                _ = this.tray.RemoveQuarantineAsync(app);
            this.Open(NotchTab.Tray);
            this.ScheduleClose();
        };

        this.media.Changed += (_, _) =>
        {
            this.PopOpenOnNewTrack();
            this.Rebuild(NotchTab.Media);
            // Any tab: the controls bar's play/pause follows the player.
            (this.live as PanelView)?.Controls.Tick();
            this.ScheduleTick();
        };
        this.countdown.StateChanged += (_, _) =>
        {
            this.Rebuild(NotchTab.Timer);
            this.ScheduleTick();
        };
        this.countdown.Finished += (_, _) =>
        {
            // Opened to say so, not by a hover - so no pointer leaving will close it. Hovering it keeps it.
            this.Open(NotchTab.Timer);
            this.ScheduleClose(FinishedHold);
        };
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
        this.weather.Changed += (_, _) => this.Rebuild(NotchTab.Weather);
        this.tasks.Changed += (_, _) => this.Rebuild(NotchTab.Tasks);
        // Typing a task holds the panel open behind the popup; done typing, it closes as usual.
        this.taskEntry.Closed += (_, _) => this.ScheduleClose();
        this.notes.Changed += (_, _) =>
        {
            this.Rebuild(NotchTab.Notes);
            this.ScheduleTick();
            // A recording keeps the panel open; once it's done, the panel goes back to closing on its own.
            if (!this.notes.IsRecording)
                this.ScheduleClose();
        };
        if (this.capture != null)
            this.capture.Changed += (_, _) => this.Rebuild(NotchTab.Capture);
        this.shell.ScreensChanged += (_, _) => this.OnScreensChanged();
        // Only the settings the Prompter tab draws with; the speed (say) changes nothing on screen.
        this.settings.Changed += (_, key) =>
        {
            if (key is nameof(AppSettings.TeleprompterScript) or nameof(AppSettings.TeleprompterFontSize))
                this.Rebuild(NotchTab.Prompter);
            // The tab strip changes. Turned off while showing it, the panel moves to Now Playing.
            else if (key is nameof(AppSettings.HiddenTabs) && this.mode == Mode.Panel)
            {
                if (this.ShownTabs.All(t => t.Tab != this.tab))
                    this.Open(this.ShownTabs[0].Tab);
                else
                    _ = this.RenderAsync();
            }
            else if (key is nameof(AppSettings.NotchLeftWidth) or nameof(AppSettings.NotchRightWidth) or nameof(AppSettings.NotchWidthsLocked) or nameof(AppSettings.NotchExtraHeight))
                this.PreviewSize();
        };

        this.media.Start();
        this.controls.Start();
        _ = this.calendar.RefreshAsync();
        _ = this.weather.RefreshAsync();
        var now = this.time.GetUtcNow();
        this.nextCalendarRefresh = now + CalendarRefreshEvery;
        this.nextWeatherCheck = now + WeatherCheckEvery;
        this.ScheduleTick();

        await this.notch.ShowAsync(this.BuildPeek(), this.ConfigurePeek);
        this.shownKey = this.Key();
    }

    static bool IsNotchWindow(Window? window) => window?.Title == "Notch";

    /// <summary>
    /// A display was plugged in, removed or rescaled. The notch service only places its window (and reads
    /// the camera cut-out) when content changes, so the window would keep its old global frame - which,
    /// once the primary display changes, is somewhere in the middle of a screen. Re-render so the content
    /// and the service pick up the new cut-out, then show again to snap the window to its new spot.
    /// </summary>
    void OnScreensChanged()
    {
        var token = ++this.screensToken;
        this.dispatcher.DispatchDelayed(ScreensSettleDelay, async () =>
        {
            if (token != this.screensToken)
                return;

            this.shownKey = null;
            await this.RenderAsync();
            await this.notch.ShowAsync();
        });
    }

    /// <summary>
    /// A size slider moved. The peek is what it sizes, so it is re-rendered (and held out for a moment
    /// even when empty, or there would be nothing to watch). The panel picks the new size up on closing.
    /// </summary>
    void PreviewSize()
    {
        var token = ++this.previewToken;
        this.previewingSize = true;
        if (this.mode == Mode.Peek)
            _ = this.RenderAsync();

        this.dispatcher.DispatchDelayed(SizePreviewHold, () =>
        {
            if (token != this.previewToken)
                return;
            this.previewingSize = false;
            if (this.mode == Mode.Peek)
                _ = this.RenderAsync();
        });
    }

    /// <summary>
    /// The collapsed notch's height: exactly the camera's, plus whatever was asked for in Settings. With no
    /// camera notch (an external display), a menu bar's height.
    /// </summary>
    double PeekHeight => (this.notch.HardwareNotchSize.Height is > 0 and var h ? h : 32) + this.settings.NotchExtraHeight;

    string SizeKey => $"{this.settings.NotchLeftWidth}|{this.settings.NotchRightWidth}|{this.settings.NotchWidthsLocked}|{this.settings.NotchExtraHeight}";

    // ---------------------------------------------------------------------------------------------
    // Hover
    // ---------------------------------------------------------------------------------------------

    void OnPointer(bool over)
    {
        // Either way a pending close is called off; with click to open, coming back is all hovering does.
        var token = ++this.hoverToken;
        if (over)
        {
            if (this.settings.ClickToOpen)
                return;

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

    /// <summary>With click to open, a click on the peek (or a HUD) opens the panel. Hovering opens it otherwise.</summary>
    void OnTapped()
    {
        if (this.settings.ClickToOpen && this.mode != Mode.Panel)
            this.Open(this.tab);
    }

    void ScheduleClose(TimeSpan? delay = null)
    {
        var token = ++this.hoverToken;
        this.dispatcher.DispatchDelayed(delay ?? HoverCloseDelay, () =>
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

    /// <summary>
    /// Folds the panel back to the peek, pinned or not, for a window that opens where it was (Settings).
    /// Hovering opens it again as usual.
    /// </summary>
    public void FoldAway()
    {
        this.pinnedByUser = false;
        if (this.mode == Mode.Panel)
            this.Show(Mode.Peek);
    }

    /// <summary>Opens the panel on a tab and keeps it open until unpinned.</summary>
    public void OpenPinned(NotchTab tab)
    {
        this.pinnedByUser = true;
        this.Open(tab);
    }

    void Open(NotchTab tab)
    {
        // The tab was turned off, or the platform lost it (macshot uninstalled), since it was last open.
        var shown = this.ShownTabs;
        if (shown.All(t => t.Tab != tab))
            tab = shown[0].Tab;
        if (tab == NotchTab.Capture)
            this.capture?.Opened();
        this.tab = tab;
        this.tabBeforePop = null;
        this.settings.LastTab = tab.ToString();
        if (tab == NotchTab.Weather)
            _ = this.weather.RefreshAsync();
        // Coming back to the calendar starts on today, not whatever day was last picked.
        if (tab == NotchTab.Calendar && this.calendar.SelectedDate != null)
            _ = this.calendar.SelectAsync(null);
        // The position is carried forward between readings; opening the player is the moment to take one.
        if (tab == NotchTab.Media)
            this.media.Refresh();
        this.Show(Mode.Panel);
    }

    void ShowHud(HudKind kind)
    {
        if (this.mode == Mode.Panel)
        {
            // The panel has the sliders under every tab already; just keep them current. Only the bar:
            // ticking the whole panel would also take an extra, off-schedule reading on the Stats tab.
            (this.live as PanelView)?.Controls.Tick();
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

    /// <summary>
    /// A new song (not a play/pause, and not the first one seen at launch) opens Now Playing for a few
    /// seconds, if the panel isn't already open. Hovering keeps it, like a finished countdown.
    /// </summary>
    void PopOpenOnNewTrack()
    {
        var now = this.media.Current;
        var previous = this.lastTrackKey;
        this.lastTrackKey = now?.TrackKey ?? previous;
        if (now is not { IsPlaying: true } || previous == null || now.TrackKey == previous)
            return;
        if (!this.settings.ShowTrackChanges || this.mode == Mode.Panel)
            return;

        var before = this.tab;
        this.Open(NotchTab.Media);
        this.tabBeforePop = before;
        this.settings.LastTab = before.ToString();
        this.ScheduleClose(TrackChangeHold);
    }

    void Show(Mode next)
    {
        // A song-change pop-open folding away hands the user's own tab back.
        if (next != Mode.Panel && this.tabBeforePop is { } restore)
        {
            this.tab = restore;
            this.tabBeforePop = null;
        }
        this.mode = next;
        _ = this.RenderAsync();
        this.ScheduleTick();
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
        Mode.Peek => $"peek|{this.SizeKey}|{this.previewingSize}|{this.media.Current?.TrackKey}|{this.media.Current?.IsPlaying}|{this.media.Artwork?.Length}|{this.countdown.IsActive}|{this.countdown.IsPaused}|{this.calendar.StartingWithin(TimeSpan.FromMinutes(15))?.Title}|{this.weather.Selected?.Temperature:0}|{this.weather.Selected?.Code}",
        Mode.Hud => $"hud|{this.SizeKey}",
        // Panels are rebuilt on every structural change, and those are rare.
        _ => $"panel|{this.tab}|{Guid.NewGuid()}"
    };

    /// <summary>
    /// Renders the current state. One render at a time: asking during one queues another, which renders
    /// whatever is current when it runs. Overlapping renders could land out of order, leaving the notch on
    /// older content than <see cref="live"/> and <see cref="shownKey"/> describe.
    /// </summary>
    async Task RenderAsync()
    {
        if (this.rendering)
        {
            this.renderQueued = true;
            return;
        }

        this.rendering = true;
        try
        {
            do
            {
                this.renderQueued = false;
                await this.RenderOnceAsync();
            }
            while (this.renderQueued);
        }
        finally
        {
            this.rendering = false;
        }
    }

    async Task RenderOnceAsync()
    {
        var key = this.Key();
        if (key == this.shownKey)
            return;
        this.shownKey = key;

        // What's on screen now is about to be replaced. Something that holds on to more than views (the
        // mirror's camera, a level meter's subscription) lets go first: once the content is swapped its
        // handlers are out of reach, and the camera would keep running behind the new tab.
        (this.live as IDisposable)?.Dispose();

        switch (this.mode)
        {
            case Mode.Peek:
                var peek = this.BuildPeek();
                this.live = peek;
                await this.notch.SetContentAsync(peek, this.ConfigurePeek);
                break;

            case Mode.Hud:
                var cutout = this.notch.HardwareNotchSize;
                var hud = new HudView(this.hudKind, cutout.Width, this.settings.NotchLeftWidth, this.settings.NotchRightWidth, this.settings.NotchWidthsLocked, this.PeekHeight, this.controls);
                this.live = hud;
                await this.notch.SetContentAsync(hud, this.ConfigurePeek);
                break;

            default:
                var panel = this.BuildPanel();
                this.live = panel;
                await this.notch.SetContentAsync(panel, this.ConfigurePanel);
                // A fresh teleprompter starts at the top; take it back to where the script is.
                (panel.Body as PrompterTab)?.Scroll();
                break;
        }
    }

    PeekView? BuildPeek()
    {
        var cutout = this.notch.HardwareNotchSize;
        return PeekView.Create(cutout.Width, this.settings.NotchLeftWidth, this.settings.NotchRightWidth, this.settings.NotchWidthsLocked, this.PeekHeight, this.previewingSize, this.media, this.countdown, this.calendar, this.weather, this.time);
    }

    PanelView BuildPanel()
    {
        View body = this.tab switch
        {
            NotchTab.Media => new MediaTab(this.media),
            NotchTab.Weather => new WeatherTab(this.weather, () => this.OpenSettings?.Invoke()),
            NotchTab.Tray => this.BuildTray(),
            NotchTab.Stats => new StatsTab(this.stats),
            NotchTab.Calendar => new CalendarTab(this.calendar, this.time, this.shell, () => _ = this.RenderAsync()),
            NotchTab.Timer => new TimerTab(this.countdown),
            NotchTab.Capture when this.capture != null => this.capture.Build(this.FoldAway),
            NotchTab.Mirror => new MirrorTab(this.settings, this.tray, this.cameraControls),
            NotchTab.Notes => new NotesTab(this.notes, this.tray),
            NotchTab.Tasks => new TasksTab(this.tasks, this.taskEntry.Show),
            _ => new PrompterTab(this.prompter, () => this.OpenSettings?.Invoke())
        };

        var cutout = this.notch.HardwareNotchSize;
        var controls = new ControlsBar(this.media, this.controls, showPlay: this.tab != NotchTab.Media);
        return new PanelView(this.tab, this.ShownTabs, body, controls, cutout.Width, Math.Max(cutout.Height, 32), this.pinnedByUser, this.Open, this.TogglePin, () => this.OpenSettings?.Invoke());
    }

    TrayTab BuildTray()
    {
        this.tray.Prune();
        return new TrayTab(this.tray, this.draggingApp);
    }

    TrayTab? LiveTray => (this.live as PanelView)?.Body as TrayTab;

    /// <summary>An app drag that left without dropping: back to the plain tray.</summary>
    void EndAppDrag()
    {
        if (!this.draggingApp)
            return;
        this.draggingApp = false;
        this.Rebuild(NotchTab.Tray);
    }

    /// <summary>
    /// Wings beside the camera, at the camera's own height (or more, if asked for). No padding at all: the
    /// wings keep their own distance from the edge, so a wing sized to 0 is flush with the camera. And no
    /// surface: the window can only grow evenly from the camera, so the peek draws its own black, over
    /// just the wings asked for (see <see cref="PeekView"/>).
    /// </summary>
    void ConfigurePeek(NotchView view)
    {
        view.ContentPadding = new Thickness(0);
        view.SurfaceColor = Colors.Transparent;
        view.MinNotchHeight = this.PeekHeight;
        view.CornerRadius = 12;
    }

    /// <summary>The panel starts in the camera band too (its tab strip lives there).</summary>
    void ConfigurePanel(NotchView view)
    {
        view.ContentPadding = new Thickness(14, 0, 14, 16);
        view.CornerRadius = 22;
    }

    // ---------------------------------------------------------------------------------------------
    // Clock
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Something on screen counts seconds: a running countdown (the peek and the Timer tab show it), the
    /// player's position, the Stats tab's sampling, or the Capture tab watching for a recording. Everything else changes by the minute at most.
    /// </summary>
    bool NeedsSeconds =>
        this.countdown.IsRunning ||
        (this.mode == Mode.Panel && this.tab switch
        {
            NotchTab.Media => this.media.Current?.IsPlaying == true,
            NotchTab.Stats => true,
            // Watching for the capture tool's state (a recording starting or ending).
            NotchTab.Capture => true,
            NotchTab.Notes => this.notes.IsRecording,
            _ => false
        });

    /// <summary>
    /// The notch's clock. Instead of a timer firing every second for ever, the next tick is set for when
    /// it is needed: the next second while something counts seconds, else the next minute. A resting
    /// notch wakes the app once a minute. Re-scheduled whenever what is on screen changes.
    /// </summary>
    void ScheduleTick()
    {
        var token = ++this.tickToken;
        var now = this.time.GetUtcNow();
        // On the boundary, so a clock steps with the system's rather than up to a second behind it.
        var delay = this.NeedsSeconds
            ? TimeSpan.FromMilliseconds(1000 - now.Millisecond)
            : NextMinute(now) - now;

        this.dispatcher.DispatchDelayed(delay, () =>
        {
            if (token != this.tickToken)
                return;
            this.OnTick();
            this.ScheduleTick();
        });
    }

    void OnTick()
    {
        // Due times rather than "second == 0": a tick can be late (App Nap, a busy main thread), and a
        // skipped second must not be a skipped refresh.
        var now = this.time.GetUtcNow();

        // Checked-off tasks clear away a day later; the minute tick is plenty for that.
        this.tasks.Prune();

        if (now >= this.nextCalendarRefresh)
        {
            this.nextCalendarRefresh = now + CalendarRefreshEvery;
            _ = this.calendar.RefreshAsync();
        }

        // The forecast refreshes itself when stale (20 minutes); asking every five is cheap.
        if (now >= this.nextWeatherCheck)
        {
            this.nextWeatherCheck = now + WeatherCheckEvery;
            _ = this.weather.RefreshAsync();
        }

        if (this.mode == Mode.Panel && this.tab == NotchTab.Capture)
            this.capture?.Tick();

        this.live?.Tick();

        // The peek re-renders when what it would show has changed - a meeting coming within 15 minutes.
        if (this.mode == Mode.Peek && this.Key() != this.shownKey)
            _ = this.RenderAsync();
    }

    static DateTimeOffset NextMinute(DateTimeOffset now)
        => new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset).AddMinutes(1);
}
