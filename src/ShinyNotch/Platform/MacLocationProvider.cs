using CoreLocation;
using Shiny;
using ShinyNotch.Platform;

namespace ShinyNotch.MacOS;

/// <summary>CoreLocation, to a kilometre, named with the reverse geocoder.</summary>
[Singleton]
sealed class MacLocationProvider : ILocationProvider
{
    readonly CLLocationManager location = new();
    TaskCompletionSource<CLLocation?>? pendingFix;

    public MacLocationProvider()
    {
        this.location.DesiredAccuracy = CLLocation.AccuracyKilometer;
        this.location.LocationsUpdated += (_, e) =>
        {
            this.location.StopUpdatingLocation();
            this.pendingFix?.TrySetResult(e.Locations.LastOrDefault());
        };
        this.location.Failed += (_, _) => this.pendingFix?.TrySetResult(null);
        this.location.AuthorizationChanged += (_, _) => this.AccessChanged?.Invoke(this, EventArgs.Empty);
    }

    public LocationAccess Access => CLLocationManager.Status switch
    {
        CLAuthorizationStatus.Authorized or CLAuthorizationStatus.AuthorizedAlways => LocationAccess.Granted,
        CLAuthorizationStatus.Denied or CLAuthorizationStatus.Restricted => LocationAccess.Denied,
        _ => LocationAccess.NotDetermined
    };

    public event EventHandler? AccessChanged;

    public void RequestAccess() => this.location.RequestWhenInUseAuthorization();

    public async Task<LocationFix?> GetCurrentAsync(TimeSpan timeout)
    {
        this.pendingFix = new TaskCompletionSource<CLLocation?>();
        this.location.StartUpdatingLocation();
        var fix = await this.pendingFix.Task.WaitAsync(timeout).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null).ConfigureAwait(true);
        this.location.StopUpdatingLocation();
        if (fix == null)
            return null;

        string? name = null;
        try
        {
            var marks = await new CLGeocoder().ReverseGeocodeLocationAsync(fix).ConfigureAwait(true);
            if (marks?.FirstOrDefault() is { } mark)
                name = mark.Locality ?? mark.SubAdministrativeArea ?? mark.AdministrativeArea;
        }
        catch
        {
            // Unnamed: the weather keeps the last name it had.
        }

        return new LocationFix(fix.Coordinate.Latitude, fix.Coordinate.Longitude, name);
    }
}
