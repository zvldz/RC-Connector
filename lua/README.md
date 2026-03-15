# EdgeTX LUA Scripts

LUA scripts for EdgeTX radio transmitters. Send RC channel data via USB Serial (VCP) to RC-Connector.

## Scripts

### r2d2-usb-vcp.lua
Original script by **Apachi Team**. Uses `lcd.*` API for drawing.
Works on all radios, but on color screens (EdgeTX 3.0+) renders in B&W mode.

### r2d2-usb-vcp-tx15.lua
Modified version (mod by Perun Team) with LVGL API for native color UI on EdgeTX 3.0+.
Should work on any color screen radio (TX15, TX16, etc.).
All serial logic unchanged — only drawing replaced.

## Installation

| Radio | Path on SD card |
|-------|----------------|
| TX16S (EdgeTX 2.x) | `/SCRIPTS/TOOLS/` |
| TX15 (EdgeTX 3.0) | `/SCRIPTS/TOOLS/` |

Copy the `.lua` file to the path above. Both scripts can coexist — they have different names in the Tools menu.

## Data format

Sends 24 channels as `$val,val,...,\r\n` where values are raw -1024..+1024 from `getOutputValue()`.
RC-Connector parses this as "R2D2" format and converts to PWM: `(raw / 2) + 1500`, clamped 1000-2000.
