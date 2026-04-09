using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RcConnector
{
    /// <summary>
    /// Local WebSocket server for WebRTC signaling.
    /// Browser connects here and relays offer/answer/ICE between RC-Connector and remote server.
    /// </summary>
    internal sealed class SignalingServer : IDisposable
    {
        private const int BUFFER_SIZE = 4096;

        private readonly int _port;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private WebSocket? _browserSocket;
        private readonly object _lock = new();

        /// <summary>Fired when a signaling message arrives from the browser.</summary>
        public event Action<string>? MessageReceived;

        /// <summary>Fired when the browser connects.</summary>
        public event Action? BrowserConnected;

        /// <summary>Fired when the browser disconnects.</summary>
        public event Action? BrowserDisconnected;

        /// <summary>True when a browser is connected via WebSocket.</summary>
        public bool IsConnected
        {
            get
            {
                lock (_lock)
                    return _browserSocket?.State == WebSocketState.Open;
            }
        }

        public SignalingServer(int port = 9999)
        {
            _port = port;
        }

        /// <summary>Start listening for browser connections.</summary>
        public void Start()
        {
            if (_listener != null) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();

            Task.Run(() => AcceptLoop(_cts.Token));
        }

        /// <summary>Stop the server and close all connections.</summary>
        public void Stop()
        {
            _cts?.Cancel();
            CloseSocket();

            try { _listener?.Stop(); } catch { }
            _listener = null;
        }

        /// <summary>Send a signaling message to the browser.</summary>
        public async Task SendAsync(string message)
        {
            WebSocket? ws;
            lock (_lock) ws = _browserSocket;

            if (ws?.State != WebSocketState.Open) return;

            var bytes = Encoding.UTF8.GetBytes(message);
            try
            {
                await ws.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    _cts?.Token ?? CancellationToken.None);
            }
            catch { }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener!.GetContextAsync().WaitAsync(ct);

                    if (!context.Request.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                        continue;
                    }

                    // Accept only one browser at a time — close previous if any
                    CloseSocket();

                    var wsContext = await context.AcceptWebSocketAsync(null);
                    lock (_lock) _browserSocket = wsContext.WebSocket;

                    BrowserConnected?.Invoke();

                    await ReceiveLoop(wsContext.WebSocket, ct);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(500, ct).ConfigureAwait(false); }
            }
        }

        private async Task ReceiveLoop(WebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[BUFFER_SIZE];
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        MessageReceived?.Invoke(msg);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                CloseSocket();
                BrowserDisconnected?.Invoke();
            }
        }

        private void CloseSocket()
        {
            WebSocket? ws;
            lock (_lock)
            {
                ws = _browserSocket;
                _browserSocket = null;
            }

            if (ws == null) return;
            try
            {
                if (ws.State == WebSocketState.Open)
                    _ = ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null,
                        new CancellationTokenSource(2000).Token)
                        .ContinueWith(_ => ws.Dispose());
                else
                    ws.Dispose();
            }
            catch { try { ws.Dispose(); } catch { } }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
