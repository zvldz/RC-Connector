# RC-Connector

Standalone Windows tray application that bridges an RC transmitter (via ESP32 or USB joystick) to an ArduPilot drone via MAVLink RC_CHANNELS_OVERRIDE.

Replaces the need for RC Override plugins in Mission Planner or QGroundControl.

## Download

[Latest release (installer)](https://github.com/zvldz/RC-Connector/releases/latest)

## Architecture

```
TX16S Radio ──CRSF──> ESP32 ──Serial/BLE/WiFi──> [RC-Connector] ──UDP MAVLink──> Drone
USB Gamepad/Joystick ─────────────────────────>        │
                                                       ├── UDP forward (RC text) ──> other app
                                                       ├── Tray icon (color-coded status)
                                                       └── Mini window (channels, log)
```

## Features

- **Four transport sources**: Serial (COM), BLE (Nordic UART Service), UDP (WiFi), USB Joystick
- **USB Joystick support**: direct gamepad/joystick input via winmm.dll — no ESP32 needed
- **Joystick channel mapping**: 8 RC channels, each assignable to axis or button group with live PWM preview
- **Button groups**: assign multiple gamepad buttons to one RC channel — PWM positions auto-distributed
- **MAVLink output**: HEARTBEAT + RC_CHANNELS_OVERRIDE (16 channels) via UDP
- **RC forwarding**: forward parsed channels as `RC 1500,1500,...` text via UDP to configurable IP:port
- **Passive mode**: listens for drone telemetry, replies to sender address
- **Tray icon** with color-coded status: gray/red/orange/green
- **Mini window**: 16 channel bars with real-time values, flight mode, armed status
- **Status badges**: transport name, data rate, armed/disarmed, flight mode as colored badges
- **Cascading Connect menu**: select transport and device directly from tray
- **BLE auto-scan** at startup with in-menu refresh
- **Auto-update**: checks GitHub releases, downloads and launches installer
- **Dark/Light theme**: auto-detection or manual selection (Auto/Light/Dark)
- **Localization**: English and Ukrainian (auto-detected from system language)
- **DPI-adaptive** UI scaling
- **Settings persistence** (JSON in %LocalAppData%\RC-Connector)
- **NSIS installer** with multilingual support (EN/UK), auto-close running instance on upgrade

## Requirements

- Windows 10+ (x64)
- ESP32 with compatible firmware sending `RC 1500,1500,...\n` format, **or** any USB joystick/gamepad
- No additional runtime required (self-contained build)

## ESP32 Protocol

The ESP32 sends RC channel data as text lines:

```
RC 1500,1500,1000,1500,1500,1500,1500,1500,1500,1500,1500,1500,1500,1500,1500,1500\n
```

- 16 PWM values (800-2200), comma-separated
- Baud: 115200 (Serial), BLE NUS, or UDP
- Rate: 10-50 Hz (controlled by ESP32)

## MAVLink Setup

RC-Connector operates in **passive mode**:

1. Drone (via Raspberry Pi / MAVProxy) sends telemetry stream to RC-Connector's listen port (default `14555`)
2. RC-Connector replies with HEARTBEAT + RC_CHANNELS_OVERRIDE to the same address
3. Works in parallel with GCS (Mission Planner on port 14550)

**Important**: `SYSID_MYGCS` on the drone must match RC-Connector's System ID (default `255`, configurable in Settings).

## Related

ESP32 firmware: [ESP32-UART-Bridge](https://github.com/zvldz/ESP32-UART-Bridge) — RC transmitter to Serial/BLE/WiFi bridge for use with this app.

## License

MIT License — see [LICENSE](LICENSE) file for details.
