namespace WinCalendar;

/// <summary>
/// Interface for calendar data sources that provide meeting information.
/// </summary>
public interface ICalendarSource
{
    /// <summary>
    /// Display name for this calendar source.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether the source is properly configured and ready to use.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Retrieves meetings within the specified date range.
    /// </summary>
    /// <param name="startDate">Start of the date range (inclusive).</param>
    /// <param name="endDate">End of the date range (inclusive).</param>
    /// <returns>List of meetings in the date range.</returns>
    Task<List<Meeting>> GetMeetingsAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// Returns the last error message if the source encountered an error, null otherwise.
    /// </summary>
    string? GetLastError();
}
