using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Timer = System.Threading.Timer;

namespace RcConnector.Transport
{
    /// <summary>
    /// UDP transport: listens for ESP32 RC data sent via WiFi.
    /// ESP32 sends "RC 1500,1500,...\n" as UDP datagrams.
    /// </summary>
    internal sealed class UdpTransport : ITransport
    {
        private const int DATA_TIMEOUT_MS = 3000;

        private readonly int _listenPort;
        private UdpClient? _udp;
        private Timer? _receiveTimer;
        private Timer? _watchdog;
        private DateTime _lastDataTime = DateTime.MinValue;
        private bool _connected;

        public string DisplayName => "UDP:" + _listenPort;
        public bool IsConnected => _connected;

        public event Action<string>? DataReceived;
        public event Action<string>? Disconnected;

        public UdpTransport(int listenPort)
        {
            _listenPort = listenPort;
        }

        public void Connect()
        {
            if (_connected)
                return;

            CloseInternal();

            _udp = new UdpClient(_listenPort);
            _udp.Client.ReceiveTimeout = 100;
            _connected = true;
            _lastDataTime = DateTime.UtcNow;

            // Poll for incoming data at ~100Hz
            _receiveTimer = new Timer(ReceiveCallback, null, 0, 10);

            // Watchdog — disconnect on data timeout
            _watchdog = new Timer(WatchdogCallback, null, DATA_TIMEOUT_MS, DATA_TIMEOUT_MS);

            Console.WriteLine("[UDP] Listening on port " + _listenPort);
        }

        public void Disconnect()
        {
            Console.WriteLine("[UDP] Disconnecting port " + _listenPort);
            CloseInternal();
        }

        public void Dispose()
        {
            CloseInternal();
        }

        private void ReceiveCallback(object? state)
        {
            if (_udp == null)
                return;

            try
            {
                while (_udp != null && _udp.Available > 0)
                {
                    var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref remoteEP);

                    string text = Encoding.ASCII.GetString(data);
                    if (!string.IsNullOrEmpty(text))
                    {
                        _lastDataTime = DateTime.UtcNow;
                        DataReceived?.Invoke(text);
                    }
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
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
                    Console.WriteLine("[UDP] Data timeout (" + (int)idle + "ms)");
                    CloseInternal();
                    Disconnected?.Invoke("Data timeout on UDP:" + _listenPort);
                }
            }
        }

        private void CloseInternal()
        {
            _connected = false;

            _receiveTimer?.Dispose();
            _receiveTimer = null;
            _watchdog?.Dispose();
            _watchdog = null;

            try { _udp?.Close(); } catch { }
            _udp = null;
        }
    }
}
