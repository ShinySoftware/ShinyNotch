namespace ShinyNotch.Platform;

/// <summary>
/// The drawing the notch's views can't do in plain MAUI on every head: icons, text measuring, hover,
/// and reading pixels out of an image.
/// </summary>
public interface IPlatformUi
{
    /// <summary>
    /// An icon by its SF Symbol name - the app's icon vocabulary on every platform - fitted to a square
    /// of <c>Ui.SymbolBox(size)</c> points. Null when there is nothing for that name. Each result is
    /// cached by the caller, so this can be slow.
    /// </summary>
    /// <param name="multicolor">The symbol's own colours (weather), rather than <paramref name="ink"/>.</param>
    ImageSource? Symbol(string name, double size, Color ink, bool multicolor, bool bold);

    /// <summary>The width, in points, of <paramref name="text"/> in the system font as the platform draws it.</summary>
    double TextWidth(string text, double fontSize, bool bold);

    /// <summary>Reports the pointer entering and leaving <paramref name="view"/>, for its hover highlight.</summary>
    void TrackHover(View view, Action<bool> changed);

    /// <summary>
    /// An encoded image (PNG/JPEG) shrunk to <paramref name="size"/>×<paramref name="size"/> RGBA pixels,
    /// for picking a colour out of it. Null when it can't be decoded.
    /// </summary>
    byte[]? Pixels(byte[] encoded, int size);

    /// <summary>The file manager's icon for a file, as PNG bytes, or null.</summary>
    byte[]? FileIcon(string path, int size);

    /// <summary>
    /// Shows a camera preview flipped left to right, like a mirror, whichever way the camera says it
    /// faces. Called again after every camera switch. By default the view itself is flipped; a head
    /// whose views don't flip around their centre does it its own way.
    /// </summary>
    void MirrorCamera(View camera)
    {
        camera.AnchorX = 0.5;
        camera.ScaleX = -1;
    }
}
