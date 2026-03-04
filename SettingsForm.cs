using System;
using System.Drawing;
using System.Windows.Forms;
using RcConnector.Core;

namespace RcConnector
{
    /// <summary>
    /// Settings window: MAVLink config, UDP ESP port, UI options.
    /// Transport selection moved to tray Connect submenu.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;

        // MAVLink
        private readonly TextBox _txtMavlinkPort;
        private readonly TextBox _txtMavlinkSysId;

        // UDP ESP source
        private readonly TextBox _txtUdpPort;

        // UI
        private readonly CheckBox _chkAdaptiveDpi;

        /// <summary>Fired when user clicks Apply with new settings.</summary>
        public event Action<AppSettings>? ApplyRequested;

        public SettingsForm(AppSettings settings, bool isConnected)
        {
            _settings = settings;

            Text = "RC-Connector Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            int y = 12;
            int controlX = 120;

            // --- MAVLink port ---
            AddLabel("MAVLink port:", 10, y);
            _txtMavlinkPort = new TextBox
            {
                Location = new Point(controlX, y),
                Width = 80,
                Text = settings.MavlinkPort.ToString(),
            };
            Controls.Add(_txtMavlinkPort);

            var lblPortHint = new Label
            {
                Text = "14550 = GCS default, avoid conflict",
                Location = new Point(controlX + 85, y + 3),
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 7.5f),
            };
            Controls.Add(lblPortHint);

            y += 30;

            // --- MAVLink System ID ---
            AddLabel("MAVLink sysid:", 10, y);
            _txtMavlinkSysId = new TextBox
            {
                Location = new Point(controlX, y),
                Width = 50,
                Text = settings.MavlinkSysId.ToString(),
            };
            Controls.Add(_txtMavlinkSysId);

            var lblSysIdHint = new Label
            {
                Text = "Must match SYSID_MYGCS on drone",
                Location = new Point(controlX + 55, y + 3),
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 7.5f),
            };
            Controls.Add(lblSysIdHint);

            y += 30;

            // --- UDP ESP listen port ---
            AddLabel("UDP ESP port:", 10, y);
            _txtUdpPort = new TextBox
            {
                Location = new Point(controlX, y),
                Width = 80,
                Text = settings.UdpListenPort.ToString(),
            };
            Controls.Add(_txtUdpPort);

            var lblUdpHint = new Label
            {
                Text = "ESP32 WiFi source port",
                Location = new Point(controlX + 85, y + 3),
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 7.5f),
            };
            Controls.Add(lblUdpHint);

            y += 36;

            // --- Adaptive DPI ---
            _chkAdaptiveDpi = new CheckBox
            {
                Text = "Adaptive UI scaling",
                Location = new Point(10, y),
                AutoSize = true,
                Checked = settings.AdaptiveDpi,
            };
            Controls.Add(_chkAdaptiveDpi);

            y += 30;

            // --- Separator ---
            var sep = new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                Location = new Point(10, y),
                Size = new Size(340, 2),
            };
            Controls.Add(sep);

            y += 12;

            // --- Buttons ---
            var btnApply = new Button
            {
                Text = "Apply",
                Location = new Point(200, y),
                Width = 75,
                Height = 28,
            };
            btnApply.Click += OnApplyClick;

            var btnClose = new Button
            {
                Text = "Close",
                Location = new Point(280, y),
                Width = 75,
                Height = 28,
            };
            btnClose.Click += (s, e) => Close();

            Controls.Add(btnApply);
            Controls.Add(btnClose);

            ClientSize = new Size(370, y + 44);
        }

        private void AddLabel(string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text,
                Location = new Point(x, y + 3),
                AutoSize = true,
            };
            Controls.Add(lbl);
        }

        private void OnApplyClick(object? sender, EventArgs e)
        {
            var newSettings = new AppSettings
            {
                // Preserve current transport settings (managed by tray Connect menu)
                SourceMode = _settings.SourceMode,
                ComPort = _settings.ComPort,
                BleDeviceId = _settings.BleDeviceId,
                BleDeviceName = _settings.BleDeviceName,

                // Editable settings
                MavlinkPort = int.TryParse(_txtMavlinkPort.Text, out int mp) && mp > 0 && mp <= 65535
                    ? mp : _settings.MavlinkPort,
                MavlinkSysId = int.TryParse(_txtMavlinkSysId.Text, out int sid) && sid >= 1 && sid <= 255
                    ? sid : _settings.MavlinkSysId,
                UdpListenPort = int.TryParse(_txtUdpPort.Text, out int up) && up > 0 && up <= 65535
                    ? up : _settings.UdpListenPort,
                AdaptiveDpi = _chkAdaptiveDpi.Checked,

                // Preserve UI state
                AlwaysOnTop = _settings.AlwaysOnTop,
                WindowX = _settings.WindowX,
                WindowY = _settings.WindowY,
            };

            ApplyRequested?.Invoke(newSettings);
            Close();
        }
    }
}
