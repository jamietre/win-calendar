using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace WinCalendar;

/// <summary>
/// Calendar source that fetches meetings from Google Calendar using OAuth2.
/// Caches results to avoid excessive API calls.
/// </summary>
public class GoogleCalendarSource : ICalendarSource
{
    private static readonly string[] Scopes = { CalendarService.Scope.CalendarReadonly };
    private const string ApplicationName = "WinCalendar";

    private readonly string _credentialsPath;
    private readonly string _calendarId;
    private readonly Action<string>? _log;
    private readonly string _tokenPath;
    private readonly TimeSpan _refreshInterval;
    private string? _lastError;
    private CalendarService? _service;

    // Cache
    private List<Meeting>? _cachedMeetings;
    private DateTime _cacheTime = DateTime.MinValue;
    private DateTime _cachedStartDate;
    private DateTime _cachedEndDate;

    public string Name => "Google";
    public bool IsConfigured => File.Exists(_credentialsPath);

    public GoogleCalendarSource(string credentialsPath, string? calendarId = null, Action<string>? log = null, TimeSpan? refreshInterval = null)
    {
        _credentialsPath = ExpandPath(credentialsPath);
        _calendarId = calendarId ?? "primary";
        _log = log;
        _refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(15);

        // Store token in user's config directory
        _tokenPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "win-calendar", "google-token");
    }

    public async Task<List<Meeting>> GetMeetingsAsync(DateTime startDate, DateTime endDate)
    {
        _lastError = null;

        if (!IsConfigured)
        {
            _lastError = "Google credentials file not found.";
            return [];
        }

        // Check cache - return cached results if still valid and covers the requested range
        var now = DateTime.Now;
        if (_refreshInterval > TimeSpan.Zero &&
            _cachedMeetings != null &&
            (now - _cacheTime) < _refreshInterval &&
            startDate.Date >= _cachedStartDate.Date &&
            endDate.Date <= _cachedEndDate.Date)
        {
            // Filter cached meetings to requested date range
            var filtered = _cachedMeetings
                .Where(m => m.Start.Date >= startDate.Date && m.Start.Date <= endDate.Date)
                .ToList();
            _log?.Invoke($"Google Calendar: returning {filtered.Count} cached events (filtered from {_cachedMeetings.Count})");
            return filtered;
        }

        try
        {
            var service = await GetServiceAsync();
            if (service == null)
            {
                return [];
            }

            // Query events - fetch a wider range to improve cache hits
            var fetchStart = startDate.Date;
            var fetchEnd = endDate.Date.AddDays(7); // Fetch a week ahead for better caching

            var request = service.Events.List(_calendarId);
            request.TimeMinDateTimeOffset = new DateTimeOffset(fetchStart);
            request.TimeMaxDateTimeOffset = new DateTimeOffset(fetchEnd.AddDays(1));
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.MaxResults = 100;

            var events = await request.ExecuteAsync();
            var meetings = new List<Meeting>();

            if (events.Items == null)
            {
                return meetings;
            }

            foreach (var eventItem in events.Items)
            {
                // Skip all-day events (they have Date instead of DateTimeDateTimeOffset)
                if (eventItem.Start?.DateTimeDateTimeOffset == null || eventItem.End?.DateTimeDateTimeOffset == null)
                    continue;

                var start = eventItem.Start.DateTimeDateTimeOffset.Value.LocalDateTime;
                var end = eventItem.End.DateTimeDateTimeOffset.Value.LocalDateTime;

                var meeting = new Meeting
                {
                    Subject = eventItem.Summary ?? "(No title)",
                    Start = start,
                    End = end,
                    Location = eventItem.Location ?? eventItem.HangoutLink,
                    Organizer = eventItem.Organizer?.Email,
                    RequiredAttendees = eventItem.Attendees?.Count(a => !a.Optional.GetValueOrDefault()) ?? 0,
                    OptionalAttendees = eventItem.Attendees?.Count(a => a.Optional.GetValueOrDefault()) ?? 0,
                    MinutesUntilStart = (start - now).TotalMinutes,
                    Source = Name
                };

                _log?.Invoke($"  Google event: '{meeting.Subject}' at {meeting.Start:yyyy-MM-dd HH:mm} (ID: {eventItem.Id})");
                meetings.Add(meeting);
            }

            // Update cache with all fetched meetings
            _cachedMeetings = meetings;
            _cacheTime = now;
            _cachedStartDate = fetchStart;
            _cachedEndDate = fetchEnd;

            // Filter to requested date range
            var filtered = meetings
                .Where(m => m.Start.Date >= startDate.Date && m.Start.Date <= endDate.Date)
                .ToList();

            _log?.Invoke($"Google Calendar: fetched {meetings.Count} events, returning {filtered.Count} for requested range (cache updated)");
            return filtered;
        }
        catch (Exception ex)
        {
            _lastError = $"Error fetching Google Calendar events: {ex.Message}";
            _log?.Invoke($"ERROR in GoogleCalendarSource: {ex.Message}");
            return _cachedMeetings ?? []; // Return stale cache on error if available
        }
    }

    public string? GetLastError() => _lastError;

    private async Task<CalendarService?> GetServiceAsync()
    {
        if (_service != null)
        {
            return _service;
        }

        try
        {
            UserCredential credential;

            await using (var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(_tokenPath, true));
            }

            _service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName
            });

            _log?.Invoke("Google Calendar service initialized");
            return _service;
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to authenticate with Google: {ex.Message}";
            _log?.Invoke($"ERROR initializing Google Calendar service: {ex.Message}");
            return null;
        }
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }
        return Environment.ExpandEnvironmentVariables(path);
    }
}
