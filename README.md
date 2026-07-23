# FocusGuard

FocusGuard is a local Windows focus app. Select the desktop apps you need,
choose a timer, and start focus mode. Other visible third-party desktop apps
are closed while the timer is active. Windows, drivers, services, Explorer,
and browser processes are left alone.

## What You Need

- Windows 10 or 11.
- Visual Studio 2022 version 17.12 or newer with the **.NET desktop
  development** workload, or the .NET 9 SDK for terminal builds.
- The browser extension only when you want website blocking.

## Use It

1. Open FocusGuard. The first screen is **Focus**.
2. Choose **Find running apps** to add visible user apps such as Anki,
   Obsidian, Spotify, or Discord. Windows components and background services
   are automatically excluded.
3. Tick the apps you want to keep available. Use **Remove** to clear anything
   you do not want in the list.
4. Set a duration and choose **Start focus**.
5. Choose **End** at any time to stop focus mode.

The **Websites** screen is optional. Add and tick the websites you want to use
during focus mode before you start a session.

## Browser Blocking

Yes. The extension is required for website blocking in Chrome, Edge, Brave,
or other Chromium browsers. The desktop app alone controls desktop apps, but
does not inspect or block individual browser tabs.

To install the extension:

1. Keep FocusGuard running.
2. Open your browser's extensions page.
3. Enable Developer Mode.
4. Choose **Load unpacked**.
5. Select `E:\Other\Coding\Whitelist\src\FocusGuard.BrowserExtension`.

That folder contains `manifest.json`, which is the file the browser needs when
you use **Load unpacked**.

The extension blocks websites during an active focus session except for the
websites ticked in FocusGuard. Browser processes are intentionally not closed,
so the extension can do its job.

## Visual Studio 2022

Use the regular solution file, not the `.slnx` file:

1. Open Visual Studio 2022.
2. Choose **Open a project or solution**.
3. Open [FocusGuard.sln](E:\Other\Coding\Whitelist\FocusGuard.sln).
4. In Solution Explorer, right-click `FocusGuard.UI` and choose **Set as Startup Project**.
5. Press `F5` to debug, or `Ctrl+F5` to run.

Visual Studio 2022 is sufficient. You do not need Visual Studio 2026 for this
project.

## Build From Terminal

```powershell
dotnet build .\FocusGuard.sln
dotnet test .\FocusGuard.sln
dotnet run --project .\src\FocusGuard.UI\FocusGuard.UI.csproj
```

## Build a Standalone EXE

```powershell
dotnet publish .\src\FocusGuard.UI\FocusGuard.UI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\publish\FocusGuard-win-x64
```

Run the published program here:

```powershell
.\artifacts\publish\FocusGuard-win-x64\FocusGuard.UI.exe
```

## Local Data

FocusGuard stores its local database at:

```text
%LOCALAPPDATA%\FocusGuard\focusguard.db
```

It has no login, cloud sync, or account setup.
