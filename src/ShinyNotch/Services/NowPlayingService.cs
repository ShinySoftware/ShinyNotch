using AppKit;
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
/// What Apple Music or Spotify is playing, read and controlled through AppleScript.
/// </summary>
/// <remarks>
/// Not MediaRemote: since macOS 15.4 the system-wide now-playing API returns nothing to apps without an
/// Apple entitlement. AppleScript only sees the two players it knows about, but it works on current macOS.
/// The first query triggers macOS's one-time Automation prompt for each player. A player that is not
/// running is never asked anything, because addressing it from AppleScript would launch it.
/// </remarks>
public sealed class NowPlayingService : IDisposable
{
    const string MusicBundle = "com.apple.Music";
    const string SpotifyBundle = "com.spotify.client";

    readonly IDispatcherTimer timer;
    readonly HttpClient http = new();
    string? artworkKey;

    public NowPlayingService(IDispatcher dispatcher)
    {
        this.timer = dispatcher.CreateTimer();
        this.timer.Interval = TimeSpan.FromSeconds(1.5);
        this.timer.Tick += (_, _) => this.Refresh();
    }

    public NowPlaying? Current { get; private set; }

    /// <summary>The current track's artwork (PNG/JPEG bytes), or null.</summary>
    public byte[]? Artwork { get; private set; }

    /// <summary>The track, play state or artwork changed. Position ticking along is not a change.</summary>
    public event EventHandler? Changed;

    public void Start()
    {
        this.Refresh();
        this.timer.Start();
    }

    public void PlayPause() => this.Command("playpause");
    public void Next() => this.Command("next track");
    public void Previous() => this.Command("previous track");

    void Command(string verb)
    {
        if (this.Current is not { } now)
            return;

        Run($"tell application \"{AppName(now.Player)}\" to {verb}");
        this.Refresh();
    }

    public void Refresh()
    {
        var next = Query(MediaPlayer.Spotify) is { IsPlaying: true } spotify
            ? spotify
            : Query(MediaPlayer.Music) ?? Query(MediaPlayer.Spotify);

        var before = this.Current;
        this.Current = next;

        var changed = before?.TrackKey != next?.TrackKey || before?.IsPlaying != next?.IsPlaying;
        if (next != null && next.TrackKey != this.artworkKey)
        {
            this.artworkKey = next.TrackKey;
            this.Artwork = null;
            _ = this.LoadArtworkAsync(next);
        }
        else if (next == null)
        {
            this.artworkKey = null;
            this.Artwork = null;
        }

        if (changed)
            this.Changed?.Invoke(this, EventArgs.Empty);
    }

    static string AppName(MediaPlayer player) => player == MediaPlayer.Music ? "Music" : "Spotify";

    static bool IsRunning(MediaPlayer player)
    {
        var bundle = player == MediaPlayer.Music ? MusicBundle : SpotifyBundle;
        return NSWorkspace.SharedWorkspace.RunningApplications.Any(a => a.BundleIdentifier == bundle);
    }

    static NowPlaying? Query(MediaPlayer player)
    {
        if (!IsRunning(player))
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

        var result = Run(script);
        if (result == null || result.NumberOfItems < 6)
            return null;

        string Item(int i) => result.DescriptorAtIndex(i)?.StringValue ?? "";
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
                var result = Run("tell application \"Music\" to get raw data of artwork 1 of current track");
                bytes = result?.Data?.ToArray();
            }
            else
            {
                var url = Run("tell application \"Spotify\" to get artwork url of current track")?.StringValue;
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
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    static NSAppleEventDescriptor? Run(string source)
    {
        using var script = new NSAppleScript(source);
        var result = script.ExecuteAndReturnError(out var error);
        return error == null ? result : null;
    }

    public void Dispose()
    {
        this.timer.Stop();
        this.http.Dispose();
    }
}
