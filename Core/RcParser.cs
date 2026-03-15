using System;
using System.Text;

namespace RcConnector.Core
{
    /// <summary>
    /// Parses RC channel data from serial stream into 16-channel PWM arrays.
    /// Supports two formats:
    ///   ESP-Bridge: "RC 1500,1500,...\n" (16 channels, PWM 1000-2000)
    ///   R2D2:       "$-512,1024,...,\r\n" (up to 24 channels, raw -1024..+1024)
    /// Auto mode detects format by prefix ($ vs RC).
    /// </summary>
    internal sealed class RcParser
    {
        private const int BUFFER_MAX_SIZE = 4096;
        private const int CHANNEL_COUNT = 16;
        private const ushort PWM_MIN = 800;
        private const ushort PWM_MAX = 2200;
        private const int UNKNOWN_FORMAT_THRESHOLD = 4096;

        private readonly StringBuilder _buffer = new(256);
        private readonly object _lock = new();

        private int _unparsedBytes;
        private bool _formatLogged;

        /// <summary>Serial data format. Can be changed at runtime via Settings.</summary>
        public SerialFormat Format { get; set; } = SerialFormat.Auto;

        /// <summary>Fired when a valid RC line is parsed. Provides 16-channel PWM array.</summary>
        public event Action<ushort[]>? OnRcData;

        /// <summary>Fired when incoming data doesn't match any known format.</summary>
        public event Action? OnUnknownFormat;

        /// <summary>Fired once when format is detected or confirmed. Arg: format name string.</summary>
        public event Action<string>? OnFormatDetected;

        /// <summary>
        /// Append incoming data from transport (serial, BLE, UDP).
        /// Thread-safe — called from transport callbacks.
        /// </summary>
        public void Feed(string data)
        {
            if (string.IsNullOrEmpty(data))
                return;

            lock (_lock)
            {
                _buffer.Append(data);

                if (_buffer.Length > BUFFER_MAX_SIZE)
                    _buffer.Clear();
            }
        }

        /// <summary>
        /// Process buffered data, extract complete lines, parse RC values.
        /// Call from main timer thread.
        /// </summary>
        public void ProcessBuffer()
        {
            string bufStr;
            lock (_lock)
            {
                if (_buffer.Length == 0)
                    return;
                bufStr = _buffer.ToString();
                _buffer.Clear();
            }

            int newline;
            while ((newline = bufStr.IndexOf('\n')) >= 0)
            {
                string line = bufStr.Substring(0, newline).Trim('\r', '\n', ' ');
                bufStr = bufStr.Substring(newline + 1);

                ushort[]? rc = ParseLine(line);
                if (rc != null)
                {
                    _unparsedBytes = 0;
                    OnRcData?.Invoke(rc);
                }
                else if (line.Length > 0)
                {
                    _unparsedBytes += line.Length;
                    if (_unparsedBytes >= UNKNOWN_FORMAT_THRESHOLD)
                    {
                        OnUnknownFormat?.Invoke();
                        _unparsedBytes = 0;
                    }
                }
            }

            // Put remaining incomplete data back
            if (bufStr.Length > 0)
            {
                lock (_lock)
                {
                    _buffer.Insert(0, bufStr);
                }
            }
        }

        /// <summary>Reset state (call on disconnect/reconnect).</summary>
        public void Reset()
        {
            _unparsedBytes = 0;
            _formatLogged = false;
            lock (_lock) { _buffer.Clear(); }
        }

        /// <summary>
        /// Parse a single line according to current Format setting.
        /// Returns 16-element PWM array or null.
        /// </summary>
        private ushort[]? ParseLine(string line)
        {
            ushort[]? rc;
            string? detectedFormat = null;

            switch (Format)
            {
                case SerialFormat.EspBridge:
                    rc = ParseEspBridge(line);
                    if (rc != null) detectedFormat = "ESP-Bridge";
                    break;
                case SerialFormat.R2D2:
                    rc = ParseR2D2(line);
                    if (rc != null) detectedFormat = "R2D2";
                    break;
                default: // Auto — try both, ESP-Bridge first (more common)
                    rc = ParseEspBridge(line);
                    if (rc != null) { detectedFormat = "ESP-Bridge (auto)"; break; }
                    rc = ParseR2D2(line);
                    if (rc != null) detectedFormat = "R2D2 (auto)";
                    break;
            }

            if (rc != null && !_formatLogged)
            {
                _formatLogged = true;
                OnFormatDetected?.Invoke(detectedFormat!);
            }

            return rc;
        }

        /// <summary>
        /// Parse ESP-Bridge format: "RC 1500,1500,..." (16+ PWM values).
        /// Accepts noise before "RC" prefix.
        /// </summary>
        public static ushort[]? ParseEspBridge(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;

            try
            {
                int start = line.IndexOf("RC", StringComparison.Ordinal);
                if (start < 0)
                    return null;

                string raw = line.Substring(start);
                if (!raw.StartsWith("RC"))
                    return null;

                string body = raw.Substring(2).TrimStart(',', ' ');

                int newlineIdx = body.IndexOfAny(new[] { '\r', '\n' });
                if (newlineIdx >= 0)
                    body = body.Substring(0, newlineIdx);

                string[] parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < CHANNEL_COUNT)
                    return null;

                ushort[] rc = new ushort[CHANNEL_COUNT];
                for (int i = 0; i < CHANNEL_COUNT; i++)
                {
                    if (!ushort.TryParse(parts[i].Trim(), out rc[i]))
                        return null;

                    if (rc[i] < PWM_MIN) rc[i] = PWM_MIN;
                    if (rc[i] > PWM_MAX) rc[i] = PWM_MAX;
                }

                return rc;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Parse R2D2 format: "$val,val,...," (raw -1024..+1024, variable channel count).
        /// Converts to PWM: (raw / 2) + 1500, clamped 1000-2000.
        /// Pads to 16 channels with 0 (passthrough).
        /// </summary>
        public static ushort[]? ParseR2D2(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;

            try
            {
                int start = line.IndexOf('$');
                if (start < 0)
                    return null;

                string body = line.Substring(start + 1).TrimEnd(',', ' ');

                int newlineIdx = body.IndexOfAny(new[] { '\r', '\n' });
                if (newlineIdx >= 0)
                    body = body.Substring(0, newlineIdx);

                string[] parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) // at least 4 channels for valid data
                    return null;

                ushort[] rc = new ushort[CHANNEL_COUNT];
                int count = Math.Min(parts.Length, CHANNEL_COUNT);

                for (int i = 0; i < count; i++)
                {
                    if (!int.TryParse(parts[i].Trim(), out int raw))
                        return null;

                    // Convert raw (-1024..+1024) to PWM (1000-2000)
                    int pwm = (raw / 2) + 1500;
                    rc[i] = (ushort)Math.Clamp(pwm, 1000, 2000);
                }

                // Channels beyond input = 0 (passthrough)
                return rc;
            }
            catch
            {
                return null;
            }
        }
    }
}
