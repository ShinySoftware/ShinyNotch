using Shiny;
using Shiny.Calendar;

namespace ShinyNotch.Linux;

/// <summary>
/// No calendar on Linux yet. Evolution Data Server is the system's calendar, but it has no D-Bus surface
/// worth binding by hand. Reporting <see cref="AccessState.NotSupported"/> takes the calendar out of
/// Settings and has its tab say so; nothing past the access check is ever asked.
/// </summary>
sealed class UnsupportedCalendarStore : ICalendarStore
{
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public AccessState GetCurrentAccess() => AccessState.NotSupported;

    public Task<AccessState> RequestAccess(CalendarAccessType accessType = CalendarAccessType.ReadWrite, CancellationToken ct = default)
        => Task.FromResult(AccessState.NotSupported);

    public Task<IReadOnlyList<Calendar>> GetAll(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Calendar>>([]);

    public Task<Calendar?> GetById(string calendarId, CancellationToken ct = default)
        => Task.FromResult<Calendar?>(null);

    public Task<IReadOnlyList<CalendarEvent>> GetEvents(string? calendarId = null, DateTimeOffset? start = null, DateTimeOffset? end = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CalendarEvent>>([]);

    public Task<CalendarEvent?> GetEvent(string eventId, CancellationToken ct = default)
        => Task.FromResult<CalendarEvent?>(null);

    public CalendarEventQuery Query() => new((_, _) => Task.FromResult(Enumerable.Empty<CalendarEvent>()));

    public Task<string> Create(string name, string? color = null, CancellationToken ct = default) => throw new PlatformNotSupportedException();
    public Task Update(string calendarId, string newName, string? newColor = null, CancellationToken ct = default) => throw new PlatformNotSupportedException();
    public Task Delete(string calendarId, CancellationToken ct = default) => throw new PlatformNotSupportedException();
    public Task<string> CreateEvent(CalendarEvent calendarEvent, CancellationToken ct = default) => throw new PlatformNotSupportedException();
    public Task UpdateEvent(CalendarEvent calendarEvent, CancellationToken ct = default) => throw new PlatformNotSupportedException();
    public Task DeleteEvent(string eventId, bool deleteSeries = false, CancellationToken ct = default) => throw new PlatformNotSupportedException();
}
