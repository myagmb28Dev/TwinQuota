# Antigravity detection and quota research

Validated on Windows on 2026-08-25 against Antigravity 2.0 v2.10.0.

## Product detection

| Surface | Installation signal | Live signal | CLI required? |
| --- | --- | --- | --- |
| Antigravity 2.0 | Per-user application path and uninstall registration | `language_server.exe` with `app_data_dir=antigravity` | No |
| Antigravity IDE | Per-user IDE application path | `language_server_windows_x64.exe` with `app_data_dir=antigravity-ide` | No |
| Antigravity CLI | `agy.exe` on PATH or in the documented local install path | CLI language-server process when exposed | Only for CLI users |

Local data without an executable is reported as a remnant, not as an installed
product. This prevents an old IDE profile from being mistaken for a current
installation.

## Live data path

The Antigravity language server listens on random localhost ports and protects
requests with `x-codeium-csrf-token`. TwinQuota discovers the active HTTP port
from the product log and the short-lived CSRF value from the matching process.
It then uses two local Connect RPC methods:

- `RetrieveUserQuotaSummary` for shared weekly and five-hour quota buckets.
- `GetAvailableModels` for the current account's recommended agent model IDs,
  display names, providers, remaining fraction, and reset time.

The CSRF value exists only in memory for the duration of a refresh. It is never
logged or included in `snapshot.json`.

## Confirmed quota grouping

The live response contained two shared groups:

- **Gemini Models**: Gemini Flash and Gemini Pro families.
- **Claude and GPT models**: Claude Opus, Claude Sonnet, and GPT-OSS families.

Each group had a weekly bucket and a five-hour bucket. Individual available
models also carried their relevant remaining fraction and reset time.

TwinQuota intentionally parses the model IDs returned in `agentModelSorts`
instead of hard-coding a catalog. This lets newly enabled, removed, or
plan-specific models appear without an app release.

## CLI behavior

Antigravity CLI is optional. If `agy.exe` is installed while no live language
server endpoint can be found, TwinQuota can run the official `agy models
--output-format json` command (with a text fallback for older builds) to obtain
the model list. Detailed live quota still comes from a running Antigravity
surface; otherwise the last successful safe snapshot is shown.

## Local validation result

At validation time:

- Antigravity 2.0 v2.10.0 was installed and its live RPC returned HTTP 200.
- Antigravity IDE profile data remained, but the IDE executable was absent.
- Antigravity CLI was not installed.
- Live model discovery included Google Gemini, Anthropic Claude, and OpenAI
  GPT-OSS providers.

## Official references

- Antigravity CLI installation and authentication:
  <https://antigravity.google/docs/cli/install/>
- Model quota panel (`/usage` and `/quota`):
  <https://antigravity.google/docs/cli/commands/usage/>
- Headless mode and machine-readable model listing:
  <https://antigravity.google/docs/cli/headless/>
- Plans and third-party model availability:
  <https://antigravity.google/docs/plans>
