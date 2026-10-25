namespace ShinyNotch.Platform;

/// <summary>What the file tray can do with its files.</summary>
public interface IFileActions
{
    /// <summary>Puts the files on the clipboard as files, so pasting in the file manager copies them there.</summary>
    void Copy(IReadOnlyList<string> paths);

    /// <summary>Shows the files selected in the file manager.</summary>
    void Reveal(IReadOnlyList<string> paths);

    /// <summary>The system file manager, for "Reveal in …" ("Finder", "File Explorer", "Files").</summary>
    string FileManagerName { get; }

    /// <summary>Opens a file in its default app.</summary>
    void Open(string path);

    /// <summary>The share button's label ("AirDrop", "Share"), or null when the platform has no way to share.</summary>
    string? ShareName { get; }

    /// <summary>The share button's SF Symbol name.</summary>
    string ShareSymbol { get; }

    void Share(IReadOnlyList<string> paths);

    /// <summary>
    /// An app the system holds back as downloaded (Gatekeeper's "can't be opened" or "is damaged") that
    /// <see cref="RemoveQuarantineAsync"/> can clear. False everywhere but macOS.
    /// </summary>
    bool CanRemoveQuarantine(string path) => false;

    /// <summary>
    /// Trusts an app: clears its quarantine flags, and gives it an ad-hoc signature when its own doesn't
    /// check out, so it opens without the system blocking it. Returns what was done; throws with the reason
    /// when it couldn't be.
    /// </summary>
    Task<string> RemoveQuarantineAsync(string path) => throw new NotSupportedException();
}
