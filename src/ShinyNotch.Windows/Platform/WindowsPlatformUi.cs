using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using ShinyNotch.Platform;
using global::Windows.Graphics.Imaging;
using global::Windows.Storage;
using global::Windows.Storage.FileProperties;
using global::Windows.Storage.Streams;
using UISize = global::Windows.Foundation.Size;

namespace ShinyNotch.Windows;

/// <summary>
/// Segoe Fluent Icons for the SF Symbol names, a WinUI TextBlock for measuring, the platform view's own
/// pointer events for hover, and Windows.Graphics.Imaging for pixels and file icons.
/// </summary>
sealed class WindowsPlatformUi : IPlatformUi
{
    /// <summary>Segoe Fluent Icons ships with Windows 11; Windows 10 has MDL2, which shares the code points used here.</summary>
    static readonly string IconFont = Environment.OSVersion.Version.Build >= 22000 ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    static readonly Color Sun = Color.FromArgb("#FFD60A");
    static readonly Color Moon = Color.FromArgb("#E5E5EA");
    static readonly Color Rain = Color.FromArgb("#64D2FF");
    static readonly Color Cloud = Color.FromArgb("#D1D1D6");

    public ImageSource? Symbol(string name, double size, Color ink, bool multicolor, bool bold)
    {
        if (!SymbolGlyphs.Map.TryGetValue(name, out var glyph))
            return null;

        return new FontImageSource
        {
            Glyph = glyph,
            FontFamily = IconFont,
            Size = size,
            Color = multicolor ? Tint(name) : ink
        };
    }

    /// <summary>
    /// The font has no colour layers, so a multicolour (weather) symbol gets the colour of what it mostly shows.
    /// </summary>
    static Color Tint(string name) =>
        name.Contains("rain") || name.Contains("drizzle") || name.Contains("snow") || name.Contains("sleet") || name.Contains("hail") ? Rain
        : name.Contains("sun") || name.Contains("bolt") ? Sun
        : name.Contains("moon") ? Moon
        : Cloud;

    public double TextWidth(string text, double fontSize, bool bold)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal
        };
        block.Measure(new UISize(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
    }

    /// <summary>The native element's own pointer events: they fire whether or not the notch window has focus.</summary>
    public void TrackHover(View view, Action<bool> changed)
    {
        UIElement? attached = null;
        void Entered(object _, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => changed(true);
        void Exited(object _, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => changed(false);

        view.HandlerChanged += (_, _) =>
        {
            if (attached != null)
            {
                attached.PointerEntered -= Entered;
                attached.PointerExited -= Exited;
                attached = null;
            }
            if (view.Handler?.PlatformView is UIElement element)
            {
                element.PointerEntered += Entered;
                element.PointerExited += Exited;
                attached = element;
            }
        };
    }

    public byte[]? Pixels(byte[] encoded, int size) => Run(async () =>
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(encoded.AsBuffer());
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform { ScaledWidth = (uint)size, ScaledHeight = (uint)size, InterpolationMode = BitmapInterpolationMode.Linear },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage
        );
        return data.DetachPixelData();
    });

    /// <summary>The shell's icon for the file - what File Explorer shows for it - re-encoded as PNG.</summary>
    public byte[]? FileIcon(string path, int size) => Run(async () =>
    {
        IStorageItemProperties item = Directory.Exists(path)
            ? await StorageFolder.GetFolderFromPathAsync(path)
            : await StorageFile.GetFileFromPathAsync(path);
        using var thumbnail = await item.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)size, ThumbnailOptions.UseCurrentScale);
        if (thumbnail == null)
            return null;

        var decoder = await BitmapDecoder.CreateAsync(thumbnail);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using var png = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        var bytes = new byte[png.Size];
        png.Seek(0);
        await png.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    });

    /// <summary>
    /// The interface is synchronous and its callers are on the UI thread, so the WinRT work runs on the
    /// pool and is waited for there - awaiting it on the UI thread and blocking would deadlock.
    /// </summary>
    static byte[]? Run(Func<Task<byte[]?>> work)
    {
        try
        {
            return Task.Run(work).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }
}
