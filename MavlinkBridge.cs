using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace RcConnector
{
    internal enum BridgeMode { Udp, Tcp }

    /// <summary>
    /// Bridges MAVLink bytes between WebRTC DataChannel and Mission Planner.
    /// UDP mode: send to MP on target port, receive from MP on listen port.
    /// TCP mode: listen for MP connection, bidirectional stream.
    /// </summary>
    internal sealed class MavlinkBridge : IDisposable
    {
        private readonly BridgeMode _mode;
        private readonly int _port;

        // UDP mode
        private UdpClient? _udp;
        private IPEndPoint? _mpEndpoint;

        // TCP mode
        private TcpListener? _tcpListener;
        private readonly List<TcpClient> _tcpClients = new();
        private readonly object _lock = new();

        private CancellationTokenSource? _cts;

        /// <summary>Fired when data arrives from Mission Planner (MP → drone).</summary>
        public event Action<byte[]>? DataReceived;

        public MavlinkBridge(BridgeMode mode, int port)
        {
            _mode = mode;
            _port = port;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();

            if (_mode == BridgeMode.Udp)
            {
                // Ephemeral local port; send to MP on _port, receive replies on auto-assigned port
                _udp = new UdpClient(0, AddressFamily.InterNetwork);
                _mpEndpoint = new IPEndPoint(IPAddress.Loopback, _port);
                Console.WriteLine($"[Bridge] UDP → localhost:{_port} for GCS");
                Task.Run(() => UdpReceiveLoop(_cts.Token));
            }
            else
            {
                _tcpListener = new TcpListener(IPAddress.Loopback, _port);
                _tcpListener.Start();
                Console.WriteLine($"[Bridge] TCP mode on localhost:{_port} for GCS");
                Task.Run(() => TcpAcceptLoop(_cts.Token));
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }

            // UDP
            try { _udp?.Close(); } catch { }
            _udp = null;

            // TCP
            lock (_lock)
            {
                foreach (var c in _tcpClients)
                    try { c.Close(); } catch { }
                _tcpClients.Clear();
            }
            try { _tcpListener?.Stop(); } catch { }
            _tcpListener = null;
        }

        /// <summary>Send MAVLink data to Mission Planner (DC → MP).</summary>
        public void Send(byte[] data)
        {
            if (_mode == BridgeMode.Udp)
            {
                if (_udp == null || _mpEndpoint == null) return;
                try { _udp.Send(data, data.Length, _mpEndpoint); }
                catch { }
            }
            else
            {
                SendTcp(data);
            }
        }

        // ---- UDP ----

        private async Task UdpReceiveLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp!.ReceiveAsync(ct);
                    // Remember sender so we reply to correct MP instance
                    _mpEndpoint = result.RemoteEndPoint;
                    DataReceived?.Invoke(result.Buffer);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { }
            }
        }

        // ---- TCP ----

        private void SendTcp(byte[] data)
        {
            List<TcpClient>? dead = null;

            lock (_lock)
            {
                foreach (var client in _tcpClients)
                {
                    try
                    {
                        client.GetStream().Write(data, 0, data.Length);
                    }
                    catch
                    {
                        dead ??= new List<TcpClient>();
                        dead.Add(client);
                    }
                }

                if (dead != null)
                {
                    foreach (var d in dead)
                    {
                        _tcpClients.Remove(d);
                        try { d.Close(); } catch { }
                    }
                }
            }
        }

        private async Task TcpAcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _tcpListener!.AcceptTcpClientAsync(ct);
                    client.NoDelay = true;
                    lock (_lock) _tcpClients.Add(client);
                    Console.WriteLine($"[Bridge] TCP client connected: {client.Client.RemoteEndPoint}");
                    _ = Task.Run(() => TcpReadLoop(client, ct));
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { await Task.Delay(500, ct).ConfigureAwait(false); }
            }
        }

        private async Task TcpReadLoop(TcpClient client, CancellationToken ct)
        {
            var buffer = new byte[4096];
            try
            {
                var stream = client.GetStream();
                while (!ct.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                    if (read == 0) break;

                    var data = new byte[read];
                    Buffer.BlockCopy(buffer, 0, data, 0, read);
                    DataReceived?.Invoke(data);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                lock (_lock) _tcpClients.Remove(client);
                try { client.Close(); } catch { }
                Console.WriteLine("[Bridge] TCP client disconnected");
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
