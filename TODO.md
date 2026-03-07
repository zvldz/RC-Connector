# RC-Connector — TODO

## Current State

Windows tray app: ESP32 (Serial/BLE/UDP) → MAVLink RC_CHANNELS_OVERRIDE → Drone.
C# / .NET 8 / WinForms. Localization: EN + UK.

All core features implemented:
- Serial, BLE, UDP, Joystick transports
- MAVLink heartbeat + RC override
- Tray icon with color-coded status
- MainForm: channel bars, log, about, connect toolbar
- SettingsForm: MAVLink/UI settings
- JoystickMappingForm: 8-channel mapping (axis/buttons), live PWM preview
- DPI scaling (AutoScaleMode.Font)
- NSIS installer, GitHub Actions release workflow

## TODO

### High Priority
- [ ] SignPath.io — бесплатный code signing для OSS. Подать заявку, интегрировать в GitHub Actions. Убирает SmartScreen предупреждения при скачивании/установке.

### Normal
- [ ] Real-world testing with ESP32 hardware and drone
- [ ] Joystick: POV hat support (dwPOV → channel mapping)
- [ ] Joystick: per-device mapping profiles (different mapping for different joysticks)

### Done
- [x] USB Joystick transport (winmm.dll, configurable poll rate, hot-plug detection)
- [x] Joystick channel mapping (8 CH, axis/buttons, invert, live PWM preview)
- [x] Status badges: separate transport name + Hz badges, flicker-free
- [x] Test UI on Full HD (100% DPI)
- [x] Auto-update check via GitHub releases API
- [x] Dark/light theme support (Theme.cs)
- [x] Theme selector in Settings (Auto/Light/Dark) — restart to apply
- [x] Dark theme: BorderlessTabControl, FixedSingle border, DWM dark titlebar
- [x] NSIS installer: multilingual (EN/UK), MUI Finish page (launch + startup checkboxes)
- [x] Stable installer filename for direct download link
- [x] RunAtStartup synced with registry (installer + app)
- [x] Status badges: armed/disarmed, flight mode, no telemetry, no RC data
- [x] Both Connect + Disconnect buttons visible when connected
- [x] BLE first in connect menus (tray + MainForm)
- [x] Tray icon: OrangeRed for no data, gray/green split for disconnected+drone
- [x] Console UTF-8 output for VS Code terminal

## Key Files

| File | Role |
|------|------|
| TrayApp.cs | Tray icon, context menu, connect/disconnect logic |
| MainForm.cs | Channel bars, status, log, connect toolbar |
| SettingsForm.cs | MAVLink port/sysid, UDP port, joystick rate, language |
| JoystickMappingForm.cs | Joystick channel mapping editor, live PWM preview |
| Transport/*.cs | Serial (DTR fix), BLE (NUS), UDP, Joystick (winmm) |
| Core/JoystickMapping.cs | Channel mapping model (axis/buttons/invert) |
| MAVLink/MavlinkService.cs | Heartbeat + RC override sender/receiver |
| Core/Localization.cs | EN/UK strings via L.Get("key") |
| Core/AppSettings.cs | JSON settings in %LocalAppData%/RC-Connector |

## Reference

- ESP32 firmware: `D:\devel\perun\build\TX16S-RC\src\main.cpp`
- MP BLE plugin: `D:\devel\perun\build\ESP32-UART-Bridge\plugins\RcOverride_v2_BLE.cs`
