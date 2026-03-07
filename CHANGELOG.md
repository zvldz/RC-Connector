# Changelog

## 0.4.0 — 2026-03-07

### Joystick Transport
- USB joystick/gamepad support via winmm.dll P/Invoke (joyGetPosEx)
- Joystick device selection in tray Connect submenu and MainForm toolbar
- Configurable poll rate (10-50 Hz) in Settings

### Joystick Channel Mapping
- Separate mapping editor window (JoystickMappingForm)
- 8 RC channels, each assignable to: None / Axis (X-V) / Button Group
- Axis invert option per channel
- Button groups: N buttons → N+1 PWM positions (1000-2000), auto-distributed
- Live PWM preview with joystick connected (10 Hz refresh)
- Device selector for multi-joystick setups
- PWM bar visualization with center mark and button position markers
- Defaults button to reset mapping (axes X-V on CH1-6)
- Unmapped channels send 0 (MAVLink passthrough)
- Accessible from tray menu and MainForm gamepad button

### Status Bar
- Transport name and Hz displayed as separate colored badges
- Transport badge: green (connected), orange (no RC data)
- Hz badge: blue, shown only when RC data present
- Flicker-free badge updates (SuspendLayout + conditional property sets)

### UI
- Gamepad button (🎮) in MainForm toolbar, docked right

## 0.3.3 — 2026-03-06

### Settings
- Theme selector (Auto / Light / Dark) with "restart to apply" hint
- Hint "avoid 14550" → "not 14550" for clarity

### UI
- Console UTF-8 output fix for VS Code terminal (Ukrainian text)
- Status badge height: AutoSize panel for proper DPI scaling
- About tab: Author and GitHub rows hidden (commented out)
- Tray menu: show only app name + version (no author)

### Other
- TODO: USB Joystick transport plan (winmm.dll, 6 axes, rate limit)

## 0.3.2 — 2026-03-06

### Status Bar (MainForm)
- Status badges with colored backgrounds for all states: Disconnected, No telemetry, No RC data, ARMED/DISARMED, flight mode
- Transport label AutoSize fix — badges no longer hidden behind long device names
- Telemetry status shown when RC connected but no drone (and vice versa)

### Tray Icon
- Disconnected + drone telemetry present: gray/green split circle
- "Connected, no data" color changed from Orange to OrangeRed
- BLE listed first in Connect submenu (before COM)

### MainForm Toolbar
- Connect and Disconnect buttons both visible when connected (quick device switch)
- Selecting new device auto-disconnects current

### Installer
- Unicode support (fixes Ukrainian text display)
- MUI Finish page with Launch + Startup checkboxes (replaces custom page)

### Settings
- RunAtStartup synced with Windows registry (installer checkbox now reflected in app)

### Other
- Copyright updated to "@zvldz & team" in csproj
- AppInfo comment fix

## 0.3.1 — 2026-03-06

### UI / Theme
- Dark/light theme auto-detection (Theme.cs) — all UI adapts to Windows theme
- Dark title bars via DwmSetWindowAttribute
- Dark context menus with custom renderer
- Owner-draw TabControl headers and ComboBox for dark theme
- Esc closes Settings dialog
- Esc hides MainForm (unless AlwaysOnTop is on)

### Connect Menu
- COM ports refresh on every dropdown open in MainForm
- BLE Refresh button in MainForm connect menu

### Settings
- DTR/RTS hint explaining ESP32 reset behavior (with info icon)
- Warning icons on MAVLink hints, info icons on UDP/DTR hints
- Removed Adaptive DPI checkbox (always auto)

### Installer
- "Launch RC-Connector now" checkbox (on by default)
- Stable installer filename (RC-Connector-Setup.exe) for direct download link

### Other
- Default author: @zvldz & team
- MIT LICENSE file added
- README: license, ESP32-UART-Bridge link, updated features
- IsInstalled detection via Uninstall.exe
- Startup registry uses installed path from HKLM
- Update checker: fixed HttpRequestException on private/404

## 0.2.2 — 2026-03-05

- Multilingual NSIS installer (EN/UK)
- Close running instance dialog before upgrade
- x64 Program Files install path

## 0.2.1 — 2026-03-05

- About tab: GitHub link, version check, log zebra striping
- Auto-update checker via GitHub releases API

## 2026-03-05 — DPI scaling fix + MainForm toolbar

### SettingsForm
- Fixed broken layout at 200% DPI (2880x1800 display)
- Added WinForms auto-scaling: AutoScaleDimensions + AutoScaleMode.Font
- Added SuspendLayout/ResumeLayout for correct scaling
- Reduced base spacing values (framework scales them automatically)
- Reduced button sizes to prevent clipping at high DPI

### MainForm
- Removed manual DPI scaling (DeviceDpi / 96f) — caused double scaling
- Switched to framework auto-scaling (AutoScaleDimensions + AutoScaleMode.Font)
- AdaptiveDpi setting toggles AutoScaleMode (Font vs None)
- Added connect/disconnect toolbar with dropdown menu
- Connect button shows COM/BLE/UDP device submenu
- Disconnect button appears when connected (replaces Connect)
- Tab control: FlatButtons appearance for visible tab separation
- Tab font: Segoe UI 8.5pt Bold for readability
- Status bar: label uses Dock.Fill + MiddleLeft for vertical centering
- Increased LABEL_WIDTH from 22 to 24 for two-digit channel numbers

### Localization
- Added Ukrainian (uk) translation for all UI strings
- Language selector in Settings (Auto / English / Українська)
