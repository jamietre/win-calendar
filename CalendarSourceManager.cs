namespace WinCalendar;

/// <summary>
/// Manages multiple calendar sources and provides unified access to meetings.
/// </summary>
public class CalendarSourceManager
{
    private readonly List<ICalendarSource> _sources = [];
    private readonly Action<string>? _log;

    /// <summary>
    /// Event raised when calendar data from any source is stale.
    /// </summary>
    public event Action<string>? OnStaleData;

    public CalendarSourceManager(Action<string>? log = null)
    {
        _log = log;
    }

    /// <summary>
    /// Adds a calendar source to the manager.
    /// </summary>
    public void AddSource(ICalendarSource source)
    {
        _sources.Add(source);

        // Wire up staleness events from FileCalendarSource
        if (source is FileCalendarSource fileSource)
        {
            fileSource.OnStaleData += message => OnStaleData?.Invoke(message);
        }
    }

    /// <summary>
    /// Gets all configured sources.
    /// </summary>
    public IReadOnlyList<ICalendarSource> Sources => _sources.AsReadOnly();

    /// <summary>
    /// Gets meetings for a specific date from all sources.
    /// </summary>
    public async Task<List<Meeting>> GetMeetingsForDateAsync(DateTime date)
    {
        return await GetMeetingsAsync(date, date);
    }

    /// <summary>
    /// Gets upcoming meetings within the next 60 minutes from all sources.
    /// </summary>
    public async Task<List<Meeting>> GetUpcomingMeetingsAsync()
    {
        var now = DateTime.Now;
        var meetings = await GetMeetingsAsync(now.Date, now.Date);

        return meetings
            .Where(m => m.MinutesUntilStart > -10 && m.MinutesUntilStart <= 60)
            .OrderBy(m => m.Start)
            .ToList();
    }

    /// <summary>
    /// Gets meetings within a date range from all sources, with deduplication.
    /// </summary>
    public async Task<List<Meeting>> GetMeetingsAsync(DateTime startDate, DateTime endDate)
    {
        var allMeetings = new List<Meeting>();
        var errors = new List<string>();

        foreach (var source in _sources)
        {
            if (!source.IsConfigured)
            {
                _log?.Invoke($"Skipping unconfigured source: {source.Name}");
                continue;
            }

            try
            {
                var meetings = await source.GetMeetingsAsync(startDate, endDate);
                allMeetings.AddRange(meetings);
                _log?.Invoke($"Source {source.Name}: {meetings.Count} meetings");
            }
            catch (Exception ex)
            {
                var error = $"Error from {source.Name}: {ex.Message}";
                errors.Add(error);
                _log?.Invoke($"ERROR: {error}");
            }

            var sourceError = source.GetLastError();
            if (sourceError != null)
            {
                _log?.Invoke($"Source {source.Name} error: {sourceError}");
            }
        }

        // Deduplicate meetings by subject + start time
        var deduplicated = DeduplicateMeetings(allMeetings);

        // Update MinutesUntilStart for all meetings
        var now = DateTime.Now;
        foreach (var meeting in deduplicated)
        {
            meeting.MinutesUntilStart = (meeting.Start - now).TotalMinutes;
        }

        return deduplicated.OrderBy(m => m.Start).ToList();
    }

    /// <summary>
    /// Deduplicates meetings that appear in multiple sources.
    /// Uses subject + start time (rounded to minute) as the deduplication key.
    /// </summary>
    private static List<Meeting> DeduplicateMeetings(List<Meeting> meetings)
    {
        var seen = new Dictionary<string, Meeting>();

        foreach (var meeting in meetings)
        {
            var key = GetDeduplicationKey(meeting);

            if (!seen.ContainsKey(key))
            {
                seen[key] = meeting;
            }
            else
            {
                // If we have a duplicate, prefer the one with more info (e.g., EntryId)
                var existing = seen[key];
                if (string.IsNullOrEmpty(existing.EntryId) && !string.IsNullOrEmpty(meeting.EntryId))
                {
                    seen[key] = meeting;
                }
            }
        }

        return seen.Values.ToList();
    }

    /// <summary>
    /// Generates a deduplication key for a meeting.
    /// </summary>
    private static string GetDeduplicationKey(Meeting meeting)
    {
        // Normalize subject (trim, lowercase) and round start time to minute
        var normalizedSubject = meeting.Subject.Trim().ToLowerInvariant();
        var roundedStart = new DateTime(
            meeting.Start.Year, meeting.Start.Month, meeting.Start.Day,
            meeting.Start.Hour, meeting.Start.Minute, 0);

        return $"{normalizedSubject}_{roundedStart:yyyyMMddHHmm}";
    }
}
