# UI Help Text

Tooltip text for every settings field in the tray app.
Used as the source of truth when wiring up ToolTip controls.

---

## PC Modes Tab

**Mode list**
> Named configurations that set audio, monitors, volume, and apps in one action.
> Home Assistant exposes these as options on the "PC Mode" select entity.

**Name**
> Unique identifier for this mode. Used in HA automations and config.
> Example: `couch`, `desktop`.

**Audio Device**
> Audio output to switch to when this mode is activated.
> Select "(Don't change)" to leave the current audio device untouched.

**Monitor Profile**
> Monitor layout to apply when this mode is activated (via MultiMonitorTool).
> Select "(Don't change)" to leave the current monitor layout untouched.

**Volume**
> System volume (0–100) to set when this mode is activated.

**Launch App**
> App to launch when this mode is activated.
> Apps are defined in the `Apps` config section.

**Kill App**
> App to terminate when this mode is activated.
> Useful for killing Steam Big Picture when switching to desktop mode.

---

## Games Tab

**Default PC Mode**
> Mode applied automatically before launching any Steam game,
> unless the game has its own per-game override below.
> Set to "(none)" to disable automatic mode switching on game launch.

**Game list — PC Mode column**
> Per-game mode override. Overrides the Default PC Mode for this specific game.
> - **(default)** — use the Default PC Mode above
> - **(none)** — suppress mode switching for this game (launch without switching)
> - Any named mode — switch to that mode before launching this game

**App ID column**
> Steam's internal identifier for the game. Read-only.

---

## General Tab

**Port**
> HTTP port the service listens on. Home Assistant must be configured with the same port.
> Changes require a restart. Valid range: 1024–65535.

**SoundVolumeView**
> Status of the NirSoft SoundVolumeView tool used for audio device switching.
> Must be placed in the configured ToolsPath directory.
> Download: https://www.nirsoft.net/utils/sound_volume_view.html

**MultiMonitorTool**
> Status of the NirSoft MultiMonitorTool used for monitor profile switching.
> Must be placed in the configured ToolsPath directory.
> Download: https://www.nirsoft.net/utils/multi_monitor_tool.html

**Log Level**
> Controls how much detail is written to the log.
> - **Error** — only failures
> - **Warning** — failures and unexpected conditions
> - **Info** — normal operational events (recommended)
> - **Verbose** — full request/response detail (for debugging)

**Auto Update**
> Automatically download and install new service releases from GitHub.
> The tray icon will notify you before restarting.

---

## Power Tab

**Auto-Sleep Timeout (minutes)**
> Minutes of total inactivity before the PC sleeps automatically.
> Inactivity means: no Steam game running AND no mouse, keyboard, or gamepad input.
> Set to 0 to disable auto-sleep entirely.
> This works independently of Home Assistant — the PC manages itself.

---

## Log Tab

> Read-only log viewer. Displays recent log entries filtered by the Log Level
> set in the General tab. Useful for diagnosing issues without opening a log file.
