using Shiny;
using ShinyNotch.Platform;
using Tmds.DBus.Protocol;

namespace ShinyNotch.Linux;

/// <summary>
/// GeoClue2 on the system bus. It has no "ask" of its own: the desktop's location agent decides when a
/// client starts, so asking is starting a client and seeing whether a fix comes back. Unnamed - GeoClue
/// doesn't reverse-geocode - so the weather keeps its last name for the place.
/// </summary>
[Singleton]
sealed class LinuxLocationProvider : ILocationProvider
{
    const string Service = "org.freedesktop.GeoClue2";
    /// <summary>GeoClue's city level: the weather needs no better, and it may come from Wi-Fi or IP alone.</summary>
    const uint CityAccuracy = 4;

    readonly IDispatcher dispatcher;
    LocationAccess access = LocationAccess.NotDetermined;

    public LinuxLocationProvider(IDispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        _ = this.ProbeAsync();
    }

    public LocationAccess Access => this.access;

    public event EventHandler? AccessChanged;

    public void RequestAccess() => _ = this.GetCurrentAsync(TimeSpan.FromSeconds(30));

    public async Task<LocationFix?> GetCurrentAsync(TimeSpan timeout)
    {
        if (this.access == LocationAccess.Unavailable)
            return null;

        IDisposable? watch = null;
        string? client = null;
        var connection = DBusConnection.System;
        try
        {
            client = await connection.CallMethodAsync(
                DBus.Call(connection, Service, "/org/freedesktop/GeoClue2/Manager", "org.freedesktop.GeoClue2.Manager", "GetClient"),
                static (Message reply, object? _) => reply.GetBodyReader().ReadObjectPathAsString()
            ).ConfigureAwait(false);

            // The desktop id is what the location agent shows and remembers the answer by.
            const string clientInterface = "org.freedesktop.GeoClue2.Client";
            await DBus.Set(connection, Service, client, clientInterface, "DesktopId", w => w.WriteVariantString("net.shiny.notch")).ConfigureAwait(false);
            await DBus.Set(connection, Service, client, clientInterface, "RequestedAccuracyLevel", w => w.WriteVariantUInt32(CityAccuracy)).ConfigureAwait(false);

            var updated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            watch = await connection.WatchSignalAsync(
                Service,
                client,
                clientInterface,
                "LocationUpdated",
                static (Message message, object? _) =>
                {
                    var reader = message.GetBodyReader();
                    reader.ReadObjectPathAsString();
                    return reader.ReadObjectPathAsString();
                },
                (Notification<string> n) =>
                {
                    if (n.HasValue)
                        updated.TrySetResult(n.Value);
                },
                ObserverFlags.None,
                emitOnCapturedContext: false
            ).ConfigureAwait(false);

            await connection.CallMethodAsync(DBus.Call(connection, Service, client, clientInterface, "Start")).ConfigureAwait(false);
            var path = await updated.Task.WaitAsync(timeout).ConfigureAwait(false);

            var properties = await DBus.GetAll(connection, Service, path, "org.freedesktop.GeoClue2.Location").ConfigureAwait(false);
            this.SetAccess(LocationAccess.Granted);
            return new LocationFix(properties["Latitude"].Unwrap().GetDouble(), properties["Longitude"].Unwrap().GetDouble(), null);
        }
        catch (DBusErrorReplyException e) when (e.ErrorName.Contains("AccessDenied", StringComparison.Ordinal))
        {
            // Location services off, or the agent said no.
            this.SetAccess(LocationAccess.Denied);
            return null;
        }
        catch
        {
            // A timeout, or GeoClue with nothing to go on: no fix this time, nothing decided.
            return null;
        }
        finally
        {
            watch?.Dispose();
            if (client != null)
            {
                try
                {
                    await connection.CallMethodAsync(DBus.Call(connection, Service, client, "org.freedesktop.GeoClue2.Client", "Stop")).ConfigureAwait(false);
                }
                catch
                {
                    // Gone with the client.
                }
            }
        }
    }

    /// <summary>GeoClue is D-Bus activated, so it is there if it's activatable, running or not.</summary>
    async Task ProbeAsync()
    {
        try
        {
            var connection = DBusConnection.System;
            var names = await connection.CallMethodAsync(
                DBus.Call(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "ListActivatableNames"),
                static (Message reply, object? _) => reply.GetBodyReader().ReadArrayOfString()
            ).ConfigureAwait(false);
            if (!names.Contains(Service))
                this.SetAccess(LocationAccess.Unavailable);
        }
        catch
        {
            // No system bus at all (a container): nothing can locate.
            this.SetAccess(LocationAccess.Unavailable);
        }
    }

    void SetAccess(LocationAccess value)
    {
        if (value == this.access)
            return;

        this.dispatcher.Dispatch(() =>
        {
            this.access = value;
            this.AccessChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}
