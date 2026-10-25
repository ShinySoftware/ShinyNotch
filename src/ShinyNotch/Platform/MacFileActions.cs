using System.Diagnostics;
using AppKit;
using Foundation;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.MacOS;

/// <summary>The pasteboard, Finder, AirDrop and Gatekeeper.</summary>
[Singleton]
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

    public bool CanRemoveQuarantine(string path)
        => path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(path, "Contents", "Info.plist"));

    /// <remarks>
    /// <para>
    /// What "Open Anyway" in Privacy &amp; Security does for one launch, made to stick: the quarantine flag
    /// is what sends a downloaded app to Gatekeeper, so with it gone (from every file in the bundle) the
    /// app opens like one built here. An app whose signature is broken or missing still won't run on Apple
    /// silicon - that's the "is damaged" message - so it's re-signed ad hoc, after clearing the extended
    /// attributes codesign refuses to sign over. A valid signature is left alone: re-signing would change
    /// who the app is to the keychain and to privacy permissions.
    /// </para>
    /// <para>
    /// An app the user can't write to (one installed for all users) is done again as an administrator,
    /// which asks for their password.
    /// </para>
    /// </remarks>
    public async Task<string> RemoveQuarantineAsync(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var result = await Run("/bin/sh", "-c", TrustScript, "sh", path).ConfigureAwait(false)
            ?? throw new InvalidOperationException("the shell couldn't be started.");

        if (result.ExitCode != 0 && result.Error.Contains("Permission denied", StringComparison.Ordinal))
        {
            result = await Run(
                "/usr/bin/osascript",
                "-e", "on run argv",
                "-e", "do shell script \"/bin/sh -c \" & quoted form of item 1 of argv & \" sh \" & quoted form of item 2 of argv with administrator privileges",
                "-e", "end run",
                TrustScript, path).ConfigureAwait(false)
                ?? throw new InvalidOperationException("the administrator prompt couldn't be shown.");
            if (result.Error.Contains("(-128)", StringComparison.Ordinal))
                throw new InvalidOperationException("cancelled.");
        }

        if (result.ExitCode != 0)
        {
            // macOS guards apps against changes by other apps unless allowed under App Management.
            if (result.Error.Contains("Operation not permitted", StringComparison.Ordinal))
                throw new InvalidOperationException("macOS wouldn't let Shiny Notch change it. Allow Shiny Notch under Privacy & Security → App Management, then try again.");
            var detail = result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            throw new InvalidOperationException(detail ?? $"{name} couldn't be changed.");
        }

        var quarantined = result.Output.Contains("quarantined", StringComparison.Ordinal);
        var resigned = result.Output.Contains("resigned", StringComparison.Ordinal);
        return (quarantined, resigned) switch
        {
            (true, true) => $"{name} is trusted: quarantine removed and re-signed ad hoc.",
            (true, false) => $"{name} is trusted: quarantine removed.",
            (false, true) => $"{name} wasn't quarantined, but its signature was broken: re-signed ad hoc.",
            _ => $"{name} wasn't quarantined and its signature checks out. Nothing to do."
        };
    }

    // $1 the app. Prints "quarantined" when it was, and "resigned" when its signature had to be replaced.
    const string TrustScript = """
        app="$1"
        /usr/bin/xattr -p com.apple.quarantine "$app" >/dev/null 2>&1 && echo quarantined
        /usr/bin/xattr -dr com.apple.quarantine "$app" || exit 1
        /usr/bin/codesign --verify --deep --strict "$app" >/dev/null 2>&1 && exit 0
        /usr/bin/xattr -cr "$app" || exit 1
        /usr/bin/codesign --force --deep --sign - "$app" || exit 1
        echo resigned
        """;

    static async Task<(int ExitCode, string Output, string Error)?> Run(string file, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in args)
                info.ArgumentList.Add(arg);

            using var process = Process.Start(info);
            if (process == null)
                return null;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch
        {
            return null;
        }
    }
}
