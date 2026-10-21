using AppKit;
using Foundation;
using ShinyNotch.Platform;

namespace ShinyNotch.MacOS;

/// <summary>The pasteboard, Finder and AirDrop.</summary>
sealed class MacFileActions : IFileActions
{
    /// <summary>File URLs on the pasteboard, so ⌘V in Finder copies the files there.</summary>
    public void Copy(IReadOnlyList<string> paths)
    {
        var urls = paths.Select(p => (INSPasteboardWriting)NSUrl.CreateFileUrl(p, null)).ToArray();
        if (urls.Length == 0)
            return;

        var board = NSPasteboard.GeneralPasteboard;
        board.ClearContents();
        board.WriteObjects(urls);
    }

    public void Reveal(IReadOnlyList<string> paths)
        => NSWorkspace.SharedWorkspace.ActivateFileViewer(paths.Select(p => NSUrl.CreateFileUrl(p, null)).ToArray());

    public string FileManagerName => "Finder";

    public void Open(string path)
        => NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.CreateFileUrl(path, null));

    public string? ShareName => "AirDrop";

    public string ShareSymbol => "dot.radiowaves.left.and.right";

    public void Share(IReadOnlyList<string> paths)
    {
        var items = paths.Select(p => (NSObject)NSUrl.CreateFileUrl(p, null)).ToArray();
        var service = NSSharingService.GetSharingService(NSSharingServiceName.SendViaAirDrop);
        if (items.Length > 0 && service != null && service.CanPerformWithItems(items))
        {
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
            service.PerformWithItems(items);
        }
    }
}
