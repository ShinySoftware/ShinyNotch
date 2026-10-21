using Shiny;
using ShinyNotch.Platform;
using global::Windows.Media.Control;
using global::Windows.Storage.Streams;

namespace ShinyNotch.Windows;

/// <summary>
/// What's playing, through the system media transport controls (the session behind the media flyout):
/// any app that reports to Windows - Spotify, a browser, Media Player - with no per-app code.
/// </summary>
/// <remarks>
/// The session manager raises events as the current session, its track, its play state or its timeline
/// change, on its own threads; each is read and published on the main thread. The timeline is only
/// reported now and then, so the position is carried forward on the clock between reports, as on macOS.
/// </remarks>
[Singleton]
sealed class WindowsNowPlayingService : INowPlayingService, IDisposable
{
    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    readonly IPlatformUi ui;
    GlobalSystemMediaTransportControlsSessionManager? manager;
    GlobalSystemMediaTransportControlsSession? session;
    DateTimeOffset sampledAt;
    string? artworkKey;
    int readVersion;

    public WindowsNowPlayingService(IDispatcher dispatcher, TimeProvider time, IPlatformUi ui)
    {
        this.dispatcher = dispatcher;
        this.time = time;
        this.ui = ui;
    }

    public NowPlaying? Current { get; private set; }

    public byte[]? Artwork { get; private set; }

    public Color? ArtworkColor { get; private set; }

    public double Position
    {
        get
        {
            if (this.Current is not { } now)
                return 0;
            if (!now.IsPlaying)
                return now.Position;

            var position = now.Position + (this.time.GetUtcNow() - this.sampledAt).TotalSeconds;
            return now.Duration > 0 ? Math.Min(position, now.Duration) : position;
        }
    }

    public event EventHandler? Changed;

    public async void Start()
    {
        if (this.manager != null)
            return;

        try
        {
            this.manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception)
        {
            // No media session service (Windows N without the media pack): nothing ever plays.
            return;
        }

        this.manager.CurrentSessionChanged += (_, _) => this.dispatcher.Dispatch(this.Follow);
        this.Follow();
    }

    public void Refresh() => _ = this.ReadAsync();

    public void PlayPause() => this.Command(s => s.TryTogglePlayPauseAsync().AsTask());
    public void Next() => this.Command(s => s.TrySkipNextAsync().AsTask());
    public void Previous() => this.Command(s => s.TrySkipPreviousAsync().AsTask());

    async void Command(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        if (this.session == null)
            return;

        try
        {
            await command(this.session);
        }
        catch (Exception)
        {
            // The app went away mid-command; the session change says so.
        }
        await this.ReadAsync();
    }

    /// <summary>Moves the event handlers to the session the system now calls current, and reads it.</summary>
    void Follow()
    {
        var next = this.manager?.GetCurrentSession();
        if (ReferenceEquals(next, this.session))
        {
            _ = this.ReadAsync();
            return;
        }

        if (this.session != null)
        {
            this.session.MediaPropertiesChanged -= this.OnSessionChanged;
            this.session.PlaybackInfoChanged -= this.OnSessionChanged;
            this.session.TimelinePropertiesChanged -= this.OnSessionChanged;
        }
        this.session = next;
        if (next != null)
        {
            next.MediaPropertiesChanged += this.OnSessionChanged;
            next.PlaybackInfoChanged += this.OnSessionChanged;
            next.TimelinePropertiesChanged += this.OnSessionChanged;
        }
        _ = this.ReadAsync();
    }

    void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        => this.dispatcher.Dispatch(() => _ = this.ReadAsync());

    /// <summary>
    /// Reads the session and publishes it. Reads overlap (a track change raises three events at once), so
    /// only the newest one publishes.
    /// </summary>
    async Task ReadAsync()
    {
        var version = ++this.readVersion;
        var session = this.session;
        NowPlaying? next = null;
        GlobalSystemMediaTransportControlsSessionMediaProperties? properties = null;
        DateTimeOffset sampledAt = this.time.GetUtcNow();

        if (session != null)
        {
            try
            {
                properties = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();

                var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
                var position = (timeline.Position - timeline.StartTime).TotalSeconds;
                // The position is as of the last report, not now: carry it to now while playing.
                if (playing && timeline.LastUpdatedTime > DateTimeOffset.MinValue && timeline.LastUpdatedTime <= sampledAt)
                    sampledAt = timeline.LastUpdatedTime;

                if (properties != null && !String.IsNullOrWhiteSpace(properties.Title) &&
                    playback.PlaybackStatus is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                {
                    next = new NowPlaying(
                        AppName(session.SourceAppUserModelId),
                        properties.Title,
                        properties.Artist ?? properties.AlbumArtist ?? "",
                        properties.AlbumTitle ?? "",
                        Math.Max(0, position),
                        Math.Max(0, duration),
                        playing
                    );
                }
            }
            catch (Exception)
            {
                // The app closed while being read: nothing playing.
            }
        }

        if (version != this.readVersion)
            return;

        var before = this.Current;
        this.Current = next;
        this.sampledAt = sampledAt;

        var changed = before?.TrackKey != next?.TrackKey || before?.IsPlaying != next?.IsPlaying;
        if (next == null)
        {
            this.artworkKey = null;
            this.Artwork = null;
            this.ArtworkColor = null;
        }
        else if (next.TrackKey != this.artworkKey)
        {
            this.artworkKey = next.TrackKey;
            this.Artwork = null;
            this.ArtworkColor = null;
            _ = this.LoadArtworkAsync(next, properties?.Thumbnail);
        }

        if (changed)
            this.Changed?.Invoke(this, EventArgs.Empty);
    }

    async Task LoadArtworkAsync(NowPlaying now, IRandomAccessStreamReference? thumbnail)
    {
        byte[]? bytes = null;
        try
        {
            if (thumbnail != null)
            {
                using var stream = await thumbnail.OpenReadAsync();
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                bytes = new byte[stream.Size];
                reader.ReadBytes(bytes);
            }
        }
        catch (Exception)
        {
            // No artwork is a fine answer.
        }

        if (this.artworkKey != now.TrackKey)
            return;

        this.Artwork = bytes is { Length: > 0 } ? bytes : null;
        // A thumbnail often lands a moment after the track: without one yet, wait for the next report.
        if (this.Artwork == null)
        {
            this.artworkKey = null;
            return;
        }
        this.ArtworkColor = ArtworkAccent.From(this.ui, this.Artwork);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Spotify.exe" or "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" → "Spotify".</summary>
    static string AppName(string appUserModelId)
    {
        var name = appUserModelId;
        var bang = name.LastIndexOf('!');
        if (bang >= 0)
            name = name[(bang + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return String.IsNullOrWhiteSpace(name) ? "Media" : name;
    }

    public void Dispose()
    {
        if (this.session != null)
        {
            this.session.MediaPropertiesChanged -= this.OnSessionChanged;
            this.session.PlaybackInfoChanged -= this.OnSessionChanged;
            this.session.TimelinePropertiesChanged -= this.OnSessionChanged;
        }
    }
}
