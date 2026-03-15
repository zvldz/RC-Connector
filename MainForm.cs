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
        private readonly Label _statusHz;
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
        private readonly Button _btnJoyMapping;
        private readonly Button _btnSettings;
        private readonly ContextMenuStrip _connectDropdown;

        /// <summary>Events for connect/disconnect actions.</summary>
        public event Action<string>? ConnectSerialRequested;
        public event Action<string, string>? ConnectBleRequested;
        public event Action? ConnectUdpRequested;
        public event Action<int, string>? ConnectJoystickRequested;
        public event Action? DisconnectRequested;
        public event Action? RefreshMenuRequested;
        public event Action? BleScanRequested;
        public event Action? CheckUpdateRequested;
        public event Action? ForceUpdateRequested;
        public event Action? JoystickMappingRequested;
        public event Action? SettingsRequested;

        public MainForm(AppSettings settings)
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Theme.FormBg;
            ForeColor = Theme.FormFg;

            Text = $"{L.Get("form_title")} v{AppInfo.Version}";
            FormBorderStyle = Theme.IsDark ? FormBorderStyle.FixedSingle : FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
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
                AutoSize = true,
                BackColor = Color.FromArgb(40, 40, 40),
            };

            _statusTransport = new Label
            {
                AutoSize = true,
                Font = statusFont,
                ForeColor = Color.LightGray,
                BackColor = Color.Transparent,
                Text = L.Get("status_disconnected"),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 1, 0, 1),
            };

            _statusHz = new Label
            {
                AutoSize = true,
                Font = statusFont,
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(4, 1, 4, 1),
            };

            _statusArm = new Label
            {
                AutoSize = true,
                Font = new Font("Consolas", FONT_SIZE, FontStyle.Bold),
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(4, 1, 4, 1),
            };

            _statusMode = new Label
            {
                AutoSize = true,
                Font = statusFont,
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(4, 1, 4, 1),
            };

            _statusPanel.Controls.Add(_statusMode);
            _statusPanel.Controls.Add(_statusArm);
            _statusPanel.Controls.Add(_statusHz);
            _statusPanel.Controls.Add(_statusTransport);

            // --- Toolbar (connect/disconnect) ---
            int toolbarHeight = 30;
            _connectDropdown = new ContextMenuStrip();
            Theme.Apply(_connectDropdown);

            _btnConnect = new Button
            {
                Text = "\uD83D\uDD17 " + L.Get("menu_connect") + " \u25BC",
                Location = new Point(2, 2),
                Size = new Size(120, toolbarHeight - 4),
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
                Location = new Point(124, 2),
                Size = new Size(100, toolbarHeight - 4),
                Visible = false,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.System,
            };
            _btnDisconnect.Click += (s, e) => DisconnectRequested?.Invoke();

            _btnJoyMapping = new Button
            {
                Text = "\uD83C\uDFAE", // gamepad emoji
                Font = new Font("Segoe UI Emoji", 12f, FontStyle.Bold),
                Dock = DockStyle.Right,
                Width = 30,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.System,
                Padding = Padding.Empty,
            };
            _btnJoyMapping.Click += (s, e) => JoystickMappingRequested?.Invoke();

            _btnSettings = new Button
            {
                Text = "\u2699", // gear emoji ⚙
                Font = new Font("Segoe UI Symbol", 12f, FontStyle.Bold),
                Dock = DockStyle.Right,
                Width = 30,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.System,
                Padding = Padding.Empty,
            };
            _btnSettings.Click += (s, e) => SettingsRequested?.Invoke();

            _toolbarPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = toolbarHeight,
                BackColor = Theme.PanelBg,
                Padding = new Padding(2),
            };
            _toolbarPanel.Controls.Add(_btnConnect);
            _toolbarPanel.Controls.Add(_btnDisconnect);
            _toolbarPanel.Controls.Add(_btnJoyMapping);
            _toolbarPanel.Controls.Add(_btnSettings);

            // --- Tab control ---
            _tabs = Theme.IsDark ? new BorderlessTabControl() : new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            _tabs.Appearance = TabAppearance.FlatButtons;
            _tabs.DrawMode = Theme.IsDark ? TabDrawMode.OwnerDrawFixed : TabDrawMode.Normal;
            if (Theme.IsDark)
                _tabs.DrawItem += TabDrawItem;

            // Tab 1: Channels
            var channelTab = new TabPage(L.Get("tab_channels")) { BackColor = Theme.FormBg, ForeColor = Theme.FormFg };
            _channelPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = false,
                Padding = new Padding(4),
                BorderStyle = Theme.IsDark ? BorderStyle.FixedSingle : BorderStyle.None,
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
                BorderStyle = BorderStyle.None,
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

            var logPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = Theme.IsDark ? BorderStyle.FixedSingle : BorderStyle.None,
            };
            logPanel.Controls.Add(_logBox);
            logTab.Controls.Add(logPanel);
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
            // aboutGrid.Rows.Add(L.Get("about_author"), AppInfo.Author);
            // int githubRowIdx = aboutGrid.Rows.Add("GitHub", "RC-Connector");
            // aboutGrid.Rows[githubRowIdx].Cells[1].Style.ForeColor = Theme.LinkFg;
            // aboutGrid.Rows[githubRowIdx].Cells[1].Style.Font = new Font("Segoe UI", FONT_SIZE, FontStyle.Underline);
            _latestVersionRow = aboutGrid.Rows[latestRowIdx];

            // aboutGrid.CellContentClick += (s, ev) =>
            // {
            //     if (ev.RowIndex == githubRowIdx && ev.ColumnIndex == 1)
            //     {
            //         System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            //             "https://github.com/zvldz/RC-Connector") { UseShellExecute = true });
            //     }
            // };

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

            // Hidden Force Update button — revealed by typing "iddqd" on About tab
            var btnForceUpdate = new Button
            {
                Text = "Force Update",
                AutoSize = true,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.System,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Visible = false,
            };
            btnForceUpdate.Click += (s, ev) => ForceUpdateRequested?.Invoke();

            // Hidden Force Update — typing "iddqd" on About tab
            string cheatBuffer = "";
            const string cheatCode = "iddqd";
            aboutTab.Enter += (s, ev) => cheatBuffer = "";
            aboutTab.Leave += (s, ev) => { cheatBuffer = ""; btnForceUpdate.Visible = false; };

            // Easter egg: 5 clicks on icon within 5 sec → open Settings + Joystick Mapping
            int iconClickCount = 0;
            DateTime iconClickStart = DateTime.MinValue;
            aboutIcon.MouseDown += (s, ev) =>
            {
                var now = DateTime.UtcNow;
                if ((now - iconClickStart).TotalSeconds > 5)
                {
                    iconClickCount = 1;
                    iconClickStart = now;
                    return;
                }
                iconClickCount++;
                if (iconClickCount >= 5)
                {
                    iconClickCount = 0;
                    SettingsRequested?.Invoke();
                    JoystickMappingRequested?.Invoke();
                }
            };
            KeyPreview = true;
            KeyPress += (s, ev) =>
            {
                if (_tabs.SelectedTab != aboutTab) return;
                cheatBuffer += ev.KeyChar;
                if (cheatBuffer.Length > cheatCode.Length)
                    cheatBuffer = cheatBuffer.Substring(cheatBuffer.Length - cheatCode.Length);
                if (cheatBuffer == cheatCode)
                    btnForceUpdate.Visible = true;
            };

            var aboutPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = Theme.IsDark ? BorderStyle.FixedSingle : BorderStyle.None,
            };
            aboutPanel.Layout += (s, ev) =>
            {
                int pad = 10;
                int iconW = aboutIcon.Width;
                int iconH = aboutIcon.Height;
                int gridWidth = aboutPanel.ClientSize.Width - pad * 2;
                aboutIcon.Location = new Point((aboutPanel.ClientSize.Width - iconW) / 2, pad);
                aboutGrid.Location = new Point(pad, iconH + pad * 2);
                int gridH = aboutGrid.Rows.GetRowsHeight(DataGridViewElementStates.Visible) + 2;
                aboutGrid.Size = new Size(gridWidth, gridH);
                aboutGrid.Columns[0].Width = gridWidth / 3;
                aboutGrid.Columns[1].Width = gridWidth - gridWidth / 3 - 2;
                btnCheckUpdate.Location = new Point(pad, aboutGrid.Bottom + pad);
                btnCheckUpdate.Width = gridWidth;
                btnForceUpdate.Location = new Point(pad, btnCheckUpdate.Bottom + 4);
                btnForceUpdate.Width = gridWidth;
            };

            aboutPanel.Controls.Add(btnForceUpdate);
            aboutPanel.Controls.Add(btnCheckUpdate);
            aboutPanel.Controls.Add(aboutGrid);
            aboutPanel.Controls.Add(aboutIcon);
            aboutTab.Controls.Add(aboutPanel);
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
            bool hasRcData, bool droneConnected, bool armed, uint customMode,
            bool unknownFormat = false)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    _statusPanel.SuspendLayout();

                    // Build desired badge values
                    string tText, hzText = "", armText = "", modeText = "";
                    Color tFg = Color.White, tBg, hzBg = Color.Transparent;
                    Color armFg = Color.White, armBg = Color.Transparent;
                    Color modeFg = Color.White, modeBg = Color.Transparent;
                    Color panelBg = Color.FromArgb(40, 40, 40);

                    if (!connected)
                    {
                        tText = $" {L.Get("status_disconnected")} ";
                        tBg = Color.FromArgb(90, 90, 90);
                        if (droneConnected)
                        {
                            modeText = $" {L.Get("status_no_rc")} ";
                            modeBg = Color.FromArgb(160, 80, 20);
                        }
                    }
                    else if (hasRcData && droneConnected)
                    {
                        tText = $" {transportName} ";
                        tBg = Color.FromArgb(50, 100, 50);
                        hzText = $" {hz:0}Hz ";
                        hzBg = Color.FromArgb(50, 80, 140);
                        armText = armed ? L.Get("status_armed") : L.Get("status_disarmed");
                        armBg = armed ? Color.FromArgb(180, 40, 40) : Color.FromArgb(40, 120, 40);
                        string modeName = DecodeCopterMode(customMode);
                        modeText = $" {modeName} ";
                        modeBg = Color.FromArgb(50, 80, 140);
                    }
                    else if (hasRcData && !droneConnected)
                    {
                        tText = $" {transportName} ";
                        tBg = Color.FromArgb(50, 100, 50);
                        hzText = $" {hz:0}Hz ";
                        hzBg = Color.FromArgb(50, 80, 140);
                        panelBg = Color.FromArgb(80, 60, 20);
                        modeText = $" {L.Get("status_no_telemetry")} ";
                        modeBg = Color.FromArgb(160, 80, 20);
                    }
                    else if (!hasRcData && droneConnected)
                    {
                        tText = $" {transportName} ";
                        tBg = Color.FromArgb(160, 80, 20);
                        panelBg = Color.FromArgb(80, 60, 20);
                        armText = armed ? L.Get("status_armed") : L.Get("status_disarmed");
                        armBg = armed ? Color.FromArgb(180, 40, 40) : Color.FromArgb(40, 120, 40);
                        string modeName = DecodeCopterMode(customMode);
                        modeText = $" {modeName} ";
                        modeBg = Color.FromArgb(50, 80, 140);
                    }
                    else if (unknownFormat)
                    {
                        tText = $" {transportName} ";
                        tBg = Color.FromArgb(160, 80, 20);
                        panelBg = Color.FromArgb(80, 40, 20);
                        modeText = $" {L.Get("status_unknown_format")} ";
                        modeBg = Color.FromArgb(180, 40, 40);
                    }
                    else
                    {
                        tText = $" {transportName} ";
                        tBg = Color.FromArgb(160, 80, 20);
                        panelBg = Color.FromArgb(80, 40, 20);
                        modeText = $" {L.Get("status_no_telemetry")} ";
                        modeBg = Color.FromArgb(160, 80, 20);
                    }

                    // Apply only changed properties to avoid flicker
                    SetBadge(_statusTransport, tText, tFg, tBg);
                    SetBadge(_statusHz, hzText, Color.White, hzBg);
                    SetBadge(_statusArm, armText, armFg, armBg);
                    SetBadge(_statusMode, modeText, modeFg, modeBg);
                    if (_statusPanel.BackColor != panelBg)
                        _statusPanel.BackColor = panelBg;

                    // Layout badges left to right
                    int x = _statusTransport.Right + 2;
                    if (_statusHz.Text != "")
                    {
                        _statusHz.Location = new Point(x, 0);
                        x = _statusHz.Right + 2;
                    }
                    if (_statusArm.Text != "")
                    {
                        _statusArm.Location = new Point(x, 0);
                        x = _statusArm.Right + 2;
                    }
                    _statusMode.Location = new Point(x, 0);

                    _statusPanel.ResumeLayout(false);
                }));
            }
            catch (ObjectDisposedException) { }
        }

        private static void SetBadge(Label lbl, string text, Color fg, Color bg)
        {
            if (lbl.Text != text) lbl.Text = text;
            if (lbl.ForeColor != fg) lbl.ForeColor = fg;
            if (lbl.BackColor != bg) lbl.BackColor = bg;
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
                    _btnConnect.Visible = true;
                    _btnDisconnect.Visible = connected;
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

            // BLE devices — refresh first, then devices
            var bleMenu = new ToolStripMenuItem("BLE");
            var refreshItem = new ToolStripMenuItem(L.Get("menu_refresh"));
            refreshItem.Click += (s, e) => BleScanRequested?.Invoke();
            bleMenu.DropDownItems.Add(refreshItem);
            if (bleDevices.Count > 0)
            {
                bleMenu.DropDownItems.Add(new ToolStripSeparator());
                foreach (var (id, name) in bleDevices)
                {
                    var item = new ToolStripMenuItem(name);
                    if (id == settings.BleDeviceId)
                        item.Font = new Font(item.Font, FontStyle.Bold);
                    string devId = id, devName = name;
                    item.Click += (s, e) => ConnectBleRequested?.Invoke(devId, devName);
                    bleMenu.DropDownItems.Add(item);
                }
            }
            _connectDropdown.Items.Add(bleMenu);

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

            // UDP
            var udpItem = new ToolStripMenuItem($"UDP :{udpPort}");
            udpItem.Click += (s, e) => ConnectUdpRequested?.Invoke();
            _connectDropdown.Items.Add(udpItem);

            // Joystick
            var joyMenu = new ToolStripMenuItem("Joystick");
            var joysticks = Transport.JoystickTransport.ListDevices();
            if (joysticks.Length == 0)
            {
                joyMenu.DropDownItems.Add(new ToolStripMenuItem(L.Get("menu_no_joysticks")) { Enabled = false });
            }
            else
            {
                foreach (var (id, name) in joysticks)
                {
                    var item = new ToolStripMenuItem(name);
                    if (id == settings.JoystickDeviceId)
                        item.Font = new Font(item.Font, FontStyle.Bold);
                    int devId = id; string devName = name;
                    item.Click += (s, e) => ConnectJoystickRequested?.Invoke(devId, devName);
                    joyMenu.DropDownItems.Add(item);
                }
            }
            _connectDropdown.Items.Add(joyMenu);
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

    /// <summary>
    /// TabControl subclass that removes the 3D border around tab pages.
    /// </summary>
    /// <summary>
    /// TabControl subclass: removes 3D border and paints dark background behind tabs.
    /// </summary>
    internal sealed class BorderlessTabControl : TabControl
    {
        private const int TCM_ADJUSTRECT = 0x1328;
        private const int WM_PAINT = 0x000F;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == TCM_ADJUSTRECT)
            {
                var rc = (RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(RECT))!;
                rc.Left -= 4;
                rc.Right += 4;
                rc.Top -= 4;
                rc.Bottom += 4;
                System.Runtime.InteropServices.Marshal.StructureToPtr(rc, m.LParam, true);
            }
            base.WndProc(ref m);

            // After default paint, overpaint the entire tab strip with dark background
            if (m.Msg == WM_PAINT && TabCount > 0)
            {
                using var g = CreateGraphics();
                using var bgBrush = new SolidBrush(Theme.FormBg);
                var lastTab = GetTabRect(TabCount - 1);
                int headerHeight = lastTab.Bottom;

                // Fill entire tab header row (covers gaps between/around tabs)
                g.FillRectangle(bgBrush, 0, 0, Width, headerHeight + 4);

                // Redraw each tab on top of the dark background
                for (int i = 0; i < TabCount; i++)
                {
                    var tabRect = GetTabRect(i);
                    bool selected = (i == SelectedIndex);
                    using var tabBrush = new SolidBrush(selected ? Theme.TabBg : Theme.FormBg);
                    g.FillRectangle(tabBrush, tabRect);
                    using var borderPen = new Pen(Theme.HintFg);
                    g.DrawRectangle(borderPen, tabRect);
                    var page = TabPages[i];
                    TextRenderer.DrawText(g, page.Text, Font, tabRect,
                        selected ? Theme.FormFg : Theme.HintFg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
    }
}
