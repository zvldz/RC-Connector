# RC-Connector

Standalone Windows tray application that bridges an RC transmitter (via ESP32, LUA script, or USB joystick) to an ArduPilot drone via MAVLink RC_CHANNELS_OVERRIDE.

Replaces the need for RC Override plugins in Mission Planner or QGroundControl.

## Download

[Latest release (installer)](https://github.com/zvldz/RC-Connector/releases/latest)

## Architecture

```
TX Radio ──SBUS/CRSF──> ESP32 ──Serial/BLE/WiFi───┐
TX Radio ──LUA script──> USB Serial (VCP) ────────┤
USB Gamepad/Joystick ─────────────────────────────┤
                                                  ▼
                                           [RC-Connector]
                                                  │
                    ┌─────────────┬────────────────┼────────────────┐
                    ▼             ▼                ▼                ▼
          UDP MAVLink ──> Drone  WebRTC DC   UDP forward    Tray icon + window
          (direct mode)       ──> Server     ──> app
                                  │
                            UDP localhost:14550
                                  │
                            Mission Planner
```

## Features

- **Four transport sources**: Serial (COM), BLE (Nordic UART Service), UDP (WiFi), USB Joystick
- **Dual data format**: ESP-Bridge (`RC 1500,...`) and R2D2 (`$val,...`) with auto-detection
- **USB Joystick support**: direct gamepad/joystick input via winmm.dll — no ESP32 needed
- **LUA scripts** for EdgeTX radios: send RC channels via USB Serial (VCP) directly from transmitter
- **Joystick channel mapping**: 8 RC channels, each assignable to axis or button group with live PWM preview
- **Button groups**: assign multiple gamepad buttons to one RC channel — PWM positions auto-distributed
- **Two telemetry modes**: Direct UDP (classic MAVLink) or WebRTC DataChannel (no VPN needed)
- **WebRTC bridge**: browser-based signaling, binary MAVLink via DataChannel, auto-forward to GCS (Mission Planner)
- **MAVLink output**: HEARTBEAT + RC_CHANNELS_OVERRIDE (16 channels) via UDP or DataChannel
- **RC forwarding**: forward parsed channels via UDP in ESP-Bridge or R2D2 format (auto/manual)
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
- ESP32 with compatible firmware, **or** EdgeTX radio with LUA script (USB VCP), **or** any USB joystick/gamepad
- No additional runtime required (self-contained build)

## Data Formats

RC-Connector supports two input formats (auto-detected by prefix, configurable in Settings):

### ESP-Bridge format
```
RC 1500,1500,1000,1500,1500,1500,1500,1500,1500,1500,1500,1500,1500,1500,1500,1500\n
```
- 16 PWM values (800-2200), comma-separated
- Used by ESP32 firmware ([ESP32-UART-Bridge](https://github.com/zvldz/ESP32-UART-Bridge))

### R2D2 format
```
$val,val,val,...,\r\n
```
- 24 raw values (-1024..+1024) from `getOutputValue()`
- Converted to PWM: `(raw / 2) + 1500`, clamped 1000-2000
- Used by EdgeTX LUA scripts (see `lua/` folder)

### Transport
- Serial: 115200 baud
- BLE: Nordic UART Service (NUS)
- UDP: WiFi
- Output rate throttled to configured send rate (10-50 Hz, default 20)

## MAVLink Setup

RC-Connector operates in **passive mode**:

1. Drone (via Raspberry Pi / MAVProxy) sends telemetry stream to RC-Connector's listen port (default `14555`)
2. RC-Connector replies with HEARTBEAT + RC_CHANNELS_OVERRIDE to the same address
3. Works in parallel with GCS (Mission Planner on port 14550)

**Important**: `SYSID_MYGCS` on the drone must match RC-Connector's System ID (default `255`, configurable in Settings).

## WebRTC Mode

In WebRTC mode, RC-Connector receives MAVLink telemetry via DataChannel instead of direct UDP — no VPN required for the operator.

```
Browser (authorized) ←wss→ Server (mavlink_v2.py)
Browser ←ws://localhost:9999→ RC-Connector (signaling)

After handshake:
Server ←── DataChannel "mavlink-binary" ──→ RC-Connector ──UDP──→ Mission Planner
```

1. RC-Connector listens on `ws://localhost:9999` for signaling
2. Browser proxies WebRTC offer/answer/ICE between RC-Connector and server
3. DataChannel established directly (UDP, encrypted via DTLS)
4. All MAVLink packets forwarded to GCS on `localhost:14550`
5. GCS commands forwarded back through DataChannel to drone
6. Browser can be closed — DataChannel persists

Select **WebRTC** telemetry mode in Settings to enable.

## LUA Scripts

EdgeTX LUA scripts for sending RC channels via USB Serial (VCP) directly from the transmitter — no ESP32 needed.

| Script | Description |
|--------|-------------|
| `r2d2-usb-vcp.lua` | Original by Apachi Team. Works on all radios (B&W on color screens) |
| `r2d2-usb-vcp-tx15.lua` | Color mod with LVGL API for EdgeTX 3.0+ (dynamic channel bars) |

Copy to `/SCRIPTS/TOOLS/` on the radio's SD card. See [`lua/README.md`](lua/README.md) for details.

## Related

- ESP32 firmware: [ESP32-UART-Bridge](https://github.com/zvldz/ESP32-UART-Bridge) — RC transmitter to Serial/BLE/WiFi bridge for use with this app

## License

MIT License — see [LICENSE](LICENSE) file for details.
