using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RcConnector.Core;
using RcConnector.MAVLink;
using System.Collections.Generic;
using System.Threading.Tasks;
using RcConnector.Transport;
using System.Runtime.InteropServices;

namespace RcConnector
{
    /// <summary>
    /// System tray application: NotifyIcon, context menu, status management.
    /// Orchestrates transport → parser → MAVLink pipeline.
    /// </summary>
    internal sealed class TrayApp : IDisposable
    {
        private const int MAIN_TIMER_MS = 20; // 50 Hz processing loop
        private const int LED_TIMEOUT_MS = 1000;
        private const int DATA_TIMEOUT_MS = 3000;

        // Components
        private readonly AppSettings _settings;
        private readonly RcParser _parser;
        private readonly MavlinkService _mavlink;
        private ITransport? _transport;

        // UI
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _connectMenu;    // "Connect ▸" cascading submenu
        private readonly ToolStripMenuItem _disconnectItem; // flat "Disconnect" (shown when connected)
        private readonly ToolStripMenuItem _showItem;
        private readonly ToolStripMenuItem _alwaysOnTopItem;
        private MainForm? _mainForm;

        // BLE device cache for submenu
        private List<(string Id, string Name)> _cachedBleDevices = new();
        private bool _bleScanInProgress;

        // RC forward
        private UdpClient? _rcForwardClient;
        private IPEndPoint? _rcForwardEndpoint;

        // Update
        private readonly UpdateChecker _updateChecker = new();
        private bool _updatePending;

        // State
        private readonly List<string> _logBuffer = new(100);
        private readonly object _logLock = new();
        private System.Windows.Forms.Timer? _mainTimer;
        private DateTime _lastRcData = DateTime.MinValue;
        private int _rcFrameCount;
        private int _rcFrameCountSnapshot;
        private DateTime _lastRateCalc = DateTime.UtcNow;
        private bool _connected;

        public float DataRateHz { get; private set; }

        public TrayApp()
        {
            _settings = AppSettings.Load();
            _parser = new RcParser();
            _mavlink = new MavlinkService();

            // Wire parser → mavlink
            _parser.OnRcData += OnRcData;
            _mavlink.DroneStatusChanged += OnDroneStatusChanged;

            // Context menu
            _connectMenu = new ToolStripMenuItem(L.Get("menu_connect"));
            _disconnectItem = new ToolStripMenuItem(L.Get("menu_disconnect"), null, (s, e) => DoDisconnect());
            _disconnectItem.Visible = false;
            _showItem = new ToolStripMenuItem(L.Get("menu_show"), null, OnShowClick);
            _alwaysOnTopItem = new ToolStripMenuItem(L.Get("menu_always_on_top"), null, OnAlwaysOnTopClick)
            {
                Checked = _settings.AlwaysOnTop
            };

            _contextMenu = new ContextMenuStrip();
            Theme.Apply(_contextMenu);
            _contextMenu.Items.Add(_connectMenu);
            _contextMenu.Items.Add(_disconnectItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_showItem);
            _contextMenu.Items.Add(_alwaysOnTopItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(L.Get("menu_joystick_mapping"), null, OnJoystickMappingClick);
            _contextMenu.Items.Add(L.Get("menu_settings"), null, OnSettingsClick);
            _contextMenu.Items.Add(new ToolStripSeparator());
            var aboutItem = new ToolStripMenuItem($"{AppInfo.AppName} v{AppInfo.Version}");
            aboutItem.Enabled = false;
            _contextMenu.Items.Add(aboutItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(L.Get("menu_exit"), null, OnExitClick);

            // Build submenu dynamically on open
            _contextMenu.Opening += (s, e) => RebuildConnectMenu();

            // Prevent all menu levels from closing during BLE scan
            ToolStripDropDownClosingEventHandler cancelWhileScanning = (s, e) =>
            {
                if (_bleScanInProgress && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                    e.Cancel = true;
            };
            _contextMenu.Closing += cancelWhileScanning;
            _connectMenu.DropDown.Closing += cancelWhileScanning;

            // Tray icon
            _trayIcon = new NotifyIcon
            {
                Icon = CreateColorIcon(Color.Gray),
                Text = L.Get("tip_disconnected"),
                ContextMenuStrip = _contextMenu,
                Visible = true,
            };
            _trayIcon.DoubleClick += (s, e) => ToggleMainForm();

            // Main processing timer
            _mainTimer = new System.Windows.Forms.Timer { Interval = MAIN_TIMER_MS };
            _mainTimer.Tick += MainTimer_Tick;
            _mainTimer.Start();

            // Start MAVLink listener
            try
            {
                _mavlink.Start(_settings.MavlinkPort, _settings.MavlinkSysId);
                Log(L.Get("log_mavlink_started", _settings.MavlinkPort, _settings.MavlinkSysId));
            }
            catch (Exception ex)
            {
                Log(L.Get("log_mavlink_start_failed", ex.Message));
            }

            // RC forward
            UpdateRcForwarder();

            // Pre-scan BLE devices in background
            _ = Task.Run(async () =>
            {
                _cachedBleDevices = await BleTransport.GetPairedNusDevicesAsync();
            });

            // First-run tip: suggest pinning tray icon
            if (!_settings.FirstRunDone)
            {
                _settings.FirstRunDone = true;
                _settings.Save();
                _trayIcon.BalloonTipTitle = "RC-Connector";
                _trayIcon.BalloonTipText = L.Get("tip_pin_icon");
                _trayIcon.ShowBalloonTip(5000);
            }

            // Check for updates (delayed, non-blocking)
            _trayIcon.BalloonTipClicked += OnBalloonClicked;
            _ = CheckForUpdatesAsync(delayMs: 5000);
        }

        public void Dispose()
        {
            _mainTimer?.Stop();
            _mainTimer?.Dispose();

            // Intentional disconnect — send ClearRcOverride
            if (_connected)
            {
                try { _mavlink.SendClearRcOverride(); } catch { }
            }

            _transport?.Dispose();
            _rcForwardClient?.Dispose();
            _mavlink.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _contextMenu.Dispose();
            _mainForm?.Dispose();
        }

        // ---------------------------------------------------------------
        // Main loop (50 Hz on UI thread)
        // ---------------------------------------------------------------

        private void MainTimer_Tick(object? sender, EventArgs e)
        {
            _parser.ProcessBuffer();
            UpdateStatus();
        }

        private void OnRcData(ushort[] channels)
        {
            _lastRcData = DateTime.UtcNow;
            _rcFrameCount++;
            _mavlink.SendRcOverride(channels);

            // RC forward via UDP
            if (_rcForwardClient != null && _rcForwardEndpoint != null)
            {
                try
                {
                    var line = "RC " + string.Join(",", channels) + "\n";
                    var bytes = Encoding.ASCII.GetBytes(line);
                    _rcForwardClient.Send(bytes, bytes.Length, _rcForwardEndpoint);
                }
                catch { }
            }

            // Update main form channel bars
            _mainForm?.UpdateChannels(channels);
        }

        private void OnDroneStatusChanged(bool connected)
        {
            if (connected)
                Log(L.Get("log_drone_connected", _mavlink.DroneSystemId));
            else
                Log(L.Get("log_drone_disconnected"));
        }

        // ---------------------------------------------------------------
        // Status / LED update
        // ---------------------------------------------------------------

        private void UpdateStatus()
        {
            // Calculate data rate (Hz) every second
            var now = DateTime.UtcNow;
            if ((now - _lastRateCalc).TotalMilliseconds >= 1000)
            {
                DataRateHz = _rcFrameCount - _rcFrameCountSnapshot;
                _rcFrameCountSnapshot = _rcFrameCount;
                _lastRateCalc = now;
            }

            string tooltip;

            // BLE auth failure — DarkRed even when disconnected
            bool bleAuthFailed = _transport is BleTransport ble && ble.AuthFailed;
            bool hasRcData = _lastRcData != DateTime.MinValue &&
                             (now - _lastRcData).TotalMilliseconds <= LED_TIMEOUT_MS;

            if (!_connected && !bleAuthFailed && _mavlink.DroneConnected)
            {
                tooltip = L.Get("tip_disconnected_drone_ok");
                UpdateTrayIcon(Color.Gray, Color.LimeGreen, tooltip);
            }
            else if (!_connected && !bleAuthFailed)
            {
                tooltip = L.Get("tip_disconnected");
                UpdateTrayIcon(Color.Gray, tooltip);
            }
            else if (bleAuthFailed)
            {
                tooltip = L.Get("tip_ble_auth_failed");
                UpdateTrayIcon(Color.DarkRed, tooltip);
            }
            else if (_transport == null || !_transport.IsConnected)
            {
                bool blink = (DateTime.Now.Millisecond / 500) % 2 == 0;
                tooltip = L.Get("tip_connecting");
                UpdateTrayIcon(blink ? Color.Red : Color.Gray, tooltip);
            }
            else if (hasRcData && _mavlink.DroneConnected)
            {
                tooltip = _mavlink.DroneArmed
                    ? L.Get("tip_ok_armed", DataRateHz.ToString("0"))
                    : L.Get("tip_ok", DataRateHz.ToString("0"));
                UpdateTrayIcon(Color.LimeGreen, tooltip);
            }
            else if (hasRcData && !_mavlink.DroneConnected)
            {
                tooltip = L.Get("tip_rc_ok_no_drone");
                UpdateTrayIcon(Color.LimeGreen, Color.FromArgb(160, 50, 30), tooltip);
            }
            else if (!hasRcData && _mavlink.DroneConnected)
            {
                tooltip = L.Get("tip_no_rc_drone_ok");
                UpdateTrayIcon(Color.FromArgb(160, 50, 30), Color.LimeGreen, tooltip);
            }
            else
            {
                tooltip = L.Get("tip_connected_no_data");
                UpdateTrayIcon(Color.OrangeRed, tooltip);
            }

            // Update main form status
            _mainForm?.UpdateStatus(
                _connected,
                _transport?.DisplayName ?? "",
                DataRateHz,
                hasRcData,
                _mavlink.DroneConnected,
                _mavlink.DroneArmed,
                _mavlink.DroneCustomMode);
        }

        private void UpdateTrayIcon(Color color, string tooltip)
        {
            UpdateTrayIconInternal(color.ToArgb().ToString(), tooltip, () => CreateColorIcon(color));
        }

        private void UpdateTrayIcon(Color leftColor, Color rightColor, string tooltip)
        {
            string colorKey = $"{leftColor.ToArgb()}|{rightColor.ToArgb()}";
            UpdateTrayIconInternal(colorKey, tooltip, () => CreateSplitIcon(leftColor, rightColor));
        }

        private void UpdateTrayIconInternal(string colorKey, string tooltip, Func<Icon> createIcon)
        {
            _trayIcon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;

            // Only recreate icon if color changed
            var currentTag = _trayIcon.Tag as string;
            if (currentTag != colorKey)
            {
                var oldIcon = _trayIcon.Icon;
                _trayIcon.Icon = createIcon();
                _trayIcon.Tag = colorKey;
                oldIcon?.Dispose();
            }
        }

        // ---------------------------------------------------------------
        // Context menu handlers
        // ---------------------------------------------------------------

        private void OnShowClick(object? sender, EventArgs e) => ToggleMainForm();

        private void OnAlwaysOnTopClick(object? sender, EventArgs e)
        {
            _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
            _alwaysOnTopItem.Checked = _settings.AlwaysOnTop;
            if (_mainForm != null)
                _mainForm.TopMost = _settings.AlwaysOnTop;
            _settings.Save();
        }

        private SettingsForm? _settingsForm;
        private JoystickMappingForm? _joystickMappingForm;

        private void OnJoystickMappingClick(object? sender, EventArgs e)
        {
            if (_joystickMappingForm != null && !_joystickMappingForm.IsDisposed)
            {
                _joystickMappingForm.BringToFront();
                return;
            }

            var currentDeviceName = _settings.JoystickDeviceName;
            _joystickMappingForm = new JoystickMappingForm(
                _settings.GetJoystickMapping(currentDeviceName), _settings.JoystickDeviceId,
                _settings.JoystickMappings, currentDeviceName);
            _joystickMappingForm.ApplyRequested += (deviceName, newMapping) =>
            {
                _settings.SetJoystickMapping(deviceName, newMapping);
                _settings.Save();
                Log(L.Get("log_settings_updated"));
            };
            _joystickMappingForm.Show();
        }

        private void OnSettingsClick(object? sender, EventArgs e)
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.BringToFront();
                return;
            }

            _settingsForm = new SettingsForm(_settings, _connected);
            _settingsForm.ApplyRequested += (newSettings) =>
            {
                bool mavlinkChanged = _settings.MavlinkPort != newSettings.MavlinkPort ||
                    _settings.MavlinkSysId != newSettings.MavlinkSysId;
                bool forwardChanged = _settings.RcForwardEnabled != newSettings.RcForwardEnabled ||
                    _settings.RcForwardIp != newSettings.RcForwardIp ||
                    _settings.RcForwardPort != newSettings.RcForwardPort;
                bool dpiChanged = _settings.AdaptiveDpi != newSettings.AdaptiveDpi;
                bool langChanged = _settings.Language != newSettings.Language;

                _settings.UdpListenPort = newSettings.UdpListenPort;
                _settings.MavlinkPort = newSettings.MavlinkPort;
                _settings.MavlinkSysId = newSettings.MavlinkSysId;
                _settings.JoystickPollHz = newSettings.JoystickPollHz;
                _settings.SerialDtrRts = newSettings.SerialDtrRts;
                _settings.RcForwardEnabled = newSettings.RcForwardEnabled;
                _settings.RcForwardIp = newSettings.RcForwardIp;
                _settings.RcForwardPort = newSettings.RcForwardPort;
                _settings.AdaptiveDpi = newSettings.AdaptiveDpi;
                _settings.Language = newSettings.Language;
                _settings.ThemeMode = newSettings.ThemeMode;
                _settings.RunAtStartup = newSettings.RunAtStartup;
                SetStartupRegistry(newSettings.RunAtStartup);

                // Apply language change
                if (langChanged)
                    L.Init(_settings.Language);

                // Restart MAVLink only if port or sysid changed
                if (mavlinkChanged)
                {
                    try
                    {
                        _mavlink.Stop();
                        _mavlink.Start(_settings.MavlinkPort, _settings.MavlinkSysId);
                        Log(L.Get("log_mavlink_restarted", _settings.MavlinkPort, _settings.MavlinkSysId));
                    }
                    catch (Exception ex)
                    {
                        Log(L.Get("log_mavlink_restart_failed", ex.Message));
                    }
                }

                // Restart RC forwarder if settings changed
                if (forwardChanged)
                    UpdateRcForwarder();

                // Recreate main form if DPI scaling or language changed
                if ((dpiChanged || langChanged) && _mainForm != null && !_mainForm.IsDisposed)
                {
                    bool wasVisible = _mainForm.Visible;
                    _mainForm.Dispose();
                    _mainForm = null;
                    if (wasVisible)
                        ToggleMainForm();
                }

                _settings.Save();
                Log(L.Get("log_settings_updated"));
            };
            _settingsForm.Show();
        }

        private async void OnBalloonClicked(object? sender, EventArgs e)
        {
            if (!_updatePending) return;
            _updatePending = false;

            Log(L.Get("log_update_downloading", _updateChecker.LatestTag ?? ""));
            _trayIcon.BalloonTipTitle = "RC-Connector";
            _trayIcon.BalloonTipText = L.Get("update_downloading");
            _trayIcon.ShowBalloonTip(3000);

            bool launched = await _updateChecker.DownloadAndLaunchAsync();
            if (launched)
            {
                DoDisconnect();
                Application.Exit();
            }
        }

        private async Task CheckForUpdatesAsync(int delayMs = 0)
        {
            if (delayMs > 0) await Task.Delay(delayMs);

            bool hasUpdate = await _updateChecker.CheckAsync();
            string? tag = _updateChecker.LatestTag;

            // Update About tab
            _mainForm?.SetLatestVersion(tag?.TrimStart('v', 'V'), hasUpdate);

            if (hasUpdate)
            {
                _updatePending = true;
                Log(L.Get("log_update_available", tag ?? ""));
                _trayIcon.BalloonTipTitle = L.Get("update_available_title");
                _trayIcon.BalloonTipText = L.Get("update_available", tag ?? "");
                _trayIcon.ShowBalloonTip(10000);
            }
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            DoDisconnect();
            Application.Exit();
        }

        // ---------------------------------------------------------------
        // Connect submenu
        // ---------------------------------------------------------------

        private void RebuildConnectMenu()
        {
            // Toggle visibility based on connection state
            _connectMenu.Visible = !_connected;
            _disconnectItem.Visible = _connected;

            if (_connected)
                return;

            _connectMenu.DropDownItems.Clear();

            // --- BLE submenu ---
            var bleMenu = new ToolStripMenuItem("BLE");
            bleMenu.DropDown.Closing += (s, e) =>
            {
                if (_bleScanInProgress && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                    e.Cancel = true;
            };
            if (_cachedBleDevices.Count > 0)
            {
                foreach (var (id, name) in _cachedBleDevices)
                {
                    var item = new ToolStripMenuItem(name);
                    if (id == _settings.BleDeviceId)
                        item.Font = new Font(item.Font, FontStyle.Bold);
                    string devId = id, devName = name; // capture
                    item.Click += (s, e) => ConnectBle(devId, devName);
                    bleMenu.DropDownItems.Add(item);
                }
                bleMenu.DropDownItems.Add(new ToolStripSeparator());
            }
            var refreshItem = new ToolStripMenuItem(L.Get("menu_refresh"));
            refreshItem.MouseDown += (s, e) => _bleScanInProgress = true;
            refreshItem.Click += async (s, e) =>
            {
                refreshItem.Text = L.Get("menu_scanning");
                refreshItem.Enabled = false;

                try
                {
                    _cachedBleDevices = await BleTransport.GetPairedNusDevicesAsync();
                    Log(L.Get("log_ble_found", _cachedBleDevices.Count));

                    // Rebuild BLE submenu items (keep refreshItem at bottom)
                    bleMenu.DropDownItems.Clear();
                    foreach (var (id, name) in _cachedBleDevices)
                    {
                        var devItem = new ToolStripMenuItem(name);
                        if (id == _settings.BleDeviceId)
                            devItem.Font = new Font(devItem.Font, FontStyle.Bold);
                        string devId = id, devName = name;
                        devItem.Click += (s2, e2) => ConnectBle(devId, devName);
                        bleMenu.DropDownItems.Add(devItem);
                    }
                    if (_cachedBleDevices.Count > 0)
                        bleMenu.DropDownItems.Add(new ToolStripSeparator());
                }
                finally
                {
                    refreshItem.Text = L.Get("menu_refresh");
                    refreshItem.Enabled = true;
                    bleMenu.DropDownItems.Add(refreshItem);
                    _bleScanInProgress = false;
                }
            };
            bleMenu.DropDownItems.Add(refreshItem);
            _connectMenu.DropDownItems.Add(bleMenu);

            // --- COM submenu ---
            var comMenu = new ToolStripMenuItem("COM");
            var ports = SerialTransport.GetPortNames();
            if (ports.Length == 0)
            {
                comMenu.DropDownItems.Add(new ToolStripMenuItem(L.Get("menu_no_ports")) { Enabled = false });
            }
            else
            {
                foreach (var port in ports)
                {
                    var item = new ToolStripMenuItem(port);
                    if (port == _settings.ComPort)
                        item.Font = new Font(item.Font, FontStyle.Bold);
                    string p = port; // capture
                    item.Click += (s, e) => ConnectSerial(p);
                    comMenu.DropDownItems.Add(item);
                }
            }
            _connectMenu.DropDownItems.Add(comMenu);

            // --- UDP ---
            var udpItem = new ToolStripMenuItem($"UDP :{_settings.UdpListenPort}");
            udpItem.Click += (s, e) => ConnectUdp();
            _connectMenu.DropDownItems.Add(udpItem);

            // --- Joystick submenu ---
            var joyMenu = new ToolStripMenuItem("Joystick");
            var joysticks = JoystickTransport.ListDevices();
            if (joysticks.Length == 0)
            {
                joyMenu.DropDownItems.Add(new ToolStripMenuItem(L.Get("menu_no_joysticks")) { Enabled = false });
            }
            else
            {
                foreach (var (id, name) in joysticks)
                {
                    var item = new ToolStripMenuItem(name);
                    if (id == _settings.JoystickDeviceId)
                        item.Font = new Font(item.Font, FontStyle.Bold);
                    int devId = id; string devName = name; // capture
                    item.Click += (s, e) => ConnectJoystick(devId, devName);
                    joyMenu.DropDownItems.Add(item);
                }
            }
            _connectMenu.DropDownItems.Add(joyMenu);
        }

        // ---------------------------------------------------------------
        // Connect / Disconnect
        // ---------------------------------------------------------------

        private void ConnectSerial(string portName)
        {
            try
            {
                _transport?.Dispose();
                var serial = new SerialTransport(portName, dtrRtsFix: !_settings.SerialDtrRts);
                WireTransport(serial);
                _transport = serial;
                _transport.Connect();

                _connected = true;
                _settings.SourceMode = SourceMode.COM;
                _settings.ComPort = portName;
                _settings.Save();

                Log(L.Get("log_connected_to", portName));
                UpdateMainFormToolbar();
            }
            catch (Exception ex)
            {
                Log(L.Get("log_connect_failed", ex.Message));
                _connected = false;
                UpdateMainFormToolbar();
            }
        }

        private void ConnectBle(string deviceId, string deviceName)
        {
            try
            {
                _transport?.Dispose();
                var ble = new BleTransport(deviceId, deviceName);
                WireTransport(ble);
                ble.AuthFailure += reason => Log(L.Get("log_ble_auth_failed", reason));
                ble.LogMessage += msg => Log(msg);
                _transport = ble;
                _transport.Connect();

                _connected = true;
                _settings.SourceMode = SourceMode.BLE;
                _settings.BleDeviceId = deviceId;
                _settings.BleDeviceName = deviceName;
                _settings.Save();

                Log(L.Get("log_connecting_ble", deviceName));
                UpdateMainFormToolbar();
            }
            catch (Exception ex)
            {
                Log(L.Get("log_connect_failed", ex.Message));
                _connected = false;
                UpdateMainFormToolbar();
            }
        }

        private void ConnectUdp()
        {
            try
            {
                _transport?.Dispose();
                int port = _settings.UdpListenPort;
                var udp = new UdpTransport(port);
                WireTransport(udp);
                _transport = udp;
                _transport.Connect();

                _connected = true;
                _settings.SourceMode = SourceMode.UDP;
                _settings.Save();

                Log(L.Get("log_listening_udp", port));
                UpdateMainFormToolbar();
            }
            catch (Exception ex)
            {
                Log(L.Get("log_connect_failed", ex.Message));
                _connected = false;
                UpdateMainFormToolbar();
            }
        }

        private void ConnectJoystick(int deviceId, string deviceName)
        {
            try
            {
                _transport?.Dispose();
                int pollMs = 1000 / Math.Clamp(_settings.JoystickPollHz, 10, 50);
                var mapping = _settings.GetJoystickMapping(deviceName);
                var joy = new JoystickTransport(deviceId, deviceName, pollMs, mapping);
                WireTransport(joy);
                _transport = joy;
                _transport.Connect();

                _connected = true;
                _settings.SourceMode = SourceMode.Joystick;
                _settings.JoystickDeviceId = deviceId;
                _settings.JoystickDeviceName = deviceName;
                _settings.Save();

                Log(L.Get("log_joystick_connected", deviceName));
                UpdateMainFormToolbar();
            }
            catch (Exception ex)
            {
                Log(L.Get("log_connect_failed", ex.Message));
                _connected = false;
                UpdateMainFormToolbar();
            }
        }

        private void WireTransport(ITransport transport)
        {
            transport.DataReceived += data => _parser.Feed(data);
            transport.Disconnected += reason =>
            {
                Log(L.Get("log_disconnected_reason", reason));
                // Don't send ClearRcOverride on connection loss — just stop sending
            };
        }

        private void DoDisconnect()
        {
            if (_connected)
            {
                // Intentional disconnect — send ClearRcOverride once
                try { _mavlink.SendClearRcOverride(); } catch { }
            }

            _transport?.Disconnect();
            _connected = false;
            _lastRcData = DateTime.MinValue;
            Log(L.Get("log_disconnected"));
            UpdateMainFormToolbar();
        }

        // ---------------------------------------------------------------
        // RC Forward
        // ---------------------------------------------------------------

        private void UpdateRcForwarder()
        {
            _rcForwardClient?.Dispose();
            _rcForwardClient = null;
            _rcForwardEndpoint = null;

            if (_settings.RcForwardEnabled &&
                !string.IsNullOrWhiteSpace(_settings.RcForwardIp) &&
                IPAddress.TryParse(_settings.RcForwardIp, out var ip) &&
                _settings.RcForwardPort > 0)
            {
                _rcForwardClient = new UdpClient(ip.AddressFamily);
                _rcForwardEndpoint = new IPEndPoint(ip, _settings.RcForwardPort);
                Log(L.Get("log_rc_forward_started", _settings.RcForwardIp, _settings.RcForwardPort));
            }
        }

        // ---------------------------------------------------------------
        // Startup registry
        // ---------------------------------------------------------------

        private static void SetStartupRegistry(bool enable)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (enable)
                {
                    // Prefer installed path from registry; fall back to current exe
                    string exePath = GetInstalledExePath()
                        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                        ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                        key.SetValue("RC-Connector", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("RC-Connector", false);
                }
            }
            catch { }
        }

        private static string? GetInstalledExePath()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\RC-Connector");
                var installDir = key?.GetValue("InstallDir") as string;
                if (!string.IsNullOrEmpty(installDir))
                {
                    string path = System.IO.Path.Combine(installDir, "RC-Connector.exe");
                    if (System.IO.File.Exists(path))
                        return path;
                }
            }
            catch { }
            return null;
        }

        // ---------------------------------------------------------------
        // Main form (mini window)
        // ---------------------------------------------------------------

        private void SaveFormPosition()
        {
            if (_mainForm != null && !_mainForm.IsDisposed &&
                _mainForm.Visible && _mainForm.WindowState == FormWindowState.Normal)
            {
                _settings.WindowX = _mainForm.Location.X;
                _settings.WindowY = _mainForm.Location.Y;
                _settings.Save();
            }
        }

        private void ToggleMainForm()
        {
            if (_mainForm != null && !_mainForm.IsDisposed)
            {
                if (_mainForm.Visible)
                {
                    SaveFormPosition();
                    _mainForm.Hide();
                }
                else
                {
                    EnsureOnScreen(_mainForm);
                    _mainForm.Show();
                    _mainForm.Activate();
                }
                return;
            }

            _mainForm = new MainForm(_settings);
            _mainForm.TopMost = _settings.AlwaysOnTop;

            // Wire connect/disconnect events
            _mainForm.ConnectSerialRequested += port => ConnectSerial(port);
            _mainForm.ConnectBleRequested += (id, name) => ConnectBle(id, name);
            _mainForm.ConnectUdpRequested += () => ConnectUdp();
            _mainForm.ConnectJoystickRequested += (id, name) => ConnectJoystick(id, name);
            _mainForm.JoystickMappingRequested += () => OnJoystickMappingClick(null, EventArgs.Empty);
            _mainForm.SettingsRequested += () => OnSettingsClick(null, EventArgs.Empty);
            _mainForm.DisconnectRequested += () => DoDisconnect();
            _mainForm.RefreshMenuRequested += () => UpdateMainFormToolbar();
            _mainForm.BleScanRequested += async () =>
            {
                _cachedBleDevices = await BleTransport.GetPairedNusDevicesAsync();
                Log(L.Get("log_ble_found", _cachedBleDevices.Count));
                UpdateMainFormToolbar();
            };
            _mainForm.CheckUpdateRequested += () => _ = CheckForUpdatesAsync();

            // Show cached latest version in About tab
            if (_updateChecker.LatestTag != null)
                _mainForm.SetLatestVersion(_updateChecker.LatestTag.TrimStart('v', 'V'),
                    _updatePending);

            _mainForm.FormClosing += (s, e) =>
            {
                // Save window position on any close/hide
                SaveFormPosition();

                // Allow close if Application.Exit() was called
                if (e.CloseReason == CloseReason.ApplicationExitCall)
                    return;

                // Otherwise hide instead of close
                e.Cancel = true;
                _mainForm.Hide();
            };
            _mainForm.Show();
            UpdateMainFormToolbar();

            // Replay buffered log entries (after Show — handle must exist)
            lock (_logLock)
            {
                foreach (var entry in _logBuffer)
                    _mainForm.AppendLog(entry);
            }
        }

        private void UpdateMainFormToolbar()
        {
            if (_mainForm == null || _mainForm.IsDisposed)
                return;

            _mainForm.SetConnected(_connected, _transport?.DisplayName ?? "");
            if (!_connected)
            {
                _mainForm.PopulateConnectMenu(
                    Transport.SerialTransport.GetPortNames(),
                    _cachedBleDevices,
                    _settings.UdpListenPort,
                    _settings);
            }
        }

        private static void EnsureOnScreen(Form form)
        {
            var screen = Screen.FromPoint(form.Location);
            var area = screen.WorkingArea;
            int x = Math.Max(area.Left, Math.Min(form.Location.X, area.Right - form.Width));
            int y = Math.Max(area.Top, Math.Min(form.Location.Y, area.Bottom - form.Height));
            form.Location = new Point(x, y);
        }

        // ---------------------------------------------------------------
        // Logging
        // ---------------------------------------------------------------

        private void Log(string message)
        {
            string entry = DateTime.Now.ToString("HH:mm:ss") + " " + message;
            Console.WriteLine("[RC] " + entry);

            lock (_logLock)
            {
                if (_logBuffer.Count >= 100)
                    _logBuffer.RemoveAt(0);
                _logBuffer.Add(entry);
            }

            _mainForm?.AppendLog(entry);
        }

        // ---------------------------------------------------------------
        // Icon generation (colored circle)
        // ---------------------------------------------------------------

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Create icon from HICON, properly transferring ownership to avoid GDI handle leak.
        /// Icon.FromHandle() does not own the HICON — clone it and destroy the original.
        /// </summary>
        private static Icon OwnIcon(IntPtr hIcon)
        {
            using var temp = Icon.FromHandle(hIcon);
            var clone = (Icon)temp.Clone();
            DestroyIcon(hIcon);
            return clone;
        }

        private static Icon CreateColorIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 14, 14);

            using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1);
            g.DrawEllipse(pen, 1, 1, 14, 14);

            return OwnIcon(bmp.GetHicon());
        }

        /// <summary>
        /// Create split circle icon: left half = RC status, right half = drone status.
        /// </summary>
        private static Icon CreateSplitIcon(Color leftColor, Color rightColor)
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Clip to circle
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(1, 1, 14, 14);
            g.SetClip(path);

            // Left half (RC)
            using var leftBrush = new SolidBrush(leftColor);
            g.FillRectangle(leftBrush, 0, 0, 8, 16);

            // Right half (drone)
            using var rightBrush = new SolidBrush(rightColor);
            g.FillRectangle(rightBrush, 8, 0, 8, 16);

            g.ResetClip();

            // Circle outline
            using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1);
            g.DrawEllipse(pen, 1, 1, 14, 14);

            return OwnIcon(bmp.GetHicon());
        }
    }
}
