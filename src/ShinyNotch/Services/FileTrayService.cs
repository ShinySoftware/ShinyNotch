using AppKit;
using Foundation;

namespace ShinyNotch.Services;

/// <summary>
/// A shelf for files: drop them on the notch, then copy them back out to paste anywhere, AirDrop them,
/// or reveal them. Holds references, not copies, and forgets files that have since been moved or deleted.
/// </summary>
public sealed class FileTrayService
{
    readonly AppSettings settings;
    readonly List<string> files;

    public FileTrayService(AppSettings settings)
    {
        this.settings = settings;
        this.files = settings.TrayFiles.Where(Exists).ToList();
    }

    public IReadOnlyList<string> Files => this.files;

    public event EventHandler? Changed;

    public void Add(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            if (Exists(path) && !this.files.Contains(path))
            {
                this.files.Add(path);
                added = true;
            }
        }
        if (added)
            this.Save();
    }

    public void Remove(string path)
    {
        if (this.files.Remove(path))
            this.Save();
    }

    public void Clear()
    {
        this.files.Clear();
        this.Save();
    }

    /// <summary>Drops entries whose file has gone. Called before the tray is shown.</summary>
    public void Prune()
    {
        if (this.files.RemoveAll(p => !Exists(p)) > 0)
            this.Save();
    }

    /// <summary>Puts the files on the clipboard as file URLs, so ⌘V in Finder copies them there.</summary>
    public static void Copy(IEnumerable<string> paths)
    {
        var urls = paths.Select(p => (INSPasteboardWriting)NSUrl.CreateFileUrl(p, null)).ToArray();
        if (urls.Length == 0)
            return;

        var board = NSPasteboard.GeneralPasteboard;
        board.ClearContents();
        board.WriteObjects(urls);
    }

    public static void Reveal(IEnumerable<string> paths)
        => NSWorkspace.SharedWorkspace.ActivateFileViewer(paths.Select(p => NSUrl.CreateFileUrl(p, null)).ToArray());

    public static void Open(string path)
        => NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.CreateFileUrl(path, null));

    public static void AirDrop(IEnumerable<string> paths)
    {
        var items = paths.Select(p => (NSObject)NSUrl.CreateFileUrl(p, null)).ToArray();
        var service = NSSharingService.GetSharingService(NSSharingServiceName.SendViaAirDrop);
        if (items.Length > 0 && service != null && service.CanPerformWithItems(items))
        {
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
            service.PerformWithItems(items);
        }
    }

    /// <summary>The Finder icon for a file, as PNG bytes.</summary>
    public static byte[]? IconPng(string path, int size = 64)
    {
        // Disposed rather than left to the GC: a tray rebuild renders a dozen of these, and the
        // managed wrappers are too small for the GC to hurry over the native images behind them.
        using var icon = NSWorkspace.SharedWorkspace.IconForFile(path);
        icon.Size = new CoreGraphics.CGSize(size, size);
        var rect = new CoreGraphics.CGRect(0, 0, size, size);
        using var cg = icon.AsCGImage(ref rect, null, null);
        if (cg == null)
            return null;

        using var rep = new NSBitmapImageRep(cg);
        return rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png)?.ToArray();
    }

    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    void Save()
    {
        this.settings.TrayFiles = this.files.ToList();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }
}
