# WinCalendar

A Windows desktop app for persistent meeting reminders with toast notifications. Supports Outlook (via VBA export) and Google Calendar.

## Features

- System tray icon with context menu
- Toast notifications that stay until dismissed
- Multiple reminder stages (15 min, 5 min, 1 min before, at start, overdue)
- Screen flash for overdue meetings
- Join button for Zoom/Teams/Meet meetings
- Snooze, Remind at start, Dismiss actions
- **Multiple calendar sources:**
  - Outlook VBA export (file-based)
  - Google Calendar (OAuth)
  - Automatic deduplication across sources

## Requirements

- Windows 10 (build 17763+) / Windows 11
- .NET 8.0 Runtime (not required if using standalone build)
- **One of:**
  - Outlook VBA macro (for file-based calendar source)
  - Google Calendar account (for Google Calendar source)
  - Or both!

## Building

```bash
dotnet build
```

## Running

To run the app without blocking your terminal:

```bash
# From Command Prompt/PowerShell
start bin\Debug\net8.0-windows10.0.19041.0\WinCalendar.exe

# From MSYS/Git Bash
./bin/Debug/net8.0-windows10.0.19041.0/WinCalendar.exe &
```

Or simply double-click the `.exe` file to launch it normally.

## Publishing

### Option 1: Standalone executable (no .NET Runtime required)

Creates a fully self-contained single .exe file that includes the .NET runtime:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true
```

Output: `bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/WinCalendar.exe`

This creates a larger file (~70MB) but runs on any Windows machine without needing .NET installed.

### Option 2: Framework-dependent (requires .NET 8.0 Runtime)

Creates a smaller executable that requires .NET 8.0 Runtime to be installed:

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

Output: `bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/WinCalendar.exe`

This creates a smaller file but users need to install the [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) first.

## Configuration

### Config File Location

The app stores all configuration and data in `%USERPROFILE%\.config\win-calendar\` (typically `C:\Users\<username>\.config\win-calendar\`).

A default `config.json` is included in the repository for reference. On first run, the app will create the config file if it doesn't exist.

Files in this directory:
- `config.json` - Application configuration (calendar sources, font settings)
- `calendar-data.json` - Calendar data from Outlook VBA export (if using file source)
- `reminder-state.json` - State tracking for dismissed/snoozed reminders
- `app.log` - Application log file
- `google-token/` - Google OAuth tokens (if using Google Calendar source)

### Example Configuration

```json
{
  "FontSizeOffset": 0,
  "CalendarSources": [
    {
      "type": "file",
      "enabled": true,
      "path": "~/.config/win-calendar/calendar-data.json",
      "refreshMinutes": 0.5
    },
    {
      "type": "google",
      "enabled": true,
      "credentialsPath": "~/.config/win-calendar/google-credentials.json",
      "calendarId": "primary",
      "refreshMinutes": 15
    }
  ]
}
```

### Calendar Source Options

- **type**: `"file"` for Outlook VBA export, `"google"` for Google Calendar
- **enabled**: `true` or `false` to enable/disable this source
- **path**: (file type only) Path to calendar JSON file. Supports `~` for home directory.
- **credentialsPath**: (google type only) Path to OAuth credentials JSON file
- **calendarId**: (google type only) Google Calendar ID, or `"primary"` for default calendar
- **refreshMinutes**: How often to refresh data from this source (optional, defaults: file=0.5, google=15)

### Setting up Google Calendar

To add Google Calendar as a data source:

1. **Create OAuth credentials:**
   - Go to [Google Cloud Console](https://console.cloud.google.com/)
   - Create a new project or select an existing one
   - Enable the Google Calendar API:
     - Navigate to "APIs & Services" > "Library"
     - Search for "Google Calendar API"
     - Click "Enable"
   - Create OAuth credentials:
     - Go to "APIs & Services" > "Credentials"
     - Click "Create Credentials" > "OAuth client ID"
     - If prompted, configure the OAuth consent screen:
       - Choose "External" user type (or "Internal" if you have Google Workspace)
       - Fill in app name (e.g., "Meeting Reminder")
       - Add your email as a test user (required for External apps)
       - Add your email as a developer contact
       - Add scope: `https://www.googleapis.com/auth/calendar.readonly`
       - **Note:** "External" doesn't mean anyone can access your calendar - unverified apps are limited to test users you add. Only you (and up to 99 other test users) can use this app.
     - Select "Desktop app" as application type
     - Give it a name (e.g., "Meeting Reminder Desktop")
     - Click "Create"
   - Download the credentials:
     - Click the download button (⬇) next to your newly created OAuth client
     - Save as `google-credentials.json` in `%USERPROFILE%\.config\win-calendar\`

2. **Add to config:**
   - Edit `%USERPROFILE%\.config\win-calendar\config.json`
   - Add a Google calendar source to the `CalendarSources` array (see example above)

3. **First-time authorization:**
   - Restart the app
   - A browser window will open asking you to sign in to Google
   - Grant the app permission to read your calendar
   - The auth token will be saved to `%USERPROFILE%\.config\win-calendar\google-token\`
   - You only need to do this once; the token will be refreshed automatically

## Installation

### Auto-start with Windows

To make WinCalendar start automatically when you log in:

1. Press `Win+R`, type `shell:startup`, press Enter
2. Create a shortcut to `WinCalendar.exe` in the Startup folder
3. Restart your computer (or just run WinCalendar.exe once manually)

The app runs in the system tray and will now start automatically with Windows.

### 1. Set up the Outlook VBA macro (optional)

1. Open Outlook
2. Press `Alt+F11` to open VBA Editor
3. Insert → Module (creates a new standard module)
4. Copy contents of `CalendarExportModule.bas` into the module
5. Close VBA Editor
6. Press `Alt+F8` → Run `StartCalendarExport`

The macro exports calendar data every 30 seconds to `~/.config/win-calendar/calendar-data.json`.

### 2. Build and install the app

1. Build or download `WinCalendar.exe`
2. Add to Windows startup:
   - Press `Win+R`, type `shell:startup`
   - Create shortcut to `WinCalendar.exe`

## Tray Menu Options

- **Check Now** - Immediately check for upcoming meetings
- **Clear Notifications** - Clear all notification history
- **Reset Reminders** - Reset state for upcoming meetings (re-enables dismissed reminders)
- **Open Data Folder** - Open ~/.outlook-automation folder
- **Show Meetings Today** - Display all meetings for today with Join/Open in Outlook links
- **Show Meetings Tomorrow** - Display all meetings for tomorrow
- **Exit** - Close the application


## Differences from PowerShell Version

- Single `.exe` file, no dependencies
- Native Windows notification APIs (no BurntToast module)
- Runs hidden by default (no console window)
- No module state issues or periodic resets needed
- **Multiple calendar sources** - supports both Outlook VBA export and Google Calendar
- **Google Calendar integration** - native OAuth support, no VBA macro needed
- Separate state file to avoid conflicts during migration
- Configurable via JSON config file
