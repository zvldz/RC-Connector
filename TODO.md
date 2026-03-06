# RC-Connector — TODO

## Current State

Windows tray app: ESP32 (Serial/BLE/UDP) → MAVLink RC_CHANNELS_OVERRIDE → Drone.
C# / .NET 8 / WinForms. Localization: EN + UK.

All core features implemented:
- Serial, BLE, UDP transports
- MAVLink heartbeat + RC override
- Tray icon with color-coded status
- MainForm: channel bars, log, about, connect toolbar
- SettingsForm: MAVLink/UI settings
- DPI scaling (AutoScaleMode.Font)
- NSIS installer, GitHub Actions release workflow

## TODO

### High Priority
- [ ] SignPath.io — бесплатный code signing для OSS. Подать заявку, интегрировать в GitHub Actions. Убирает SmartScreen предупреждения при скачивании/установке.

### Normal
- [ ] USB Joystick transport — пульт по USB как геймпад (winmm.dll joyGetPosEx), оси → RC каналы 1000-2000. Без ESP32, напрямую с компа.
  - P/Invoke: joyGetNumDevs, joyGetDevCapsW, joyGetPosEx
  - 6 осей (X/Y/Z/R/U/V) → CH1-CH6, каналы 7-8 = 1500 (центр). Фиксированный маппинг для MVP.
  - Polling rate: 10Hz (100ms) — совпадает с типичным ESP32 rate, не перегружает MAVLink канал.
  - Rate limit: предусмотреть настраиваемый интервал (10-50Hz), по умолчанию 10Hz.
  - Deadzone ~5% около центра осей (шум джойстика).
  - Hot-plug: joyGetPosEx возвращает ошибку при отключении → обрабатываем как disconnect.
  - Каналы 9-16 = 1500 (центр) для совместимости с 16-канальным форматом.
  - Потом: настраиваемый маппинг осей → каналов в Settings.
- [ ] Real-world testing with ESP32 hardware and drone

### Done
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
| SettingsForm.cs | MAVLink port/sysid, UDP port, DPI, language |
| Transport/*.cs | Serial (DTR fix), BLE (NUS), UDP |
| MAVLink/MavlinkService.cs | Heartbeat + RC override sender/receiver |
| Core/Localization.cs | EN/UK strings via L.Get("key") |
| Core/AppSettings.cs | JSON settings in %LocalAppData%/RC-Connector |

## Reference

- ESP32 firmware: `D:\devel\perun\build\TX16S-RC\src\main.cpp`
- MP BLE plugin: `D:\devel\perun\build\ESP32-UART-Bridge\plugins\RcOverride_v2_BLE.cs`
