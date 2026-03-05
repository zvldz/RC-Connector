using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using RcConnector.Core;

namespace RcConnector
{
    /// <summary>
    /// Mini window with channel bars (tab 1) and log (tab 2).
    /// Compact, non-resizable, position remembered.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int CHANNEL_COUNT = 16;
        private const ushort PWM_MIN = 800;
        private const ushort PWM_MAX = 2200;

        // Base sizes at 96 DPI — framework auto-scales via AutoScaleMode.Font
        private const int BAR_HEIGHT = 16;
        private const int BAR_SPACING = 2;
        private const int BAR_WIDTH = 200;
        private const int LABEL_WIDTH = 24;
        private const int VALUE_WIDTH = 38;
        private const float FONT_SIZE = 7.5f;
        private const float LOG_FONT_SIZE = 8f;
        private const int STATUS_HEIGHT = 18;

        private readonly TabControl _tabs;
        private readonly Panel _channelPanel;
        private readonly Panel[] _barBgs = new Panel[CHANNEL_COUNT];
        private readonly Panel[] _bars = new Panel[CHANNEL_COUNT];
        private readonly Label[] _valueLabels = new Label[CHANNEL_COUNT];
        private readonly Panel _statusPanel;
        private readonly Label _statusTransport;
        private readonly Label _statusArm;
        private readonly Label _statusMode;
        private readonly RichTextBox _logBox;
        private readonly Button _clearLogButton;
        private int _logLineCount;
        private DataGridViewRow? _latestVersionRow;

        // Toolbar
        private readonly Panel _toolbarPanel;
        private readonly Button _btnConnect;
        private readonly Button _btnDisconnect;
        private readonly ContextMenuStrip _connectDropdown;

        /// <summary>Events for connect/disconnect actions.</summary>
        public event Action<string>? ConnectSerialRequested;
        public event Action<string, string>? ConnectBleRequested;
        public event Action? ConnectUdpRequested;
        public event Action? DisconnectRequested;
        public event Action? RefreshMenuRequested;
        public event Action? BleScanRequested;
        public event Action? CheckUpdateRequested;

        public MainForm(AppSettings settings)
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Theme.FormBg;
            ForeColor = Theme.FormFg;

            Text = L.Get("form_title");
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MaximizeBox = false;
            KeyPreview = true;

            int formWidth = LABEL_WIDTH + BAR_WIDTH + VALUE_WIDTH + 30;
            int channelAreaHeight = CHANNEL_COUNT * (BAR_HEIGHT + BAR_SPACING) + 10;

            // Restore position
            if (settings.WindowX >= 0 && settings.WindowY >= 0)
            {
                Location = new Point(settings.WindowX, settings.WindowY);
            }
            else
            {
                // Default: near system tray (bottom-right)
                var screen = Screen.PrimaryScreen!.WorkingArea;
                Location = new Point(screen.Right - formWidth - 20, screen.Bottom - 400);
            }

            // --- Status bar (top) ---
            var statusFont = new Font("Consolas", FONT_SIZE);

            _statusPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = STATUS_HEIGHT,
                BackColor = Color.FromArgb(40, 40, 40),
            };

            _statusTransport = new Label
            {
                AutoSize = false,
                Font = statusFont,
                ForeColor = Color.LightGray,
                BackColor = Color.Transparent,
                Text = L.Get("status_disconnected"),
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                Padding = new Padding(4, 0, 0, 0),
            };

            _statusArm = new Label
            {
                AutoSize = true,
                Font = new Font("Consolas", FONT_SIZE, FontStyle.Bold),
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Height = STATUS_HEIGHT,
                Padding = new Padding(4, 0, 4, 0),
            };

            _statusMode = new Label
            {
                AutoSize = true,
                Font = statusFont,
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Height = STATUS_HEIGHT,
                Padding = new Padding(4, 0, 4, 0),
            };

            _statusPanel.Controls.Add(_statusMode);
            _statusPanel.Controls.Add(_statusArm);
            _statusPanel.Controls.Add(_statusTransport);

            // --- Toolbar (connect/disconnect) ---
            int toolbarHeight = 30;
            _connectDropdown = new ContextMenuStrip();
            Theme.Apply(_connectDropdown);

            _btnConnect = new Button
            {
                Text = L.Get("menu_connect") + " \u25BC",
                Location = new Point(2, 2),
                Size = new Size(100, toolbarHeight - 4),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.System,
            };
            _btnConnect.Click += (s, e) =>
            {
                RefreshMenuRequested?.Invoke();
                _connectDropdown.Show(_btnConnect, new Point(0, _btnConnect.Height));
            };

            _btnDisconnect = new Button
            {
                Text = L.Get("menu_disconnect"),
                Location = new Point(2, 2),
                Size = new Size(100, toolbarHeight - 4),
                Visible = false,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.System,
            };
            _btnDisconnect.Click += (s, e) => DisconnectRequested?.Invoke();

            _toolbarPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = toolbarHeight,
                BackColor = Theme.PanelBg,
                Padding = new Padding(2),
            };
            _toolbarPanel.Controls.Add(_btnConnect);
            _toolbarPanel.Controls.Add(_btnDisconnect);

            // --- Tab control ---
            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Appearance = TabAppearance.FlatButtons,
                DrawMode = Theme.IsDark ? TabDrawMode.OwnerDrawFixed : TabDrawMode.Normal,
            };
            if (Theme.IsDark)
                _tabs.DrawItem += TabDrawItem;

            // Tab 1: Channels
            var channelTab = new TabPage(L.Get("tab_channels")) { BackColor = Theme.FormBg, ForeColor = Theme.FormFg };
            _channelPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = false,
                Padding = new Padding(4),
            };

            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                int y = 4 + i * (BAR_HEIGHT + BAR_SPACING);

                var chLabel = new Label
                {
                    Text = (i + 1).ToString(),
                    Location = new Point(4, y),
                    Size = new Size(LABEL_WIDTH, BAR_HEIGHT),
                    TextAlign = ContentAlignment.MiddleRight,
                    Font = new Font("Consolas", FONT_SIZE, FontStyle.Bold),
                    ForeColor = Theme.ChannelNumFg,
                };

                _barBgs[i] = new Panel
                {
                    Location = new Point(LABEL_WIDTH + 6, y),
                    Size = new Size(BAR_WIDTH, BAR_HEIGHT),
                    BackColor = Theme.BarBg,
                    BorderStyle = BorderStyle.FixedSingle,
                };
                _bars[i] = new Panel
                {
                    Location = new Point(0, 0),
                    Size = new Size(BAR_WIDTH / 2, BAR_HEIGHT),
                    BackColor = Theme.BarFg,
                };
                _barBgs[i].Controls.Add(_bars[i]);

                _valueLabels[i] = new Label
                {
                    Text = "----",
                    Location = new Point(LABEL_WIDTH + BAR_WIDTH + 8, y),
                    Size = new Size(VALUE_WIDTH, BAR_HEIGHT),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Consolas", FONT_SIZE),
                };

                _channelPanel.Controls.Add(chLabel);
                _channelPanel.Controls.Add(_barBgs[i]);
                _channelPanel.Controls.Add(_valueLabels[i]);
            }

            channelTab.Controls.Add(_channelPanel);
            _tabs.TabPages.Add(channelTab);

            // Tab 2: Log
            var logTab = new TabPage(L.Get("tab_log")) { BackColor = Theme.FormBg, ForeColor = Theme.FormFg };
            _logBox = new RichTextBox
            {
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", LOG_FONT_SIZE),
                BackColor = Theme.LogBg,
                ForeColor = Theme.LogFg,
            };
            _clearLogButton = new Button
            {
                Text = L.Get("btn_clear"),
                Dock = DockStyle.Bottom,
                Height = 24,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            _clearLogButton.Click += (s, e) => { _logBox.Clear(); _logLineCount = 0; };

            logTab.Controls.Add(_logBox);
            logTab.Controls.Add(_clearLogButton);
            _tabs.TabPages.Add(logTab);

            // Tab 3: About
            var aboutTab = new TabPage(L.Get("tab_about")) { BackColor = Theme.FormBg, ForeColor = Theme.FormFg };

            var aboutIcon = new PictureBox
            {
                Size = new Size(48, 48),
                SizeMode = PictureBoxSizeMode.Zoom,
                Anchor = AnchorStyles.Top,
            };
            try
            {
                var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null)
                    aboutIcon.Image = ico.ToBitmap();
            }
            catch { }

            var aboutGrid = new DataGridView
            {
                ColumnCount = 2,
                RowHeadersVisible = false,
                ColumnHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false,
                ReadOnly = true,
                ScrollBars = ScrollBars.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Theme.GridBg,
                DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.GridBg, ForeColor = Theme.FormFg },
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.Single,
                GridColor = Theme.GridLine,
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.GridAltBg, ForeColor = Theme.FormFg },
                Font = new Font("Segoe UI", FONT_SIZE),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            aboutGrid.Columns[0].DefaultCellStyle.Font = new Font("Segoe UI", FONT_SIZE, FontStyle.Bold);
            aboutGrid.Columns[0].DefaultCellStyle.BackColor = Theme.GridHeaderBg;
            aboutGrid.DefaultCellStyle.SelectionBackColor = Theme.GridBg;
            aboutGrid.DefaultCellStyle.SelectionForeColor = Theme.FormFg;
            aboutGrid.Rows.Add(L.Get("about_app"), AppInfo.AppName);
            aboutGrid.Rows.Add(L.Get("about_version"), AppInfo.Version);
            int latestRowIdx = aboutGrid.Rows.Add(L.Get("about_latest"), "—");
            aboutGrid.Rows.Add(L.Get("about_build"), AppInfo.BuildDate);
            aboutGrid.Rows.Add(L.Get("about_author"), AppInfo.Author);
            int githubRowIdx = aboutGrid.Rows.Add("GitHub", "zvldz/RC-Connector");
            aboutGrid.Rows[githubRowIdx].Cells[1].Style.ForeColor = Theme.LinkFg;
            aboutGrid.Rows[githubRowIdx].Cells[1].Style.Font = new Font("Segoe UI", FONT_SIZE, FontStyle.Underline);
            _latestVersionRow = aboutGrid.Rows[latestRowIdx];

            aboutGrid.CellContentClick += (s, ev) =>
            {
                if (ev.RowIndex == githubRowIdx && ev.ColumnIndex == 1)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "https://github.com/zvldz/RC-Connector") { UseShellExecute = true });
                }
            };

            var btnCheckUpdate = new Button
            {
                Text = L.Get("about_check_update"),
                AutoSize = true,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.System,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
            };
            btnCheckUpdate.Click += (s, ev) => CheckUpdateRequested?.Invoke();

            aboutTab.Layout += (s, ev) =>
            {
                int pad = 10;
                int iconW = aboutIcon.Width;
                int iconH = aboutIcon.Height;
                int gridWidth = aboutTab.ClientSize.Width - pad * 2;
                aboutIcon.Location = new Point((aboutTab.ClientSize.Width - iconW) / 2, pad);
                aboutGrid.Location = new Point(pad, iconH + pad * 2);
                int gridH = aboutGrid.Rows.GetRowsHeight(DataGridViewElementStates.Visible) + 2;
                aboutGrid.Size = new Size(gridWidth, gridH);
                aboutGrid.Columns[0].Width = gridWidth / 3;
                aboutGrid.Columns[1].Width = gridWidth - gridWidth / 3 - 2;
                btnCheckUpdate.Location = new Point(pad, aboutGrid.Bottom + pad);
                btnCheckUpdate.Width = gridWidth;
            };

            aboutTab.Controls.Add(btnCheckUpdate);
            aboutTab.Controls.Add(aboutGrid);
            aboutTab.Controls.Add(aboutIcon);
            _tabs.TabPages.Add(aboutTab);

            // Layout (reverse order: last added Dock.Top renders first)
            Controls.Add(_tabs);
            Controls.Add(_statusPanel);
            Controls.Add(_toolbarPanel);

            ClientSize = new Size(formWidth, toolbarHeight + STATUS_HEIGHT + channelAreaHeight + 30);

            ResumeLayout(false);
            PerformLayout();

            Theme.ApplyDarkTitleBar(this);
        }

        /// <summary>
        /// Update channel bars with new RC data. Thread-safe.
        /// </summary>
        public void UpdateChannels(ushort[] channels)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    for (int i = 0; i < CHANNEL_COUNT && i < channels.Length; i++)
                    {
                        int val = Math.Clamp(channels[i], PWM_MIN, PWM_MAX);
                        int bgWidth = _barBgs[i].ClientSize.Width;
                        int fillWidth = (int)((val - PWM_MIN) / (float)(PWM_MAX - PWM_MIN) * bgWidth);
                        _bars[i].Width = Math.Max(0, fillWidth);
                        _valueLabels[i].Text = channels[i].ToString();

                        // Color: red at extremes, green in normal range
                        bool extreme = channels[i] <= 810 || channels[i] >= 2190;
                        _valueLabels[i].ForeColor = extreme ? Theme.ChannelValExtremeFg : Theme.ChannelValFg;
                        _bars[i].BackColor = extreme ? Theme.BarFgExtreme : Theme.BarFg;
                    }
                }));
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Update status bar text. Thread-safe.
        /// </summary>
        public void UpdateStatus(bool connected, string transportName, float hz,
            bool droneConnected, bool armed, uint customMode)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!connected)
                    {
                        _statusTransport.Text = L.Get("status_disconnected");
                        _statusPanel.BackColor = Color.FromArgb(40, 40, 40);
                        _statusArm.Text = "";
                        _statusMode.Text = "";
                    }
                    else if (!droneConnected)
                    {
                        _statusTransport.Text = $"{transportName} {hz:0}Hz";
                        _statusPanel.BackColor = Color.FromArgb(80, 60, 20);
                        _statusArm.Text = "";
                        _statusMode.Text = L.Get("status_no_drone");
                        _statusMode.ForeColor = Color.Orange;
                        _statusMode.BackColor = Color.Transparent;
                    }
                    else
                    {
                        _statusTransport.Text = $"{transportName} {hz:0}Hz";
                        _statusPanel.BackColor = Color.FromArgb(40, 40, 40);

                        // Armed / Disarmed badge
                        _statusArm.Text = armed ? L.Get("status_armed") : L.Get("status_disarmed");
                        _statusArm.ForeColor = Color.White;
                        _statusArm.BackColor = armed
                            ? Color.FromArgb(180, 40, 40)
                            : Color.FromArgb(40, 120, 40);

                        // Flight mode badge
                        string modeName = DecodeCopterMode(customMode);
                        _statusMode.Text = $" {modeName} ";
                        _statusMode.ForeColor = Color.White;
                        _statusMode.BackColor = Color.FromArgb(50, 80, 140);
                    }

                    // Layout labels left to right
                    _statusArm.Location = new Point(_statusTransport.Right + 2, 0);
                    _statusMode.Location = new Point(_statusArm.Text != "" ? _statusArm.Right + 2 : _statusTransport.Right + 2, 0);
                }));
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Append log entry. Thread-safe.
        /// </summary>
        public void AppendLog(string entry)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (_logBox.TextLength > 32000)
                    {
                        _logBox.Clear();
                        _logLineCount = 0;
                    }

                    string line = entry + Environment.NewLine;
                    int start = _logBox.TextLength;
                    _logBox.AppendText(line);
                    _logBox.Select(start, line.Length);
                    _logBox.SelectionBackColor = (_logLineCount % 2 == 0)
                        ? Theme.LogBg
                        : Theme.LogAltBg;
                    _logBox.SelectionColor = Theme.LogFg;
                    _logBox.Select(_logBox.TextLength, 0);
                    _logLineCount++;
                }));
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Update toolbar connect/disconnect visibility. Thread-safe.
        /// </summary>
        public void SetConnected(bool connected, string transportName)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    _btnConnect.Visible = !connected;
                    _btnDisconnect.Visible = connected;
                    if (connected)
                        _btnDisconnect.Text = L.Get("menu_disconnect") + " (" + transportName + ")";
                }));
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Update the "Latest" version row in About tab. Thread-safe.
        /// </summary>
        public void SetLatestVersion(string? version, bool isNewer)
        {
            if (IsDisposed || !IsHandleCreated || _latestVersionRow == null)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    string text = version ?? "—";
                    if (isNewer)
                        text += " \u2B06"; // ⬆ arrow
                    _latestVersionRow.Cells[1].Value = text;
                    _latestVersionRow.Cells[1].Style.ForeColor = isNewer
                        ? Color.FromArgb(0, 180, 0)
                        : Theme.FormFg;
                }));
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Populate the connect dropdown menu with available devices.
        /// </summary>
        public void PopulateConnectMenu(string[] comPorts, List<(string Id, string Name)> bleDevices,
            int udpPort, AppSettings settings)
        {
            _connectDropdown.Items.Clear();

            // COM ports
            var comMenu = new ToolStripMenuItem("COM");
            if (comPorts.Length == 0)
            {
                comMenu.DropDownItems.Add(new ToolStripMenuItem(L.Get("menu_no_ports")) { Enabled = false });
            }
            else
            {
                foreach (var port in comPorts)
                {
                    var item = new ToolStripMenuItem(port);
                    if (port == settings.ComPort)
                        item.Font = new Font(item.Font, FontStyle.Bold);
                    string p = port;
                    item.Click += (s, e) => ConnectSerialRequested?.Invoke(p);
                    comMenu.DropDownItems.Add(item);
                }
            }
            _connectDropdown.Items.Add(comMenu);

            // BLE devices
            var bleMenu = new ToolStripMenuItem("BLE");
            foreach (var (id, name) in bleDevices)
            {
                var item = new ToolStripMenuItem(name);
                if (id == settings.BleDeviceId)
                    item.Font = new Font(item.Font, FontStyle.Bold);
                string devId = id, devName = name;
                item.Click += (s, e) => ConnectBleRequested?.Invoke(devId, devName);
                bleMenu.DropDownItems.Add(item);
            }
            if (bleDevices.Count == 0)
                bleMenu.DropDownItems.Add(new ToolStripMenuItem("—") { Enabled = false });
            bleMenu.DropDownItems.Add(new ToolStripSeparator());
            var refreshItem = new ToolStripMenuItem(L.Get("menu_refresh"));
            refreshItem.Click += (s, e) => BleScanRequested?.Invoke();
            bleMenu.DropDownItems.Add(refreshItem);
            _connectDropdown.Items.Add(bleMenu);

            // UDP
            var udpItem = new ToolStripMenuItem($"UDP :{udpPort}");
            udpItem.Click += (s, e) => ConnectUdpRequested?.Invoke();
            _connectDropdown.Items.Add(udpItem);
        }

        /// <summary>
        /// Decode ArduPilot Copter custom_mode to human-readable name.
        /// </summary>
        private static string DecodeCopterMode(uint mode) => mode switch
        {
            0 => "Stabilize",
            1 => "Acro",
            2 => "AltHold",
            3 => "Auto",
            4 => "Guided",
            5 => "Loiter",
            6 => "RTL",
            7 => "Circle",
            9 => "Land",
            11 => "Drift",
            13 => "Sport",
            14 => "Flip",
            15 => "AutoTune",
            16 => "PosHold",
            17 => "Brake",
            18 => "Throw",
            19 => "Avoid_ADSB",
            20 => "Guided_NoGPS",
            21 => "SmartRTL",
            22 => "FlowHold",
            23 => "Follow",
            24 => "ZigZag",
            25 => "SystemId",
            26 => "Heli_Autorotate",
            27 => "Auto_RTL",
            _ => "Mode" + mode,
        };

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !TopMost)
            {
                Hide();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static void TabDrawItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabs) return;
            var page = tabs.TabPages[e.Index];
            bool selected = (e.Index == tabs.SelectedIndex);
            using var bgBrush = new SolidBrush(selected ? Theme.TabBg : Theme.FormBg);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, page.Text, e.Font, e.Bounds,
                selected ? Theme.FormFg : Theme.HintFg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
