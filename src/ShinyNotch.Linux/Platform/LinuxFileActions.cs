using System.Text;
using Shiny;
using ShinyNotch.Platform;
using Tmds.DBus.Protocol;

namespace ShinyNotch.Linux;

/// <summary>The GDK clipboard, the freedesktop file manager interface, and xdg-open.</summary>
[Singleton]
sealed class LinuxFileActions : IFileActions
{
    /// <summary>A text/uri-list on the clipboard: what file managers read when pasting files.</summary>
    public void Copy(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || Gdk.Display.GetDefault() is not { } display)
            return;

        var list = String.Concat(paths.Select(p => new Uri(p).AbsoluteUri + "\r\n"));
        var bytes = GLib.Bytes.New(Encoding.UTF8.GetBytes(list));
        display.GetClipboard().SetContent(Gdk.ContentProvider.NewForBytes("text/uri-list", bytes));
    }

    /// <summary>
    /// org.freedesktop.FileManager1, which Files, Dolphin, Nemo and Caja all answer, selects the files in a
    /// window. Without it, the folder of the first is opened instead.
    /// </summary>
    public void Reveal(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;

        _ = this.RevealAsync(paths);
    }

    async Task RevealAsync(IReadOnlyList<string> paths)
    {
        try
        {
            var connection = DBusConnection.Session;
            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.FileManager1",
                path: "/org/freedesktop/FileManager1",
                @interface: "org.freedesktop.FileManager1",
                member: "ShowItems",
                signature: "ass"
            );
            writer.WriteArray(paths.Select(p => new Uri(p).AbsoluteUri).ToArray());
            writer.WriteString("");
            await connection.CallMethodAsync(writer.CreateMessage()).ConfigureAwait(false);
        }
        catch
        {
            if (Path.GetDirectoryName(paths[0]) is { } folder)
                Command.Start("xdg-open", folder);
        }
    }

    public string FileManagerName => "Files";

    public void Open(string path) => Command.Start("xdg-open", path);

    // No AirDrop counterpart every desktop has; the share button stays out of the tray.
    public string? ShareName => null;

    public string ShareSymbol => "square.and.arrow.up";

    public void Share(IReadOnlyList<string> paths)
    {
    }
}
