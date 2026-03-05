using System;
using System.Drawing;
using System.Windows.Forms;
using RcConnector.Core;

namespace RcConnector
{
    /// <summary>
    /// Settings window: MAVLink config, UDP ESP port, UI options, language.
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

        // Serial
        private readonly CheckBox _chkDtrRtsFix;

        // UI
        private readonly CheckBox _chkAdaptiveDpi;
        private readonly ComboBox _cboLanguage;
        private readonly CheckBox _chkStartup;

        /// <summary>Fired when user clicks Apply with new settings.</summary>
        public event Action<AppSettings>? ApplyRequested;

        public SettingsForm(AppSettings settings, bool isConnected)
        {
            _settings = settings;

            SuspendLayout();

            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;

            Text = L.Get("settings_title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            int y = 12;
            int controlX = 120;

            // --- MAVLink port ---
            AddLabel(L.Get("settings_mavlink_port"), 10, y);
            _txtMavlinkPort = new TextBox
            {
                Location = new Point(controlX, y),
                Width = 80,
                Text = settings.MavlinkPort.ToString(),
            };
            Controls.Add(_txtMavlinkPort);
            AddHint(L.Get("settings_mavlink_port_hint"), controlX + 85, y);

            y += 26;

            // --- MAVLink System ID ---
            AddLabel(L.Get("settings_mavlink_sysid"), 10, y);
            _txtMavlinkSysId = new TextBox
            {
                Location = new Point(controlX, y),
                Width = 50,
                Text = settings.MavlinkSysId.ToString(),
            };
            Controls.Add(_txtMavlinkSysId);
            AddHint(L.Get("settings_mavlink_sysid_hint"), controlX + 55, y);

            y += 26;

            // --- UDP ESP listen port ---
            AddLabel(L.Get("settings_udp_port"), 10, y);
            _txtUdpPort = new TextBox
            {
                Location = new Point(controlX, y),
                Width = 80,
                Text = settings.UdpListenPort.ToString(),
            };
            Controls.Add(_txtUdpPort);
            AddHint(L.Get("settings_udp_port_hint"), controlX + 85, y);

            y += 28;

            // --- Serial DTR/RTS fix ---
            _chkDtrRtsFix = new CheckBox
            {
                Text = L.Get("settings_dtr_rts"),
                Location = new Point(10, y),
                AutoSize = true,
                Checked = settings.SerialDtrRts,
            };
            Controls.Add(_chkDtrRtsFix);

            y += 22;

            // --- Adaptive DPI ---
            _chkAdaptiveDpi = new CheckBox
            {
                Text = L.Get("settings_adaptive_dpi"),
                Location = new Point(10, y),
                AutoSize = true,
                Checked = settings.AdaptiveDpi,
            };
            Controls.Add(_chkAdaptiveDpi);

            y += 22;

            // --- Language ---
            AddLabel(L.Get("settings_language"), 10, y);
            _cboLanguage = new ComboBox
            {
                Location = new Point(controlX, y),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _cboLanguage.Items.Add(L.Get("settings_lang_auto"));
            _cboLanguage.Items.Add("English");
            _cboLanguage.Items.Add("Українська");
            _cboLanguage.SelectedIndex = settings.Language switch
            {
                "en" => 1,
                "uk" => 2,
                _ => 0,
            };
            Controls.Add(_cboLanguage);

            y += 26;

            // --- Run at startup ---
            _chkStartup = new CheckBox
            {
                Text = L.Get("settings_startup"),
                Location = new Point(10, y),
                AutoSize = true,
                Checked = settings.RunAtStartup,
            };
            Controls.Add(_chkStartup);

            y += 28;

            // --- Separator ---
            Controls.Add(new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                Location = new Point(10, y),
                Size = new Size(340, 2),
            });

            y += 8;

            // --- Buttons ---
            var btnApply = new Button
            {
                Text = L.Get("settings_apply"),
                Location = new Point(185, y),
                Size = new Size(80, 24),
            };
            btnApply.Click += OnApplyClick;

            var btnClose = new Button
            {
                Text = L.Get("settings_close"),
                Location = new Point(270, y),
                Size = new Size(80, 24),
            };
            btnClose.Click += (s, e) => Close();

            Controls.Add(btnApply);
            Controls.Add(btnClose);

            ClientSize = new Size(355, y + 32);

            ResumeLayout(false);
            PerformLayout();
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label
            {
                Text = text,
                Location = new Point(x, y + 3),
                AutoSize = true,
            });
        }

        private void AddHint(string text, int x, int y)
        {
            Controls.Add(new Label
            {
                Text = text,
                Location = new Point(x, y + 3),
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 7.5f),
            });
        }

        private void OnApplyClick(object? sender, EventArgs e)
        {
            string lang = _cboLanguage.SelectedIndex switch
            {
                1 => "en",
                2 => "uk",
                _ => "auto",
            };

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
                SerialDtrRts = _chkDtrRtsFix.Checked,
                AdaptiveDpi = _chkAdaptiveDpi.Checked,
                Language = lang,
                RunAtStartup = _chkStartup.Checked,

                // Preserve UI state
                AlwaysOnTop = _settings.AlwaysOnTop,
                WindowX = _settings.WindowX,
                WindowY = _settings.WindowY,
                FirstRunDone = _settings.FirstRunDone,
            };

            ApplyRequested?.Invoke(newSettings);
            Close();
        }
    }
}
