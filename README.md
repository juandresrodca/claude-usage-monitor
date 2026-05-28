# Claude Usage Monitor

A lightweight Windows tray app that shows your Claude.ai usage — the **5-hour
session window** and **7-day weekly window** — without opening a browser tab.

| Section | What it shows |
|---|---|
| **Current session** | `% used` of the rolling 5-hour quota, with the exact reset time |
| **Weekly — all models** | `% used` of the 7-day window across every model |
| **Weekly — Opus** | `% used` of the 7-day Opus-only window (shown as 0% if you haven't used Opus this week) |

The app pulls data straight from the same endpoint your browser hits at
`claude.ai/settings/usage`, so the numbers match what Anthropic shows you.

---

## Features

- 🔄 Live sync with your real Claude account (no scraping screenshots)
- 🪟 System tray icon — single click opens a compact dashboard
- 🌐 Built-in browser sign-in via WebView2 (no copy/paste of session keys needed)
- 🔍 **Endpoint discovery** tool — captures every `/api/` call claude.ai makes,
  so the app keeps working if Anthropic changes the URL shape
- 🌍 **English / Spanish** UI, switchable at runtime
- 🔐 Session credentials encrypted on disk with Windows **DPAPI** (per-user)
- 🚀 Optional "Start with Windows" toggle

---

## Quick start — End users

### Prerequisites

| What | Version | Where to get it |
|---|---|---|
| Windows | 10 / 11 (x64) | — |
| .NET Desktop Runtime | **8.0** | <https://dotnet.microsoft.com/download/dotnet/8.0> |
| WebView2 Runtime | Evergreen (latest) | <https://developer.microsoft.com/microsoft-edge/webview2/> — pre-installed on Windows 11 |

> **Note**: if you only ever run the published self-contained build (see below),
> you don't need the .NET runtime — it ships inside the EXE.

### Install from a release

Two options on every release:

**A. Installer (recommended)**
1. Download `ClaudeUsageMonitor-vX.Y.Z-Setup.exe` from [Releases](../../releases).
2. Double-click and follow the wizard. Installs **per-user** under
   `%LOCALAPPDATA%\Programs\Claude Usage Monitor` — no admin rights needed.
3. Adds a Start Menu entry and an uninstaller (visible in **Settings →
   Apps**). Optionally creates a desktop shortcut and a "start with Windows"
   entry.
4. If the Microsoft Edge **WebView2 Runtime** is missing, the installer points
   you at the official Microsoft download page.

> SmartScreen may warn that the installer is from an unverified publisher
> (the binary isn't code-signed). Click **More info → Run anyway**. The
> source is fully visible in this repo.

**B. Portable zip**
1. Download `ClaudeUsageMonitor-vX.Y.Z-win-x64.zip` from [Releases](../../releases).
2. Unzip anywhere (e.g. `C:\Apps\ClaudeUsageMonitor\`).
3. Double-click `ClaudeUsageMonitor.exe`.

In both cases: right-click the tray icon → **Settings / Configuración** →
**Sign in with Claude**. The dashboard populates within a couple of seconds
and auto-refreshes every 10 minutes (configurable).

---

## Build from source

```powershell
# 1. Clone
git clone https://github.com/<your-user>/claude-usage-monitor.git
cd claude-usage-monitor

# 2. Restore + build (debug)
dotnet build

# 3. Run
dotnet run
```

### Building a self-contained release (single EXE)

```powershell
pwsh build/publish.ps1
```

This wraps `dotnet publish` with the right flags and lands the output in
`dist/ClaudeUsageMonitor-<version>-win-x64/`. The runtime is bundled so end
users don't need .NET installed.

Override the version on the CLI:

```powershell
pwsh build/publish.ps1 -Version 0.2.0
```

### Building the Setup.exe installer

```powershell
# One-time: install Inno Setup 6
winget install JRSoftware.InnoSetup

# Then:
pwsh build/build-installer.ps1
```

Produces `dist/ClaudeUsageMonitor-<version>-Setup.exe` — the installer wizard
that lands the app under `%LOCALAPPDATA%\Programs\` and registers the
uninstaller with Windows.

### Automated releases via GitHub Actions

The repo ships with [.github/workflows/release.yml](.github/workflows/release.yml).
Push a tag `vX.Y.Z` (or trigger the workflow manually) and CI will:

1. Publish the self-contained EXE.
2. Zip it as `ClaudeUsageMonitor-vX.Y.Z-win-x64.zip`.
3. Compile the Inno Setup installer as `ClaudeUsageMonitor-vX.Y.Z-Setup.exe`.
4. Attach both to a GitHub Release with auto-generated notes.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

---

## How the data flow works

```
┌────────────────────────────────┐    ┌────────────────────────┐
│  WebView2 (claude.ai session)  │ ─► │  /api/account          │ ─► org UUID
│  reuses your real browser-     │ ─► │  /api/orgs/{id}/usage  │ ─► session %, weekly %
│  style cookies (Cloudflare +   │    └────────────────────────┘
│  sessionKey, captured via      │
│  the built-in sign-in dialog)  │
└────────────────────────────────┘
              │
              ▼
┌────────────────────────────────┐
│  ClaudeApiService.ParseLimits  │
│   five_hour.utilization   ──► current session %
│   five_hour.resets_at     ──► next reset
│   seven_day.utilization   ──► weekly all-models %
│   seven_day_opus.*        ──► weekly Opus %
└────────────────────────────────┘
              │
              ▼
   UsageDashboardViewModel ──► dashboard bindings
```

The fetch happens **inside** the WebView2 (`window.fetch` → `/api/...`), so the
request looks identical to a normal browser call and Anthropic's Cloudflare
layer never sees it as a bot.

---

## Configuration

Right-click tray → **Settings / Configuración**:

| Option | Default | What it does |
|---|---|---|
| Sign in with Claude | — | Opens an embedded browser; cookies are captured automatically |
| Clear session | — | Wipes the WebView2 user-data folder and stored credentials |
| Discover usage endpoint | — | Diagnostic tool — captures every `/api/` call claude.ai makes and saves to `%APPDATA%\ClaudeUsageMonitor\endpoint_discovery_<ts>.json`. Use when the dashboard goes blank because Anthropic shipped a new endpoint shape |
| Auto refresh | 10 min | Background sync interval |
| Language | Español | Switch between **Español** and **English** (saved per user) |

### Files on disk

Everything lives under `%APPDATA%\ClaudeUsageMonitor\`:

| File | Purpose |
|---|---|
| `settings.json` | App config — `SessionKey` and `AllCookies` are **DPAPI-encrypted** |
| `config.json` | Cached usage snapshot for fast startup |
| `WebView2\` | The browser profile / cookie jar used by the embedded sign-in |
| `debug_last_fetch.json` | Tiny sanitized diagnostic (timestamp + cookie names, no PII) |
| `endpoint_discovery_*.json` | Output of the **Discover** diagnostic tool. Not auto-cleaned |

### Wiping everything

If you want to completely uninstall and remove all traces:

```powershell
Stop-Process -Name ClaudeUsageMonitor -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "$env:APPDATA\ClaudeUsageMonitor"
```

Then delete the folder where you unzipped the app.

---

## Security notes

- The **session key** and the full **cookie jar** are encrypted with Windows
  DPAPI (`DataProtectionScope.CurrentUser`). The ciphertext cannot be decrypted
  by another user on the same machine, nor by anyone if the file is copied to a
  different machine.
- The app **never sends your session anywhere** other than `claude.ai` itself.
  Inspect [Services/ClaudeApiService.cs](Services/ClaudeApiService.cs) and
  [Services/WebInterceptService.cs](Services/WebInterceptService.cs) to verify.
- All embedded hyperlinks go through a scheme allowlist (`http`/`https` only)
  before being passed to `ShellExecute`.

If you find a security issue please open a private security advisory in
GitHub — **don't** file a public issue.

---

## Disclaimer

This is an **unofficial, community-built** tool. It is not affiliated with,
endorsed by, or supported by Anthropic. It uses the same publicly accessible
`claude.ai` endpoints your own browser uses when you visit
`claude.ai/settings/usage` — no private APIs, no scraping, no automation of the
chat itself.

You are solely responsible for complying with Anthropic's
[Terms of Service](https://www.anthropic.com/legal/consumer-terms) when
using this tool with your account.

---

## Contributing

PRs welcome. Please:

1. Open an issue first if it's a large change so we can agree on the approach.
2. Keep the dashboard's "minimal noise" feel — this is a tray utility, not a
   full analytics product.
3. Run `dotnet build` before pushing; the project has no test suite yet so
   manual verification of the sync + dashboard is required.

See [Architecture](#how-the-data-flow-works) above before touching the
fetch/parse path.

---

## License

[MIT](LICENSE) — do whatever you want, but you keep your own liability.
