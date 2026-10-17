using EventKit;
using Foundation;

namespace ShinyNotch.Services;

public sealed record CalendarItem(string Title, DateTime Start, DateTime End, bool AllDay, Color Color, string? Location);

/// <summary>Upcoming events from every calendar the user has, through EventKit.</summary>
public sealed class CalendarService
{
    readonly EKEventStore store = new();

    // Full access shares Authorized's raw value (3); this binding only names the old one.
    public bool IsAuthorized => EKEventStore.GetAuthorizationStatus(EKEntityType.Event) == EKAuthorizationStatus.Authorized;

    public bool IsDenied => EKEventStore.GetAuthorizationStatus(EKEntityType.Event) is EKAuthorizationStatus.Denied or EKAuthorizationStatus.Restricted;

    public IReadOnlyList<CalendarItem> Upcoming { get; private set; } = [];

    public event EventHandler? Changed;

    public CalendarService()
    {
        // Edits made in Calendar.app (or synced in) arrive as this notification.
        NSNotificationCenter.DefaultCenter.AddObserver(EKEventStore.ChangedNotification, _ => this.Refresh(), this.store);
    }

    public async Task<bool> RequestAccessAsync()
    {
        if (this.IsAuthorized)
            return true;

        try
        {
            var (granted, _) = await this.store.RequestFullAccessToEventsAsync().ConfigureAwait(true);
            if (granted)
                this.Refresh();
            return granted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The rest of today and all of tomorrow.</summary>
    public void Refresh()
    {
        if (!this.IsAuthorized)
            return;

        var now = DateTime.Now;
        var end = now.Date.AddDays(2);
        var predicate = this.store.PredicateForEvents((NSDate)now.ToUniversalTime(), (NSDate)end.ToUniversalTime(), null);
        var events = this.store.EventsMatching(predicate) ?? [];

        this.Upcoming = events
            .Select(e => new CalendarItem(
                e.Title ?? "(No title)",
                ((DateTime)e.StartDate).ToLocalTime(),
                ((DateTime)e.EndDate).ToLocalTime(),
                e.AllDay,
                ToColor(e.Calendar?.Color),
                String.IsNullOrWhiteSpace(e.Location) ? null : e.Location
            ))
            .Where(e => e.End > now)
            .OrderBy(e => e.AllDay ? 0 : 1)
            .ThenBy(e => e.Start)
            .Take(8)
            .ToList();

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The next timed event starting within <paramref name="window"/>, for the peek.</summary>
    public CalendarItem? StartingWithin(TimeSpan window)
    {
        var now = DateTime.Now;
        return this.Upcoming.FirstOrDefault(e => !e.AllDay && e.Start > now && e.Start - now <= window);
    }

    static Color ToColor(AppKit.NSColor? color)
    {
        if (color?.UsingColorSpace(AppKit.NSColorSpace.DeviceRGB) is not { } rgb)
            return Color.FromArgb("#22D3EE");

        rgb.GetRgba(out var r, out var g, out var b, out var a);
        return new Color((float)r, (float)g, (float)b, (float)a);
    }
}
