using System;
using System.Text;

namespace RcConnector.Core
{
    /// <summary>
    /// Parses "RC 1500,1500,1000,...\n" lines from ESP32 into 16-channel PWM arrays.
    /// Logic ported from RcOverride_v2_BLE.cs plugin.
    /// </summary>
    internal sealed class RcParser
    {
        private const int BUFFER_MAX_SIZE = 4096;
        private const int CHANNEL_COUNT = 16;
        private const ushort PWM_MIN = 800;
        private const ushort PWM_MAX = 2200;

        private readonly StringBuilder _buffer = new(256);
        private readonly object _lock = new();

        /// <summary>
        /// Fired when a valid RC line is parsed. Provides 16-channel PWM array.
        /// </summary>
        public event Action<ushort[]>? OnRcData;

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

                ushort[]? rc = ParseRcLine(line);
                if (rc != null)
                    OnRcData?.Invoke(rc);
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

        /// <summary>
        /// Parse a single RC line. Returns 16-element array or null.
        /// Accepts: "RC 1500,1500,..." / "RC,1500,..." / noise before RC.
        /// </summary>
        public static ushort[]? ParseRcLine(string line)
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
    }
}
