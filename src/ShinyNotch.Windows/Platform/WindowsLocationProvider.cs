using Shiny;
using ShinyNotch.Platform;
using global::Windows.Devices.Geolocation;

namespace ShinyNotch.Windows;

/// <summary>
/// Windows location services. There's no reverse geocoder without a Maps key, so a fix comes unnamed and
/// the weather keeps the name it had ("My Location" until then).
/// </summary>
[Singleton]
sealed class WindowsLocationProvider : ILocationProvider
{
    // Windows doesn't say before it's asked; asking doesn't prompt a desktop app, it just reports the
    // privacy setting, so the first refresh asks and the answer arrives through AccessChanged.
    LocationAccess access = LocationAccess.NotDetermined;

    public LocationAccess Access => this.access;

    public event EventHandler? AccessChanged;

    public async void RequestAccess()
    {
        LocationAccess next;
        try
        {
            next = await Geolocator.RequestAccessAsync() switch
            {
                GeolocationAccessStatus.Allowed => LocationAccess.Granted,
                GeolocationAccessStatus.Denied => LocationAccess.Denied,
                _ => LocationAccess.NotDetermined
            };
        }
        catch (Exception)
        {
            // No location platform at all (a Windows N edition, a locked-down machine).
            next = LocationAccess.Unavailable;
        }

        if (next == this.access)
            return;
        this.access = next;
        MainThread.BeginInvokeOnMainThread(() => this.AccessChanged?.Invoke(this, EventArgs.Empty));
    }

    public async Task<LocationFix?> GetCurrentAsync(TimeSpan timeout)
    {
        try
        {
            var locator = new Geolocator { DesiredAccuracyInMeters = 1000 };
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(10), timeout);
            var point = position.Coordinate.Point.Position;
            return new LocationFix(point.Latitude, point.Longitude, null);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
