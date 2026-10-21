using System.Globalization;
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
using Shiny;
using Shiny.Maui.Controls.Camera;
using ShinyNotch.Platform;

namespace ShinyNotch.MacOS;

/// <summary>
/// A USB webcam's own brightness, through UVC class requests on its control pipe - the camera keeps the
/// setting, so every app using it sees the change. The built-in camera and virtual cameras aren't USB
/// devices an app can talk to, and have none.
/// </summary>
/// <remarks>
/// <para>
/// AVFoundation names a USB camera "0x" + its locationID (8 hex digits) + vendor ID (4) + product ID (4),
/// which is how the camera the mirror shows is found among the USB devices.
/// </para>
/// <para>
/// IOKit's USB user-client interface is COM-shaped, so it is called through its vtable. The
/// <c>IOUSBDeviceInterface182</c> slots used here, counting the pointer-sized fields of
/// <c>IOUSBDeviceStruct182</c> in IOUSBLib.h from 0: 0 _reserved, 1 QueryInterface, 2 AddRef, 3 Release
/// (IUNKNOWN_C_GUTS), then 21 GetConfigurationDescriptorPtr and 30 DeviceRequestTO. The plug-in interface
/// (IOCFPlugInInterface) is only asked QueryInterface (slot 1). Neither request needs the device opened:
/// class requests on the default control pipe go through while the system's camera driver holds it.
/// </para>
/// </remarks>
[Singleton]
sealed unsafe class MacCameraControls : ICameraControls, IDisposable
{
    const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";
    const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // UVC 1.5: the VideoControl interface, its class-specific descriptors, and the brightness control.
    const byte InterfaceDescriptor = 0x04;
    const byte ClassSpecificInterface = 0x24;
    const byte VideoClass = 0x0E;
    const byte VideoControlSubclass = 0x01;
    const byte ProcessingUnit = 0x05;
    const ushort BrightnessSelector = 0x02 << 8;
    const byte SetCur = 0x01;
    const byte GetCur = 0x81;
    const byte GetMin = 0x82;
    const byte GetMax = 0x83;
    const byte GetDef = 0x87;
    const byte ClassToInterfaceOut = 0x21;
    const byte ClassToInterfaceIn = 0xA1;
    const uint RequestTimeoutMs = 500;

    const int QueryInterfaceSlot = 1;
    const int ReleaseSlot = 3;
    const int GetConfigurationDescriptorPtrSlot = 21;
    const int DeviceRequestTOSlot = 30;

    /// <summary>Looked up once per camera (a miss included): slider drags call in many times a second.</summary>
    readonly Dictionary<string, UvcCamera?> cameras = new();

    public CameraBrightness? GetBrightness(CameraInfo camera)
    {
        try
        {
            if (this.Find(camera) is not { } uvc)
                return null;

            if (uvc.Get(GetMin) is not { } min || uvc.Get(GetMax) is not { } max || uvc.Get(GetCur) is not { } current || max <= min)
                return null;

            // Some cameras don't answer GET_DEF; the middle of the range is the honest guess.
            var standard = uvc.Get(GetDef) ?? (short)((min + max) / 2);
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
            return this.Find(camera) is { } uvc && uvc.Set((short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue));
        }
        catch
        {
            return false;
        }
    }

    UvcCamera? Find(CameraInfo camera)
    {
        if (!this.cameras.TryGetValue(camera.Id, out var uvc))
        {
            uvc = ParseUniqueId(camera.Id) is { } usb ? UvcCamera.Open(usb.LocationId, usb.Vendor, usb.Product) : null;
            this.cameras[camera.Id] = uvc;
        }
        return uvc;
    }

    /// <summary>"0x14100000046d0825": locationID 0x14100000, vendor 0x046d, product 0x0825. Null for anything else.</summary>
    static (uint LocationId, ushort Vendor, ushort Product)? ParseUniqueId(string id)
    {
        if (id.Length != 18 || !id.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return null;

        const NumberStyles hex = NumberStyles.AllowHexSpecifier;
        var ci = CultureInfo.InvariantCulture;
        return UInt32.TryParse(id.AsSpan(2, 8), hex, ci, out var location) &&
               UInt16.TryParse(id.AsSpan(10, 4), hex, ci, out var vendor) &&
               UInt16.TryParse(id.AsSpan(14, 4), hex, ci, out var product)
            ? (location, vendor, product)
            : null;
    }

    public void Dispose()
    {
        foreach (var uvc in this.cameras.Values)
            uvc?.Dispose();
        this.cameras.Clear();
    }

    /// <summary>One webcam's device interface and where its brightness control lives.</summary>
    sealed class UvcCamera(IntPtr device, byte unitId, byte interfaceNumber) : IDisposable
    {
        IntPtr device = device;

        ushort Index => (ushort)((unitId << 8) | interfaceNumber);

        public static UvcCamera? Open(uint locationId, ushort vendor, ushort product)
        {
            // IOUSBHostDevice on current macOS; IOUSBDevice on the old stack.
            foreach (var className in new[] { "IOUSBHostDevice", "IOUSBDevice" })
            {
                if (IOServiceGetMatchingServices(0, IOServiceMatching(className), out var iterator) != 0)
                    continue;

                try
                {
                    uint service;
                    while ((service = IOIteratorNext(iterator)) != 0)
                    {
                        try
                        {
                            if (Number(service, "locationID") == locationId && Number(service, "idVendor") == vendor && Number(service, "idProduct") == product)
                                return FromService(service);
                        }
                        finally
                        {
                            IOObjectRelease(service);
                        }
                    }
                }
                finally
                {
                    IOObjectRelease(iterator);
                }
            }
            return null;
        }

        static UvcCamera? FromService(uint service)
        {
            if (IOCreatePlugInInterfaceForService(service, DeviceUserClientType, PlugInInterface, out var plugin, out _) != 0 || plugin == IntPtr.Zero)
                return null;

            IntPtr device;
            try
            {
                var query = (delegate* unmanaged<IntPtr, CFUUIDBytes, IntPtr*, int>)Slot(plugin, QueryInterfaceSlot);
                if (query(plugin, DeviceInterface182, &device) != 0 || device == IntPtr.Zero)
                    return null;
            }
            finally
            {
                IODestroyPlugInInterface(plugin);
            }

            if (FindBrightness(device) is { } control)
                return new UvcCamera(device, control.UnitId, control.Interface);

            Release(device);
            return null;
        }

        /// <summary>
        /// The VideoControl interface's number, and the Processing Unit after it whose controls include
        /// brightness - read from the first configuration's descriptors.
        /// </summary>
        static (byte UnitId, byte Interface)? FindBrightness(IntPtr device)
        {
            IntPtr config;
            var getConfig = (delegate* unmanaged<IntPtr, byte, IntPtr*, int>)Slot(device, GetConfigurationDescriptorPtrSlot);
            if (getConfig(device, 0, &config) != 0 || config == IntPtr.Zero)
                return null;

            var bytes = (byte*)config;
            var total = bytes[2] | (bytes[3] << 8);
            int? videoControl = null;
            for (var i = 0; i + 2 < total && bytes[i] > 0; i += bytes[i])
            {
                var length = bytes[i];
                var type = bytes[i + 1];
                if (type == InterfaceDescriptor && length >= 9)
                {
                    // A new interface: only the VideoControl one's class-specific descriptors count.
                    videoControl = bytes[i + 5] == VideoClass && bytes[i + 6] == VideoControlSubclass ? bytes[i + 2] : null;
                }
                else if (type == ClassSpecificInterface && videoControl is { } vc && length >= 9 && bytes[i + 2] == ProcessingUnit)
                {
                    // bUnitID at 3, bControlSize at 7, bmControls from 8; brightness is bit 0.
                    if (bytes[i + 7] > 0 && (bytes[i + 8] & 0x01) != 0)
                        return (bytes[i + 3], (byte)vc);
                }
            }
            return null;
        }

        public short? Get(byte request)
        {
            short value = 0;
            return this.Request(ClassToInterfaceIn, request, &value) ? value : null;
        }

        public bool Set(short value) => this.Request(ClassToInterfaceOut, SetCur, &value);

        bool Request(byte requestType, byte request, short* data)
        {
            if (this.device == IntPtr.Zero)
                return false;

            var req = new IOUSBDevRequestTO
            {
                bmRequestType = requestType,
                bRequest = request,
                wValue = BrightnessSelector,
                wIndex = this.Index,
                wLength = 2,
                pData = (IntPtr)data,
                noDataTimeout = RequestTimeoutMs,
                completionTimeout = RequestTimeoutMs
            };
            var send = (delegate* unmanaged<IntPtr, IOUSBDevRequestTO*, int>)Slot(this.device, DeviceRequestTOSlot);
            // The value goes over the bus little-endian, which is this machine's order too.
            return send(this.device, &req) == 0 && req.wLenDone == 2;
        }

        public void Dispose()
        {
            if (this.device == IntPtr.Zero)
                return;
            Release(this.device);
            this.device = IntPtr.Zero;
        }

        static void Release(IntPtr instance)
            => ((delegate* unmanaged<IntPtr, uint>)Slot(instance, ReleaseSlot))(instance);

        /// <summary>A function pointer from a COM-style instance: a pointer to a pointer to its vtable.</summary>
        static IntPtr Slot(IntPtr instance, int slot) => (*(IntPtr**)instance)[slot];

        static long Number(uint service, string key)
        {
            using var name = new NSString(key);
            var value = IORegistryEntryCreateCFProperty(service, name.Handle, IntPtr.Zero, 0);
            if (value == IntPtr.Zero)
                return -1;
            using var number = Runtime.GetNSObject<NSNumber>(value, owns: true);
            return number?.Int64Value ?? -1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IOUSBDevRequestTO
    {
        public byte bmRequestType;
        public byte bRequest;
        public ushort wValue;
        public ushort wIndex;
        public ushort wLength;
        public IntPtr pData;
        public uint wLenDone;
        public uint noDataTimeout;
        public uint completionTimeout;
    }

    /// <summary>A REFIID: 16 bytes, passed by value.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct CFUUIDBytes(ReadOnlySpan<byte> bytes)
    {
        public ulong Low = BitConverter.ToUInt64(bytes[..8]);
        public ulong High = BitConverter.ToUInt64(bytes[8..]);
    }

    // kIOUSBDeviceUserClientTypeID 9dc7b780-9ec0-11d4-a54f-000a27052861
    static readonly IntPtr DeviceUserClientType = ConstantUuid([0x9d, 0xc7, 0xb7, 0x80, 0x9e, 0xc0, 0x11, 0xd4, 0xa5, 0x4f, 0x00, 0x0a, 0x27, 0x05, 0x28, 0x61]);

    // kIOCFPlugInInterfaceID c244e858-109c-11d4-91d4-0050e4c6426f
    static readonly IntPtr PlugInInterface = ConstantUuid([0xc2, 0x44, 0xe8, 0x58, 0x10, 0x9c, 0x11, 0xd4, 0x91, 0xd4, 0x00, 0x50, 0xe4, 0xc6, 0x42, 0x6f]);

    // kIOUSBDeviceInterfaceID182 152fc496-4891-11d5-9d52-000a27801e86
    static readonly CFUUIDBytes DeviceInterface182 = new([0x15, 0x2f, 0xc4, 0x96, 0x48, 0x91, 0x11, 0xd5, 0x9d, 0x52, 0x00, 0x0a, 0x27, 0x80, 0x1e, 0x86]);

    static IntPtr ConstantUuid(byte[] b) => CFUUIDGetConstantUUIDWithBytes(
        IntPtr.Zero, b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7], b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);

    [DllImport(CoreFoundationLib)]
    static extern IntPtr CFUUIDGetConstantUUIDWithBytes(IntPtr alloc, byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6, byte b7, byte b8, byte b9, byte b10, byte b11, byte b12, byte b13, byte b14, byte b15);

    [DllImport(IOKitLib)]
    static extern IntPtr IOServiceMatching(string name);

    [DllImport(IOKitLib)]
    static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

    [DllImport(IOKitLib)]
    static extern uint IOIteratorNext(uint iterator);

    [DllImport(IOKitLib)]
    static extern int IOObjectRelease(uint obj);

    [DllImport(IOKitLib)]
    static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport(IOKitLib)]
    static extern int IOCreatePlugInInterfaceForService(uint service, IntPtr pluginType, IntPtr interfaceType, out IntPtr theInterface, out int theScore);

    [DllImport(IOKitLib)]
    static extern int IODestroyPlugInInterface(IntPtr theInterface);
}
