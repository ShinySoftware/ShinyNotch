using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;

namespace ShinyNotch.Native;

/// <summary>CoreAudio: the default output device's volume and mute, and word when either changes.</summary>
static unsafe class CoreAudio
{
    const string CoreAudioLib = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    const string AudioToolboxLib = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    const uint SystemObject = 1;
    static readonly uint DefaultOutputDevice = FourCC("dOut");
    static readonly uint VirtualMainVolume = FourCC("vmvc");
    static readonly uint Mute = FourCC("mute");
    static readonly uint ScopeGlobal = FourCC("glob");
    static readonly uint ScopeOutput = FourCC("outp");

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;
    }

    [DllImport(CoreAudioLib)]
    static extern int AudioObjectGetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint dataSize, out uint data);

    [DllImport(CoreAudioLib)]
    static extern int AudioObjectSetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, uint dataSize, ref uint data);

    // The "virtual main" volume is a property the AudioHardwareService layer synthesises over a device's
    // channels, so it works on devices that only expose per-channel volume (most built-in speakers).
    [DllImport(AudioToolboxLib)]
    static extern int AudioHardwareServiceGetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint dataSize, out float data);

    [DllImport(AudioToolboxLib)]
    static extern int AudioHardwareServiceSetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, uint dataSize, ref float data);

    [DllImport(CoreAudioLib)]
    static extern int AudioObjectAddPropertyListener(uint objectId, ref PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, IntPtr, int> listener, IntPtr clientData);

    [DllImport(CoreAudioLib)]
    static extern int AudioObjectRemovePropertyListener(uint objectId, ref PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, IntPtr, int> listener, IntPtr clientData);

    [DllImport(AudioToolboxLib)]
    static extern int AudioHardwareServiceAddPropertyListener(uint objectId, ref PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, IntPtr, int> listener, IntPtr clientData);

    [DllImport(AudioToolboxLib)]
    static extern int AudioHardwareServiceRemovePropertyListener(uint objectId, ref PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, IntPtr, int> listener, IntPtr clientData);

    static Action? changed;
    static uint observedDevice;

    static uint FourCC(string s) => (uint)((s[0] << 24) | (s[1] << 16) | (s[2] << 8) | s[3]);

    static uint OutputDevice()
    {
        var address = new PropertyAddress { Selector = DefaultOutputDevice, Scope = ScopeGlobal, Element = 0 };
        uint size = sizeof(uint);
        return AudioObjectGetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, ref size, out var device) == 0 ? device : 0;
    }

    /// <summary>0..1, or null when the output device has no volume control (HDMI, some interfaces).</summary>
    public static float? GetVolume()
    {
        var device = OutputDevice();
        if (device == 0)
            return null;

        var address = new PropertyAddress { Selector = VirtualMainVolume, Scope = ScopeOutput, Element = 0 };
        uint size = sizeof(float);
        return AudioHardwareServiceGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, out var volume) == 0 ? volume : null;
    }

    public static void SetVolume(float volume)
    {
        var device = OutputDevice();
        if (device == 0)
            return;

        volume = Math.Clamp(volume, 0f, 1f);
        var address = new PropertyAddress { Selector = VirtualMainVolume, Scope = ScopeOutput, Element = 0 };
        AudioHardwareServiceSetPropertyData(device, ref address, 0, IntPtr.Zero, sizeof(float), ref volume);
        if (volume > 0 && IsMuted())
            SetMuted(false);
    }

    public static bool IsMuted()
    {
        var device = OutputDevice();
        var address = new PropertyAddress { Selector = Mute, Scope = ScopeOutput, Element = 0 };
        uint size = sizeof(uint);
        return device != 0 && AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, out var muted) == 0 && muted != 0;
    }

    public static void SetMuted(bool muted)
    {
        var device = OutputDevice();
        if (device == 0)
            return;

        var address = new PropertyAddress { Selector = Mute, Scope = ScopeOutput, Element = 0 };
        uint value = muted ? 1u : 0u;
        AudioObjectSetPropertyData(device, ref address, 0, IntPtr.Zero, sizeof(uint), ref value);
    }

    /// <summary>
    /// Calls <paramref name="onChange"/> when the default output device, its volume or its mute changes:
    /// listeners rather than a poll, so nothing runs while nothing changes. It arrives on a CoreAudio
    /// thread; the handler should hop to the main thread and call <see cref="FollowDefaultDevice"/>.
    /// </summary>
    public static void Observe(Action onChange)
    {
        changed = onChange;
        var address = new PropertyAddress { Selector = DefaultOutputDevice, Scope = ScopeGlobal, Element = 0 };
        AudioObjectAddPropertyListener(SystemObject, ref address, &OnPropertyChanged, IntPtr.Zero);
        FollowDefaultDevice();
    }

    /// <summary>Moves the volume and mute listeners to the default output device, if it has changed.</summary>
    public static void FollowDefaultDevice()
    {
        var device = OutputDevice();
        if (device == observedDevice)
            return;

        if (observedDevice != 0)
            Listen(observedDevice, false);
        observedDevice = device;
        if (device != 0)
            Listen(device, true);
    }

    static void Listen(uint device, bool add)
    {
        var volume = new PropertyAddress { Selector = VirtualMainVolume, Scope = ScopeOutput, Element = 0 };
        var mute = new PropertyAddress { Selector = Mute, Scope = ScopeOutput, Element = 0 };
        if (add)
        {
            AudioHardwareServiceAddPropertyListener(device, ref volume, &OnPropertyChanged, IntPtr.Zero);
            AudioObjectAddPropertyListener(device, ref mute, &OnPropertyChanged, IntPtr.Zero);
        }
        else
        {
            AudioHardwareServiceRemovePropertyListener(device, ref volume, &OnPropertyChanged, IntPtr.Zero);
            AudioObjectRemovePropertyListener(device, ref mute, &OnPropertyChanged, IntPtr.Zero);
        }
    }

    [UnmanagedCallersOnly]
    static int OnPropertyChanged(uint objectId, uint count, PropertyAddress* addresses, IntPtr clientData)
    {
        changed?.Invoke();
        return 0;
    }
}

/// <summary>
/// Built-in display brightness through the private DisplayServices framework — the same call the
/// brightness keys and Control Centre use. There is no public API for it on Apple silicon.
/// </summary>
static unsafe class Brightness
{
    const string DisplayServicesLib = "/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices";
    const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(DisplayServicesLib)]
    static extern int DisplayServicesGetBrightness(uint display, out float brightness);

    [DllImport(DisplayServicesLib)]
    static extern int DisplayServicesSetBrightness(uint display, float brightness);

    [DllImport(DisplayServicesLib)]
    static extern int DisplayServicesRegisterForBrightnessChangeNotifications(uint display, IntPtr observer, delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void> callback);

    static Action? changed;

    [DllImport(CoreGraphicsLib)]
    static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint count);

    [DllImport(CoreGraphicsLib)]
    static extern int CGDisplayIsBuiltin(uint display);

    [DllImport(CoreGraphicsLib)]
    static extern uint CGMainDisplayID();

    static uint Display()
    {
        var displays = new uint[16];
        if (CGGetActiveDisplayList(16, displays, out var count) == 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (CGDisplayIsBuiltin(displays[i]) != 0)
                    return displays[i];
            }
        }
        return CGMainDisplayID();
    }

    /// <summary>0..1, or null when the display's brightness can't be read (most external monitors).</summary>
    public static float? Get()
    {
        try
        {
            return DisplayServicesGetBrightness(Display(), out var value) == 0 ? value : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public static void Set(float value)
    {
        try
        {
            DisplayServicesSetBrightness(Display(), Math.Clamp(value, 0f, 1f));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Calls <paramref name="onChange"/> whenever the built-in display's brightness changes - keys, Control
    /// Centre or auto-brightness - delivered on the main run loop. False when the private call is missing
    /// (a future macOS), so the caller can fall back to polling.
    /// </summary>
    public static bool Observe(Action onChange)
    {
        try
        {
            changed = onChange;
            var display = Display();
            // The observer is only an identity for the notification centre; the display id will do.
            return DisplayServicesRegisterForBrightnessChangeNotifications(display, (IntPtr)display, &OnBrightnessChanged) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [UnmanagedCallersOnly]
    static void OnBrightnessChanged(IntPtr center, IntPtr observer, IntPtr name, IntPtr obj, IntPtr userInfo)
        => changed?.Invoke();
}

/// <summary>Mach host statistics, sysctl, IOKit registry and getifaddrs — the raw counters behind the Stats tab.</summary>
static unsafe class SystemCounters
{
    const string LibSystem = "/usr/lib/libSystem.dylib";
    const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";

    const int HostCpuLoadInfo = 3;
    const int HostVmInfo64 = 4;
    const int AfLink = 18;

    [DllImport(LibSystem)]
    static extern uint mach_host_self();

    [DllImport(LibSystem)]
    static extern int host_statistics(uint host, int flavor, int* info, ref uint count);

    [DllImport(LibSystem)]
    static extern int host_statistics64(uint host, int flavor, int* info, ref uint count);

    [DllImport(LibSystem)]
    static extern int host_page_size(uint host, out nuint pageSize);

    [DllImport(LibSystem)]
    static extern int sysctlbyname(string name, out ulong value, ref nuint size, IntPtr newValue, nuint newSize);

    [DllImport(LibSystem)]
    static extern int getifaddrs(out IntPtr addresses);

    [DllImport(LibSystem)]
    static extern void freeifaddrs(IntPtr addresses);

    [DllImport(IOKitLib)]
    static extern IntPtr IOServiceMatching(string name);

    [DllImport(IOKitLib)]
    static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

    [DllImport(IOKitLib)]
    static extern uint IOIteratorNext(uint iterator);

    [DllImport(IOKitLib)]
    static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport(IOKitLib)]
    static extern int IOObjectRelease(uint obj);

    static readonly uint Host = mach_host_self();

    // Sampled once a second while the Stats tab is open; made once rather than per service per sample.
    static readonly NSString GpuUtilisationKey = new("Device Utilization %");
    static readonly NSString BytesReadKey = new("Bytes (Read)");
    static readonly NSString BytesWrittenKey = new("Bytes (Write)");

    /// <summary>Cumulative CPU ticks: busy (user + system + nice) and total.</summary>
    public static (ulong Busy, ulong Total) CpuTicks()
    {
        var info = stackalloc int[4];
        uint count = 4;
        if (host_statistics(Host, HostCpuLoadInfo, info, ref count) != 0)
            return (0, 0);

        ulong user = (uint)info[0], system = (uint)info[1], idle = (uint)info[2], nice = (uint)info[3];
        return (user + system + nice, user + system + idle + nice);
    }

    /// <summary>
    /// Memory in use the way Activity Monitor counts it — app memory (internal minus purgeable) plus wired
    /// plus compressed — and physical memory.
    /// </summary>
    public static (ulong Used, ulong Total) Memory()
    {
        var info = stackalloc int[38];
        uint count = 38;
        if (host_statistics64(Host, HostVmInfo64, info, ref count) != 0 || host_page_size(Host, out var pageSize) != 0)
            return (0, Total());

        var bytes = (byte*)info;
        ulong wired = *(uint*)(bytes + 12);
        ulong purgeable = *(uint*)(bytes + 88);
        ulong compressor = *(uint*)(bytes + 128);
        ulong internalPages = *(uint*)(bytes + 140);
        var app = internalPages > purgeable ? internalPages - purgeable : 0;
        return ((app + wired + compressor) * pageSize, Total());
    }

    static ulong Total()
    {
        nuint size = sizeof(ulong);
        return sysctlbyname("hw.memsize", out var total, ref size, IntPtr.Zero, 0) == 0 ? total : 0;
    }

    /// <summary>The GPU's "Device Utilization %" from the IOAccelerator registry entry, 0..100.</summary>
    public static double? GpuUtilisation()
    {
        double? result = null;
        ForEachService("IOAccelerator", "PerformanceStatistics", stats =>
        {
            if (stats.ObjectForKey(GpuUtilisationKey) is NSNumber n)
                result = Math.Max(result ?? 0, n.DoubleValue);
        });
        return result;
    }

    /// <summary>Cumulative bytes read and written across every block storage driver.</summary>
    public static (ulong Read, ulong Written) DiskBytes()
    {
        ulong read = 0, written = 0;
        ForEachService("IOBlockStorageDriver", "Statistics", stats =>
        {
            if (stats.ObjectForKey(BytesReadKey) is NSNumber r)
                read += r.UInt64Value;
            if (stats.ObjectForKey(BytesWrittenKey) is NSNumber w)
                written += w.UInt64Value;
        });
        return (read, written);
    }

    static void ForEachService(string className, string property, Action<NSDictionary> visit)
    {
        var matching = IOServiceMatching(className);
        if (matching == IntPtr.Zero || IOServiceGetMatchingServices(0, matching, out var iterator) != 0)
            return;

        using var key = new NSString(property);
        try
        {
            uint entry;
            while ((entry = IOIteratorNext(iterator)) != 0)
            {
                try
                {
                    // Released here, not by the GC: there is one of these per GPU and per disk, every sample.
                    var value = IORegistryEntryCreateCFProperty(entry, key.Handle, IntPtr.Zero, 0);
                    using var dict = value != IntPtr.Zero ? Runtime.GetNSObject<NSDictionary>(value, owns: true) : null;
                    if (dict != null)
                        visit(dict);
                }
                finally
                {
                    IOObjectRelease(entry);
                }
            }
        }
        finally
        {
            IOObjectRelease(iterator);
        }
    }

    /// <summary>
    /// Cumulative bytes in and out across every non-loopback interface. These are the 32-bit if_data
    /// counters, so they wrap at 4GB; <see cref="Services.SystemStatsService"/> handles that.
    /// </summary>
    public static (ulong In, ulong Out) NetworkBytes()
    {
        if (getifaddrs(out var list) != 0)
            return (0, 0);

        ulong rx = 0, tx = 0;
        try
        {
            // struct ifaddrs (LP64): next@0, name@8, flags@16, addr@24, netmask@32, dstaddr@40, data@48
            for (var p = list; p != IntPtr.Zero; p = Marshal.ReadIntPtr(p))
            {
                var addr = Marshal.ReadIntPtr(p, 24);
                var data = Marshal.ReadIntPtr(p, 48);
                if (addr == IntPtr.Zero || data == IntPtr.Zero || Marshal.ReadByte(addr, 1) != AfLink)
                    continue;

                var name = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(p, 8));
                if (name == null || name.StartsWith("lo", StringComparison.Ordinal))
                    continue;

                // struct if_data: ifi_ibytes@40, ifi_obytes@44 (u_int32_t)
                rx += (uint)Marshal.ReadInt32(data, 40);
                tx += (uint)Marshal.ReadInt32(data, 44);
            }
        }
        finally
        {
            freeifaddrs(list);
        }
        return (rx, tx);
    }
}
