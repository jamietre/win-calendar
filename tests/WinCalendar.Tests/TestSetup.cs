using System.Runtime.CompilerServices;

namespace WinCalendar.Tests;

/// <summary>
/// Redirects AppConfig's config.json to an isolated temp directory for the
/// entire test run, so tests never read or write the real user config at
/// %UserProfile%\.config\win-calendar\config.json.
///
/// This runs as a module initializer, guaranteed to execute before any test
/// (or any other code in this assembly) runs, so it's set before AppConfig's
/// ConfigFolder property is ever evaluated.
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    public static void Init()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "win-calendar-tests-" + Guid.NewGuid());
        Environment.SetEnvironmentVariable("WINCALENDAR_CONFIG_DIR", tempDir);
    }
}
