# Codex Quota Bar

[简体中文](README.md) · [Download for Windows](https://github.com/Useless-Craft/codex-quota-bar/releases/latest) · [Report an issue](https://github.com/Useless-Craft/codex-quota-bar/issues)

Show your weekly remaining quota and next reset time beside **Help** in the Windows Codex desktop app.

**Weekly usage limit 11% left**　│　Resets Sep 6, 2026, 19:29

**每周额度剩余 11%**　│　重置时间 2026年9月6日 19:29

The third segment always uses `N% reset`: an active reset announcement appears as red `100% reset`; otherwise it shows the experimental 24-hour probability, such as `20% reset`. Once the announcement expires or is withdrawn, the probability returns. The `100%` label means an active announcement, not that quota has already reset or that OpenAI guarantees the outcome.

These are formatting examples. Live values come from the currently signed-in Codex account.

## Features

- A compact rounded capsule containing weekly remaining quota and reset time, plus a Tibo status when space allows. If the menu is too close to the edge, the original two fields stay visible and a short status remains in the tooltip.
- Automatic light and dark appearance; orange at 20% remaining or less, red at 10% or less.
- Follows the Codex UI language: Chinese for Chinese locales, English otherwise. Dates use local time and a 24-hour clock.
- Multiple Codex windows, with window-event tracking for immediate movement and matching minimize, occlusion and close behavior.
- One shared quota refresh every 60 seconds. Right-click to refresh manually or exit.
- Public reset data refreshes asynchronously every 15 minutes. Two read-only requests run in parallel with a 30-second timeout. No account ID, quota, login information or X credentials are sent.
- An active announcement shows red `100% reset`; otherwise the bar shows the source's experimental 24-hour probability. Earlier usage or banked resets no longer hide the probability for three days. Unavailable data appears as `--% reset`.
- No hover popup appears when the third segment is visible. When space hides it, a single short line appears on hover. Right-click opens the data source.
- A dedicated shortcut starts Codex with the quota bar. Closing the last Codex window closes the tool and its reader process.

## Requirements

- Windows 10 / 11, x64, with .NET Framework 4.8.
- The Codex Windows desktop app, installed and signed in, with its top menu visible. Open Codex normally at least once before using this tool.
- Browser, macOS and Linux versions are not supported.

This is an independent community project, unaffiliated with OpenAI. It does not modify Codex installation files. Changes to Codex menus, CLI locations or interfaces may require an update to this tool. Release executables are unsigned.

## Download and run

1. Download `codex-quota-bar-v1.1.8-windows-x64.zip` from [Releases](https://github.com/Useless-Craft/codex-quota-bar/releases/latest).
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
| `tests/test-forecast.ps1` | Reset-data parser checks |

## How it works and data handling

The tool starts its own installed Codex CLI process with `app-server --stdio` and calls `account/rateLimits/read`. It shows only the 10080-minute weekly window in the `codex` quota bucket. Missing data is not interpreted as 0%; after a reset time passes, the display waits for fresh service data.

It reuses the existing Codex login, does not start model conversations, purchase quota or redeem reset credits, and has no additional telemetry or upload service. It reads `locale` from `CODEX_HOME/computer-use/config.json`, defaulting to the user's `.codex` folder when `CODEX_HOME` is unset. Temporarily unavailable locale data preserves the last language; the initial default is English.

The third segment reads public, read-only JSON from [codex-reset.com](https://codex-reset.com/): `/api/forecast` supplies the experimental 24-hour probability and active reset announcement, while `/api/feed` supplies Tibo posts and historical updates that no longer change the bar's status. Only an active announcement replaces the probability; the probability returns when the announcement ends. The service may be delayed, unavailable or changed and is not an OpenAI guarantee. **Open reset data (codex-reset.com)** opens the provider page.

A temporary UI Automation process locates the menu, with a five-second timeout. Failed probes retain the last valid position. A successful probe is rechecked after 30 seconds, a failed one after 10 seconds; new windows, language changes and DPI changes trigger another probe. Movement uses cached positions and WinEvent notifications without waiting for menu reads. Reset-data requests run in a separate asynchronous flow and cannot block movement, menu discovery, quota reads or exit; a transient failure keeps the last result while it remains fresh.

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
