using System.Text;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.Linux;

/// <summary>
/// The GDK clipboard, which says when it changes. On Wayland the compositor only tells the app that has
/// focus, so copies made elsewhere show up once the notch (or Settings) is next focused - or not at all;
/// on X11 every copy is seen.
/// </summary>
[Singleton]
sealed class LinuxClipboardWatcher : IClipboardWatcher
{
    /// <summary>KDE's hint, which KeePassXC and other password managers set on what they copy.</summary>
    const string PasswordHint = "x-kde-passwordManagerHint";

    Gdk.Clipboard? clipboard;

    public event EventHandler<string>? TextCopied;

    public void Start()
    {
        if (this.clipboard != null || Gdk.Display.GetDefault() is not { } display)
            return;

        this.clipboard = display.GetClipboard();
        this.clipboard.OnChanged += (_, _) => _ = this.ReadAsync();
    }

    async Task ReadAsync()
    {
        if (this.clipboard is not { } board)
            return;

        try
        {
            var formats = board.GetFormats();
            if (formats.ContainMimeType(PasswordHint) || formats.ContainMimeType("text/uri-list"))
                return;

            var text = await board.ReadTextAsync();
            if (!String.IsNullOrEmpty(text))
                this.TextCopied?.Invoke(this, text);
        }
        catch (Exception)
        {
            // Not text after all, or the owner went away before answering.
        }
    }

    public void SetText(string text)
    {
        if (Gdk.Display.GetDefault() is not { } display)
            return;

        var bytes = GLib.Bytes.New(Encoding.UTF8.GetBytes(text));
        display.GetClipboard().SetContent(Gdk.ContentProvider.NewForBytes("text/plain;charset=utf-8", bytes));
    }
}
