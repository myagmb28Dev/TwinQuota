# TwinQuota

[![CI](https://github.com/myagmb28Dev/TwinQuota/actions/workflows/ci.yml/badge.svg)](https://github.com/myagmb28Dev/TwinQuota/actions/workflows/ci.yml)

TwinQuota is a small Windows tray app for monitoring model quota usage across
Google Antigravity 2.0, Antigravity IDE, and Antigravity CLI.

It reads the localhost language-server API already exposed by a running
Antigravity surface. The CLI is optional: Antigravity 2.0 or Antigravity IDE is
enough for live quota and model discovery.

## Features

- Detects Antigravity 2.0, Antigravity IDE, and Antigravity CLI independently.
- Shows the currently active agent model reported by Antigravity.
- Shows only the weekly and five-hour quota windows associated with that active
  model's provider.
- Uses a compact window instead of listing every account-available model.
- Keeps the latest successful snapshot available while Antigravity is closed.
- Lives in the Windows notification area and refreshes every 30 seconds.
- Optionally shows the window only while an Antigravity 2.0, IDE, or CLI window is open.
- Never reads or persists Google OAuth credentials.

## Requirements

- Windows 10 or later.
- .NET 10 Desktop Runtime for the default framework-dependent package.
- At least one Antigravity product for live data. Antigravity CLI is **not**
  required.

## Build and run

```powershell
.\preview
```

That command closes an older TwinQuota preview, builds the current working tree,
and starts the newly packaged executable without installing it.

For a per-user Windows installation:

```powershell
.\install
```

The install command copies TwinQuota to `%LOCALAPPDATA%\Programs\TwinQuota`,
adds a Start menu shortcut, registers TwinQuota in Windows Installed Apps, and
starts the installed executable. Remove it from Windows Installed Apps or run:

```powershell
.\uninstall
```

The longer development commands are:

```powershell
dotnet restore .\TwinQuota.slnx -p:NuGetAudit=false
dotnet test .\TwinQuota.slnx --no-restore
dotnet run --project .\src\TwinQuota.Windows\TwinQuota.Windows.csproj
```

Start Antigravity 2.0, Antigravity IDE, or Antigravity CLI for a new live
snapshot. The tray menu only provides Exit; window visibility follows the saved
`Follow Antigravity windows` setting. Use an in-app Quit button to exit as well.

## Package

```powershell
.\scripts\publish.ps1
```

The command creates a single-file, framework-dependent Windows executable and a
zip archive under `artifacts\win-x64`.

## How collection works

1. Detect installed products and running Antigravity language servers.
2. Read the server's localhost HTTP port from its own log.
3. Read the short-lived local CSRF value from the running process command line.
4. Call `RetrieveUserQuotaSummary` and `GetAvailableModels` on `127.0.0.1`.
5. Register a namespaced Antigravity `PreInvocation` hook that records only the
   actually invoked `modelName` and observation time.
6. Prefer that invoked model over `defaultAgentModelId`, then discard unrelated
   model and quota groups.
7. Discard the CSRF value and cache only display-safe quota/model data.

No request is sent directly to Google's private backend, and no OAuth token,
cookie, prompt, transcript, workspace path, or source file is persisted. See
[`docs/antigravity-detection.md`](docs/antigravity-detection.md) for the
validation notes and fallback behavior.
