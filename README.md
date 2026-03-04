# RC-Connector

Standalone Windows tray application that bridges an RC transmitter (via ESP32) to an ArduPilot drone via MAVLink RC_CHANNELS_OVERRIDE.

Replaces the need for RC Override plugins in Mission Planner or QGroundControl.

## Architecture

```
TX16S Radio ──CRSF──> ESP32 ──Serial/BLE/WiFi──> [RC-Connector] ──UDP MAVLink──> Drone
                                                        │
                                                        ├── Tray icon (color-coded status)
                                                        └── Mini window (channels, log)
```

## Features

- **Three transport sources**: Serial (COM), BLE (Nordic UART Service), UDP (WiFi)
- **MAVLink output**: HEARTBEAT + RC_CHANNELS_OVERRIDE (16 channels) via UDP
- **Passive mode**: listens for drone telemetry, replies to sender address
- **Tray icon** with color-coded status: gray/red/orange/green
- **Mini window**: 16 channel bars with real-time values, flight mode, armed status
- **Cascading Connect menu**: select transport and device directly from tray
- **BLE auto-scan** at startup with in-menu refresh
- **DPI-adaptive** UI scaling
- **Settings persistence** (JSON)

## Requirements

- Windows 10+ (x64)
- .NET 8 Runtime (or use self-contained build)
- ESP32 with compatible firmware sending `RC 1500,1500,...\n` format

## Build

```bash
# Development
dotnet build
dotnet run

# Self-contained single-file exe (~75 MB)
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

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

## License

All rights reserved.
