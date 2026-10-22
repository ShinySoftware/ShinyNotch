namespace ShinyNotch.Platform;

/// <param name="Player">The app playing it ("Music", "Spotify"), for telling tracks apart.</param>
/// <param name="Position">Seconds into the track when this was read.</param>
/// <param name="Duration">Seconds, or 0 when unknown (a stream).</param>
public sealed record NowPlaying(
    string Player,
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

/// <summary>What is playing, and control of it. Each platform finds out its own way.</summary>
public interface INowPlayingService
{
    NowPlaying? Current { get; }

    /// <summary>The current track's artwork (PNG/JPEG bytes), or null.</summary>
    byte[]? Artwork { get; }

    /// <summary>A colour taken from the artwork, bright enough to read on black - or null without artwork.</summary>
    Color? ArtworkColor { get; }

    /// <summary>Seconds into the track now: the last reading, carried forward while it plays.</summary>
    double Position { get; }

    /// <summary>The track, play state or artwork changed, on the main thread. Position ticking along is not a change.</summary>
    event EventHandler? Changed;

    /// <summary>Starts listening. Called once, on the main thread.</summary>
    void Start();

    /// <summary>Takes a fresh reading (an exact position) now.</summary>
    void Refresh();

    void PlayPause();
    void Next();
    void Previous();

    /// <summary>Moves the current track to <paramref name="seconds"/> in.</summary>
    void Seek(double seconds);
}
