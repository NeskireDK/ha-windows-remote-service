# Planned Features

Goal: seamless couch gaming with a desktop PC that power-saves when not in use.
The PC is a multi-function machine — desktop (keyboard + mouse, multiple monitors) and
couch/TV (controller, living room display). Switching between these modes gracefully is
the core use case.

---

## Blockers

Documented bugs that undermine trust in the integration.

- [ ] **ThreadStateException throughout tray UI** — Root cause: `Main()` entry point is
  missing `[STAThread]`, so the process runs in MTA mode. COM/OLE calls anywhere in
  WinForms fail. The v1.3.1 `OnHandleCreated` fix deferred the symptom in `ModesTab`
  but didn't address the cause. Still throws in at least two places:
  - Games tab → clicking PC mode dropdown in `DataGridView` (`ComboBox.set_AutoCompleteSource`
    in `DataGridViewComboBoxCell.InitializeEditingControl`)
  - Modes tab → `ComboBox.set_AutoCompleteMode` in `ModesTab.OnHandleCreated`
  Fix: wrap entry point in explicit `static Program.Main()` with `[STAThread]`.
  Implemented locally, uncommitted. *(service)*

- [x] **PC auto-sleeps immediately after waking** — `GetLastInputInfo` idle counter is not
  reset by sleep/wake. After the PC wakes, the first inactivity check sees the accumulated
  pre-sleep idle time (often 5000+ s) and triggers sleep immediately. Fixed post-v1.3.2:
  subscribe to `SystemEvents.PowerModeChanged`; on `ResumeOrWake`, record the wake timestamp
  and cap reported idle to seconds-since-wake until real input resets the baseline. *(service)*

- [x] **Stop game: optimistic state not clearing** — After stop, the media player holds
  optimistic `playing` state for 30 s, but the game shows as still running beyond that
  window. Fixed: `_stop_issued_at` slides forward on each coordinator poll that still
  reports the game running, so the 30 s window only starts after the service confirms
  the game is gone. *(integration)*

- [x] **Artwork not loading** — Granular path logging added (v1.3.4). CDN fallback added
  (v1.3.5): if all local paths miss, official games download from cdn.akamai.steamstatic.com
  and cache to librarycache. Also checks _library_hero, _header, _logo variants before
  falling back to CDN. CDN use logged at Warning with [CDN FALLBACK] prefix. *(service)*

- [x] **Steam Big Picture launch idempotent** — Launching `steam-bigpicture` when Steam is
  already running does nothing. Fixed post-v1.3.2: launch via `steam://open/bigpicture` URI
  (via `xdg-open` / `ShellExecute`) instead of re-launching the Steam process. The URI is
  handled by the running Steam instance and switches to Big Picture regardless of current state. *(service)*

- [ ] **Footer button UX** — Apply/Save/Cancel buttons inconsistently positioned. Centralise
  in right-aligned footer across all tabs (General, Games, Power, Log). PC Modes tab is
  the exception (has its own row management buttons). *(service)*

- [ ] **Monitor switch: switching back to main display fails** — Switching to TV monitor works, but switching back to main display does not. Root cause unknown — may be a util/display-switching utility issue or a service-side issue. Needs debug logging added to the monitor-switch util response to understand what is returned when the reverse switch is attempted.

- [x] **General tab: Save + Restart → Apply with live reload** — No restart required.
  Kestrel stops, rebuilds on the new port, and restarts in-process. `KestrelRestartService`
  singleton wires the delegate from Program.cs into DI so GeneralTab can call it directly.
  No process relaunch, no UAC prompt. *(service)*


- [x] **Steam: tray 503** — `POST /api/steam/run/{appId}` returns 200 even when the tray
  is not running and no game launches. Fixed in service v0.9.0: `IpcSteamPlatform` throws
  `TrayUnavailableException` → endpoint returns 503. *(service)*

- [x] **Steam: running game not in source list** — `GetRunningGameAsync` falls back to
  `"Unknown ({appId})"` if the games cache isn't warm. Fixed in service v0.9.0:
  `GetRunningGameAsync` warms cache on first call and falls back to direct manifest
  lookup for games outside the top-20. *(service)*

---

## Architecture Refactor — Collapse Service + Tray into Single Process *(done in v0.9.1)*

### Background

The old architecture had a Windows Service (SYSTEM session) and a WinForms tray app (user session) communicating via named pipe IPC. Every meaningful feature required the user session anyway — audio, monitors, Steam, app launch. Collapsed everything into the tray process. Kestrel runs inside the tray. No IPC, no session boundary, no `TrayUnavailableException`. Linux gets a natural headless binary as well.

### Releases

#### ~~0.9.2~~ — Extract `HaPcRemote.Core` library *(shipped in v0.9.1)*

- [x] Create `HaPcRemote.Core` class library
- [x] Move services, interfaces, implementations, endpoints, models into Core
- [x] `HaPcRemote.Tray` references Core
- [x] Update test project references

#### ~~0.9.3~~ — Embed Kestrel in Tray, replace IPC with direct calls *(shipped in v0.9.1)*

- [x] Add ASP.NET Core / Kestrel hosting to `HaPcRemote.Tray`
- [x] Wire all Core services into Tray's DI container
- [x] Replace IPC wrappers with direct calls (`WindowsSteamPlatform`, `CliRunner`, `Process.Start`)
- [x] Migrate config path to `%AppData%\HaPcRemote\`

#### ~~0.9.4~~ — Delete Service project, IPC layer, update installer *(shipped in v0.9.1)*

- [x] Delete `HaPcRemote.Service` project
- [x] Delete IPC layer and wrappers
- [x] Update Inno Setup installer (no service registration, startup via all-users startup folder, config migration)
- [x] Update README

#### 0.9.5 — Linux foundation *(service repo)* *(done)*

Same binary, headless mode, systemd user service.

- [x] Wrap all WinForms/tray code behind `OperatingSystem.IsWindows()` / `[SupportedOSPlatform]`
- [x] Add Linux `IPowerService`: `systemctl suspend` or `loginctl suspend`
- [x] Add Linux `ISteamPlatform`: filesystem path (`~/.steam/steam/`), running game via VDF, launch via `xdg-open steam://run/<id>`
- [x] Add Linux audio (`pactl`-based `LinuxAudioService`)
- [x] Add headless entry point (Linux): plain Kestrel + mDNS, no tray icon, SIGTERM clean exit
- [x] Add systemd user service unit file to release artifacts
- [x] Add Linux build job to GitHub Actions CI
- [x] Document install steps in README

### Key decisions made

- **Why collapse?** Every feature requires the user session. IPC is complexity with no benefit.
- **Config path**: moves to `%AppData%` (user-owned, no elevation needed for reads/writes)
- **Native AOT**: dropped — framework-dependent is fine, .NET 10 auto-install already ships
- **Linux tray**: no system tray on Linux. API key via config file, logs via `journalctl`, updates via package manager — these are Linux-native equivalents, not a degraded experience.
- **Monitor profiles on Linux**: xrandr/Wayland too fragmented — skip initially, document as known gap

---

## v1.0

### 1. PC Mode — `POST /api/system/mode` + `select` entity *(done in v1.0)*

Single endpoint that atomically sequences audio output, monitor profile, volume, and
app launch/kill from a named config block.

```json
"Modes": {
  "couch": {
    "AudioDevice": "HDMI Output",
    "MonitorProfile": "tv-only",
    "Volume": 40,
    "LaunchApp": "steam-bigpicture"
  },
  "desktop": {
    "AudioDevice": "Speakers",
    "MonitorProfile": "desk-full",
    "Volume": 25,
    "KillApp": "steam-bigpicture"
  }
}
```

HA exposes a `select` entity "PC Mode" with options from `GET /api/system/modes`.
Selecting a mode calls the endpoint. The service handles sequencing and waits between
steps — no fragile automation chains.

- [x] Service: add `Modes` config section and `POST /api/system/mode` endpoint *(service)*
- [x] Service: add `GET /api/system/modes` to list available mode names *(service)*
- [x] Integration: `PcRemoteModeSelect` entity in `select.py` *(integration)*
- [x] Integration: `set_mode()` in `api.py` *(integration)*

### 2. Couch Gaming Automation Blueprint *(done in v1.0)*

Blueprint with selector inputs — no hard-coded entity names.

- [x] `blueprints/automation/pc_remote/couch_gaming.yaml` *(integration)*

### 3. Aggregated State Endpoint — `GET /api/system/state` *(done in v1.0)*

Single endpoint replaces the 6+ individual coordinator calls per poll cycle.

- [x] Service: add `GET /api/system/state` endpoint *(service)*
- [x] Integration: refactor `_async_update_data` to use single call *(integration)*

---

## v1.1

### 4. Post-Session Sleep Blueprint *(done in v1.0)*

When the Steam media player transitions `playing → idle`, wait N minutes, confirm
still idle, then sleep the PC. Closes the power-saving loop without manual action.

- [x] `blueprints/automation/pc_remote/post_session_sleep.yaml` *(integration)*

### 5. Media Browser for Steam Games + Apps *(done in v1.0.2)*

`select_source` works via developer tools / service calls but the dropdown only shows
in the entity detail dialog — not on dashboard cards. Add `browse_media` + `play_media`
support so Steam games (and later apps) appear in the HA media browser with thumbnails
and hierarchical navigation.

- [x] Integration: implement `async_browse_media()` returning `BrowseMedia` tree *(integration)*
- [x] Integration: implement `async_play_media()` to launch games/apps *(integration)*
- [x] Integration: add `BROWSE_MEDIA` + `PLAY_MEDIA` feature flags *(integration)*
- [x] Integration: add tests for browse/play media *(integration)*

### 6. User Idle Time Sensor *(done in v1.0.2)*

`GetLastInputInfo` Win32 API → seconds since last keyboard/mouse input.
Guards the sleep blueprint against sleeping a PC that someone is actively using at the desk.

- [x] Service: expose via `GET /api/system/idle` *(service)*
- [x] Integration: `sensor` entity "Idle Time" (device class `duration`) *(integration)*

### Settings Panel *(done in v1.1)*

Tabbed settings UI in the tray app: Modes, General, and log viewer.

- [x] Service: config panel with multiple tabs *(service)*
- [x] Service: PC mode config UI with dropdowns for monitor profiles, audio devices *(service)*
- [x] Service: general settings for log level *(service)*
- [x] Service: log viewer in settings panel *(service)*

---

## v1.2

### Bugs

- [x] **Kestrel status stuck on "Starting..."** — Fixed: synchronous fast path in
  `GeneralTab.UpdatePortStatus()` when `KestrelStatus.Started` is already completed. *(service)*

- [x] **Update race condition** — Fixed: `SemaphoreSlim` guard in `HandleDownloadAsync`
  prevents concurrent manual + auto-update downloads. *(service)*

- [x] **Update button color** — Fixed: removed stale `BackColor` reset. *(service)*

### 7. Rename Duration Sensor to Idle Duration *(done in v1.2)*

- [x] Service: renamed log message from "idle time" → "idle duration" *(service)*
- [x] Integration: renamed sensor to "Idle Duration" (`idle_duration` entity ID, translations, strings) *(integration)*

### 8. Non-Steam Game Discovery *(done in v1.2)*

Parses `shortcuts.vdf` (binary VDF via ValveKeyValue) to discover non-Steam game shortcuts.
Shortcuts merge into the game list with `IsShortcut` flag and launch via shifted
`steam://rungameid/` URI. CRC32-based appid generation matches Steam's algorithm.

- [x] Service: parse `shortcuts.vdf`, merge into game list, launch via shifted appid *(service)*
- [x] Integration: non-Steam games appear in media browser automatically (no changes needed — data flows through existing game list) *(integration)*

### 9. Steam Artwork / Poster Serving *(done in v1.2)*

`GET /api/steam/artwork/{appId}` serves game artwork from Steam's local cache.
Resolution order: custom grid art (`userdata/{steamid}/config/grid/{appId}p.*`) →
library cache (`appcache/librarycache/{appId}_library_600x900.*`). `SteamUserIdResolver`
discovers the active user via `loginusers.vdf`.

- [x] Service: artwork endpoint with grid → librarycache fallback *(service)*
- [x] Integration: media browser thumbnails use local artwork endpoint instead of Steam CDN *(integration)*

### 10. API Debug Page *(done in v1.2)*

Self-hosted HTML page at `GET /debug` (localhost-only, excluded from auth). Lists all
endpoints with method, path, description, and "Try it" buttons. API key auto-injected
via `<meta>` tag. Dark theme, no external dependencies.

- [x] Service: `/debug` endpoint, localhost-only, API key injection, endpoint catalog *(service)*
- [x] Tray: "API Explorer" context menu item *(service)*

### 11. Game-to-PC-Mode Binding *(done in v1.2)*

Per-game and default PC mode bindings. Mode switch executes before game launch.
Config in `Steam.DefaultPcMode` + `Steam.GamePcModeBindings`. Games settings tab
in tray with per-game dropdown.

```json
"Steam": {
  "DefaultPcMode": "couch",
  "GamePcModeBindings": {
    "730": "desktop",
    "1245620": "couch"
  }
}
```

- [x] Service: config, resolution logic (per-game → default → none), mode switch before launch *(service)*
- [x] Service: `GET/PUT /api/steam/bindings` endpoints *(service)*
- [x] Tray: "Games" settings tab with per-game mode dropdown *(service)*
- [x] Integration: `game_pc_mode_binding` attribute on media player entity *(integration)*

---

## v1.2.2

### 14. Steam Cold-Start Support

`steam_ready` in system state signals when Steam is up and ready. Prevents game launch
commands silently failing when Steam isn't running. Integration auto-launches Steam via
the existing app system and waits for readiness before sending the game command.

Auto-detects Steam path from registry on startup and writes a default `"steam"` app
config entry if missing — no manual setup required.

**Service:**
- [x] Add `SteamReady: bool` to `GET /api/system/state` response *(service)*
- [x] Add `UseShellExecute: bool` to `AppDefinitionOptions` (default `false`) *(service)*
- [x] Pass `UseShellExecute` through `DirectAppLauncher.LaunchAsync` *(service)*
- [x] On startup: detect Steam exe from registry, write default `"steam"` app entry (`-bigpicture` args) if not already configured *(service)*

**Integration:**
- [x] Read `steam_ready` from system state in coordinator *(integration)*
- [x] Cold path: replace fixed 15 s sleep with poll-until-`steam_ready` (max 2 min) *(integration)*
- [x] Warm path: check `steam_ready`; if false → launch `"steam"` app, wait for `steam_ready`, then launch game *(integration)*

---

## v1.3.1

### Bugs

- [x] **Opening settings throws `ThreadStateException`** — `ModesTab` constructor sets `AutoCompleteMode` on `ComboBox` controls before a window handle exists. In .NET 10, setting `AutoCompleteMode` requires STA and triggers an OLE call that fails when the handle isn't created yet. Triggered by any tray action that opens the settings form (double-click, right-click → Show Log, etc.). Fix: remove `AutoCompleteMode`/`AutoCompleteSource` from the constructor and apply them in `OnHandleCreated`. *(service)*

---

## v1.3.2

### Bugs

- [ ] **WinForms tray runs in MTA thread** — Top-level C# statements don't automatically
  apply `[STAThread]`, so the tray process runs in MTA mode. COM/OLE calls in WinForms
  (autocomplete, clipboard, drag-and-drop, file dialogs) require STA and will throw
  `InvalidOperationException` or `ThreadStateException` in MTA context. Root cause of the
  v1.3.1 `ThreadStateException` — the `OnHandleCreated` fix mitigated the symptom but not
  the cause. Fix: wrap entry point in an explicit `static Program.Main()` decorated with
  `[STAThread]`. Implemented locally, uncommitted. *(service)*

---

## v1.3

### Bugs

- [x] **Game artwork returns 401** — Fixed: added `/api/steam/artwork` prefix to `ExemptPaths` in `ApiKeyMiddleware`. *(service)*

- [x] **Non-Steam games show idle when launched via integration** — Fixed: when `GetRunningAppId()` returns 0, `GetRunningGameAsync` now scans running process paths against shortcut `ExePath` fields parsed from `shortcuts.vdf`. First match returns the `SteamRunningGame`. *(service)*

- [x] **turn_off connection error** — HA throws `CannotConnectError` on `switch/turn_off` and `media_player/turn_off` after sleep. PC suspends mid-request so `_TIMEOUT` fires → `TimeoutError` → `CannotConnectError` bubbles uncaught. Fixed: wrap `sleep()` in both `switch.py` and `media_player.py` to catch `CannotConnectError` and treat it as success. *(integration)*

- [x] **Tray "Log" menu item opens Power tab instead of Log tab** — Fixed: `ShowTab(4)` now correctly targets the Log tab. *(service)*

- [x] **Steam Big Picture not auto-registered as app entry** — Fixed: bootstrapper runs on every startup and writes both `"steam"` and `"steam-bigpicture"` entries if absent. *(service)*

- [x] **HACS install instructions missing repository step** — Fixed: README updated to include "Add custom repository" step with fenced code block. *(integration)*

- [x] **Info tooltip suppressed on click** — Fixed: `Click` event on `ⓘ` label now calls `ToolTip.Show()` with 3 s duration. *(service)*

- [x] **Games tab PC mode dropdown throws `ThreadStateException`** — Fixed: removed `AutoCompleteSource` from the in-grid `DataGridViewComboBoxColumn`. *(service)*

- [x] **Update check fails with "no such host: api.github.com"** — Fixed: `HttpRequestException` and `SocketException` caught and treated as "no update available". *(service)*

- [x] **Game launch buffers for ~3 minutes** — Fixed: game launch now uses the sustained 20 s WoL retry loop before polling for `steam_ready`. *(integration)*

- [x] **Stop game: be optimistic for 30 s** — Fixed: optimistic playing state held for 30 s after stop command. *(integration)*

---

## v1.3.6

### Features

- [x] **Artwork debug HTML endpoints** — `GET /api/steam/artwork/debug` shows all top-20 games with path resolution diagnostics and inline image previews. `GET /api/steam/artwork/{appId}/debug` shows per-game detail. Color-coded, localhost-only. *(service)*
- [x] **Service reload endpoint** — `POST /api/system/reload` triggers graceful service restart. Tray does in-process Kestrel restart, Headless stops for systemd. *(service)*
- [x] **Auto-update endpoint** — `POST /api/system/update` checks GitHub releases, downloads installer, runs silent update. NoOp on Linux. *(service)*

### Refactors

- [x] **HTML/CSS as embedded resources** — Inline HTML/CSS extracted to `.html`/`.css` files in `Views/` folder, loaded via `EmbeddedResourceHelper`. Endpoints build only dynamic content. *(service)*

---

## Backlog

### 12. Auto-Sleep on Inactivity *(done in v1.3)*

Auto-sleep the PC when it's been idle for a configurable duration. Conditions:
no game running, no mouse/keyboard/gamepad input for X minutes → sleep.

**Service-side:**
- Config: `Power.AutoSleepAfterMinutes` in `appsettings.json` (0 = disabled)
- Monitor loop: checks game state via `ISteamService` + input idle time via
  `GetLastInputInfo` / `loginctl` — if both exceed threshold, trigger sleep
- Power settings tab in tray with timeout slider/input
- `GET/PUT /api/system/power` endpoints to read/write config remotely

**HA integration:**
- Number entity "Auto-Sleep Timeout" to adjust minutes from dashboard
- Complements the post-session sleep blueprint (F4) which handles the
  "game just ended" case — this covers the broader "PC sitting idle" scenario

**Investigation — gamepad input detection:**
The current plan mentions gamepad integration as a prerequisite for detecting user activity. Investigate whether we can instead listen to gamepad connect/disconnect events (e.g. Windows `RawInput`/`XInput` device arrival, or Linux `udev` events / `/dev/input` monitoring) to infer activity without full gamepad integration. If connect/disconnect events are reliable enough (gamepad turns off when user is done), this could replace or supplement the `GetLastInputInfo` idle check without needing to read axis/button state.

```json
"Power": {
  "AutoSleepAfterMinutes": 30
}
```

- [x] Service: inactivity monitor loop + `Power.AutoSleepAfterMinutes` config *(service)*
- [x] Service: Power settings tab in tray *(service)*
- [x] Service: `GET/PUT /api/system/power` endpoints *(service)*
- [x] Integration: number entity for auto-sleep timeout *(integration)*

### 13. Help Tooltips for All UI Elements *(done in v1.3)*

Add contextual help to every setting in the tray app. Each field gets a `ToolTip`
with a small "ⓘ" icon label explaining what the setting does.

- [x] Service: add `ToolTip` component + help icons to Modes tab *(service)*
- [x] Service: add help icons to Games tab *(service)*
- [x] Service: add help icons to General tab *(service)*
- [x] Service: add help icons to Power tab *(service)*

---

### 19. Integration Brand Icons *(done in v1.3)*

HA 2026.3 supports bundling brand images directly in the custom integration — no external CDN or separate brands repo required. Local files take priority over CDN automatically.

`brand/` directory added under `custom_components/pc_remote/` with `icon.png` and `dark_icon.png`. Logo variants not included (not required by HA).

- [x] Integration: create `brand/` folder with `icon.png` and `dark_icon.png` *(integration)*

---

### 18. Steam Logo as Media Player Artwork When Idle *(done in v1.3)*

Shows the Steam icon (square, 960×960) when idle with no game running. Fetched once from
Wikimedia CDN on first request, then served from a module-level in-memory cache for the
lifetime of the HA session. Image is always proxied through HA (`media_image_remotely_accessible = False`).

- [x] Integration: `media_image_url` returns Wikimedia Steam icon URL when idle *(integration)*
- [x] Integration: `async_get_media_image()` overridden — returns cached bytes when idle, delegates to `super()` for game artwork *(integration)*

---

### 17. App Key Autocomplete for LaunchApp / KillApp in Modes Tab *(done in v1.3)*

`_launchAppCombo` and `_killAppCombo` now use `DropDownStyle.DropDown` with `AutoCompleteMode.SuggestAppend` — type a custom app key or pick from suggestions.

- [x] Service: change `_launchAppCombo` and `_killAppCombo` to `DropDownStyle.DropDown` with autocomplete *(service)*
- [x] Service: populate autocomplete source from `Apps` keys + well-known built-ins *(service)*
- [x] Service: update save logic to read `.Text` when `SelectedItem` is null (free-text path) *(service)*

---

### 16. Immediate Row Creation on "Add New" in PC Mode UI *(done in v1.3)*

When clicking New in the Modes tab, a blank placeholder row is immediately inserted and selected. Clicking Delete on the placeholder discards it without saving.

- [x] Service: Add inserts a blank placeholder row and selects it immediately *(service)*
- [x] Service: Form fields clear and bind to the new row on creation *(service)*
- [x] Service: Delete removes the uncommitted placeholder row *(service)*

---

### 15. Apply Button for Settings UI *(done in v1.3)*

Replace auto-save on change with an explicit Apply button. Settings are staged in memory and only written to disk when Apply is clicked. Discard/Cancel reverts unsaved changes.

- [x] Service: add Apply and Cancel buttons to each settings tab *(service)*
- [x] Service: defer config writes until Apply is clicked *(service)*
- [x] Service: Cancel/discard reloads current config from disk and resets form fields *(service)*

---

### Double-Click Tray Icon Opens Settings *(done in v1.3)*

- [x] Service: handle `NotifyIcon.DoubleClick` event and open the settings form *(service)*

---

### 20. Start Menu Shortcut *(done in v1.3.1)*

Add a Start Menu shortcut to the Inno Setup installer so users can find and launch the tray app by searching in the Start menu. Previously the app only auto-started via the startup folder with no searchable entry.

- [x] Service: add `[Icons]` entry to installer script creating a Start Menu shortcut for `HaPcRemote.Tray.exe` *(service)*

---

### Other

- [ ] Verify Linux headless daemon + systemd user service end-to-end *(service)*
