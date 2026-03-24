namespace WinCalendar.Tests;

public class FileCalendarSourceTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    private void WriteJson(DateTime exportTime, params (string Subject, DateTime Start, DateTime End, string Location, string Organizer, string EntryId, int Required, int Optional)[] events)
    {
        var data = new CalendarData
        {
            ExportTime = exportTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            Events = events.Select(e => new CalendarEvent
            {
                Subject = e.Subject,
                Start = e.Start.ToString("yyyy-MM-ddTHH:mm:ss"),
                End = e.End.ToString("yyyy-MM-ddTHH:mm:ss"),
                Location = e.Location,
                Organizer = e.Organizer,
                EntryId = e.EntryId,
                RequiredAttendees = e.Required,
                OptionalAttendees = e.Optional
            }).ToList()
        };
        File.WriteAllText(_tempFile, System.Text.Json.JsonSerializer.Serialize(data));
    }

    private void WriteJson(DateTime exportTime) => WriteJson(exportTime, []);

    [Fact]
    public async Task GetMeetingsAsync_ReturnsMeetingsFromFile()
    {
        var today = DateTime.Today;
        WriteJson(DateTime.Now, (Subject: "Standup", Start: today.AddHours(9), End: today.AddHours(9.5), Location: "", Organizer: "", EntryId: "", Required: 0, Optional: 0));

        var source = new FileCalendarSource(_tempFile);
        var results = await source.GetMeetingsAsync(today, today);

        Assert.Single(results);
        Assert.Equal("Standup", results[0].Subject);
    }

    [Fact]
    public async Task GetMeetingsAsync_FiltersByDateRange()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);
        WriteJson(DateTime.Now,
            (Subject: "Today",    Start: today.AddHours(9),    End: today.AddHours(10),    Location: "", Organizer: "", EntryId: "", Required: 0, Optional: 0),
            (Subject: "Tomorrow", Start: tomorrow.AddHours(9), End: tomorrow.AddHours(10), Location: "", Organizer: "", EntryId: "", Required: 0, Optional: 0));

        var source = new FileCalendarSource(_tempFile);
        var results = await source.GetMeetingsAsync(today, today);

        Assert.Single(results);
        Assert.Equal("Today", results[0].Subject);
    }

    [Fact]
    public async Task GetMeetingsAsync_PopulatesAllFields()
    {
        var today = DateTime.Today;
        var start = today.AddHours(14);
        var end = today.AddHours(15);
        WriteJson(DateTime.Now, (Subject: "Team Sync", Start: start, End: end, Location: "https://zoom.us/j/12345", Organizer: "boss@example.com", EntryId: "ENTRY123", Required: 3, Optional: 1));

        var source = new FileCalendarSource(_tempFile);
        var results = await source.GetMeetingsAsync(today, today);

        var m = Assert.Single(results);
        Assert.Equal("Team Sync", m.Subject);
        Assert.Equal(start, m.Start);
        Assert.Equal(end, m.End);
        Assert.Equal("https://zoom.us/j/12345", m.Location);
        Assert.Equal("boss@example.com", m.Organizer);
        Assert.Equal(3, m.RequiredAttendees);
        Assert.Equal(1, m.OptionalAttendees);
        Assert.Equal("ENTRY123", m.EntryId);
        Assert.Equal("File", m.Source);
    }

    [Fact]
    public async Task GetMeetingsAsync_MissingFile_ReturnsEmptyAndSetsError()
    {
        var source = new FileCalendarSource("/nonexistent/path/calendar.json");
        var results = await source.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.Empty(results);
        Assert.NotNull(source.GetLastError());
    }

    [Fact]
    public async Task GetMeetingsAsync_MalformedJson_ReturnsEmptyAndSetsError()
    {
        File.WriteAllText(_tempFile, "{ not valid json }");

        var source = new FileCalendarSource(_tempFile);
        var results = await source.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.Empty(results);
        Assert.NotNull(source.GetLastError());
    }

    [Fact]
    public async Task GetMeetingsAsync_StaleExportTime_RaisesOnStaleData()
    {
        WriteJson(DateTime.Now.AddMinutes(-5));

        var source = new FileCalendarSource(_tempFile);
        string? staleMessage = null;
        source.OnStaleData += msg => staleMessage = msg;

        await source.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.NotNull(staleMessage);
    }

    [Fact]
    public async Task GetMeetingsAsync_FreshExportTime_DoesNotRaiseOnStaleData()
    {
        WriteJson(DateTime.Now);

        var source = new FileCalendarSource(_tempFile);
        string? staleMessage = null;
        source.OnStaleData += msg => staleMessage = msg;

        await source.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.Null(staleMessage);
    }

    [Theory]
    [InlineData("Meeting with \"quotes\"", "Meeting with \"quotes\"")]
    [InlineData("C:\\path\\to\\file", "C:\\path\\to\\file")]
    [InlineData("Tab\there", "Tab\there")]
    public async Task GetMeetingsAsync_HandlesSpecialCharactersInSubject(string subject, string expected)
    {
        var today = DateTime.Today;
        WriteJson(DateTime.Now, (Subject: subject, Start: today.AddHours(9), End: today.AddHours(10), Location: "", Organizer: "", EntryId: "", Required: 0, Optional: 0));

        var source = new FileCalendarSource(_tempFile);
        var results = await source.GetMeetingsAsync(today, today);

        Assert.Single(results);
        Assert.Equal(expected, results[0].Subject);
    }
}
