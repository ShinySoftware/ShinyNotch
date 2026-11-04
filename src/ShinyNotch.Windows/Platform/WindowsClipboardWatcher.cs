using Shiny;
using ShinyNotch.Platform;
using Clipboard = global::Windows.ApplicationModel.DataTransfer.Clipboard;
using DataPackage = global::Windows.ApplicationModel.DataTransfer.DataPackage;
using StandardDataFormats = global::Windows.ApplicationModel.DataTransfer.StandardDataFormats;

namespace ShinyNotch.Windows;

/// <summary>The Windows clipboard, which says when it changes.</summary>
[Singleton]
sealed class WindowsClipboardWatcher(IDispatcher dispatcher) : IClipboardWatcher
{
    /// <summary>
    /// Formats a copy carries to keep itself out of clipboard managers and history: what password
    /// managers set. Files copied in Explorer are left to the file tray.
    /// </summary>
    static readonly string[] Skipped =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
        StandardDataFormats.StorageItems
    ];

    bool started;

    public event EventHandler<string>? TextCopied;

    public void Start()
    {
        if (this.started)
            return;
        this.started = true;
        // Raised on the thread that subscribed: this is called from the UI thread at startup.
        Clipboard.ContentChanged += (_, _) => _ = this.ReadAsync();
    }

    async Task ReadAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text) || content.AvailableFormats.Any(f => Skipped.Contains(f)))
                return;

            var text = await content.GetTextAsync();
            if (!String.IsNullOrEmpty(text))
                dispatcher.Dispatch(() => this.TextCopied?.Invoke(this, text));
        }
        catch (Exception)
        {
            // Another app holds the clipboard open; the copy is missed, which is no worse than not watching.
        }
    }

    public void SetText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception)
        {
            // Held open by another app for the moment.
        }
    }
}
