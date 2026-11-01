# Whitelist Lock In

Whitelist Lock In is a Windows 10/11 focus utility for choosing desktop apps and websites to keep available during a timed focus session. It is local-first: configuration and session state are stored in SQLite on the PC.

## Use

1. Open **Focus** and choose **Find running apps** or **Browse .exe**.
2. Search the app list and tick the desktop apps you want to keep available.
3. Choose a session duration and select **Start focus**.
4. Optionally open **Websites**, add sites you need, and tick them. During a session, the browser extension allows those domains and blocks other web page navigations.
5. Closing the window hides the app in the notification area and keeps the session running. The tray menu shows the live session status; Exit is disabled until the active session has ended.

Choose **Settings → Appearance** to select **System**, **Light**, or **Dark**. The preference is saved locally; System follows the Windows app theme. In Settings, enable **Prevent ending sessions early** before starting a session to disable the app's early-end action for its duration. This option can only be changed between sessions. The main title bar remains native so window snapping, resizing, and system controls keep their normal behavior, with its caption color matching the selected theme.

Use **Saved setup** on the Focus screen to save the current app and website selections under a name. Choose that setup later to restore its selections. Starting a session from a saved setup records which setup was used.

App discovery considers visible, titled desktop windows and excludes Windows components, background processes without a normal window, hardware utilities, and Whitelist Lock In itself. Browse can add a specific `.exe`. Duplicate paths are matched case-insensitively.

## Enforcement limits

Windows does not provide a safe per-user API for an ordinary desktop app to guarantee a locked application whitelist. This implementation is best-effort user-mode enforcement: it checks visible top-level processes every three seconds and closes recognized, non-protected apps outside the selected list. It does not prevent a process from starting between checks, does not enforce on hidden/background processes, and a user with control of the PC can stop the app. Windows system locations, known critical processes, and Whitelist Lock In itself are protected from termination. Browsers are handled like other desktop apps: include the browser in the whitelist if it should stay open and its website rules should apply. Do not treat this as a security boundary or parental-control tool.

The app stores an active session end time and allowed apps/sites in SQLite, resumes the session when the app next starts, and keeps running when its main window is closed. If the process is forcibly stopped, desktop enforcement is suspended until it is relaunched. Session expiry is still recorded from the saved end time. While the desktop is running, an ended session clears browser rules on the next extension sync (normally within 30 seconds). If the desktop is stopped unexpectedly, the extension retains its last rules until the original session expiry.

## Website extension

The included Manifest V3 extension uses Chrome's dynamic `declarativeNetRequest` rules. It applies rules to top-level HTTP/HTTPS page navigations, allows the selected domain (and subdomains when enabled), and redirects disallowed pages to the bundled focus page. The toolbar popup reports desktop connection state, active session, time remaining, and allowed-site count. The blocked page and popup follow the browser's system light/dark theme. It does not inspect browsing history or collect page contents. Dynamic rules survive browser restarts; when the desktop is unreachable, the last valid rules remain until the saved session end. [Chrome documents that dynamic DNR rules persist across browser sessions.](https://developer.chrome.com/docs/extensions/reference/api/declarativeNetRequest)

Installed releases use a Chrome Native Messaging host to read the current session from the desktop database. The per-user installer registers the host for Chrome and Edge under `HKCU`; it only accepts messages from the pinned extension ID. The desktop app reports connection activity through a local heartbeat file. Unpacked development builds also retain the loopback endpoint on `127.0.0.1:47837` as a fallback.

To install the unpacked extension:

1. Open **Websites** and select **Open extension folder**.
2. In Chrome open `chrome://extensions`; in Edge open `edge://extensions`.
3. Turn on **Developer mode**, choose **Load unpacked**, and select the opened `BrowserExtension` folder.
4. Run the Whitelist Lock In installer so it registers the Native Messaging host, then reload the unpacked extension on `chrome://extensions` or `edge://extensions`.
5. Open the extension's toolbar popup and choose **Connect to desktop**. Return to Whitelist Lock In; its indicator changes to **Browser connected** after a successful sync.

For a portable release build without the installer, run `WhitelistLockIn.exe --register-native-host` once, then reload the extension. Re-run that command after moving the application folder because the registered host path is absolute.

The folder is included beside the executable in the release. A store listing is not part of this repository, so the installer cannot silently install a consumer browser extension. Browser policy based force installation is an administrator-managed enterprise mechanism that requires a published/hosted extension and policy configuration; the supported user flow here is the explicit unpacked install. See [Chrome distribution options](https://developer.chrome.com/docs/extensions/how-to/distribute) and [Microsoft Edge extension management](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-manage-extensions-ref-guide).

Website blocking covers top-level navigation in Chromium browsers where the extension is installed and enabled. It cannot block other browsers, non-HTTP(S) browser pages, or traffic from other desktop applications. Each saved setup contains one website allowlist alongside its app selection; separate website-only lists, blocklist mode, page-level exceptions, and schedules are not implemented.

## Architecture

- `FocusGuard.Core` — domain models and domain matching.
- `FocusGuard.Infrastructure` — SQLite persistence and Windows app discovery.
- `FocusGuard.Enforcement` — visible process enforcement and loopback browser session API.
- `FocusGuard.NativeHost` — Native Messaging protocol host for installed Chrome/Edge integration.
- `FocusGuard.UI` — WPF desktop interface, session countdown, and tray lifecycle.
- `FocusGuard.BrowserExtension` — Manifest V3 network rules and blocked page.
- `tests/FocusGuard.Tests` — domain and discovery checks, legacy database migration, duplicate app paths, saved profile edits, active-session recovery, and loopback API tests.
- `tests/extension-tests.mjs` — rule generation, desktop disconnect, rule expiry, and session-clear tests for the extension worker.

The historical `%LOCALAPPDATA%\FocusGuard\focusguard.db` path is intentionally retained to preserve existing local data.

## Build, test, and run

Prerequisites: .NET 9 SDK (the repository targets .NET 9), Node.js 18 or newer for extension tests, Windows 10/11 x64 for the WPF app, and Inno Setup 6 to produce the installer.

```powershell
dotnet restore .\FocusGuard.sln
dotnet build .\FocusGuard.sln
dotnet test .\FocusGuard.sln
node --test .\tests\extension-tests.mjs
dotnet run --project .\src\FocusGuard.UI\FocusGuard.UI.csproj
```

To publish the self-contained executable and compile the installer:

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

Outputs:

- `artifacts\publish\win-x64\WhitelistLockIn.exe` — single-file, self-contained x64 executable.
- `artifacts\publish\win-x64\BrowserExtension\` — unpacked browser extension.
- `artifacts\installer\WhitelistLockIn-Setup-1.0.0-x64.exe` — per-user installer.

The installer creates Start Menu and optional desktop shortcuts. Uninstall removes the installed application and shortcuts; it intentionally preserves `%LOCALAPPDATA%\FocusGuard\focusguard.db`. The release is unsigned; clean-machine SmartScreen behavior, installation, and uninstall should be verified before public distribution.
