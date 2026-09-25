---
name: codex-quota-monitor
description: Control or troubleshoot the installed Windows Codex quota overlay when the user asks to show, hide, refresh, switch its layout or theme, inspect current limits, or remove its local data.
---

# Codex Quota Monitor

Use the scripts relative to this skill directory. The shared controller is `../../scripts/control.ps1`.

- For show, hide, toggle, refresh, exit, layout A/B/C, or theme auto/light/dark, run the controller with the matching `-Action` value.
- For a quota question, run the controller with `-Action status` and explain the returned JSON. Treat `estimatedExhaustion`, `burnRatePerHour`, and `outputTokensPerSecond` as local estimates. `cacheHitPercent` is the cached-input share for the most recent model response.
- If the controller cannot connect, run `../../scripts/start-monitor.ps1`, wait briefly, and retry once.
- For installation repair, run `../../scripts/install.ps1`.
- Remove cached settings only when the user explicitly asks; run `../../scripts/uninstall.ps1 -RemoveLocalData`.

The overlay is Windows-only and reads ChatGPT-backed Codex limits through `codex app-server`. API-key-only sessions do not expose subscription quota data.
