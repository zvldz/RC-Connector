using System;
using System.Linq;
using System.Text.Json;
using DataChannelDotnet;
using DataChannelDotnet.Data;
using DataChannelDotnet.Bindings;
using DataChannelDotnet.Impl;

namespace RcConnector
{
    /// <summary>
    /// WebRTC peer for binary MAVLink exchange via DataChannel.
    /// Uses SignalingServer to relay offer/answer/ICE through the browser.
    /// </summary>
    internal sealed class WebRtcPeer : IDisposable
    {
        private readonly SignalingServer _signaling;
        private readonly string[] _iceServers;
        private readonly Action<string>? _log;

        private IRtcPeerConnection? _pc;
        private IRtcDataChannel? _dc;
        private bool _disposed;

        /// <summary>Fired when binary MAVLink data arrives from the server.</summary>
        public event Action<byte[]>? DataReceived;

        /// <summary>Fired when DataChannel opens (ready to send/receive).</summary>
        public event Action? ChannelOpened;

        /// <summary>Fired when DataChannel closes.</summary>
        public event Action? ChannelClosed;

        /// <summary>True when DataChannel is open and ready.</summary>
        public bool IsConnected => _dc != null;

        public WebRtcPeer(SignalingServer signaling, Action<string>? log = null, string[]? iceServers = null)
        {
            _signaling = signaling;
            _log = log;
            _iceServers = iceServers ?? new[] { "stun:stun.l.google.com:19302" };

            _signaling.BrowserConnected += OnBrowserConnected;
            _signaling.MessageReceived += OnSignalingMessage;
            _signaling.BrowserDisconnected += OnBrowserDisconnected;
        }

        /// <summary>Send binary MAVLink data to the server via DataChannel.</summary>
        public void Send(byte[] data)
        {
            if (_dc == null) return;
            try { _dc.Send(data); }
            catch { }
        }

        private void OnBrowserConnected()
        {
            if (_pc != null)
            {
                _log?.Invoke($"[WebRTC] Browser reconnected, PC already exists (state={_pc.ConnectionState}), skipping");
                return;
            }
            _log?.Invoke("[WebRTC] Browser connected, creating offer...");
            CreatePeerAndOffer();
        }

        private void OnBrowserDisconnected()
        {
            _log?.Invoke("[WebRTC] Browser disconnected");
        }

        private void CreatePeerAndOffer()
        {
            ClosePeer();

            var config = new RtcPeerConfiguration
            {
                IceServers = _iceServers
            };

            _pc = new RtcPeerConnection(config);

            _pc.OnLocalDescriptionSafe += (pc, desc) =>
            {
                var typeStr = desc.Type.ToString().ToLower();
                var json = JsonSerializer.Serialize(new
                {
                    type = typeStr,
                    sdp = desc.Sdp,
                    rc = true
                });
                _log?.Invoke($"[WebRTC] Sending local {typeStr}");
                _ = _signaling.SendAsync(json);
            };

            _pc.OnCandidateSafe += (pc, candidate) =>
            {
                // _log?.Invoke($"[WebRTC] Local ICE: {candidate.Content}");
                var json = JsonSerializer.Serialize(new
                {
                    type = "candidate",
                    candidate = candidate.Content,
                    sdpMid = candidate.Mid,
                    sdpMLineIndex = 0,
                    rc = true
                });
                _ = _signaling.SendAsync(json);
            };

            _pc.OnConnectionStateChange += (pc, state) =>
            {
                _log?.Invoke($"[WebRTC] Connection state: {state}");
            };

            var dc = _pc.CreateDataChannel(new RtcCreateDataChannelArgs
            {
                Label = "mavlink-binary",
                Unordered = true,
                MaxRetransmits = 0
            });

            SetupDataChannel(dc);
        }

        private void SetupDataChannel(IRtcDataChannel dc)
        {
            _dc = dc;

            dc.OnOpen += (ch) =>
            {
                _log?.Invoke("[WebRTC] DataChannel open");
                ChannelOpened?.Invoke();
            };

            dc.OnClose += (ch) =>
            {
                _log?.Invoke("[WebRTC] DataChannel closed");
                _dc = null;
                ChannelClosed?.Invoke();
            };

            dc.OnBinaryReceivedSafe += (ch, data) =>
            {
                DataReceived?.Invoke(data.Data.ToArray());
            };

            dc.OnTextReceivedSafe += (ch, text) =>
            {
                _log?.Invoke("[WebRTC] Text: " + text.Text);
            };
        }

        private void OnSignalingMessage(string message)
        {
            // _log?.Invoke($"[WebRTC] WS recv: {(message.Length > 200 ? message[..200] + "..." : message)}");

            if (_pc == null) return;

            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                var msgType = root.GetProperty("type").GetString();

                if (msgType == "offer" || msgType == "answer" || msgType == "pranswer")
                {
                    var sdp = root.GetProperty("sdp").GetString() ?? "";
                    var descType = msgType switch
                    {
                        "offer" => RtcDescriptionType.Offer,
                        "answer" => RtcDescriptionType.Answer,
                        "pranswer" => RtcDescriptionType.PrAnswer,
                        _ => RtcDescriptionType.Unknown
                    };

                    // Count candidates embedded in SDP
                    var candidateLines = sdp.Split('\n')
                        .Where(l => l.TrimStart().StartsWith("a=candidate:"))
                        .ToArray();
                    _log?.Invoke($"[WebRTC] Remote {msgType}, {candidateLines.Length} ICE candidates");

                    _pc.SetRemoteDescription(new RtcDescription
                    {
                        Sdp = sdp,
                        Type = descType
                    });

                    // Explicitly add candidates from SDP in case library doesn't parse them
                    if (candidateLines.Length > 0)
                    {
                        // Find mid from SDP (first m= line → mid "0")
                        var mid = "0";
                        foreach (var line in sdp.Split('\n'))
                        {
                            if (line.TrimStart().StartsWith("a=mid:"))
                            {
                                mid = line.Split(':')[1].Trim();
                                break;
                            }
                        }

                        foreach (var line in candidateLines)
                        {
                            var candidate = line.Trim();
                            if (candidate.StartsWith("a="))
                                candidate = candidate[2..]; // strip "a=" prefix
                            try
                            {
                                _pc.AddRemoteCandidate(new RtcCandidate
                                {
                                    Content = candidate,
                                    Mid = mid
                                });
                            }
                            catch (Exception ex)
                            {
                                _log?.Invoke($"[WebRTC] Failed to add SDP candidate: {ex.Message}");
                            }
                        }
                        _log?.Invoke($"[WebRTC] Added {candidateLines.Length} candidates from SDP (mid={mid})");
                    }
                }
                else if (msgType == "candidate")
                {
                    var cand = root.GetProperty("candidate").GetString();
                    var mid = root.GetProperty("sdpMid").GetString();
                    // _log?.Invoke($"[WebRTC] ICE candidate: {cand}");

                    _pc.AddRemoteCandidate(new RtcCandidate
                    {
                        Content = cand!,
                        Mid = mid!
                    });
                }
                else
                {
                    _log?.Invoke($"[WebRTC] Unknown message type: {msgType}");
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke("[WebRTC] Signaling error: " + ex.Message);
            }
        }

        private void ClosePeer()
        {
            if (_dc != null)
            {
                // _log?.Invoke("[WebRTC] Closing DataChannel");
                try { _dc.Dispose(); } catch { }
                _dc = null;
                ChannelClosed?.Invoke();
            }

            if (_pc != null)
            {
                // _log?.Invoke($"[WebRTC] Closing PeerConnection (state={_pc.ConnectionState})");
                try { _pc.Dispose(); } catch { }
                _pc = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _signaling.BrowserConnected -= OnBrowserConnected;
            _signaling.MessageReceived -= OnSignalingMessage;
            _signaling.BrowserDisconnected -= OnBrowserDisconnected;

            ClosePeer();
        }
    }
}
