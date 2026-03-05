using System;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Timer = System.Threading.Timer;

namespace RcConnector.Transport
{
    /// <summary>
    /// Serial COM port transport with ESP32 DTR/RTS fix.
    /// Ported from RcOverride_v2_BLE.cs EnsurePort() / ClosePort().
    /// </summary>
    internal sealed class SerialTransport : ITransport
    {
        private const int BAUD = 115200;
        private const int DATA_TIMEOUT_MS = 3000;
        private const int PORT_RETRY_INTERVAL_MS = 2000;

        private SerialPort? _port;
        private readonly string _portName;
        private readonly bool _dtrRtsFix;
        private DateTime _lastDataTime = DateTime.MinValue;
        private Timer? _watchdog;

        public string DisplayName => _portName;
        public bool IsConnected => _port != null && _port.IsOpen;

        public event Action<string>? DataReceived;
        public event Action<string>? Disconnected;

        public SerialTransport(string portName, bool dtrRtsFix = true)
        {
            _portName = portName;
            _dtrRtsFix = dtrRtsFix;
        }

        public void Connect()
        {
            if (IsConnected)
                return;

            try
            {
                CloseInternal();

                _port = new SerialPort(_portName, BAUD, Parity.None, 8, StopBits.One);
                _port.ReadTimeout = 100;

                if (_dtrRtsFix)
                {
                    // ESP32-C3/S3 DTR/RTS fix: prevent reset on port open
                    _port.DtrEnable = false;
                    _port.Handshake = Handshake.RequestToSend;
                    _port.Open();
                    _port.Handshake = Handshake.None;
                }
                else
                {
                    _port.Open();
                }

                _port.DataReceived += Port_DataReceived;
                _lastDataTime = DateTime.UtcNow;

                // Watchdog timer — check for data timeout
                _watchdog = new Timer(WatchdogCallback, null, DATA_TIMEOUT_MS, DATA_TIMEOUT_MS);

                Console.WriteLine("[Serial] Opened " + _portName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Serial] Failed to open " + _portName + ": " + ex.Message);
                CloseInternal();
                throw;
            }
        }

        public void Disconnect()
        {
            Console.WriteLine("[Serial] Disconnecting " + _portName);
            CloseInternal();
        }

        public void Dispose()
        {
            CloseInternal();
        }

        /// <summary>
        /// Get available COM port names.
        /// </summary>
        public static string[] GetPortNames()
        {
            var ports = SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Array.Sort(ports, StringComparer.OrdinalIgnoreCase);
            return ports;
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_port == null || !_port.IsOpen)
                    return;

                string data = _port.ReadExisting();
                if (!string.IsNullOrEmpty(data))
                {
                    _lastDataTime = DateTime.UtcNow;
                    DataReceived?.Invoke(data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Serial] Read error: " + ex.Message);
            }
        }

        private void WatchdogCallback(object? state)
        {
            if (!IsConnected)
                return;

            if (_lastDataTime != DateTime.MinValue)
            {
                var idle = (DateTime.UtcNow - _lastDataTime).TotalMilliseconds;
                if (idle > DATA_TIMEOUT_MS)
                {
                    Console.WriteLine("[Serial] Data timeout (" + (int)idle + "ms), closing port");
                    CloseInternal();
                    Disconnected?.Invoke("Data timeout on " + _portName);
                }
            }
        }

        private void CloseInternal()
        {
            _watchdog?.Dispose();
            _watchdog = null;

            try
            {
                if (_port != null)
                {
                    _port.DataReceived -= Port_DataReceived;
                    if (_port.IsOpen)
                        _port.Close();
                    _port.Dispose();
                }
            }
            catch { }
            finally
            {
                _port = null;
            }
        }
    }
}
