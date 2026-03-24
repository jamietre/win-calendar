namespace WinCalendar.Tests;

public class ReminderServiceTests
{
    [Theory]
    [InlineData("https://zoom.us/j/123456", "https://zoom.us/j/123456")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/abc", "https://teams.microsoft.com/l/meetup-join/abc")]
    [InlineData("https://meet.google.com/abc-defg-hij", "https://meet.google.com/abc-defg-hij")]
    [InlineData("Conference Room B", null)]
    [InlineData(null, null)]
    [InlineData("Join at https://zoom.us/j/999 or call in", "https://zoom.us/j/999")]
    public void GetMeetingUrl_ExtractsUrlFromLocation(string? location, string? expected)
    {
        var result = ReminderService.GetMeetingUrlPublic(location);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetMeetingKey_CombinesSubjectAndStart()
    {
        var meeting = new Meeting { Subject = "Standup", Start = new DateTime(2026, 3, 23, 9, 30, 0) };
        var key = ReminderService.GetMeetingKey(meeting);
        Assert.Equal("Standup_202603230930", key);
    }
}
