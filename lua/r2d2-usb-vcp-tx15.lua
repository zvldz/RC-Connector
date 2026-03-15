-- TNS|R2D2 USB-VCP (color)|TNE

local AUTHOR = "Apachi Team"
local BUILD_DATETIME = "2025-10-21 12:08"

local CHANNEL_COUNT = 24
local SERIAL_BAUDRATE = 115200
local SERIAL_PREFIX = "$"
local UPDATE_INTERVAL_TICKS = 10
local BATT_VOLT = 0

---------------------------------------------------------
-- State
---------------------------------------------------------
local modelInfo
local mixesInfo = {}
local channelValues = {}
local useLvglUI = false

---------------------------------------------------------
-- Channel & serial functions
---------------------------------------------------------
local function readChannels()
  for i = 1, CHANNEL_COUNT do
    channelValues[i] = getOutputValue(i - 1)
  end
end

local function readMixes()
  for i = 1, CHANNEL_COUNT do
    local m = model.getMix(i - 1, 0)
    local srcName = ""
    if m then
      if getSourceName and m.source then
        local ok, name = pcall(getSourceName, m.source)
        if ok and name then srcName = name end
      end
      local mix = mixesInfo[i] or {}
      mix.name = m.name or ""
      mix.src = srcName
      mixesInfo[i] = mix
    else
      local mix = mixesInfo[i] or {}
      mix.name = ""
      mix.src = ""
      mixesInfo[i] = mix
    end
  end
end

local function sendChannels()
  local data = SERIAL_PREFIX
  for i = 1, #channelValues do
    data = data .. channelValues[i] .. ","
  end
  serialWrite(data .. "\r\n")
end

---------------------------------------------------------
-- Drawing: lcd.* (EdgeTX 2.x, no LVGL)
---------------------------------------------------------
local function drawNothing() end

local function drawSmall()
  lcd.clear()
  lcd.drawText(0, 0, modelInfo.name, SMLSIZE)
  lcd.drawText(55, 1, "R2D2", SMLSIZE)
  lcd.drawText(84, 0, string.format("Batt: %1.1fV", BATT_VOLT), SMLSIZE)

  lcd.drawFilledRectangle(0, 7, 128, 1)
  lcd.drawFilledRectangle(53, 0, 22, 7)

  for i = 1, CHANNEL_COUNT do
    local col = (i - 1) // 8
    local row = (i - 1) % 8
    local x = col * 42
    local y = 9 + row * 7

    lcd.drawText(x, y, string.format("%02d    %s", i, string.sub(mixesInfo[i].src, 3)), SMLSIZE)

    local w = (channelValues[i] + 1024 + 256) // 255 -- means divide by 8
    if w < 1 then w = 1 elseif w > 9 then w = 9 end
    lcd.drawFilledRectangle(x + 11, y, w, 6)
    lcd.drawPoint(x + 11 + 4, y + 2)
  end
end

local function drawLarge()
  local back = lcd.RGB(0, 0, 0)
  local front = lcd.RGB(255, 255, 255)
  local warn = lcd.RGB(255, 165, 0)
  local fTitle = MIDSIZE + front
  local fSmall = SMLSIZE + front
  local fWarn = MIDSIZE + warn

  lcd.clear(back)
  lcd.drawText(10, 10, "R2D2 USB-VCP", fTitle)
  lcd.drawText(240, 10, modelInfo.name, fTitle)
  lcd.drawText(240, 240, "TEST VERSION", fWarn)
  lcd.drawText(10, 240, string.format("RC BATTERY: %.2fV", BATT_VOLT), fTitle)

  for i = 1, CHANNEL_COUNT do
    local v = (channelValues[i] // 2) + 1500
    local w = (channelValues[i] + 1024 + 34) // 34

    local colBase = (i <= 12) and 10 or 240
    local row = (i <= 12) and i or (i - 12)
    local y = 30 + row * 16

    lcd.drawText(colBase, y, string.format("CH%02d:%04d", i, v), fSmall)
    lcd.drawFilledRectangle(colBase, y + 14, w, 2, front)

    local mix = mixesInfo[i]
    if mix then
      lcd.drawText(colBase + 80, y, mix.src, fSmall)
      lcd.drawText(colBase + 140, y, mix.name, fSmall)
    end
  end
end

---------------------------------------------------------
-- Drawing: LVGL (EdgeTX 3.0+)
---------------------------------------------------------
local BAR_MAX_W = 60

local function buildLvglUI()
  local pg = lvgl.page({
    title = "R2D2 USB-VCP",
    subtitle = modelInfo.name,
    back = function() return 2 end,
  })

  -- Battery voltage
  pg:label({x = 0, y = 0, font = MIDSIZE,
    text = function() return string.format("Batt: %.1fV", BATT_VOLT) end,
  })

  for i = 1, CHANNEL_COUNT do
    local colBase = (i <= 12) and 0 or 230
    local row = (i <= 12) and i or (i - 12)
    local yOfs = 20 + row * 22

    -- Channel label: "CH01: 1500 Ail"
    pg:label({x = colBase, y = yOfs, font = SMLSIZE,
      text = function()
        local v = (channelValues[i] // 2) + 1500
        local src = mixesInfo[i] and mixesInfo[i].src or ""
        return string.format("CH%02d:%5d %s", i, v, src)
      end,
    })

    -- Channel bar (dynamic width via size function)
    pg:rectangle({x = colBase, y = yOfs + 14,
      w = BAR_MAX_W, h = 3, filled = true,
      color = COLOR_THEME_PRIMARY1,
      size = (function()
        local w = (channelValues[i] + 1024) * BAR_MAX_W // 2048
        if w < 1 then w = 1 elseif w > BAR_MAX_W then w = BAR_MAX_W end
        return w, 3
      end),
    })
  end
end

---------------------------------------------------------
-- Init & Run
---------------------------------------------------------
local function init()
  modelInfo = model.getInfo() or { name = "Unknown" }
  readMixes()
  setSerialBaudrate(SERIAL_BAUDRATE)

  -- LVGL available on EdgeTX 3.0+ color radios
  if lvgl then
    useLvglUI = true
    buildLvglUI()
  end
end

local function run(event)
  BATT_VOLT = getValue("tx-voltage")

  -- Pre-draw loop
  for i = 1, UPDATE_INTERVAL_TICKS do
    readChannels()
    sendChannels()
  end

  -- Draw via lcd.* only when LVGL is not active
  if not useLvglUI then
    if LCD_W >= 480 and LCD_H >= 272 then
      drawLarge()
    elseif LCD_W == 128 and LCD_H == 64 then
      drawSmall()
    else
      drawNothing()
    end
  end

  -- Post-draw loop
  for i = 1, UPDATE_INTERVAL_TICKS do
    readChannels()
    sendChannels()
  end

  return 0
end

return { run = run, init = init, useLvgl = true }
