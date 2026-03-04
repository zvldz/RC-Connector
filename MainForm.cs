using System;
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

        // Base sizes at 96 DPI (100% scaling)
        private const int BASE_BAR_HEIGHT = 16;
        private const int BASE_BAR_SPACING = 2;
        private const int BASE_BAR_WIDTH = 200;
        private const int BASE_LABEL_WIDTH = 22;
        private const int BASE_VALUE_WIDTH = 38;
        private const float BASE_FONT_SIZE = 7.5f;
        private const float BASE_LOG_FONT_SIZE = 8f;
        private const int BASE_STATUS_HEIGHT = 18;

        private readonly TabControl _tabs;
        private readonly Panel _channelPanel;
        private readonly ProgressBar[] _bars = new ProgressBar[CHANNEL_COUNT];
        private readonly Label[] _valueLabels = new Label[CHANNEL_COUNT];
        private readonly Panel _statusPanel;
        private readonly Label _statusTransport;
        private readonly Label _statusArm;
        private readonly Label _statusMode;
        private readonly TextBox _logBox;
        private readonly Button _clearLogButton;

        public MainForm(AppSettings settings)
        {
            Text = "RC-Connector";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MaximizeBox = false;

            // DPI-adaptive scaling (can be disabled in settings → use base sizes)
            float scale = settings.AdaptiveDpi ? DeviceDpi / 96f : 1f;
            int barHeight = (int)(BASE_BAR_HEIGHT * scale);
            int barSpacing = (int)(BASE_BAR_SPACING * scale);
            int barWidth = (int)(BASE_BAR_WIDTH * scale);
            int labelWidth = (int)(BASE_LABEL_WIDTH * scale);
            int valueWidth = (int)(BASE_VALUE_WIDTH * scale);
            float fontSize = BASE_FONT_SIZE * scale;
            float logFontSize = BASE_LOG_FONT_SIZE * scale;
            int statusHeight = (int)(BASE_STATUS_HEIGHT * scale);

            int formWidth = labelWidth + barWidth + valueWidth + (int)(30 * scale);
            int channelAreaHeight = CHANNEL_COUNT * (barHeight + barSpacing) + (int)(10 * scale);

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
            var statusFont = new Font("Consolas", fontSize);
            int pad4 = (int)(4 * scale);

            _statusPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = statusHeight,
                BackColor = Color.FromArgb(40, 40, 40),
            };

            _statusTransport = new Label
            {
                AutoSize = true,
                Font = statusFont,
                ForeColor = Color.LightGray,
                BackColor = Color.Transparent,
                Text = "Disconnected",
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(pad4, 0),
                Height = statusHeight,
            };

            _statusArm = new Label
            {
                AutoSize = true,
                Font = new Font("Consolas", fontSize, FontStyle.Bold),
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Height = statusHeight,
                Padding = new Padding(pad4, 0, pad4, 0),
            };

            _statusMode = new Label
            {
                AutoSize = true,
                Font = statusFont,
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                Height = statusHeight,
                Padding = new Padding(pad4, 0, pad4, 0),
            };

            _statusPanel.Controls.Add(_statusMode);
            _statusPanel.Controls.Add(_statusArm);
            _statusPanel.Controls.Add(_statusTransport);

            // --- Tab control ---
            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
            };

            // Tab 1: Channels
            var channelTab = new TabPage("Channels");
            int pad = (int)(4 * scale);
            _channelPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = false,
                Padding = new Padding(pad),
            };

            int gap = (int)(6 * scale);
            for (int i = 0; i < CHANNEL_COUNT; i++)
            {
                int y = pad + i * (barHeight + barSpacing);

                var chLabel = new Label
                {
                    Text = (i + 1).ToString(),
                    Location = new Point(pad, y),
                    Size = new Size(labelWidth, barHeight),
                    TextAlign = ContentAlignment.MiddleRight,
                    Font = new Font("Consolas", fontSize),
                };

                _bars[i] = new ProgressBar
                {
                    Location = new Point(labelWidth + gap, y),
                    Size = new Size(barWidth, barHeight),
                    Minimum = PWM_MIN,
                    Maximum = PWM_MAX,
                    Value = 1500,
                    Style = ProgressBarStyle.Continuous,
                };

                _valueLabels[i] = new Label
                {
                    Text = "----",
                    Location = new Point(labelWidth + barWidth + gap + (int)(2 * scale), y),
                    Size = new Size(valueWidth, barHeight),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Consolas", fontSize),
                };

                _channelPanel.Controls.Add(chLabel);
                _channelPanel.Controls.Add(_bars[i]);
                _channelPanel.Controls.Add(_valueLabels[i]);
            }

            channelTab.Controls.Add(_channelPanel);
            _tabs.TabPages.Add(channelTab);

            // Tab 2: Log
            var logTab = new TabPage("Log");
            _logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", logFontSize),
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.LightGray,
            };
            _clearLogButton = new Button
            {
                Text = "Clear",
                Dock = DockStyle.Bottom,
                Height = (int)(24 * scale),
            };
            _clearLogButton.Click += (s, e) => _logBox.Clear();

            logTab.Controls.Add(_logBox);
            logTab.Controls.Add(_clearLogButton);
            _tabs.TabPages.Add(logTab);

            // Tab 3: About
            var aboutTab = new TabPage("About");
            var aboutLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Consolas", logFontSize),
                Text = $"{AppInfo.AppName} v{AppInfo.Version}\n" +
                       $"Build: {AppInfo.BuildDate}\n\n" +
                       AppInfo.Author,
            };
            aboutTab.Controls.Add(aboutLabel);
            _tabs.TabPages.Add(aboutTab);

            // Layout
            Controls.Add(_tabs);
            Controls.Add(_statusPanel);

            ClientSize = new Size(formWidth, statusHeight + channelAreaHeight + (int)(30 * scale));
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
                        _bars[i].Value = val;
                        _valueLabels[i].Text = channels[i].ToString();

                        // Color: red at extremes, green in normal range
                        _valueLabels[i].ForeColor =
                            (channels[i] <= 810 || channels[i] >= 2190) ? Color.Red : Color.Black;
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
                        _statusTransport.Text = "Disconnected";
                        _statusPanel.BackColor = Color.FromArgb(40, 40, 40);
                        _statusArm.Text = "";
                        _statusMode.Text = "";
                    }
                    else if (!droneConnected)
                    {
                        _statusTransport.Text = $"{transportName} {hz:0}Hz";
                        _statusPanel.BackColor = Color.FromArgb(80, 60, 20);
                        _statusArm.Text = "";
                        _statusMode.Text = "No drone";
                        _statusMode.ForeColor = Color.Orange;
                        _statusMode.BackColor = Color.Transparent;
                    }
                    else
                    {
                        _statusTransport.Text = $"{transportName} {hz:0}Hz";
                        _statusPanel.BackColor = Color.FromArgb(40, 40, 40);

                        // Armed / Disarmed badge
                        _statusArm.Text = armed ? " ARMED " : " DISARMED ";
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
                        _logBox.Text = _logBox.Text.Substring(_logBox.TextLength - 16000);

                    _logBox.AppendText(entry + Environment.NewLine);
                }));
            }
            catch (ObjectDisposedException) { }
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
    }
}
