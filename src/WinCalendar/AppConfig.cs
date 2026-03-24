using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinCalendar;

public class AppConfig
{
    private static readonly string ConfigFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "win-calendar");
    private static readonly string ConfigFile = Path.Combine(ConfigFolder, "config.json");

    public int FontSizeOffset { get; set; } = 0;

    /// <summary>
    /// List of calendar sources to fetch meetings from.
    /// Defaults to the file-based source if not configured.
    /// </summary>
    public List<CalendarSourceConfig> CalendarSources { get; set; } = new()
    {
        new CalendarSourceConfig { Type = "file", Path = "~/.config/win-calendar/calendar-data.json" }
    };

    private static AppConfig? _instance;
    public static AppConfig Instance => _instance ??= Load();

    public static float BaseFontSize => SystemFonts.DefaultFont.Size;
    public float CurrentFontSize => Math.Max(8, BaseFontSize + FontSizeOffset);

    public Font GetFont(float sizeOffset = 0, FontStyle style = FontStyle.Regular)
    {
        var size = Math.Max(8, CurrentFontSize + sizeOffset);
        return new Font(SystemFonts.DefaultFont.FontFamily, size, style);
    }

    public void IncreaseFontSize()
    {
        FontSizeOffset = Math.Min(FontSizeOffset + 1, 10);
        Save();
    }

    public void DecreaseFontSize()
    {
        FontSizeOffset = Math.Max(FontSizeOffset - 1, -4);
        Save();
    }

    public void ResetFontSize()
    {
        FontSizeOffset = 0;
        Save();
    }

    private static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch { }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(ConfigFolder))
            {
                Directory.CreateDirectory(ConfigFolder);
            }
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch { }
    }
}

/// <summary>
/// Configuration for a calendar data source.
/// </summary>
public class CalendarSourceConfig
{
    /// <summary>
    /// Type of calendar source: "file" or "google".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "file";

    /// <summary>
    /// Whether this calendar source is enabled.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the calendar data file (for "file" type).
    /// Supports ~ for home directory and environment variables.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>
    /// Path to Google OAuth credentials file (for "google" type).
    /// </summary>
    [JsonPropertyName("credentialsPath")]
    public string? CredentialsPath { get; set; }

    /// <summary>
    /// Google Calendar ID to fetch events from (for "google" type).
    /// Defaults to "primary" if not specified.
    /// </summary>
    [JsonPropertyName("calendarId")]
    public string? CalendarId { get; set; }

    /// <summary>
    /// How often to refresh data from this source (in minutes).
    /// The app will use cached data between refreshes.
    /// Defaults: file = 0.5 (30 sec), google = 15 min.
    /// </summary>
    [JsonPropertyName("refreshMinutes")]
    public double? RefreshMinutes { get; set; }

    /// <summary>
    /// Gets the effective refresh interval for this source.
    /// </summary>
    [JsonIgnore]
    public TimeSpan RefreshInterval => TimeSpan.FromMinutes(RefreshMinutes ?? GetDefaultRefreshMinutes());

    private double GetDefaultRefreshMinutes() => Type.ToLowerInvariant() switch
    {
        "file" => 0.5,    // File source: check every 30 seconds
        "google" => 15,   // Google: refresh every 15 minutes
        _ => 5
    };

    /// <summary>
    /// Gets a display name for this calendar source.
    /// </summary>
    [JsonIgnore]
    public string DisplayName => Type.ToLowerInvariant() switch
    {
        "file" => "Outlook (File)",
        "google" => string.IsNullOrEmpty(CalendarId) || CalendarId == "primary"
            ? "Google Calendar"
            : $"Google ({CalendarId})",
        _ => Type
    };
}
