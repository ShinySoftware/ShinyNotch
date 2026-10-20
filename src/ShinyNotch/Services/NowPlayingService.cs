using AppKit;
using CoreGraphics;
using Foundation;

namespace ShinyNotch.Services;

public enum MediaPlayer
{
    Music,
    Spotify
}

public sealed record NowPlaying(
    MediaPlayer Player,
    string Title,
    string Artist,
    string Album,
    double Position,
    double Duration,
    bool IsPlaying
)
{
    public string TrackKey => $"{this.Player}|{this.Title}|{this.Artist}|{this.Album}";
}

/// <summary>
/// What Apple Music or Spotify is playing, and control of it.
/// </summary>
/// <remarks>
/// <para>
/// Not MediaRemote: since macOS 15.4 the system-wide now-playing API returns nothing to apps without an
/// Apple entitlement. Instead, both players broadcast a distributed notification on every play, pause and
/// track change, carrying the track - so the app just listens, and does nothing between changes. The
/// position is read when it matters (at start, after a command, when the panel opens) and carried forward
/// on the clock in between. A slow AppleScript check backs the notifications up while a player is open.
/// </para>
/// <para>
/// A player that is not running is never sent AppleScript, because addressing it would launch it. That is
/// also why a "stopped" notification (sent as a player quits) is taken at its word rather than checked.
/// NSAppleScript belongs on the main thread, so every script carries a short AppleEvent timeout, and a
/// player that times out is left alone for a while before it is asked again.
/// </para>
/// </remarks>
public sealed class NowPlayingService : IDisposable
{
    const string MusicBundle = "com.apple.Music";
    const string SpotifyBundle = "com.spotify.client";
    const int TimeoutError = -1712; // errAETimeout
    static readonly TimeSpan QuietAfterTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan BackupCheck = TimeSpan.FromSeconds(30);

    readonly IDispatcherTimer backup;
    readonly TimeProvider time;
    readonly HttpClient http = new();
    readonly Dictionary<MediaPlayer, DateTimeOffset> quietUntil = new();
    readonly Dictionary<MediaPlayer, NowPlaying?> players = new();
    readonly List<NSObject> observers = [];
    DateTimeOffset sampledAt;
    string? artworkKey;

    public NowPlayingService(IDispatcher dispatcher, TimeProvider time)
    {
        this.time = time;
        this.backup = dispatcher.CreateTimer();
        this.backup.Interval = BackupCheck;
        this.backup.Tick += (_, _) =>
        {
            if (IsRunning(MediaPlayer.Music) || IsRunning(MediaPlayer.Spotify))
                this.Refresh();
        };
    }

    public NowPlaying? Current { get; private set; }

    /// <summary>The current track's artwork (PNG/JPEG bytes), or null.</summary>
    public byte[]? Artwork { get; private set; }

    /// <summary>A colour taken from the artwork, bright enough to read on black - or null without artwork.</summary>
    public Color? ArtworkColor { get; private set; }

    /// <summary>Seconds into the track now: the last reading, carried forward while it plays.</summary>
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

    /// <summary>The track, play state or artwork changed. Position ticking along is not a change.</summary>
    public event EventHandler? Changed;

    public void Start()
    {
        if (this.observers.Count > 0)
            return;

        var center = NSDistributedNotificationCenter.DefaultCenter;
        this.observers.Add(center.AddObserver(new NSString("com.apple.Music.playerInfo"), n => this.OnPlayerInfo(MediaPlayer.Music, n)));
        this.observers.Add(center.AddObserver(new NSString("com.spotify.client.PlaybackStateChanged"), n => this.OnPlayerInfo(MediaPlayer.Spotify, n)));
        this.observers.Add(NSWorkspace.Notifications.ObserveDidTerminateApplication((_, e) =>
        {
            if (PlayerOf(e.Application?.BundleIdentifier) is { } player)
                this.Publish(player, null);
        }));

        this.Refresh();
        this.backup.Start();
    }

    public void PlayPause() => this.Command("playpause");
    public void Next() => this.Command("next track");
    public void Previous() => this.Command("previous track");

    void Command(string verb)
    {
        if (this.Current is not { } now)
            return;

        this.Run(now.Player, $"tell application \"{AppName(now.Player)}\" to {verb}", 2)?.Dispose();
        this.Refresh();
    }

    /// <summary>Asks the running players directly: the state, and an exact position.</summary>
    public void Refresh()
    {
        foreach (var player in Enum.GetValues<MediaPlayer>())
            this.players[player] = this.Query(player);
        this.Choose();
    }

    void OnPlayerInfo(MediaPlayer player, NSNotification notification)
    {
        var info = notification.UserInfo;
        string? Text(string key) => info?.ObjectForKey(new NSString(key))?.ToString();
        double Number(string key) => info?.ObjectForKey(new NSString(key)) is NSNumber n ? n.DoubleValue : 0;

        var state = Text("Player State");
        if (state == "Stopped")
        {
            // Also what a player says as it quits - so it is not asked to confirm, which would relaunch it.
            this.Publish(player, null);
            return;
        }

        var title = Text("Stream Title") ?? Text("Name");
        if (state == null || title == null)
        {
            // Not the shape expected: ask the player itself instead.
            this.Publish(player, this.Query(player));
            return;
        }

        var playing = state == "Playing";
        var before = this.players.GetValueOrDefault(player);
        var duration = (player == MediaPlayer.Music ? Number("Total Time") : Number("Duration")) / 1000;
        double position;
        if (player == MediaPlayer.Spotify && info?.ObjectForKey(new NSString("Playback Position")) is NSNumber p)
            position = p.DoubleValue;
        else if (before != null && before.Title == title && before.Player == player)
            position = this.Current?.Player == player ? this.Position : before.Position; // same track, play/pause: carry on
        else
            position = 0; // a new track starts at the top

        var artist = Text("Artist") ?? (Text("Stream Title") != null ? Text("Name") : null) ?? "";
        this.Publish(player, new NowPlaying(player, title, artist, Text("Album") ?? "", position, duration, playing));
    }

    void Publish(MediaPlayer player, NowPlaying? state)
    {
        this.players[player] = state;
        this.Choose();
    }

    /// <summary>Spotify when it's playing, else Music, else a paused Spotify.</summary>
    void Choose()
    {
        var spotify = this.players.GetValueOrDefault(MediaPlayer.Spotify);
        var next = spotify is { IsPlaying: true } ? spotify : this.players.GetValueOrDefault(MediaPlayer.Music) ?? spotify;

        var before = this.Current;
        this.Current = next;
        // A fresh reading restarts the clock the position is carried on; the other player's news doesn't.
        if (!ReferenceEquals(before, next))
            this.sampledAt = this.time.GetUtcNow();

        var changed = before?.TrackKey != next?.TrackKey || before?.IsPlaying != next?.IsPlaying;
        if (next != null && next.TrackKey != this.artworkKey)
        {
            this.artworkKey = next.TrackKey;
            this.Artwork = null;
            this.ArtworkColor = null;
            _ = this.LoadArtworkAsync(next);
        }
        else if (next == null)
        {
            this.artworkKey = null;
            this.Artwork = null;
            this.ArtworkColor = null;
        }

        if (changed)
            this.Changed?.Invoke(this, EventArgs.Empty);
    }

    static string AppName(MediaPlayer player) => player == MediaPlayer.Music ? "Music" : "Spotify";

    static MediaPlayer? PlayerOf(string? bundle) => bundle switch
    {
        MusicBundle => MediaPlayer.Music,
        SpotifyBundle => MediaPlayer.Spotify,
        _ => null
    };

    static bool IsRunning(MediaPlayer player)
        => NSRunningApplication.GetRunningApplications(player == MediaPlayer.Music ? MusicBundle : SpotifyBundle).Any(a => !a.Terminated);

    NowPlaying? Query(MediaPlayer player)
    {
        if (!IsRunning(player) || (this.quietUntil.TryGetValue(player, out var until) && this.time.GetUtcNow() < until))
            return null;

        var app = AppName(player);
        // Spotify reports duration in milliseconds, Music in seconds.
        var script = $$"""
            tell application "{{app}}"
                if player state is stopped then return {}
                set t to current track
                return {name of t, artist of t, album of t, player position, duration of t, player state as text}
            end tell
            """;

        using var result = this.Run(player, script, 1);
        if (result == null || result.NumberOfItems < 6)
            return null;

        string Item(int i)
        {
            using var item = result.DescriptorAtIndex(i);
            return item?.StringValue ?? "";
        }
        double Number(int i) => Double.TryParse(Item(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

        var duration = Number(5);
        if (player == MediaPlayer.Spotify)
            duration /= 1000;

        return new NowPlaying(player, Item(1), Item(2), Item(3), Number(4), duration, Item(6) == "playing");
    }

    async Task LoadArtworkAsync(NowPlaying now)
    {
        byte[]? bytes = null;
        try
        {
            if (now.Player == MediaPlayer.Music)
            {
                using var result = this.Run(MediaPlayer.Music, "tell application \"Music\" to get raw data of artwork 1 of current track", 2);
                using var data = result?.Data;
                bytes = data?.ToArray();
            }
            else
            {
                string? url;
                using (var result = this.Run(MediaPlayer.Spotify, "tell application \"Spotify\" to get artwork url of current track", 2))
                    url = result?.StringValue;
                if (!String.IsNullOrWhiteSpace(url))
                    bytes = await this.http.GetByteArrayAsync(url).ConfigureAwait(true);
            }
        }
        catch
        {
            // No artwork is a fine answer.
        }

        if (this.artworkKey != now.TrackKey)
            return;

        this.Artwork = bytes is { Length: > 0 } ? bytes : null;
        this.ArtworkColor = this.Artwork != null ? AccentOf(this.Artwork) : null;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The artwork's colour: shrunk to a few pixels and averaged, weighted towards the saturated ones so a
    /// grey border or a dark background doesn't wash it out, then lifted to read on the black notch.
    /// </summary>
    static Color? AccentOf(byte[] bytes)
    {
        try
        {
            using var data = NSData.FromArray(bytes);
            using var image = new NSImage(data);
            var rect = new CGRect(0, 0, image.Size.Width, image.Size.Height);
            using var cg = image.AsCGImage(ref rect, null, null);
            if (cg == null)
                return null;

            const int n = 12;
            var pixels = new byte[n * n * 4];
            using (var space = CGColorSpace.CreateDeviceRGB())
            using (var context = new CGBitmapContext(pixels, n, n, 8, n * 4, space, CGImageAlphaInfo.PremultipliedLast))
            {
                context.InterpolationQuality = CGInterpolationQuality.Medium;
                context.DrawImage(new CGRect(0, 0, n, n), cg);
            }

            double r = 0, g = 0, b = 0, total = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                double pr = pixels[i] / 255d, pg = pixels[i + 1] / 255d, pb = pixels[i + 2] / 255d;
                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var saturation = max == 0 ? 0 : (max - min) / max;
                var weight = saturation * saturation * max + 0.02;
                r += pr * weight;
                g += pg * weight;
                b += pb * weight;
                total += weight;
            }

            Color.FromRgb(r / total, g / total, b / total).ToHsl(out var h, out var s, out var l);
            return Color.FromHsla(h, Math.Min(1, s * 1.15), Math.Clamp(l, 0.6, 0.75));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Runs a script against <paramref name="player"/>, waiting at most <paramref name="timeoutSeconds"/> for it to answer.</summary>
    NSAppleEventDescriptor? Run(MediaPlayer player, string source, int timeoutSeconds)
    {
        using var script = new NSAppleScript($"with timeout of {timeoutSeconds} seconds\n{source}\nend timeout");
        var result = script.ExecuteAndReturnError(out var error);
        if (error == null)
            return result;

        result?.Dispose();
        using var key = new NSString("NSAppleScriptErrorNumber");
        if (error.ObjectForKey(key) is NSNumber { Int32Value: TimeoutError })
            this.quietUntil[player] = this.time.GetUtcNow() + QuietAfterTimeout;
        return null;
    }

    public void Dispose()
    {
        this.backup.Stop();
        foreach (var observer in this.observers)
        {
            NSDistributedNotificationCenter.DefaultCenter.RemoveObserver(observer);
            NSWorkspace.SharedWorkspace.NotificationCenter.RemoveObserver(observer);
        }
        this.observers.Clear();
        this.http.Dispose();
    }
}
