using System.Text.Json;

namespace WinCalendar;

/// <summary>
/// Calendar source that reads meetings from a local JSON file.
/// </summary>
public class FileCalendarSource : ICalendarSource
{
    private readonly string _filePath;
    private readonly Action<string>? _log;
    private readonly TimeSpan _refreshInterval;
    private string? _lastError;
    private DateTime _lastStalenessWarning = DateTime.MinValue;

    // Cache
    private List<Meeting>? _cachedMeetings;
    private DateTime _cacheTime = DateTime.MinValue;

    public string Name => "File";
    public bool IsConfigured => !string.IsNullOrEmpty(_filePath);

    /// <summary>
    /// Event raised when calendar data is stale (>2 minutes old).
    /// </summary>
    public event Action<string>? OnStaleData;

    public FileCalendarSource(string filePath, Action<string>? log = null, TimeSpan? refreshInterval = null)
    {
        _filePath = ExpandPath(filePath);
        _log = log;
        _refreshInterval = refreshInterval ?? TimeSpan.Zero; // No caching by default for local files
    }

    public Task<List<Meeting>> GetMeetingsAsync(DateTime startDate, DateTime endDate)
    {
        _lastError = null;

        if (!File.Exists(_filePath))
        {
            _lastError = "Calendar data file not found.";
            return Task.FromResult(new List<Meeting>());
        }

        // Check cache if enabled
        var now = DateTime.Now;
        if (_refreshInterval > TimeSpan.Zero &&
            _cachedMeetings != null &&
            (now - _cacheTime) < _refreshInterval)
        {
            // Return cached meetings filtered to requested date range
            var cached = _cachedMeetings
                .Where(m => m.Start.Date >= startDate.Date && m.Start.Date <= endDate.Date)
                .ToList();
            return Task.FromResult(cached);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<CalendarData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (data?.ExportTime != null)
            {
                var exportTime = DateTime.Parse(data.ExportTime);
                var age = DateTime.Now - exportTime;
                if (age.TotalMinutes > 2)
                {
                    // Only raise staleness warning every 10 minutes
                    if ((DateTime.Now - _lastStalenessWarning).TotalMinutes >= 10)
                    {
                        _lastStalenessWarning = DateTime.Now;
                        var message = $"Calendar data is {(int)age.TotalMinutes} minutes old. Is Outlook running?";
                        OnStaleData?.Invoke(message);
                    }
                }
            }

            var allMeetings = data?.Events?
                .Select(e => new Meeting
                {
                    Subject = e.Subject ?? "",
                    Start = DateTime.Parse(e.Start ?? now.ToString()),
                    End = DateTime.Parse(e.End ?? now.ToString()),
                    Location = e.Location,
                    EntryId = e.EntryId,
                    Organizer = e.Organizer,
                    RequiredAttendees = e.RequiredAttendees,
                    OptionalAttendees = e.OptionalAttendees,
                    MinutesUntilStart = (DateTime.Parse(e.Start ?? now.ToString()) - now).TotalMinutes,
                    Source = Name
                })
                .ToList() ?? [];

            // Update cache
            if (_refreshInterval > TimeSpan.Zero)
            {
                _cachedMeetings = allMeetings;
                _cacheTime = now;
            }

            // Filter to requested date range
            var meetings = allMeetings
                .Where(m => m.Start.Date >= startDate.Date && m.Start.Date <= endDate.Date)
                .ToList();

            return Task.FromResult(meetings);
        }
        catch (Exception ex)
        {
            _lastError = $"Error reading calendar data: {ex.Message}";
            _log?.Invoke($"ERROR in FileCalendarSource: {ex.Message}");
            return Task.FromResult(new List<Meeting>());
        }
    }

    public string? GetLastError() => _lastError;

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        }
        return Environment.ExpandEnvironmentVariables(path);
    }
}
