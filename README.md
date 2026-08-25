# TwinQuota

TwinQuota is a small Windows tray app for monitoring model quota usage across
Google Antigravity 2.0, Antigravity IDE, and Antigravity CLI.

It reads the localhost language-server API already exposed by a running
Antigravity surface. The CLI is optional: Antigravity 2.0 or Antigravity IDE is
enough for live quota and model discovery.

## Features

- Detects Antigravity 2.0, Antigravity IDE, and Antigravity CLI independently.
- Shows live weekly and five-hour quota windows.
- Discovers the account's currently available agent models instead of shipping
  a stale hard-coded list.
- Includes Gemini, Claude, and GPT-OSS models when the signed-in Antigravity
  account exposes them.
- Keeps the latest successful snapshot available while Antigravity is closed.
- Lives in the Windows notification area and refreshes every minute.
- Never reads or persists Google OAuth credentials.

## Requirements

- Windows 10 or later.
- .NET 10 Desktop Runtime for the default framework-dependent package.
- At least one Antigravity product for live data. Antigravity CLI is **not**
  required.

## Build and run

```powershell
dotnet restore .\TwinQuota.slnx -p:NuGetAudit=false
dotnet test .\TwinQuota.slnx --no-restore
dotnet run --project .\src\TwinQuota.Windows\TwinQuota.Windows.csproj
```

Start Antigravity 2.0, Antigravity IDE, or Antigravity CLI before refreshing if
you want a new live snapshot. Closing the TwinQuota window keeps it in the tray;
use the tray menu to exit.

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
5. Discard the CSRF value and cache only display-safe quota/model data.

No request is sent directly to Google's private backend, and no OAuth token,
cookie, prompt, conversation, or source file is read. See
[`docs/antigravity-detection.md`](docs/antigravity-detection.md) for the
validation notes and fallback behavior.
