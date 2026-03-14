using System;
using System.Runtime.InteropServices;
using System.Text;
using RcConnector.Core;
using Timer = System.Threading.Timer;

namespace RcConnector.Transport
{
    /// <summary>
    /// USB Joystick transport: reads HID gamepad axes via winmm.dll,
    /// converts to RC channel format "RC 1500,1500,...\n" compatible with RcParser.
    /// Channel mapping is configurable via JoystickMapping.
    /// Unmapped channels send 0 (MAVLink passthrough).
    /// </summary>
    internal sealed class JoystickTransport : ITransport
    {
        private const int DEFAULT_POLL_MS = 100; // 10 Hz default
        private const int DATA_TIMEOUT_MS = 3000;
        private const int NUM_CHANNELS = 16;
        private const int CENTER_PWM = 1500;
        private const int MIN_PWM = 1000;
        private const int MAX_PWM = 2000;
        private const double DEADZONE = 0.05; // 5% deadzone around center

        private readonly int _deviceId;
        private readonly string _deviceName;
        private readonly int _pollIntervalMs;
        private readonly JoystickMapping _mapping;
        private const int RECONNECT_INTERVAL_MS = 2000;

        private Timer? _pollTimer;
        private Timer? _watchdog;
        private Timer? _reconnectTimer;
        private DateTime _lastDataTime = DateTime.MinValue;
        private bool _connected;
        private bool _shouldReconnect;

        public string DisplayName => _deviceName;
        public bool IsConnected => _connected;

        public event Action<string>? DataReceived;
        public event Action<string>? Disconnected;

        public JoystickTransport(int deviceId, string deviceName, int pollIntervalMs = DEFAULT_POLL_MS, JoystickMapping? mapping = null)
        {
            _deviceId = deviceId;
            _deviceName = deviceName;
            _pollIntervalMs = Math.Clamp(pollIntervalMs, 20, 1000); // 1-50 Hz
            _mapping = mapping ?? new JoystickMapping();
        }

        public void Connect()
        {
            _shouldReconnect = true;
            TryOpen();

            // Reconnect timer — retries if device disconnected
            _reconnectTimer = new Timer(ReconnectCallback, null, RECONNECT_INTERVAL_MS, RECONNECT_INTERVAL_MS);
        }

        public void Disconnect()
        {
            _shouldReconnect = false;
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            CloseInternal();
            Console.WriteLine($"[JOY] Disconnected {_deviceName}");
        }

        public void Dispose()
        {
            _shouldReconnect = false;
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            CloseInternal();
        }

        private bool TryOpen()
        {
            if (_connected)
                return true;

            CloseInternal();

            // Verify joystick is available
            var caps = new JOYCAPS();
            uint result = joyGetDevCapsW((uint)_deviceId, ref caps, (uint)Marshal.SizeOf<JOYCAPS>());
            if (result != 0)
            {
                Console.WriteLine($"[JOY] Device {_deviceId} not available (error {result})");
                return false;
            }

            _connected = true;
            _lastDataTime = DateTime.UtcNow;

            _pollTimer = new Timer(PollCallback, null, 0, _pollIntervalMs);
            _watchdog = new Timer(WatchdogCallback, null, DATA_TIMEOUT_MS, DATA_TIMEOUT_MS);

            Console.WriteLine($"[JOY] Connected to {_deviceName} (id={_deviceId}, poll={_pollIntervalMs}ms)");
            return true;
        }

        private void ReconnectCallback(object? state)
        {
            if (!_shouldReconnect || _connected)
                return;

            Console.WriteLine($"[JOY] Reconnecting {_deviceName}...");
            TryOpen();
        }

        private void PollCallback(object? state)
        {
            if (!_connected)
                return;

            var info = new JOYINFOEX
            {
                dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                dwFlags = JOY_RETURNALL
            };

            uint result = joyGetPosEx((uint)_deviceId, ref info);
            if (result != 0)
            {
                // Joystick disconnected or error
                Console.WriteLine($"[JOY] Read error {result}, disconnecting");
                CloseInternal();
                Disconnected?.Invoke($"Joystick {_deviceName} disconnected");
                return;
            }

            _lastDataTime = DateTime.UtcNow;

            // Get axis ranges from caps
            var caps = new JOYCAPS();
            joyGetDevCapsW((uint)_deviceId, ref caps, (uint)Marshal.SizeOf<JOYCAPS>());

            // Axis values and ranges indexed by JoystickAxis enum
            uint[] axVals = { info.dwXpos, info.dwYpos, info.dwZpos, info.dwRpos, info.dwUpos, info.dwVpos };
            uint[] axMins = { caps.wXmin, caps.wYmin, caps.wZmin, caps.wRmin, caps.wUmin, caps.wVmin };
            uint[] axMaxs = { caps.wXmax, caps.wYmax, caps.wZmax, caps.wRmax, caps.wUmax, caps.wVmax };

            // Apply channel mapping
            var channels = new int[NUM_CHANNELS];
            int mapCount = Math.Min(_mapping.Channels.Length, JoystickMapping.NUM_MAPPED_CHANNELS);
            for (int i = 0; i < mapCount; i++)
            {
                var ch = _mapping.Channels[i];
                switch (ch.SourceType)
                {
                    case ChannelSourceType.Axis:
                        int ax = (int)ch.Axis;
                        uint val = axVals[ax];
                        if (ch.Invert)
                            val = axMaxs[ax] - (val - axMins[ax]);
                        channels[i] = AxisToPwm(val, axMins[ax], axMaxs[ax]);
                        break;

                    case ChannelSourceType.ButtonGroup:
                        channels[i] = JoystickMapping.ButtonGroupToPwm(ch.Buttons, info.dwButtons);
                        break;

                    default: // None — passthrough
                        channels[i] = 0;
                        break;
                }
            }

            // Channels beyond mapping = 0 (passthrough)
            for (int i = mapCount; i < NUM_CHANNELS; i++)
                channels[i] = 0;

            // Format as "RC 1500,1500,...\n" — same as ESP32
            var sb = new StringBuilder("RC ");
            for (int i = 0; i < NUM_CHANNELS; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(channels[i]);
            }
            sb.Append('\n');

            DataReceived?.Invoke(sb.ToString());
        }

        /// <summary>
        /// Convert raw axis value to PWM (1000-2000) with deadzone around center.
        /// </summary>
        private static int AxisToPwm(uint value, uint min, uint max)
        {
            if (max <= min)
                return CENTER_PWM;

            // Normalize to 0.0 - 1.0
            double norm = (double)(value - min) / (max - min);

            // Apply deadzone around center (0.5)
            double centered = norm - 0.5;
            if (Math.Abs(centered) < DEADZONE)
                centered = 0;
            else
                centered = centered > 0
                    ? (centered - DEADZONE) / (0.5 - DEADZONE)
                    : (centered + DEADZONE) / (0.5 - DEADZONE);

            // Scale to PWM range
            int pwm = CENTER_PWM + (int)(centered * (MAX_PWM - MIN_PWM) / 2.0);
            return Math.Clamp(pwm, MIN_PWM, MAX_PWM);
        }

        private void WatchdogCallback(object? state)
        {
            if (!_connected)
                return;

            if (_lastDataTime != DateTime.MinValue)
            {
                var idle = (DateTime.UtcNow - _lastDataTime).TotalMilliseconds;
                if (idle > DATA_TIMEOUT_MS)
                {
                    Console.WriteLine($"[JOY] Data timeout ({(int)idle}ms)");
                    CloseInternal();
                    Disconnected?.Invoke($"Data timeout on {_deviceName}");
                }
            }
        }

        private void CloseInternal()
        {
            _connected = false;

            _pollTimer?.Dispose();
            _pollTimer = null;
            _watchdog?.Dispose();
            _watchdog = null;
        }

        // ---------------------------------------------------------------
        // Static helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// List available joystick devices (id + name).
        /// </summary>
        public static (int Id, string Name)[] ListDevices()
        {
            uint numDevs = joyGetNumDevs();
            var result = new System.Collections.Generic.List<(int, string)>();

            for (uint i = 0; i < numDevs; i++)
            {
                var caps = new JOYCAPS();
                if (joyGetDevCapsW(i, ref caps, (uint)Marshal.SizeOf<JOYCAPS>()) == 0)
                {
                    // Verify device is actually connected by trying to read it
                    var info = new JOYINFOEX
                    {
                        dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                        dwFlags = JOY_RETURNALL
                    };
                    if (joyGetPosEx(i, ref info) == 0)
                    {
                        string name = caps.szPname?.TrimEnd('\0') ?? $"Joystick {i}";
                        result.Add(((int)i, name));
                    }
                }
            }

            return result.ToArray();
        }

        // ---------------------------------------------------------------
        // winmm.dll P/Invoke (internal — shared with JoystickMappingForm)
        // ---------------------------------------------------------------

        private const uint JOY_RETURNALL = 0xFF;

        [DllImport("winmm.dll")]
        private static extern uint joyGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern uint joyGetDevCapsW(uint uJoyID, ref JOYCAPS pjc, uint cbjc);

        [DllImport("winmm.dll")]
        private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPS
        {
            public ushort wMid;
            public ushort wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint wXmin;
            public uint wXmax;
            public uint wYmin;
            public uint wYmax;
            public uint wZmin;
            public uint wZmax;
            public uint wNumButtons;
            public uint wPeriodMin;
            public uint wPeriodMax;
            public uint wRmin;
            public uint wRmax;
            public uint wUmin;
            public uint wUmax;
            public uint wVmin;
            public uint wVmax;
            public uint wCaps;
            public uint wMaxAxes;
            public uint wNumAxes;
            public uint wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwXpos;
            public uint dwYpos;
            public uint dwZpos;
            public uint dwRpos;
            public uint dwUpos;
            public uint dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }

        // ---------------------------------------------------------------
        // Public P/Invoke wrappers for JoystickMappingForm live preview
        // ---------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        internal struct JOYINFOEX_Public
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwXpos;
            public uint dwYpos;
            public uint dwZpos;
            public uint dwRpos;
            public uint dwUpos;
            public uint dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct JOYCAPS_Public
        {
            public ushort wMid;
            public ushort wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint wXmin;
            public uint wXmax;
            public uint wYmin;
            public uint wYmax;
            public uint wZmin;
            public uint wZmax;
            public uint wNumButtons;
            public uint wPeriodMin;
            public uint wPeriodMax;
            public uint wRmin;
            public uint wRmax;
            public uint wUmin;
            public uint wUmax;
            public uint wVmin;
            public uint wVmax;
            public uint wCaps;
            public uint wMaxAxes;
            public uint wNumAxes;
            public uint wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        [DllImport("winmm.dll", EntryPoint = "joyGetPosEx")]
        internal static extern uint JoyGetPosEx(uint uJoyID, ref JOYINFOEX_Public pji);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
        internal static extern uint JoyGetDevCaps(uint uJoyID, ref JOYCAPS_Public pjc, uint cbjc);
    }
}
