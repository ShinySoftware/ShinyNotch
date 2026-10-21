using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Shiny;
using Shiny.Maui.Controls.Camera;
using ShinyNotch.Platform;

namespace ShinyNotch.Windows;

/// <summary>
/// A webcam's own brightness, through DirectShow's <c>IAMVideoProcAmp</c> on its capture filter. The
/// driver keeps the setting, so Teams and browsers see the change. Cameras without the control (most
/// virtual ones) have none.
/// </summary>
/// <remarks>
/// The camera view names a camera by its device interface path from Windows.Devices.Enumeration; DirectShow
/// knows the same device by a path that differs only in the interface class at the end (and in case), so
/// the two are matched on everything before it.
/// </remarks>
[Singleton]
sealed class WindowsCameraControls : ICameraControls
{
    const int VideoProcAmpBrightness = 0;
    const int VideoProcAmpFlagsManual = 2;

    static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    static readonly Guid PropertyBagId = new("55272A00-42CB-11CE-8135-00AA004BB851");
    static readonly Guid BaseFilterId = new("56a86895-0ad4-11ce-b03a-0020af0ba770");

    /// <summary>Looked up once per camera (a miss included): slider drags call in many times a second.</summary>
    readonly Dictionary<string, IAMVideoProcAmp?> cameras = new();

    public CameraBrightness? GetBrightness(CameraInfo camera)
    {
        try
        {
            if (this.Find(camera) is not { } amp)
                return null;

            if (amp.GetRange(VideoProcAmpBrightness, out var min, out var max, out _, out var standard, out _) != 0 || max <= min)
                return null;
            if (amp.Get(VideoProcAmpBrightness, out var current, out _) != 0)
                return null;

            return new CameraBrightness(min, max, standard, current);
        }
        catch
        {
            return null;
        }
    }

    public bool SetBrightness(CameraInfo camera, double value)
    {
        try
        {
            return this.Find(camera) is { } amp && amp.Set(VideoProcAmpBrightness, (int)Math.Round(value), VideoProcAmpFlagsManual) == 0;
        }
        catch
        {
            return false;
        }
    }

    IAMVideoProcAmp? Find(CameraInfo camera)
    {
        if (!this.cameras.TryGetValue(camera.Id, out var amp))
        {
            amp = Open(camera.Id);
            this.cameras[camera.Id] = amp;
        }
        return amp;
    }

    static IAMVideoProcAmp? Open(string cameraId)
    {
        var wanted = DeviceKey(cameraId);
        var devices = (ICreateDevEnum)new SystemDeviceEnum();
        var category = VideoInputDeviceCategory;
        // S_FALSE (1) means the category is empty.
        if (devices.CreateClassEnumerator(ref category, out var monikers, 0) != 0 || monikers == null)
            return null;

        var one = new IMoniker[1];
        while (monikers.Next(1, one, IntPtr.Zero) == 0)
        {
            var moniker = one[0];
            try
            {
                if (DevicePath(moniker) is not { } path || !String.Equals(DeviceKey(path), wanted, StringComparison.OrdinalIgnoreCase))
                    continue;

                var filterId = BaseFilterId;
                moniker.BindToObject(null!, null!, ref filterId, out var filter);
                // Kept for as long as the app runs: the filter is the camera's handle here.
                return filter as IAMVideoProcAmp;
            }
            catch
            {
                // A device that won't bind is one without the control, as far as the mirror is concerned.
            }
        }
        return null;
    }

    static string? DevicePath(IMoniker moniker)
    {
        var bagId = PropertyBagId;
        moniker.BindToStorage(null!, null!, ref bagId, out var bag);
        object? value = null;
        return bag is IPropertyBag properties && properties.Read("DevicePath", ref value, IntPtr.Zero) == 0 ? value as string : null;
    }

    /// <summary>
    /// "\\?\USB#VID_046D&amp;PID_0825&amp;MI_00#7&amp;2a1f3b0&amp;0&amp;0000#{interface class}\GLOBAL" without its
    /// interface class: what the camera view's id and DirectShow's path have in common.
    /// </summary>
    static string DeviceKey(string path)
    {
        var end = path.LastIndexOf("#{", StringComparison.Ordinal);
        return end > 0 ? path[..end] : path;
    }

    [ComImport, Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")]
    class SystemDeviceEnum;

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid deviceClass, out IEnumMoniker? enumerator, int flags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string name, ref object? value, IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string name, ref object value);
    }

    [ComImport, Guid("C6E13360-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMVideoProcAmp
    {
        [PreserveSig]
        int GetRange(int property, out int minimum, out int maximum, out int step, out int standard, out int capsFlags);

        [PreserveSig]
        int Set(int property, int value, int flags);

        [PreserveSig]
        int Get(int property, out int value, out int flags);
    }
}
