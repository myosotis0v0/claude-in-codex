# Claude in Codex

A Windows companion packaged as a Codex plugin. The orange character follows the inside bottom-right corner of the Codex window. It is a separate transparent native WPF process, not a patch or injection into ChatGPT/Codex.

## Version 0.1

- Layered, simplified orange eyes follow the cursor behind fixed lashes. No added sclera or lower eyelids.
- Click the character for real quota windows, reset times, last-update state and manual refresh.
- Lock/unlock movement, resize the character and reset its position. Drag offsets are relative to the Codex client-area bottom-right corner and saved locally.
- Follow moves and resizes; hide on minimize, tray hiding and virtual-desktop cloaking. Exit when the owning app/window closes. The window is owned by Codex, not globally topmost.
- Follow Windows application light/dark mode, or choose an explicit theme.
- Transparent image corners pass input through. No screen capture, computer-control integration, HID scanning or global input hook.

The quota response determines the displayed windows. If the account returns only a weekly window, only that window is shown. Missing data is never replaced with a made-up percentage. A failed refresh marks the previous result stale. This is account-level quota, not per-chat token consumption.

## Start from this source checkout

Requires Windows 10/11, a signed-in Codex desktop app, and the .NET 10 SDK (or a newer SDK supporting .NET 10 and the .NET 10 Desktop Runtime).

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\plugins\claude-in-codex\scripts\companion.ps1 -Action Start
```

Use `-Action Status` or `-Action Stop` to inspect or stop only this companion. Right-click also opens the panel; its power icon exits the companion. Installation does not enable login startup or automatic lifecycle hooks.

## Install as a Codex plugin

With a source checkout, register this repository directory as a local marketplace, then install:

```powershell
codex plugin marketplace add D:\ToyProjects\claude-in-codex
codex plugin add claude-in-codex@claude-in-codex-local
```

Start a new local desktop Codex chat and ask to start **Claude in Codex**. The skill launches the local helper. Web/mobile/cloud hosts cannot create a window on your Windows desktop. The source plugin includes the C# sources and artwork, so it can build inside the installed plugin cache. A private GitHub source requires Git authentication if installing by owner/repository.

For Git installation after this source is pushed:

```powershell
codex plugin marketplace add myosotis0v0/claude-in-codex
codex plugin add claude-in-codex@claude-in-codex-local
```

This registers a personal marketplace, not a publication in the public plugin directory.

## Self-contained release package

```powershell
.\scripts\package.ps1
```

The ZIP under `dist` contains a marketplace and self-contained Windows x64 plugin. Extract it to a permanent location; register that extracted folder with `codex plugin marketplace add`. The packaged helper needs no .NET SDK/runtime installation. The executable is not code-signed. Review it under your organization's software policy; do not bypass company security controls.

## Verification

```powershell
dotnet build .\plugins\claude-in-codex\src\ClaudeCompanion\ClaudeCompanion.csproj -c Release
$env:CLAUDE_TEST_OUTPUT = Join-Path $PWD 'artifacts\diagnostics'
$exe = '.\plugins\claude-in-codex\src\ClaudeCompanion\bin\Release\net10.0-windows\ClaudeCompanion.exe'
Start-Process $exe -ArgumentList '--self-test','--live','--window-test' -Wait
Get-Content artifacts\diagnostics\report.json
```

Tests cover rate-limit parsing, unknown data, source mask composition, alpha hit testing, 100/125/150/200 percent positioning math, negative monitor coordinates, real local usage reads, and native move/resize/hide/minimize behavior on a dedicated test window. The fixture does not move/minimize the user's Codex window or send mouse/keyboard input. It tests panel and lock commands directly, not physical pointer dragging. Test settings are nonpersistent. Diagnostics may contain real usage percentages; `artifacts/` is excluded from Git.

## Privacy and lifecycle

Settings live under `%LOCALAPPDATA%\ClaudeInCodex\settings.json`. The companion persists only display preferences, lock state and relative offsets. The small bounded log contains timestamps and error categories, not tokens or quota payloads. Usage remains in memory. The companion starts its own `codex app-server --stdio`, performs initialization and requests `account/rateLimits/read`; it never reads `auth.json` or conversation files. It listens for `account/rateLimits/updated`, with a two-minute fallback while visible. Exiting the companion closes its owned app-server, not the app's existing server.

The gaze timer runs only while visible. Window discovery is throttled, and position changes use physical coordinates and the host window DPI. Existing app-specific dark-mode overrides are not detected: **Follow system** intentionally follows Windows AppsUseLightTheme.

## Source and artwork

The runtime plugin is under `plugins/claude-in-codex`. Sprite development and earlier previews are under `tools`, `assets` and `previews`. Runtime assets are isolated under the plugin's `assets/claude-v2` directory. `ASSETS.md` records the generation and compositing workflow. Supplied character artwork has separate provenance; the repository's MIT code license does not grant rights to an underlying third-party character.

References: [Codex plugins](https://developers.openai.com/plugins/build/plugins), [App Server](https://learn.chatgpt.com/docs/app-server). Implementation research also considered [codex-usage-pet](https://github.com/lanyiyrt/codex-usage-pet) and [codexusage](https://github.com/jayhilwig/codexusage); this implementation does not copy their source.
