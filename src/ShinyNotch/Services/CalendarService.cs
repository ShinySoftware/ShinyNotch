using Shiny;
using Shiny.Calendar;

namespace ShinyNotch.Services;

public sealed record CalendarItem(string Title, DateTime Start, DateTime End, bool AllDay, Color Color, string? Location);

/// <summary>Upcoming events from every calendar the user has, through Shiny.Calendar.</summary>
public sealed class CalendarService
{
    static readonly Color DefaultColor = Color.FromArgb("#22D3EE");

    readonly ICalendarStore store;
    readonly IDispatcher dispatcher;
    readonly TimeProvider time;
    int refreshVersion;

    public bool IsAuthorized => this.store.GetCurrentAccess() == AccessState.Available;

    public bool IsDenied => this.store.GetCurrentAccess() is AccessState.Denied or AccessState.Restricted;

    IReadOnlyList<CalendarItem> fetched = [];

    /// <summary>
    /// What's left of the last query: events drop off here as they end, so the store only needs asking
    /// again when it changes, or now and then for the day moving on.
    /// </summary>
    public IReadOnlyList<CalendarItem> Upcoming
    {
        get
        {
            var now = this.time.GetLocalNow().DateTime;
            return this.fetched.Where(e => e.End > now).Take(8).ToList();
        }
    }

    public event EventHandler? Changed;

    public CalendarService(ICalendarStore store, IDispatcher dispatcher, TimeProvider time)
    {
        this.store = store;
        this.dispatcher = dispatcher;
        this.time = time;

        // Edits made in Calendar.app (or synced in) arrive here, possibly off the main thread.
        this.store.Changed += (_, _) => this.dispatcher.Dispatch(() => _ = this.RefreshAsync());
    }

    public async Task<bool> RequestAccessAsync()
    {
        if (this.IsAuthorized)
            return true;

        try
        {
            var granted = await this.store.RequestAccess(CalendarAccessType.ReadOnly).ConfigureAwait(true) == AccessState.Available;
            if (granted)
                await this.RefreshAsync().ConfigureAwait(true);
            return granted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The rest of today and all of tomorrow.</summary>
    public async Task RefreshAsync()
    {
        if (!this.IsAuthorized)
            return;

        // A newer refresh can start while this one awaits; only the latest gets to publish.
        var version = ++this.refreshVersion;
        var now = this.time.GetLocalNow().DateTime;

        IReadOnlyList<Shiny.Calendar.Calendar> calendars;
        IReadOnlyList<CalendarEvent> events;
        try
        {
            calendars = await this.store.GetAll().ConfigureAwait(true);
            events = await this.store
                .Query()
                .Between(now, now.Date.AddDays(2))
                .ToListAsync()
                .ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (version != this.refreshVersion)
            return;

        var colors = calendars
            .Where(c => c.Id is not null)
            .ToDictionary(c => c.Id!, c => ToColor(c.Color));

        this.fetched = events
            .Select(e => new CalendarItem(
                String.IsNullOrWhiteSpace(e.Title) ? "(No title)" : e.Title,
                e.Start.LocalDateTime,
                e.End.LocalDateTime,
                e.IsAllDay,
                e.CalendarId is not null && colors.TryGetValue(e.CalendarId, out var color) ? color : DefaultColor,
                String.IsNullOrWhiteSpace(e.Location) ? null : e.Location
            ))
            .Where(e => e.End > now)
            .OrderBy(e => e.AllDay ? 0 : 1)
            .ThenBy(e => e.Start)
            .Take(24)
            .ToList();

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The next timed event starting within <paramref name="window"/>, for the peek.</summary>
    public CalendarItem? StartingWithin(TimeSpan window)
    {
        var now = this.time.GetLocalNow().DateTime;
        return this.fetched.FirstOrDefault(e => !e.AllDay && e.Start > now && e.Start - now <= window);
    }

    static Color ToColor(string? hex)
        => !String.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var color) ? color : DefaultColor;
}
