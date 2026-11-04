using System.Text.Json;
using System.Text.Json.Serialization;
using Shiny;
using ShinyNotch.Platform;
using ShinyNotch.Views;

namespace ShinyNotch.Services;

/// <param name="Copied">When it was last copied (UTC), by anyone: picking it again counts.</param>
public sealed record ClipboardItem(string Id, string Text, DateTimeOffset Copied, bool Pinned);

[JsonSerializable(typeof(List<ClipboardItem>))]
sealed partial class ClipboardJson : JsonSerializerContext;

/// <summary>
/// What's been copied, latest first. Copying the same text again moves it back to the top rather than
/// adding it twice, and picking an entry puts it back on the clipboard (which moves it to the top too).
/// Pinned entries stay - first, and out of reach of Clear and the size limit.
/// </summary>
/// <remarks>
/// Nothing is recorded while the Clipboard tab is turned off in Settings: turning it off is how to say
/// "don't keep what I copy".
/// </remarks>
[Singleton]
public sealed class ClipboardService
{
    /// <summary>Unpinned entries kept; older ones fall off the end.</summary>
    public const int Limit = 50;

    /// <summary>Longer copies are kept cut short, so one huge paste doesn't bloat the settings.</summary>
    const int MaxLength = 20_000;

    readonly AppSettings settings;
    readonly IClipboardWatcher clipboard;
    readonly TimeProvider time;
    readonly List<ClipboardItem> items;

    public ClipboardService(AppSettings settings, IClipboardWatcher clipboard, TimeProvider time)
    {
        this.settings = settings;
        this.clipboard = clipboard;
        this.time = time;
        this.items = Load(settings.ClipboardHistory);
        clipboard.TextCopied += (_, text) => this.Record(text);
    }

    /// <summary>An entry was added, picked, pinned, deleted or cleared.</summary>
    public event EventHandler? Changed;

    /// <summary>Pinned entries, then the rest; each the most recently copied first.</summary>
    public IReadOnlyList<ClipboardItem> Items => this.items
        .OrderByDescending(i => i.Pinned)
        .ThenByDescending(i => i.Copied)
        .ToList();

    /// <summary>The entry that's on the clipboard now, as far as is known: the last one copied.</summary>
    public string? CurrentId => this.items.MaxBy(i => i.Copied)?.Id;

    bool IsRecording => !this.settings.HiddenTabs.Contains(nameof(NotchTab.Clipboard));

    public void Start() => this.clipboard.Start();

    /// <summary>Puts an entry back on the clipboard. The watcher sees the copy and moves it to the top.</summary>
    public void Pick(string id)
    {
        var item = this.items.Find(i => i.Id == id);
        if (item == null)
            return;

        // Moved now rather than waiting for the watcher, which may not report the app's own copies.
        this.Touch(item);
        this.clipboard.SetText(item.Text);
    }

    public void TogglePin(string id)
    {
        var i = this.items.FindIndex(x => x.Id == id);
        if (i < 0)
            return;

        this.items[i] = this.items[i] with { Pinned = !this.items[i].Pinned };
        this.Save();
    }

    public void Delete(string id)
    {
        if (this.items.RemoveAll(i => i.Id == id) > 0)
            this.Save();
    }

    /// <summary>Forgets everything but the pinned entries.</summary>
    public void Clear()
    {
        if (this.items.RemoveAll(i => !i.Pinned) > 0)
            this.Save();
    }

    /// <summary>One line of an entry, for a list: whitespace runs (newlines and tabs too) collapsed to a space.</summary>
    public static string Preview(string text, int max = 120)
    {
        var span = text.AsSpan().Trim();
        var builder = new System.Text.StringBuilder(Math.Min(span.Length, max + 1));
        var space = false;
        foreach (var c in span)
        {
            if (Char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }
            if (space && builder.Length > 0)
                builder.Append(' ');
            space = false;
            builder.Append(c);
            if (builder.Length >= max)
                return builder.Append('…').ToString();
        }
        return builder.ToString();
    }

    void Record(string text)
    {
        if (!this.IsRecording || String.IsNullOrWhiteSpace(text))
            return;
        if (text.Length > MaxLength)
            text = text[..MaxLength];

        var existing = this.items.Find(i => i.Text == text);
        if (existing != null)
        {
            // Already on top: a copy of what was just picked, or the same text copied twice.
            if (existing.Id != this.CurrentId)
                this.Touch(existing);
            return;
        }

        this.items.Add(new ClipboardItem(Guid.NewGuid().ToString("N"), text, this.time.GetUtcNow(), false));
        var unpinned = this.items.Where(i => !i.Pinned).OrderByDescending(i => i.Copied).Skip(Limit).ToList();
        foreach (var old in unpinned)
            this.items.Remove(old);
        this.Save();
    }

    void Touch(ClipboardItem item)
    {
        var i = this.items.IndexOf(item);
        this.items[i] = item with { Copied = this.time.GetUtcNow() };
        this.Save();
    }

    void Save()
    {
        this.settings.ClipboardHistory = JsonSerializer.Serialize(this.items, ClipboardJson.Default.ListClipboardItem);
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    static List<ClipboardItem> Load(string? json)
    {
        if (String.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize(json, ClipboardJson.Default.ListClipboardItem) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
