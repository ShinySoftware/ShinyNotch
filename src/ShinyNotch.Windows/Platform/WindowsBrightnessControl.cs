using System.Management;
using ShinyNotch.Platform;

namespace ShinyNotch.Windows;

/// <summary>
/// The laptop panel's brightness through WMI (root\WMI), the same provider the brightness keys and the
/// quick settings slider drive. Desktop monitors don't expose it, so there it is null and unobserved.
/// </summary>
sealed class WindowsBrightnessControl : IBrightnessControl, IDisposable
{
    const string Scope = @"root\WMI";
    ManagementEventWatcher? watcher;

    public float? Brightness
    {
        get
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(Scope, "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True");
                foreach (var monitor in searcher.Get())
                {
                    using (monitor)
                        return Convert.ToByte(monitor["CurrentBrightness"]) / 100f;
                }
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
    }

    public void SetBrightness(float value)
    {
        var percent = (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 100);
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
            foreach (ManagementObject monitor in searcher.Get())
            {
                using (monitor)
                    monitor.InvokeMethod("WmiSetBrightness", [(uint)1, percent]);
            }
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>WmiMonitorBrightnessEvent fires for the keys, the slider and adaptive brightness alike.</summary>
    public bool Observe(Action onChange)
    {
        if (this.Brightness == null)
            return false;

        try
        {
            this.watcher = new ManagementEventWatcher(new ManagementScope(Scope), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            this.watcher.EventArrived += (_, _) => onChange();
            this.watcher.Start();
            return true;
        }
        catch (ManagementException)
        {
            this.watcher?.Dispose();
            this.watcher = null;
            return false;
        }
    }

    public void Dispose()
    {
        this.watcher?.Stop();
        this.watcher?.Dispose();
    }
}
