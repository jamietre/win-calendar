# WinCalendar Project Rules

## Version Management

### Version Location
The version is defined in `TrayApplicationContext.cs` as:
```csharp
private const string Version = "1.1.0";
```

### Version Increment Rules
Follow semantic versioning (MAJOR.MINOR.PATCH):

1. **PATCH** (x.x.X) - Increment for:
   - Bug fixes
   - UI tweaks
   - Performance improvements
   - Documentation updates
   - Any change that doesn't add new features or break compatibility

2. **MINOR** (x.X.0) - Increment for:
   - New features
   - New functionality
   - Non-breaking API changes
   - Significant UI changes

3. **MAJOR** (X.0.0) - Increment for:
   - Breaking changes
   - Major rewrites
   - Incompatible API changes

### Mandatory Version Increment
**IMPORTANT:** After ANY code change (except documentation-only changes), increment at least the PATCH version before committing.

### Process
1. Make your code changes
2. Update the version in `TrayApplicationContext.cs` (line 24)
3. Commit both the code changes and version update together

### Current Version
Current version: **1.1.0**
Last change: Added persistent restart message for calendar source changes (replaced dialog with red label)
Next version should be: **1.1.1**

## Code Style

### General
- Use C# naming conventions
- Prefer explicit types over `var` for clarity
- Use meaningful variable names
- Add comments for complex logic

### UI/UX
- Maintain consistent font sizes using `AppConfig.GetFont()`
- Use double-buffering for panels that update frequently
- Follow existing color scheme for status indicators
- Ensure all dialogs are properly disposed

### Error Handling
- Always log errors via `_reminderService.Log()`
- Use try-catch blocks for external operations (file I/O, process starts)
- Fail gracefully with user-friendly messages

## Testing Checklist

Before committing changes, verify:
- [ ] Version number incremented in `TrayApplicationContext.cs`
- [ ] Code compiles without warnings
- [ ] Tray icon and context menu work
- [ ] Meetings dialog displays correctly
- [ ] Calendar source toggling works
- [ ] Restart functionality works
- [ ] No memory leaks (dispose timers, forms properly)
