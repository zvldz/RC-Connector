using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace RcConnector.Transport
{
    /// <summary>
    /// BLE transport via Nordic UART Service (NUS).
    /// Ported from RcOverride_v2_BLE.cs — ConnectBleAsync, auth detection, device enum.
    /// </summary>
    internal sealed class BleTransport : ITransport
    {
        // Nordic UART Service UUIDs
        private static readonly Guid NUS_SERVICE_UUID = new("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
        private static readonly Guid NUS_TX_CHAR_UUID = new("6E400003-B5A3-F393-E0A9-E50E24DCCA9E"); // Notify (ESP→PC)
        private static readonly Guid NUS_RX_CHAR_UUID = new("6E400002-B5A3-F393-E0A9-E50E24DCCA9E"); // Write (PC→ESP)

        private const int PORT_RETRY_INTERVAL_MS = 2000;
        private const int DATA_TIMEOUT_MS = 3000;
        private const int BLE_FAST_FAIL_THRESHOLD_MS = 5000;
        private const int BLE_FAST_FAIL_COUNT_FOR_AUTH = 5;

        private readonly string _deviceId;
        private readonly string _deviceName;

        private BluetoothLEDevice? _device;
        private GattDeviceService? _service;
        private GattCharacteristic? _txCharacteristic;
        private bool _connected;
        private bool _connecting; // Prevent concurrent ConnectAsync
        private DateTime _connectedAt = DateTime.MinValue;
        private DateTime _lastConnectAttempt = DateTime.MinValue;

        // Auth failure detection
        private int _failCount;
        private int _fastFailCount;
        private bool _authFailed;
        private string? _authReason;

        // Reconnect timer
        private Timer? _reconnectTimer;
        private bool _shouldReconnect;

        public string DisplayName => _deviceName;
        public bool IsConnected => _connected;
        public bool AuthFailed => _authFailed;
        public string? AuthFailReason => _authReason;

        public event Action<string>? DataReceived;
        public event Action<string>? Disconnected;

        /// <summary>Fired on BLE auth failure (user needs to re-pair device).</summary>
        public event Action<string>? AuthFailure;

        /// <summary>Fired for log messages (connection attempts, failures, etc).</summary>
        public event Action<string>? LogMessage;

        public BleTransport(string deviceId, string deviceName)
        {
            _deviceId = deviceId;
            _deviceName = deviceName;
        }

        public void Connect()
        {
            _shouldReconnect = true;
            _authFailed = false;
            _authReason = null;
            _failCount = 0;
            _fastFailCount = 0;

            // Start connection attempt
            Task.Run(ConnectAsync);

            // Start reconnect timer for auto-retry
            _reconnectTimer = new Timer(ReconnectCallback, null, PORT_RETRY_INTERVAL_MS, PORT_RETRY_INTERVAL_MS);
        }

        public void Disconnect()
        {
            _shouldReconnect = false;
            _authFailed = false;
            _authReason = null;
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            CloseInternal();
            EmitLog("Disconnected " + _deviceName);
        }

        public void Dispose()
        {
            _shouldReconnect = false;
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            CloseInternal();
        }

        // ---------------------------------------------------------------
        // Connection
        // ---------------------------------------------------------------

        private async Task ConnectAsync()
        {
            if (_connected || _authFailed || _connecting)
                return;
            _connecting = true;

            try
            {
                await ConnectAsyncCore();
            }
            finally
            {
                _connecting = false;
            }
        }

        private async Task ConnectAsyncCore()
        {
            // Rate limit
            var now = DateTime.UtcNow;
            if ((now - _lastConnectAttempt).TotalMilliseconds < PORT_RETRY_INTERVAL_MS)
                return;
            _lastConnectAttempt = now;

            var attemptStart = DateTime.UtcNow;
            int elapsed;

            try
            {
                EmitLog("Connecting to " + _deviceName + " (attempt " + (_failCount + 1) + ")");

                _device = await BluetoothLEDevice.FromIdAsync(_deviceId);
                if (_device == null)
                {
                    elapsed = (int)(DateTime.UtcNow - attemptStart).TotalMilliseconds;
                    EmitLog("Failed to get device (" + elapsed + "ms)");
                    OnConnectFailed(elapsed);
                    return;
                }

                _connectedAt = DateTime.UtcNow;
                _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

                // Get NUS service
                var servicesResult = await _device.GetGattServicesForUuidAsync(NUS_SERVICE_UUID);
                if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
                {
                    elapsed = (int)(DateTime.UtcNow - attemptStart).TotalMilliseconds;
                    bool auth = IsGattAuthError(servicesResult.Status);
                    EmitLog("Service discovery failed: " + servicesResult.Status +
                        " (" + elapsed + "ms)" + (auth ? " → auth error" : ""));
                    CloseInternal();
                    if (auth)
                        OnAuthFailed("Service discovery: " + servicesResult.Status);
                    else
                        OnConnectFailed(elapsed);
                    return;
                }

                _service = servicesResult.Services[0];

                // Get TX characteristic (notifications from ESP)
                var charsResult = await _service.GetCharacteristicsForUuidAsync(NUS_TX_CHAR_UUID);
                if (charsResult.Status != GattCommunicationStatus.Success || charsResult.Characteristics.Count == 0)
                {
                    elapsed = (int)(DateTime.UtcNow - attemptStart).TotalMilliseconds;
                    bool auth = IsGattAuthError(charsResult.Status);
                    EmitLog("Char discovery failed: " + charsResult.Status +
                        " (" + elapsed + "ms)" + (auth ? " → auth error" : ""));
                    CloseInternal();
                    if (auth)
                        OnAuthFailed("Characteristic discovery: " + charsResult.Status);
                    else
                        OnConnectFailed(elapsed);
                    return;
                }

                _txCharacteristic = charsResult.Characteristics[0];

                // Subscribe to notifications
                var status = await _txCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);

                if (status != GattCommunicationStatus.Success)
                {
                    elapsed = (int)(DateTime.UtcNow - attemptStart).TotalMilliseconds;
                    bool auth = IsGattAuthError(status);
                    EmitLog("Subscribe failed: " + status +
                        " (" + elapsed + "ms)" + (auth ? " → auth error" : ""));
                    CloseInternal();
                    if (auth)
                        OnAuthFailed("Notification subscribe: " + status);
                    else
                        OnConnectFailed(elapsed);
                    return;
                }

                _txCharacteristic.ValueChanged += Characteristic_ValueChanged;
                _failCount = 0;
                _fastFailCount = 0;
                _connected = true;

                elapsed = (int)(DateTime.UtcNow - attemptStart).TotalMilliseconds;
                EmitLog("Connected (" + elapsed + "ms)");
            }
            catch (Exception ex)
            {
                elapsed = (int)(DateTime.UtcNow - attemptStart).TotalMilliseconds;
                EmitLog("Connection failed (" + elapsed + "ms): " + ex.Message);
                CloseInternal();
                OnConnectFailed(elapsed);
            }
        }

        // ---------------------------------------------------------------
        // Data reception (BLE notifications)
        // ---------------------------------------------------------------

        private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                var reader = DataReader.FromBuffer(args.CharacteristicValue);
                byte[] data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);

                string incoming = Encoding.ASCII.GetString(data);
                DataReceived?.Invoke(incoming);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BLE] ValueChanged error: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------
        // Connection status / auth failure detection
        // ---------------------------------------------------------------

        private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                var uptime = (int)(DateTime.UtcNow - _connectedAt).TotalMilliseconds;
                EmitLog("Disconnected after " + uptime + "ms");

                bool wasConnected = _connected;
                CloseInternal();

                // Rapid disconnect = auth/bond failure (same as MP plugin)
                if (wasConnected && uptime < PORT_RETRY_INTERVAL_MS)
                {
                    OnAuthFailed("Rapid disconnect after " + uptime + "ms");
                }
                else if (wasConnected)
                {
                    Disconnected?.Invoke("BLE disconnected from " + _deviceName);
                }
            }
        }

        private void OnConnectFailed(int elapsedMs)
        {
            _failCount++;

            // Timing-based auth detection: auth failure is fast (<5s), device offline is slow (~7-8s)
            if (elapsedMs > 0 && elapsedMs < BLE_FAST_FAIL_THRESHOLD_MS)
            {
                _fastFailCount++;
                EmitLog("Fail #" + _failCount + " (" + elapsedMs +
                    "ms, fast #" + _fastFailCount + "/" + BLE_FAST_FAIL_COUNT_FOR_AUTH + ")");

                if (_fastFailCount >= BLE_FAST_FAIL_COUNT_FOR_AUTH)
                {
                    OnAuthFailed("Possible auth (timing): " + _fastFailCount +
                        " fast failures (<" + BLE_FAST_FAIL_THRESHOLD_MS + "ms)");
                }
            }
            else
            {
                // Slow failure — device offline, reset fast counter
                EmitLog("Fail #" + _failCount + " (" + elapsedMs + "ms, slow)");
                _fastFailCount = 0;
            }
        }

        private void OnAuthFailed(string reason)
        {
            _authFailed = true;
            _authReason = reason;
            _shouldReconnect = false;
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;

            EmitLog("AUTH FAILED: " + reason);
            AuthFailure?.Invoke(reason);
        }

        private static bool IsGattAuthError(GattCommunicationStatus status)
        {
            return status == GattCommunicationStatus.AccessDenied ||
                   status == GattCommunicationStatus.ProtocolError;
        }

        // ---------------------------------------------------------------
        // Reconnect timer
        // ---------------------------------------------------------------

        private void ReconnectCallback(object? state)
        {
            if (!_shouldReconnect || _connected || _authFailed)
                return;

            Task.Run(ConnectAsync);
        }

        private void EmitLog(string message)
        {
            Console.WriteLine("[BLE] " + message);
            LogMessage?.Invoke("[BLE] " + message);
        }

        // ---------------------------------------------------------------
        // Cleanup
        // ---------------------------------------------------------------

        private void CloseInternal()
        {
            _connected = false;

            try
            {
                if (_device != null)
                    _device.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
            }
            catch { }

            try
            {
                if (_txCharacteristic != null)
                    _txCharacteristic.ValueChanged -= Characteristic_ValueChanged;
            }
            catch { }

            try { _service?.Dispose(); } catch { }
            try { _device?.Dispose(); } catch { }

            _txCharacteristic = null;
            _service = null;
            _device = null;
        }

        // ---------------------------------------------------------------
        // Device enumeration (static)
        // ---------------------------------------------------------------

        /// <summary>
        /// Find paired BLE devices that have Nordic UART Service.
        /// Returns list of (deviceId, deviceName) tuples.
        /// </summary>
        public static async Task<List<(string Id, string Name)>> GetPairedNusDevicesAsync()
        {
            var result = new List<(string, string)>();

            try
            {
                Console.WriteLine("[BLE] Scanning for paired NUS devices...");

                string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
                var pairedDevices = await DeviceInformation.FindAllAsync(selector);

                foreach (var deviceInfo in pairedDevices)
                {
                    if (string.IsNullOrEmpty(deviceInfo.Name))
                        continue;

                    try
                    {
                        using var bleDevice = await BluetoothLEDevice.FromIdAsync(deviceInfo.Id);
                        if (bleDevice == null) continue;

                        var services = await bleDevice.GetGattServicesForUuidAsync(NUS_SERVICE_UUID);
                        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                        {
                            Console.WriteLine("[BLE] Skipping " + deviceInfo.Name + " (no NUS)");
                            continue;
                        }
                    }
                    catch
                    {
                        Console.WriteLine("[BLE] Skipping " + deviceInfo.Name + " (unreachable)");
                        continue;
                    }

                    result.Add((deviceInfo.Id, deviceInfo.Name));
                    Console.WriteLine("[BLE] Found: " + deviceInfo.Name);
                }

                Console.WriteLine("[BLE] Found " + result.Count + " NUS devices");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[BLE] Scan failed: " + ex.Message);
            }

            return result;
        }
    }
}
