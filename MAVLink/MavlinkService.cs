using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Timer = System.Threading.Timer;

namespace RcConnector.MAVLink
{
    /// <summary>
    /// MAVLink UDP service: sends HEARTBEAT + RC_CHANNELS_OVERRIDE, receives drone HEARTBEAT.
    /// Passive mode: listens on port, replies to sender address.
    /// </summary>
    internal sealed class MavlinkService : IDisposable
    {
        private const byte MAV_COMPID = 1;
        private const byte MAV_TYPE_GCS = 6;
        private const byte MAV_AUTOPILOT_INVALID = 8;
        private const byte MAV_MODE_FLAG_CUSTOM = 1;
        private const byte MAV_STATE_ACTIVE = 4;
        private const int HEARTBEAT_INTERVAL_MS = 1000;
        private const int DRONE_TIMEOUT_MS = 5000;

        private UdpClient? _udp;
        private IPEndPoint? _droneEndpoint;
        private Timer? _heartbeatTimer;
        private Timer? _receiveTimer;
        private readonly global::MAVLink.MavlinkParse _parser = new();
        private readonly global::MAVLink.MavlinkParse _externalParser = new();
        private int _port;
        private byte _sysId = 255;
        private int _seqNum;

        // Drone state (from received HEARTBEAT)
        public bool DroneConnected =>
            _lastDroneHeartbeat != DateTime.MinValue &&
            (DateTime.UtcNow - _lastDroneHeartbeat).TotalMilliseconds < DRONE_TIMEOUT_MS;
        public byte DroneSystemId { get; private set; }
        public byte DroneComponentId { get; private set; }
        public uint DroneCustomMode { get; private set; }
        public bool DroneArmed { get; private set; }

        private DateTime _lastDroneHeartbeat = DateTime.MinValue;

        /// <summary>Fired when drone connectivity changes (connected/disconnected).</summary>
        public event Action<bool>? DroneStatusChanged;

        /// <summary>
        /// Start MAVLink service. Listens on port, replies to sender address.
        /// </summary>
        public void Start(int port, int sysId = 255)
        {
            Stop();
            _port = port;
            _sysId = (byte)Math.Clamp(sysId, 1, 255);

            try
            {
                _udp = new UdpClient(port);
                _udp.Client.ReceiveTimeout = 100;
                Console.WriteLine("[MAVLink] Listening on UDP port " + port + ", sysid=" + _sysId);

                // Heartbeat timer — 1 Hz
                _heartbeatTimer = new Timer(SendHeartbeat, null, 0, HEARTBEAT_INTERVAL_MS);

                // Receive timer — poll for incoming packets
                _receiveTimer = new Timer(ReceivePackets, null, 0, 50);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MAVLink] Failed to start on port " + port + ": " + ex.Message);
                Stop();
                throw;
            }
        }

        public void Stop()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _receiveTimer?.Dispose();
            _receiveTimer = null;

            try { _udp?.Close(); } catch { }
            _udp = null;
            _droneEndpoint = null;
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Send RC_CHANNELS_OVERRIDE to drone. Only call when fresh ESP32 data available.
        /// </summary>
        public void SendRcOverride(ushort[] channels)
        {
            if (_udp == null || _droneEndpoint == null || channels.Length < 16)
                return;

            var msg = new global::MAVLink.mavlink_rc_channels_override_t
            {
                target_system = DroneSystemId > 0 ? DroneSystemId : (byte)1,
                target_component = DroneComponentId > 0 ? DroneComponentId : (byte)1,
                chan1_raw = channels[0],
                chan2_raw = channels[1],
                chan3_raw = channels[2],
                chan4_raw = channels[3],
                chan5_raw = channels[4],
                chan6_raw = channels[5],
                chan7_raw = channels[6],
                chan8_raw = channels[7],
                chan9_raw = channels[8],
                chan10_raw = channels[9],
                chan11_raw = channels[10],
                chan12_raw = channels[11],
                chan13_raw = channels[12],
                chan14_raw = channels[13],
                chan15_raw = channels[14],
                chan16_raw = channels[15],
            };

            SendPacket(msg, global::MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_OVERRIDE);
        }

        /// <summary>
        /// Send ClearRcOverride (release) — chan=65535 for intentional disconnect.
        /// </summary>
        public void SendClearRcOverride()
        {
            if (_udp == null || _droneEndpoint == null)
                return;

            var msg = new global::MAVLink.mavlink_rc_channels_override_t
            {
                target_system = DroneSystemId > 0 ? DroneSystemId : (byte)1,
                target_component = DroneComponentId > 0 ? DroneComponentId : (byte)1,
                chan1_raw = ushort.MaxValue,
                chan2_raw = ushort.MaxValue,
                chan3_raw = ushort.MaxValue,
                chan4_raw = ushort.MaxValue,
                chan5_raw = ushort.MaxValue,
                chan6_raw = ushort.MaxValue,
                chan7_raw = ushort.MaxValue,
                chan8_raw = ushort.MaxValue,
            };

            SendPacket(msg, global::MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_OVERRIDE);
            Console.WriteLine("[MAVLink] Sent ClearRcOverride");
        }

        private void SendHeartbeat(object? state)
        {
            if (_udp == null || _droneEndpoint == null)
                return;

            var msg = new global::MAVLink.mavlink_heartbeat_t
            {
                type = MAV_TYPE_GCS,
                autopilot = MAV_AUTOPILOT_INVALID,
                base_mode = MAV_MODE_FLAG_CUSTOM,
                system_status = MAV_STATE_ACTIVE,
                mavlink_version = 3,
            };

            SendPacket(msg, global::MAVLink.MAVLINK_MSG_ID.HEARTBEAT);
        }

        /// <summary>
        /// Generate RC_CHANNELS_OVERRIDE packet as raw bytes (for WebRTC mode).
        /// </summary>
        public byte[]? GenerateRcOverridePacket(ushort[] channels)
        {
            if (channels.Length < 16) return null;

            var msg = new global::MAVLink.mavlink_rc_channels_override_t
            {
                target_system = DroneSystemId > 0 ? DroneSystemId : (byte)1,
                target_component = DroneComponentId > 0 ? DroneComponentId : (byte)1,
                chan1_raw = channels[0],  chan2_raw = channels[1],
                chan3_raw = channels[2],  chan4_raw = channels[3],
                chan5_raw = channels[4],  chan6_raw = channels[5],
                chan7_raw = channels[6],  chan8_raw = channels[7],
                chan9_raw = channels[8],  chan10_raw = channels[9],
                chan11_raw = channels[10], chan12_raw = channels[11],
                chan13_raw = channels[12], chan14_raw = channels[13],
                chan15_raw = channels[14], chan16_raw = channels[15],
            };

            try
            {
                return _parser.GenerateMAVLinkPacket20(
                    global::MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_OVERRIDE, msg, false, _sysId, MAV_COMPID, _seqNum++);
            }
            catch { return null; }
        }

        private void SendPacket<T>(T msg, global::MAVLink.MAVLINK_MSG_ID msgId) where T : struct
        {
            if (_udp == null || _droneEndpoint == null)
                return;

            try
            {
                byte[] packet = _parser.GenerateMAVLinkPacket20(
                    msgId, msg, false, _sysId, MAV_COMPID, _seqNum++);
                _udp.Send(packet, packet.Length, _droneEndpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MAVLink] Send error: " + ex.Message);
            }
        }

        private void ReceivePackets(object? state)
        {
            if (_udp == null)
                return;

            try
            {
                while (_udp != null && _udp.Available > 0)
                {
                    var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udp.Receive(ref remoteEP);

                    // Remember sender address for replies
                    _droneEndpoint = remoteEP;

                    // Parse MAVLink packet
                    ParseIncoming(data);
                }

                // Check drone timeout
                bool wasConnected = DroneConnected;
                if (_lastDroneHeartbeat != DateTime.MinValue &&
                    (DateTime.UtcNow - _lastDroneHeartbeat).TotalMilliseconds > DRONE_TIMEOUT_MS)
                {
                    if (wasConnected)
                        DroneStatusChanged?.Invoke(false);
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Parse incoming MAVLink data and update drone status.
        /// Called internally for UDP and externally for WebRTC DataChannel.
        /// </summary>
        public void ParseIncoming(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                while (stream.Position < stream.Length)
                {
                    var packet = _externalParser.ReadPacket(stream);
                    if (packet == null)
                        break;

                    if (packet.msgid == (uint)global::MAVLink.MAVLINK_MSG_ID.HEARTBEAT)
                    {
                        var hb = (global::MAVLink.mavlink_heartbeat_t)packet.data;
                        if (hb.type == MAV_TYPE_GCS)
                            continue;

                        bool wasConnected = DroneConnected;

                        DroneSystemId = packet.sysid;
                        DroneComponentId = packet.compid;
                        DroneCustomMode = hb.custom_mode;
                        DroneArmed = (hb.base_mode & 128) != 0;
                        _lastDroneHeartbeat = DateTime.UtcNow;

                        if (!wasConnected)
                            DroneStatusChanged?.Invoke(true);
                    }
                }
            }
            catch { }
        }

    }
}
