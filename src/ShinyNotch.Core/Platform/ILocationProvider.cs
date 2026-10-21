namespace ShinyNotch.Platform;

public enum LocationAccess
{
    /// <summary>Not asked yet: the weather asks on first use.</summary>
    NotDetermined,
    Granted,
    Denied,
    /// <summary>The platform can't locate at all; the weather sticks to places added by hand.</summary>
    Unavailable
}

public sealed record LocationFix(double Latitude, double Longitude, string? Name);

/// <summary>Where the device is, roughly (a kilometre will do), for the weather.</summary>
public interface ILocationProvider
{
    LocationAccess Access { get; }

    /// <summary>The answer to <see cref="RequestAccess"/>, or a change made in system settings, on the main thread.</summary>
    event EventHandler? AccessChanged;

    void RequestAccess();

    /// <summary>A fix with a place name if one can be found, or null on failure. Gives up after <paramref name="timeout"/>.</summary>
    Task<LocationFix?> GetCurrentAsync(TimeSpan timeout);
}
