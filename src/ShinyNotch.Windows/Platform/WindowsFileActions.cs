using System.Diagnostics;
using ShinyNotch.Platform;
using global::Windows.ApplicationModel.DataTransfer;
using Clipboard = global::Windows.ApplicationModel.DataTransfer.Clipboard;
using DataPackage = global::Windows.ApplicationModel.DataTransfer.DataPackage;
using DataPackageOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using global::Windows.Storage;
using Launcher = global::Windows.System.Launcher;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace ShinyNotch.Windows;

/// <summary>The clipboard as storage items, File Explorer, and the Windows share sheet.</summary>
sealed class WindowsFileActions : IFileActions
{
    /// <summary>The window whose share manager is hooked up. One handler per manager, reading <see cref="sharing"/>.</summary>
    IntPtr hooked;
    IReadOnlyList<string> sharing = [];

    /// <summary>Storage items on the clipboard, so Ctrl+V in File Explorer copies the files there.</summary>
    public async void Copy(IReadOnlyList<string> paths)
    {
        var items = await Items(paths);
        if (items.Count == 0)
            return;

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems(items);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    /// <summary>
    /// One Explorer window per folder; /select only takes one file, so the first of each folder is the
    /// one selected.
    /// </summary>
    public void Reveal(IReadOnlyList<string> paths)
    {
        foreach (var path in paths.GroupBy(p => Path.GetDirectoryName(p) ?? p).Select(g => g.First()))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    public string FileManagerName => "File Explorer";

    public async void Open(string path)
    {
        if (Directory.Exists(path))
            await Launcher.LaunchFolderPathAsync(path);
        else
            await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path));
    }

    public string? ShareName => "Share";

    public string ShareSymbol => "square.and.arrow.up";

    /// <summary>
    /// The share sheet, through the interop that ties it to a window - unpackaged apps have no
    /// CoreWindow. The notch window, so the sheet opens by the notch.
    /// </summary>
    public void Share(IReadOnlyList<string> paths)
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows
            .OrderByDescending(w => w.Title == "Notch")
            .Select(w => w.Handler?.PlatformView)
            .OfType<WinUIWindow>()
            .FirstOrDefault();
        if (window == null || paths.Count == 0)
            return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        this.sharing = paths.ToList();
        if (this.hooked != hwnd)
        {
            this.hooked = hwnd;
            DataTransferManagerInterop.GetForWindow(hwnd).DataRequested += async (_, e) =>
            {
                var files = this.sharing;
                var deferral = e.Request.GetDeferral();
                try
                {
                    e.Request.Data.Properties.Title = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} files";
                    e.Request.Data.SetStorageItems(await Items(files));
                }
                finally
                {
                    deferral.Complete();
                }
            };
        }
        DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
    }

    static async Task<List<IStorageItem>> Items(IReadOnlyList<string> paths)
    {
        var items = new List<IStorageItem>();
        foreach (var path in paths)
        {
            try
            {
                items.Add(Directory.Exists(path) ? await StorageFolder.GetFolderFromPathAsync(path) : await StorageFile.GetFileFromPathAsync(path));
            }
            catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException)
            {
                // Gone since it was dropped: the tray prunes it on its next showing.
            }
        }
        return items;
    }
}
