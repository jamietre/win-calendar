namespace WinCalendar.Tests;

public class CalendarSourceManagerTests
{
    private static Meeting MakeMeeting(string subject, DateTime start, string? entryId = null) => new()
    {
        Subject = subject,
        Start = start,
        End = start.AddHours(1),
        EntryId = entryId
    };

    [Fact]
    public async Task GetMeetingsAsync_SkipsUnconfiguredSources()
    {
        var source = new FakeCalendarSource(isConfigured: false, meetings: [MakeMeeting("Test", DateTime.Today)]);
        var manager = new CalendarSourceManager();
        manager.AddSource(source);

        var results = await manager.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetMeetingsAsync_DeduplicatesBySubjectAndStartTime()
    {
        var start = DateTime.Today.AddHours(9);
        var source1 = new FakeCalendarSource(meetings: [MakeMeeting("Standup", start)]);
        var source2 = new FakeCalendarSource(meetings: [MakeMeeting("Standup", start)]);
        var manager = new CalendarSourceManager();
        manager.AddSource(source1);
        manager.AddSource(source2);

        var results = await manager.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.Single(results);
    }

    [Fact]
    public async Task GetMeetingsAsync_PrefersEntryIdWhenDeduplicating()
    {
        var start = DateTime.Today.AddHours(10);
        var withId = MakeMeeting("Review", start, entryId: "abc123");
        var withoutId = MakeMeeting("Review", start);
        var manager = new CalendarSourceManager();
        manager.AddSource(new FakeCalendarSource(meetings: [withoutId]));
        manager.AddSource(new FakeCalendarSource(meetings: [withId]));

        var results = await manager.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.Single(results);
        Assert.Equal("abc123", results[0].EntryId);
    }

    [Fact]
    public async Task GetMeetingsAsync_ReturnsOrderedByStartTime()
    {
        var later = DateTime.Today.AddHours(14);
        var earlier = DateTime.Today.AddHours(9);
        var source = new FakeCalendarSource(meetings: [MakeMeeting("Late", later), MakeMeeting("Early", earlier)]);
        var manager = new CalendarSourceManager();
        manager.AddSource(source);

        var results = await manager.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.Equal(earlier, results[0].Start);
        Assert.Equal(later, results[1].Start);
    }

    [Fact]
    public async Task GetMeetingsAsync_ContinuesAfterSourceException()
    {
        var good = new FakeCalendarSource(meetings: [MakeMeeting("Good", DateTime.Today.AddHours(9))]);
        var bad = new ThrowingCalendarSource();
        var manager = new CalendarSourceManager();
        manager.AddSource(bad);
        manager.AddSource(good);

        var results = await manager.GetMeetingsAsync(DateTime.Today, DateTime.Today);

        Assert.Single(results);
    }
}

// --- Test doubles ---

file class FakeCalendarSource(bool isConfigured = true, List<Meeting>? meetings = null) : ICalendarSource
{
    public string Name => "Fake";
    public bool IsConfigured => isConfigured;
    public Task<List<Meeting>> GetMeetingsAsync(DateTime start, DateTime end) =>
        Task.FromResult(meetings ?? []);
    public string? GetLastError() => null;
}

file class ThrowingCalendarSource : ICalendarSource
{
    public string Name => "Thrower";
    public bool IsConfigured => true;
    public Task<List<Meeting>> GetMeetingsAsync(DateTime start, DateTime end) =>
        throw new InvalidOperationException("source failed");
    public string? GetLastError() => null;
}
