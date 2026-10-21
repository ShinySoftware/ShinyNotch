using Shiny.Maui.Controls.Camera;

namespace ShinyNotch.Platform;

/// <summary>A camera's own brightness setting, in the camera's units.</summary>
public sealed record CameraBrightness(double Minimum, double Maximum, double Default, double Value);

/// <summary>
/// Settings kept by the camera itself - on a USB (UVC) webcam, on the device - so every app using the
/// camera (Teams, a browser) sees a change, not just the notch. Optional: a head registers one where it
/// can reach a camera's controls. A camera built into the computer, or a virtual one, has none an app can
/// set, and the mirror then only brightens its own picture.
/// </summary>
public interface ICameraControls
{
    /// <summary>The camera's brightness, or null when it has none an app can change.</summary>
    CameraBrightness? GetBrightness(CameraInfo camera);

    /// <summary>Sets the camera's brightness, between its minimum and maximum. False when it couldn't.</summary>
    bool SetBrightness(CameraInfo camera, double value);
}
