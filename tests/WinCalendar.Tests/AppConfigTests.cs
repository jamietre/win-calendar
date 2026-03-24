namespace WinCalendar.Tests;

public class AppConfigTests
{
    [Theory]
    [InlineData("file", 0.5)]
    [InlineData("google", 15)]
    [InlineData("unknown", 5)]
    public void CalendarSourceConfig_DefaultRefreshMinutes(string type, double expectedMinutes)
    {
        var config = new CalendarSourceConfig { Type = type };
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), config.RefreshInterval);
    }

    [Theory]
    [InlineData("file", "Outlook (File)")]
    [InlineData("google", "Google Calendar")]
    public void CalendarSourceConfig_DisplayName(string type, string expected)
    {
        var config = new CalendarSourceConfig { Type = type };
        Assert.Equal(expected, config.DisplayName);
    }

    [Fact]
    public void CalendarSourceConfig_GoogleWithCalendarId_ShowsIdInDisplayName()
    {
        var config = new CalendarSourceConfig { Type = "google", CalendarId = "work@example.com" };
        Assert.Equal("Google (work@example.com)", config.DisplayName);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 10)]
    [InlineData(10, 10)]  // capped at 10
    public void AppConfig_IncreaseFontSize_CapsAt10(int initial, int expected)
    {
        var config = new AppConfig { FontSizeOffset = initial };
        config.IncreaseFontSize();
        Assert.Equal(expected, config.FontSizeOffset);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(-3, -4)]
    [InlineData(-4, -4)]  // capped at -4
    public void AppConfig_DecreaseFontSize_CapsAtMinus4(int initial, int expected)
    {
        var config = new AppConfig { FontSizeOffset = initial };
        config.DecreaseFontSize();
        Assert.Equal(expected, config.FontSizeOffset);
    }
}
