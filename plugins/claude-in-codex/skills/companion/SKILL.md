---
name: companion
description: Start, stop, or check the local Windows Claude in Codex animated companion. Use only when the user asks for this companion; not for native ChatGPT Pets or other applications.
---

# Claude in Codex

This plugin launches a separate transparent Windows companion window; it never modifies Codex application files.

Use the packaged `scripts/companion.ps1` with `-Action Start`, `Expand`, `Stop`, or `Status`. Start opens a small persistent icon inside the Codex bottom-right corner. Expand opens the character and quota panel, including when the icon process already exists. Resolve the script relative to this plugin's root. Use Windows PowerShell or PowerShell, not a cloud or WSL host. Run only the action the user requests.

Example: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "<plugin-root>\scripts\companion.ps1" -Action Start`.

Start uses a packaged local executable when present. A source checkout requires the .NET 10 SDK and the script builds the app if needed. Explain build/runtime errors without editing user configuration or searching for credentials. Never read auth.json, tokens, or session transcripts. Usage is obtained by the companion from local Codex app-server through account/rateLimits/read only. If quota cannot be read, show unavailable; do not fabricate values or initiate sign-in automatically.

Click the small launcher to expand the character and quota panel. Click the character for the quota panel and position controls. The header minimize icon returns to the small launcher; the footer power icon completely exits the helper. It starts locked. Unlock through the lock icon before dragging. Theme follows Windows AppsUseLightTheme by default. Position is relative to the Codex client-area bottom-right corner; minimization, hiding and virtual-desktop cloaking hide the character and launcher. Stop only this companion's process, never ChatGPT or unrelated codex.exe processes.

No startup-at-login task is installed. The optional bundled SessionStart hook starts the small launcher only after the user reviews and trusts it in Codex. AutoStart never builds source during a session-start hook; explicitly Start once if a source plugin needs building. Never approve hooks or bypass hook trust for the user. No screenshot or computer-control permission is needed. Installing or enabling the plugin is not authorization for unrelated background actions.
