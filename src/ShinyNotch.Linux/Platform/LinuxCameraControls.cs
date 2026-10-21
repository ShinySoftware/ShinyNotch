using System.Runtime.InteropServices;
using Shiny;
using Shiny.Maui.Controls.Camera;
using ShinyNotch.Platform;

namespace ShinyNotch.Linux;

/// <summary>
/// A webcam's own brightness, through V4L2's brightness control on its /dev/video node. The driver keeps
/// it, so every app using the camera sees the change. Nodes without the control (metadata nodes, most
/// virtual cameras) have none.
/// </summary>
/// <remarks>
/// GTK has no camera view yet, so the Mirror tab doesn't show here; this is ready for when it does. A
/// camera is matched by its node path, or else by the name the driver gives it in sysfs.
/// </remarks>
[Singleton]
sealed unsafe class LinuxCameraControls : ICameraControls
{
    const uint BrightnessId = 0x00980900;       // V4L2_CID_BRIGHTNESS
    const uint ControlDisabled = 0x0001;        // V4L2_CTRL_FLAG_DISABLED
    // _IOWR('V', nr, size): (3 << 30) | (size << 16) | ('V' << 8) | nr
    const uint QueryControl = 0xC0445624;       // VIDIOC_QUERYCTRL, struct v4l2_queryctrl (68 bytes)
    const uint GetControl = 0xC008561B;         // VIDIOC_G_CTRL, struct v4l2_control (8 bytes)
    const uint SetControl = 0xC008561C;         // VIDIOC_S_CTRL
    const int ReadWrite = 2;                    // O_RDWR

    /// <summary>The node for each camera, looked up once (a miss included).</summary>
    readonly Dictionary<string, string?> nodes = new();

    public CameraBrightness? GetBrightness(CameraInfo camera)
    {
        try
        {
            if (this.Find(camera) is not { } node)
                return null;

            var fd = open(node, ReadWrite);
            if (fd < 0)
                return null;
            try
            {
                var query = new QueryCtrl { Id = BrightnessId };
                if (ioctl(fd, QueryControl, &query) != 0 || (query.Flags & ControlDisabled) != 0 || query.Maximum <= query.Minimum)
                    return null;

                var control = new Control { Id = BrightnessId };
                if (ioctl(fd, GetControl, &control) != 0)
                    return null;

                return new CameraBrightness(query.Minimum, query.Maximum, query.Default, control.Value);
            }
            finally
            {
                close(fd);
            }
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
            if (this.Find(camera) is not { } node)
                return false;

            var fd = open(node, ReadWrite);
            if (fd < 0)
                return false;
            try
            {
                var control = new Control { Id = BrightnessId, Value = (int)Math.Round(value) };
                return ioctl(fd, SetControl, &control) == 0;
            }
            finally
            {
                close(fd);
            }
        }
        catch
        {
            return false;
        }
    }

    string? Find(CameraInfo camera)
    {
        if (!this.nodes.TryGetValue(camera.Id, out var node))
        {
            node = Locate(camera);
            this.nodes[camera.Id] = node;
        }
        return node;
    }

    static string? Locate(CameraInfo camera)
    {
        if (camera.Id.StartsWith("/dev/video", StringComparison.Ordinal) && File.Exists(camera.Id))
            return camera.Id;

        const string sysfs = "/sys/class/video4linux";
        if (!Directory.Exists(sysfs))
            return null;

        // A webcam often has two nodes with one name (video and metadata); the lower number is the video one.
        return Directory.GetDirectories(sysfs, "video*")
            .OrderBy(d => Int32.TryParse(Path.GetFileName(d)[5..], out var n) ? n : Int32.MaxValue)
            .Where(d => String.Equals(ReadName(d), camera.Name, StringComparison.Ordinal))
            .Select(d => "/dev/" + Path.GetFileName(d))
            .FirstOrDefault(File.Exists);
    }

    static string? ReadName(string sysfsNode)
    {
        try
        {
            return File.ReadAllText(Path.Combine(sysfsNode, "name")).Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>struct v4l2_queryctrl.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct QueryCtrl
    {
        public uint Id;
        public uint Type;
        public fixed byte Name[32];
        public int Minimum;
        public int Maximum;
        public int Step;
        public int Default;
        public uint Flags;
        public fixed uint Reserved[2];
    }

    /// <summary>struct v4l2_control.</summary>
    [StructLayout(LayoutKind.Sequential)]
    struct Control
    {
        public uint Id;
        public int Value;
    }

    [DllImport("libc", SetLastError = true)]
    static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc")]
    static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    static extern int ioctl(int fd, nuint request, void* argument);
}
