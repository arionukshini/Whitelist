# FocusGuard

FocusGuard is a local-only Windows allowlist productivity app built with C#,
.NET 10, WPF, MVVM, SQLite, and EF Core.

The MVP lets you:

- Add allowed desktop applications from running processes or a selected `.exe`.
- Add allowed website domains with safe hostname normalization.
- Create reusable focus profiles.
- Start and restore timer-based focus sessions using an absolute end timestamp.
- Persist data locally in `%LOCALAPPDATA%\FocusGuard\focusguard.db`.
- Run conservative in-process application enforcement while a session is active.
- Load the Chromium extension in `src/FocusGuard.BrowserExtension` to block
  non-allowed websites through the local desktop session endpoint.

Build and test:

```powershell
dotnet build .\FocusGuard.slnx
dotnet test .\FocusGuard.slnx
```
