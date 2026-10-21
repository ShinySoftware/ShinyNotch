using System.Runtime.InteropServices;
using System.Text.Json;
using AppKit;
using Foundation;
using ImageIO;
using Shiny;

namespace ShinyNotch.MacOS;

/// <summary>
/// Drives macshot (macshot.io), the open-source screenshot app, through its <c>macshot://</c> URL scheme,
/// and reads its capture history so the notch can show the latest captures.
/// </summary>
/// <remarks>
/// <para>
/// macshot is sandboxed, so its history lives in its container. The first read there makes macOS ask
/// once whether Shiny Notch may access another app's data; refused, the recent strip just stays empty.
/// The history is macshot's own format (index.json plus one image per revision), read leniently.
/// </para>
/// <para>
/// macshot doesn't say when it is recording, but it always outlines the recorded area with a click-through
/// window at the status bar level + 1 (its SelectionBorderOverlay), shown for the countdown and recording
/// and closed when it ends. The window list gives every window's level and owner without asking for screen
/// recording permission (only titles need that), so the Stop button follows that window.
/// </para>
/// </remarks>
[Singleton]
public sealed class MacShotService : IDisposable
{
    public const string BundleId = "com.sw33tlie.macshot.macshot";

    /// <summary>A capture in macshot's history, newest first, with a small PNG thumbnail.</summary>
    public sealed record Capture(string Id, DateTimeOffset When, byte[]? Thumbnail);

    const int ThumbnailPixels = 160;
    /// <summary>NSWindow.Level.statusBar (25) + 1: macshot's recording outline.</summary>
    const int RecordingBorderLevel = 26;
    static readonly DateTimeOffset ReferenceDate = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
    /// <summary>macshot rewrites the index in a burst (staging file, then the swap); act once it settles.</summary>
    static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(300);

    readonly IDispatcher dispatcher;
    readonly string historyDir;
    readonly Dictionary<string, byte[]?> thumbnails = new();
    FileSystemWatcher? watcher;
    IReadOnlyList<Capture>? recent;
    int changeToken;
    bool recording;

    public MacShotService(IDispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        this.historyDir = Path.Combine(home, "Library/Containers", BundleId, "Data/Library/Application Support/com.sw33tlie.macshot/history");
    }

    /// <summary>The history changed (a capture taken, edited or deleted), or a recording started or ended.</summary>
    public event EventHandler? Changed;

    public bool IsInstalled => NSWorkspace.SharedWorkspace.UrlForApplication(BundleId) != null;

    /// <summary>A recording (or its countdown) is under way, as of the last <see cref="CheckRecording"/>.</summary>
    public bool IsRecording => this.recording;

    /// <summary>Looks for macshot's recording outline, raising <see cref="Changed"/> when it comes or goes.</summary>
    public void CheckRecording()
    {
        var now = FindRecordingBorder();
        if (now == this.recording)
            return;
        this.recording = now;
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    static bool FindRecordingBorder()
    {
        var pids = NSRunningApplication.GetRunningApplications(BundleId).Select(a => a.ProcessIdentifier).ToHashSet();
        if (pids.Count == 0)
            return false;

        var handle = CGWindowListCopyWindowInfo(OnScreenOnly, 0);
        if (handle == IntPtr.Zero)
            return false;
        using var windows = ObjCRuntime.Runtime.GetNSObject<NSArray>(handle, owns: true);
        if (windows == null)
            return false;

        for (nuint i = 0; i < windows.Count; i++)
        {
            var window = windows.GetItem<NSDictionary>(i);
            if (window?[OwnerPidKey] is NSNumber pid && pids.Contains(pid.Int32Value) &&
                window[LayerKey] is NSNumber layer && layer.Int32Value == RecordingBorderLevel)
                return true;
        }
        return false;
    }

    const uint OnScreenOnly = 1;
    static readonly NSString OwnerPidKey = new("kCGWindowOwnerPID");
    static readonly NSString LayerKey = new("kCGWindowLayer");

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    /// <summary>Runs a macshot action, e.g. <c>capture</c>, <c>record</c> or <c>history</c>.</summary>
    public static void Run(string action)
        => NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl($"macshot://{action}"));

    /// <summary>Watches the history, once macshot is known to be there. Safe to call again.</summary>
    public void Start()
    {
        if (this.watcher != null || !Directory.Exists(this.historyDir))
            return;

        try
        {
            this.watcher = new FileSystemWatcher(this.historyDir, "index.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            this.watcher.Changed += (_, _) => this.OnChanged();
            this.watcher.Created += (_, _) => this.OnChanged();
            this.watcher.Renamed += (_, _) => this.OnChanged();
            this.watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // No access to the container: the strip stays empty, the actions still work.
            this.watcher?.Dispose();
            this.watcher = null;
        }
    }

    /// <summary>The newest captures. Cached until the history changes; thumbnails are kept per revision.</summary>
    public IReadOnlyList<Capture> Recent(int count)
    {
        this.recent ??= this.Load();
        return this.recent.Take(count).ToList();
    }

    void OnChanged()
    {
        var token = Interlocked.Increment(ref this.changeToken);
        this.dispatcher.DispatchDelayed(SettleDelay, () =>
        {
            if (token != this.changeToken)
                return;
            this.recent = null;
            this.Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    List<Capture> Load()
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(this.historyDir, "index.json"));
            using var doc = JsonDocument.Parse(stream);
            var captures = new List<(string Id, DateTimeOffset When, string Image)>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (Str(entry, "id") is not { } id || !Guid.TryParse(id, out _))
                    continue;
                var ext = Str(entry, "fileExtension") ?? "png";
                var revision = Str(entry, "revision");
                var image = revision != null
                    ? Path.Combine(this.historyDir, id, revision, $"image.{ext}")
                    : Path.Combine(this.historyDir, $"{id}.{ext}");
                // Ordered as macshot's own history is: by the last edit, else when it was taken.
                var when = Date(entry, "lastEditedAt") ?? Date(entry, "timestamp") ?? ReferenceDate;
                captures.Add((id, when, image));
            }

            return captures
                .OrderByDescending(c => c.When)
                .Select(c => new Capture(c.Id, c.When, this.Thumbnail(c.Image)))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    byte[]? Thumbnail(string path)
    {
        // Keyed by path: every edit publishes a new revision folder, so an edited capture gets a new thumbnail.
        if (this.thumbnails.TryGetValue(path, out var png))
            return png;

        png = null;
        try
        {
            using var source = CGImageSource.FromUrl(NSUrl.CreateFileUrl(path, null));
            using var image = source?.CreateThumbnail(0, new CGImageThumbnailOptions
            {
                MaxPixelSize = ThumbnailPixels,
                CreateThumbnailFromImageAlways = true,
                CreateThumbnailWithTransform = true
            });
            if (image != null)
            {
                using var rep = new NSBitmapImageRep(image);
                png = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)?.ToArray();
            }
        }
        catch (Exception)
        {
        }

        this.thumbnails[path] = png;
        return png;
    }

    static string? Str(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Foundation's default JSON dates: seconds since 1 January 2001.</summary>
    static DateTimeOffset? Date(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var s) && double.IsFinite(s)
            ? ReferenceDate.AddSeconds(Math.Clamp(s, -1e10, 1e10))
            : null;

    public void Dispose() => this.watcher?.Dispose();
}
