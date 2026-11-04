namespace ShinyNotch.Platform;

/// <summary>The system clipboard's text, watched for copies made in any app. One per head.</summary>
public interface IClipboardWatcher
{
    /// <summary>
    /// Text was copied, here or in another app. Raised on the main thread. Copies a password manager marks
    /// as concealed or transient, and copies of files, are not reported.
    /// </summary>
    event EventHandler<string>? TextCopied;

    /// <summary>Starts watching. Called once, at startup.</summary>
    void Start();

    /// <summary>Puts text on the clipboard, as if it had just been copied.</summary>
    void SetText(string text);
}
