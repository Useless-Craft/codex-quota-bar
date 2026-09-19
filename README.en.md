# Codex Quota Bar

[简体中文](README.md) · [Download for Windows](https://github.com/Useless-Craft/codex-quota-bar/releases/latest) · [Report an issue](https://github.com/Useless-Craft/codex-quota-bar/issues)

Show your weekly remaining quota and next reset time beside **Help** in the Windows Codex desktop app.

**Weekly usage limit 11% left**　│　Resets Sep 6, 2026, 19:29

**每周额度剩余 11%**　│　重置时间 2026年9月6日 19:29

When a public signal is available, a third segment shows a Tibo reset signal or the estimated next-24-hour probability, for example `Tibo 24h 16%`. This is an experimental third-party estimate from NextReset, not an OpenAI commitment.

These are formatting examples. Live values come from the currently signed-in Codex account.

## Features

- A compact rounded capsule containing weekly remaining quota and reset time, plus a Tibo status when space allows. If the menu is too close to the edge, the original two fields stay visible and the full Tibo status remains in the tooltip.
- Automatic light and dark appearance; orange at 20% remaining or less, red at 10% or less.
- Follows the Codex UI language: Chinese for Chinese locales, English otherwise. Dates use local time and a 24-hour clock.
- Multiple Codex windows, with window-event tracking for immediate movement and matching minimize, occlusion and close behavior.
- One shared quota refresh every 60 seconds. Right-click to refresh manually or exit.
- NextReset is refreshed asynchronously every 15 minutes, with an 8-second request timeout. No account ID, quota, login information or X credentials are sent.
- Only a future absolute timestamp supplied by the source is used. Vague text such as “tonight” or “tomorrow” becomes `Tibo signal · time unknown`; no countdown is invented. Expired or failed data shows `Tibo not updated`.
- The tooltip includes the source, update time, expiry time and experimental disclaimer. Right-click opens the Tibo forecast page.
- A dedicated shortcut starts Codex with the quota bar. Closing the last Codex window closes the tool and its reader process.

## Requirements

- Windows 10 / 11, x64, with .NET Framework 4.8.
- The Codex Windows desktop app, installed and signed in, with its top menu visible. Open Codex normally at least once before using this tool.
- Browser, macOS and Linux versions are not supported.

This is an independent community project, unaffiliated with OpenAI. It does not modify Codex installation files. Changes to Codex menus, CLI locations or interfaces may require an update to this tool. Release executables are unsigned.

## Download and run

1. Download `codex-quota-bar-v1.1.0-windows-x64.zip` from [Releases](https://github.com/Useless-Craft/codex-quota-bar/releases/latest).
2. **Extract the entire ZIP** into a folder you intend to keep.
3. With Codex open, double-click `CodexQuotaBar.exe`.

To start both apps together, open PowerShell in the extracted folder and run once:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\create-shortcut.ps1
```

Use **Codex + Quota Bar** in the Start menu afterwards. You can also pin that shortcut to the taskbar manually. The script creates one shortcut for the current user and does not need administrator privileges. The execution policy above applies only to that PowerShell process.

To create the shortcut on your desktop or in another folder:

```powershell
.\create-shortcut.ps1 -ShortcutDirectory ([Environment]::GetFolderPath('Desktop'))
```

The shortcut points to the extracted folder; recreate it if you move the tool. Use the new shortcut when you want linked startup. The tool does not configure startup at login or install a resident watcher.

Only one instance runs per user session. Starting it again does not stack bars. Choosing **Exit quota display** on any bar closes all quota bars and leaves Codex running.

## Build from source

Uses the local .NET Framework compiler, without Visual Studio, NuGet or third-party libraries:

```powershell
git clone https://github.com/Useless-Craft/codex-quota-bar.git
cd codex-quota-bar
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The output is `CodexQuotaBar.exe` in the repository root. Exit the quota tool running from that directory before rebuilding or replacing it.

| File | Purpose |
| --- | --- |
| `QuotaBar.cs` | WPF display, window tracking, quota reader and process lifecycle |
| `quota.manifest` | User-level privileges and DPI settings |
| `build.ps1` | x64 build |
| `create-shortcut.ps1` | Linked-startup shortcut creation |

## How it works and data handling

The tool starts its own installed Codex CLI process with `app-server --stdio` and calls `account/rateLimits/read`. It shows only the 10080-minute weekly window in the `codex` quota bucket. Missing data is not interpreted as 0%; after a reset time passes, the display waits for fresh service data.

It reuses the existing Codex login, does not start model conversations, purchase quota or redeem reset credits, and has no additional telemetry or upload service. It reads `locale` from `CODEX_HOME/computer-use/config.json`, defaulting to the user's `.codex` folder when `CODEX_HOME` is unset. Temporarily unavailable locale data preserves the last language; the initial default is English.

The Tibo segment reads the public JSON endpoint at `https://nextreset.ai/api/forecast`. It uses the 24-hour probability, `asOf`, `expiresAt` and an explicit future timestamp only when the source supplies one. Relative wording is never converted into a made-up time. NextReset is an experimental public estimate that can be delayed, degraded or changed; it is not an OpenAI service guarantee. The context-menu command **Open Tibo forecast** opens [NextReset forecast](https://nextreset.ai/forecast/).

A temporary UI Automation process locates the menu, with a five-second timeout. Failed probes retain the last valid position. A successful probe is rechecked after 30 seconds, a failed one after 10 seconds; new windows, language changes and DPI changes trigger another probe. Movement uses cached positions and WinEvent notifications without waiting for menu reads. The Tibo request runs in a separate asynchronous flow and cannot block movement, menu discovery, quota reads or exit.

## Troubleshooting

- **No bar:** keep Help visible and make the window wide enough. Allow time for menu discovery. The bar hides when minimized or when space is insufficient. A quota read failure shows **Not updated**; right-click to refresh.
- **Slow Codex startup after an update:** `--launch` waits up to 120 seconds for a window. If that expires, open Codex and then start the tool again.
- **Other errors:** unhandled errors overwrite `last-error.txt` beside the executable with a timestamp, stage and exception stack. Normal operation does not continuously write logs.

Command-line operations:

```powershell
.\CodexQuotaBar.exe --launch
.\CodexQuotaBar.exe --exit
.\CodexQuotaBar.exe --check "$PWD\quota-check.json"
```

`--check` reads live quota and checks menu placement and date formatting. Its report includes the account ID and window title. Remove those fields and personal paths in logs before attaching them to an issue. Do not commit diagnostic reports or logs.

To uninstall, exit the tool and delete its folder and the shortcuts you created.

## License

[MIT](LICENSE). Codex application files and OpenAI icons are not included.
