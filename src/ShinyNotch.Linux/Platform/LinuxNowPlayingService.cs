using Shiny;
using ShinyNotch.Platform;
using Tmds.DBus.Protocol;

namespace ShinyNotch.Linux;

/// <summary>
/// What any MPRIS player is playing - Spotify, Rhythmbox, a browser tab, mpv - and control of it.
/// </summary>
/// <remarks>
/// Every player on the session bus publishes the same <c>org.mpris.MediaPlayer2.Player</c> object, and
/// announces its changes with PropertiesChanged. The service listens for those (and for players coming
/// and going), and reads every player again a moment after a burst of them: the one playing wins, else
/// the first one paused on a track. The position is carried forward on the clock between readings, as on
/// the Mac.
/// </remarks>
[Singleton]
sealed class LinuxNowPlayingService : INowPlayingService, IDisposable
{
    const string Prefix = "org.mpris.MediaPlayer2.";
    const string ObjectPath = "/org/mpris/MediaPlayer2";
    const string PlayerInterface = "org.mpris.MediaPlayer2.Player";
    /// <summary>A track change arrives as several PropertiesChanged; read once they settle.</summary>
    static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    readonly IPlatformUi ui;
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly List<IDisposable> watches = [];
    /// <summary>The bus name of the player <see cref="Current"/> came from, for sending it commands.</summary>
    string? currentBusName;
    DateTimeOffset sampledAt;
    string? artworkKey;
    int refreshToken;
    bool started;

    public LinuxNowPlayingService(IDispatcher dispatcher, TimeProvider time, IPlatformUi ui)
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

    public void Start()
    {
        if (this.started)
            return;
        this.started = true;
        _ = this.WatchAsync();
        this.Refresh();
    }

    public void PlayPause() => this.Command("PlayPause");
    public void Next() => this.Command("Next");
    public void Previous() => this.Command("Previous");

    // Seek takes an offset in microseconds; SetPosition would need the track id, which not every player sets.
    public void Seek(double seconds)
    {
        var offset = (long)((Math.Max(0, seconds) - this.Position) * 1_000_000);
        this.Command("Seek", "x", w => w.WriteInt64(offset));
    }

    void Command(string method, string? signature = null, Action<MessageWriter>? write = null)
    {
        if (this.currentBusName is not { } name)
            return;

        _ = Send();

        async Task Send()
        {
            try
            {
                var connection = DBusConnection.Session;
                await connection.CallMethodAsync(DBus.Call(connection, name, ObjectPath, PlayerInterface, method, signature, write)).ConfigureAwait(false);
            }
            catch
            {
                // The player went away; the next reading says so.
            }
            this.dispatcher.Dispatch(this.Refresh);
        }
    }

    /// <summary>Reads every player again, after the current burst of changes settles.</summary>
    public void Refresh()
    {
        var token = ++this.refreshToken;
        this.dispatcher.DispatchDelayed(SettleDelay, async () =>
        {
            if (token != this.refreshToken)
                return;

            var (busName, next, artUrl) = await ReadPlayersAsync().ConfigureAwait(true);
            if (token == this.refreshToken)
                this.Apply(busName, next, artUrl);
        });
    }

    async Task WatchAsync()
    {
        try
        {
            var connection = DBusConnection.Session;
            void Changed() => this.dispatcher.Dispatch(this.Refresh);

            // Any player's properties: sender unset, since each player has a bus name of its own.
            this.watches.Add(await connection.WatchSignalAsync(
                null, ObjectPath, "org.freedesktop.DBus.Properties", "PropertiesChanged",
                (Notification _) => Changed(), ObserverFlags.None, emitOnCapturedContext: false
            ).ConfigureAwait(false));
            this.watches.Add(await connection.WatchSignalAsync(
                null, ObjectPath, PlayerInterface, "Seeked",
                (Notification _) => Changed(), ObserverFlags.None, emitOnCapturedContext: false
            ).ConfigureAwait(false));

            // Players starting and quitting.
            this.watches.Add(await connection.WatchSignalAsync(
                "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameOwnerChanged",
                static (Message message, object? _) => message.GetBodyReader().ReadString(),
                (Notification<string> n) =>
                {
                    if (n.HasValue && n.Value.StartsWith(Prefix, StringComparison.Ordinal))
                        Changed();
                },
                ObserverFlags.None,
                emitOnCapturedContext: false
            ).ConfigureAwait(false));
        }
        catch
        {
            // No session bus: nothing will ever be playing.
        }
    }

    /// <summary>Playing beats paused; a player with nothing loaded doesn't count.</summary>
    static async Task<(string? BusName, NowPlaying? Playing, string? ArtUrl)> ReadPlayersAsync()
    {
        (string? BusName, NowPlaying? Playing, string? ArtUrl) paused = (null, null, null);
        try
        {
            var connection = DBusConnection.Session;
            var names = await connection.CallMethodAsync(
                DBus.Call(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListNames"),
                static (Message reply, object? _) => reply.GetBodyReader().ReadArrayOfString()
            ).ConfigureAwait(false);

            foreach (var name in names.Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                if (await ReadPlayerAsync(connection, name).ConfigureAwait(false) is not { } read)
                    continue;
                if (read.Playing.IsPlaying)
                    return (name, read.Playing, read.ArtUrl);
                if (paused.Playing == null)
                    paused = (name, read.Playing, read.ArtUrl);
            }
        }
        catch
        {
            // The bus went away mid-read: whatever was found so far.
        }
        return paused;
    }

    static async Task<(NowPlaying Playing, string? ArtUrl)?> ReadPlayerAsync(DBusConnection connection, string name)
    {
        try
        {
            var properties = await DBus.GetAll(connection, name, ObjectPath, PlayerInterface).ConfigureAwait(false);
            var status = properties.TryGetValue("PlaybackStatus", out var s) ? s.Unwrap().GetString() : "Stopped";
            if (status == "Stopped" || !properties.TryGetValue("Metadata", out var m))
                return null;

            var metadata = m.Unwrap().GetDictionary<string, VariantValue>();
            var title = Text(metadata, "xesam:title");
            if (String.IsNullOrEmpty(title))
                return null;

            var artist = metadata.TryGetValue("xesam:artist", out var a) && a.Unwrap() is { Type: VariantValueType.Array } artists
                ? String.Join(", ", artists.GetArray<string>())
                : Text(metadata, "xesam:artist") ?? "";

            string identity;
            try
            {
                identity = (await DBus.Get(connection, name, ObjectPath, "org.mpris.MediaPlayer2", "Identity").ConfigureAwait(false)).Unwrap().GetString();
            }
            catch
            {
                identity = name[Prefix.Length..];
            }

            // Microseconds, in whichever integer type the player chose.
            var duration = Microseconds(metadata.GetValueOrDefault("mpris:length"));
            var position = Microseconds(properties.GetValueOrDefault("Position"));
            var playing = new NowPlaying(identity, title, artist, Text(metadata, "xesam:album") ?? "", position, duration, status == "Playing");
            return (playing, Text(metadata, "mpris:artUrl"));
        }
        catch
        {
            return null;
        }
    }

    void Apply(string? busName, NowPlaying? next, string? artUrl)
    {
        var before = this.Current;
        this.Current = next;
        this.currentBusName = busName;
        // Every reading is fresh, so the position is carried on from now.
        this.sampledAt = this.time.GetUtcNow();

        var changed = before?.TrackKey != next?.TrackKey || before?.IsPlaying != next?.IsPlaying;
        if (next != null && next.TrackKey != this.artworkKey)
        {
            this.artworkKey = next.TrackKey;
            this.Artwork = null;
            this.ArtworkColor = null;
            _ = this.LoadArtworkAsync(next.TrackKey, artUrl);
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

    /// <summary>mpris:artUrl is a local file (most players cache it) or a web address (Spotify, browsers).</summary>
    async Task LoadArtworkAsync(string trackKey, string? url)
    {
        byte[]? bytes = null;
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                bytes = uri.IsFile
                    ? await File.ReadAllBytesAsync(uri.LocalPath).ConfigureAwait(true)
                    : uri.Scheme is "http" or "https" ? await this.http.GetByteArrayAsync(uri).ConfigureAwait(true) : null;
            }
        }
        catch
        {
            // No artwork is a fine answer.
        }

        if (this.artworkKey != trackKey || bytes is not { Length: > 0 })
            return;

        this.Artwork = bytes;
        this.ArtworkColor = ArtworkAccent.From(this.ui, bytes);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    static string? Text(Dictionary<string, VariantValue> metadata, string key)
        => metadata.TryGetValue(key, out var value) && value.Unwrap() is { Type: VariantValueType.String } text ? text.GetString() : null;

    static double Microseconds(VariantValue value)
    {
        value = value.Unwrap();
        return value.Type switch
        {
            VariantValueType.Int64 => value.GetInt64(),
            VariantValueType.UInt64 => value.GetUInt64(),
            VariantValueType.Int32 => value.GetInt32(),
            VariantValueType.UInt32 => value.GetUInt32(),
            VariantValueType.Double => value.GetDouble(),
            _ => 0
        } / 1_000_000d;
    }

    public void Dispose()
    {
        foreach (var watch in this.watches)
            watch.Dispose();
        this.watches.Clear();
        this.http.Dispose();
    }
}
