---
name: companion
description: Start, stop, or check the local Windows Claude in Codex animated companion. Use only when the user asks for this companion; not for native ChatGPT Pets or other applications.
---

# Claude in Codex

This plugin launches a separate transparent Windows companion window; it never modifies Codex application files.

Use the packaged `scripts/companion.ps1` with `-Action Start`, `Stop`, or `Status`. Resolve the script relative to this plugin's root. Use Windows PowerShell or PowerShell, not a cloud or WSL host. Run only the action the user requests.

Example: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<plugin-root>\scripts\companion.ps1" -Action Start`.

Start uses a packaged local executable when present. A source checkout requires the .NET 10 SDK and the script builds the app if needed. Explain build/runtime errors without editing user configuration or searching for credentials. Never read auth.json, tokens, or session transcripts. Usage is obtained by the companion from local Codex app-server through account/rateLimits/read only. If quota cannot be read, show unavailable; do not fabricate values or initiate sign-in automatically.

Click the character for the quota panel and position controls. It starts locked. Unlock through the lock icon before dragging. Theme follows Windows AppsUseLightTheme by default. Position is relative to the Codex client-area bottom-right corner; minimization, hiding and virtual-desktop cloaking hide the character. Stop only this companion's process, never ChatGPT or unrelated codex.exe processes.

No startup-at-login task or hook is installed. No screenshot or computer-control permission is needed. Do not auto-start without a user request. Installing or enabling the plugin is not authorization for unrelated background actions.
