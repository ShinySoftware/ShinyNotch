using System.Runtime.InteropServices;
using ShinyNotch.Platform;

namespace ShinyNotch.Windows;

/// <summary>
/// Core Audio: the default render endpoint's master volume and mute, with a volume callback on it and a
/// notification client that moves the callback when the default device changes (headphones in).
/// </summary>
sealed class WindowsAudioControl : IAudioControl
{
    static readonly Guid AudioEndpointVolumeId = typeof(IAudioEndpointVolume).GUID;
    const int ClsCtxAll = 23;

    readonly IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
    readonly VolumeCallback volumeCallback;
    readonly DeviceCallback deviceCallback;
    IAudioEndpointVolume? endpoint;
    string? endpointId;
    Action? changed;

    public WindowsAudioControl()
    {
        this.volumeCallback = new VolumeCallback(() => this.changed?.Invoke());
        this.deviceCallback = new DeviceCallback(() => this.changed?.Invoke());
    }

    public float? Volume
    {
        get
        {
            // Read after every change, on the main thread: where a new default device gets the callback.
            this.FollowDefaultDevice();
            if (this.endpoint == null)
                return null;
            try
            {
                Marshal.ThrowExceptionForHR(this.endpoint.GetMasterVolumeLevelScalar(out var level));
                return level;
            }
            catch (COMException)
            {
                return null;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            this.FollowDefaultDevice();
            return this.endpoint != null && this.endpoint.GetMute(out var muted) == 0 && muted;
        }
    }

    public void SetVolume(float value)
    {
        this.FollowDefaultDevice();
        if (this.endpoint == null)
            return;

        value = Math.Clamp(value, 0f, 1f);
        var context = Guid.Empty;
        this.endpoint.SetMasterVolumeLevelScalar(value, ref context);
        if (value > 0 && this.IsMuted)
            this.SetMuted(false);
    }

    public void SetMuted(bool muted)
    {
        this.FollowDefaultDevice();
        var context = Guid.Empty;
        this.endpoint?.SetMute(muted, ref context);
    }

    public void Observe(Action onChange)
    {
        this.changed = onChange;
        this.enumerator.RegisterEndpointNotificationCallback(this.deviceCallback);
        this.FollowDefaultDevice();
    }

    /// <summary>Moves to the default render endpoint, and its volume callback with it, if it has changed.</summary>
    void FollowDefaultDevice()
    {
        IMMDevice? device = null;
        string? id = null;
        try
        {
            if (this.enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device) == 0)
                device.GetId(out id);
        }
        catch (COMException)
        {
            device = null;
        }

        if (id == this.endpointId && this.endpoint != null)
            return;

        if (this.endpoint != null)
        {
            try
            {
                this.endpoint.UnregisterControlChangeNotify(this.volumeCallback);
            }
            catch (COMException) { }
        }

        this.endpoint = null;
        this.endpointId = id;
        if (device == null)
            return;

        var iid = AudioEndpointVolumeId;
        if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var activated) == 0 && activated is IAudioEndpointVolume volume)
        {
            this.endpoint = volume;
            if (this.changed != null)
                volume.RegisterControlChangeNotify(this.volumeCallback);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // COM
    // ---------------------------------------------------------------------------------------------

    enum EDataFlow
    {
        Render,
        Capture,
        All
    }

    enum ERole
    {
        Console,
        Multimedia,
        Communications
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    /// <summary>Volume or mute changed, from the keys, the flyout or the mixer. Arrives on an audio thread.</summary>
    [ComVisible(true)]
    sealed class VolumeCallback(Action changed) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            changed();
            return 0;
        }
    }

    /// <summary>A new default output device. The reader follows it on the main thread.</summary>
    [ComVisible(true)]
    sealed class DeviceCallback(Action changed) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, int newState) => 0;
        public int OnDeviceAdded(string deviceId) => 0;
        public int OnDeviceRemoved(string deviceId) => 0;
        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.Render && role == ERole.Multimedia)
                changed();
            return 0;
        }
    }
}
