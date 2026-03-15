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

        // Serial format
        private readonly ComboBox _cboSerialFormat;

        // MAVLink
        private readonly TextBox _txtMavlinkPort;
        private readonly TextBox _txtMavlinkSysId;

        // UDP ESP source
        private readonly TextBox _txtUdpPort;

        // Send rate
        private readonly ComboBox _cboSendRate;

        // Serial
        private readonly CheckBox _chkDtrRtsFix;

        // RC Forward
        private readonly CheckBox _chkRcForward;
        private readonly TextBox _txtRcForwardIp;
        private readonly TextBox _txtRcForwardPort;

        // Telemetry
        private readonly CheckBox _chkIgnoreDrone;

        // UI
        private readonly ComboBox _cboLanguage;
        private readonly ComboBox _cboTheme;
        private readonly Label _lblThemeHint;
        private readonly CheckBox _chkStartup;

        /// <summary>Fired when user clicks Apply with new settings.</summary>
        public event Action<AppSettings>? ApplyRequested;

        public SettingsForm(AppSettings settings, bool isConnected)
        {
            _settings = settings;

            SuspendLayout();

            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Theme.FormBg;
            ForeColor = Theme.FormFg;

            Text = L.Get("settings_title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            int y = 12;
            int controlX = 120;

            // --- Data format ---
            AddLabel(L.Get("settings_data_format"), 10, y);
            _cboSerialFormat = new ComboBox
            {
                Location = new Point(controlX, y),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                DrawMode = Theme.IsDark ? DrawMode.OwnerDrawFixed : DrawMode.Normal,
            };
            if (Theme.IsDark)
                _cboSerialFormat.DrawItem += ComboDrawItem;
            _cboSerialFormat.Items.Add(L.Get("settings_format_auto"));
            _cboSerialFormat.Items.Add("R2D2");
            _cboSerialFormat.Items.Add("ESP-Bridge");
            _cboSerialFormat.SelectedIndex = settings.SerialFormat switch
            {
                SerialFormat.R2D2 => 1,
                SerialFormat.EspBridge => 2,
                _ => 0,
            };
            Controls.Add(_cboSerialFormat);
            AddHint(L.Get("settings_data_format_hint"), controlX + 125, y);

            y += 28;

            // --- Send rate (global throttle for all transports) ---
            AddLabel(L.Get("settings_send_rate"), 10, y);
            _cboSendRate = new ComboBox
            {
                Location = new Point(controlX, y),
                Width = 60,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                DrawMode = Theme.IsDark ? DrawMode.OwnerDrawFixed : DrawMode.Normal,
            };
            if (Theme.IsDark)
                _cboSendRate.DrawItem += ComboDrawItem;
            int[] rates = { 10, 20, 30, 40, 50 };
            int selectedIdx = 0;
            for (int i = 0; i < rates.Length; i++)
            {
                _cboSendRate.Items.Add(rates[i].ToString());
                if (rates[i] == settings.RcSendRateHz)
                    selectedIdx = i;
            }
            _cboSendRate.SelectedIndex = selectedIdx;
            Controls.Add(_cboSendRate);
            AddHint(L.Get("settings_send_rate_hint"), controlX + 65, y);

            y += 28;

            // --- MAVLink port ---
            AddLabel(L.Get("settings_mavlink_port"), 10, y);
            _txtMavlinkPort = new TextBox
            {
                Location = new Point(controlX, y),
                Width = 80,
                Text = settings.MavlinkPort.ToString(),
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
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
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
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
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
            };
            Controls.Add(_txtUdpPort);
            AddHint(L.Get("settings_udp_port_hint"), controlX + 85, y);

            y += 26;

            // --- Serial DTR/RTS fix ---
            _chkDtrRtsFix = new CheckBox
            {
                Text = L.Get("settings_dtr_rts"),
                Location = new Point(10, y),
                AutoSize = true,
                Checked = settings.SerialDtrRts,
            };
            Controls.Add(_chkDtrRtsFix);
            y += 15;
            AddHint(L.Get("settings_dtr_rts_hint"), 28, y);

            y += 24;

            // --- RC Forward ---
            _chkRcForward = new CheckBox
            {
                Text = L.Get("settings_rc_forward"),
                Location = new Point(10, y),
                AutoSize = true,
                Checked = settings.RcForwardEnabled,
            };
            Controls.Add(_chkRcForward);
            y += 20;

            AddLabel(L.Get("settings_rc_forward_ip"), 28, y);
            _txtRcForwardIp = new TextBox
            {
                Location = new Point(controlX, y),
                Width = 120,
                Text = settings.RcForwardIp,
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                Enabled = settings.RcForwardEnabled,
            };
            Controls.Add(_txtRcForwardIp);

            AddLabel(L.Get("settings_rc_forward_port"), 248, y);
            _txtRcForwardPort = new TextBox
            {
                Location = new Point(285, y),
                Width = 50,
                Text = settings.RcForwardPort.ToString(),
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                Enabled = settings.RcForwardEnabled,
            };
            Controls.Add(_txtRcForwardPort);
            y += 15;
            AddHint(L.Get("settings_rc_forward_hint"), 28, y);

            _chkRcForward.CheckedChanged += (s, e) =>
            {
                _txtRcForwardIp.Enabled = _chkRcForward.Checked;
                _txtRcForwardPort.Enabled = _chkRcForward.Checked;
            };

            y += 24;

            // --- Ignore drone telemetry ---
            _chkIgnoreDrone = new CheckBox
            {
                Text = L.Get("settings_ignore_drone"),
                Location = new Point(10, y),
                AutoSize = true,
                Checked = settings.IgnoreDroneTelemetry,
            };
            Controls.Add(_chkIgnoreDrone);
            y += 15;
            AddHint(L.Get("settings_ignore_drone_hint"), 28, y);

            y += 24;

            // --- Language ---
            AddLabel(L.Get("settings_language"), 10, y);
            _cboLanguage = new ComboBox
            {
                Location = new Point(controlX, y),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                DrawMode = Theme.IsDark ? DrawMode.OwnerDrawFixed : DrawMode.Normal,
            };
            if (Theme.IsDark)
                _cboLanguage.DrawItem += ComboDrawItem;
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

            // --- Theme ---
            AddLabel(L.Get("settings_theme"), 10, y);
            _cboTheme = new ComboBox
            {
                Location = new Point(controlX, y),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                DrawMode = Theme.IsDark ? DrawMode.OwnerDrawFixed : DrawMode.Normal,
            };
            if (Theme.IsDark)
                _cboTheme.DrawItem += ComboDrawItem;
            _cboTheme.Items.Add(L.Get("settings_theme_auto"));
            _cboTheme.Items.Add(L.Get("settings_theme_light"));
            _cboTheme.Items.Add(L.Get("settings_theme_dark"));
            _cboTheme.SelectedIndex = settings.ThemeMode switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0,
            };

            _lblThemeHint = new Label
            {
                Text = "\u2139 " + L.Get("settings_theme_hint"),
                Location = new Point(controlX + 125, y + 3),
                AutoSize = true,
                ForeColor = Theme.HintFg,
                Font = new Font(Font.FontFamily, 7.5f),
                Visible = false,
            };
            _cboTheme.SelectedIndexChanged += (s, e) =>
            {
                string selected = _cboTheme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "auto" };
                _lblThemeHint.Visible = selected != settings.ThemeMode;
            };
            Controls.Add(_cboTheme);
            Controls.Add(_lblThemeHint);

            y += 26;

            // --- Run at startup ---
            _chkStartup = new CheckBox
            {
                Text = L.Get("settings_startup"),
                Location = new Point(10, y),
                AutoSize = true,
                Checked = settings.RunAtStartup,
                Enabled = AppInfo.IsInstalled,
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
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            btnApply.Click += OnApplyClick;

            var btnClose = new Button
            {
                Text = L.Get("settings_close"),
                Location = new Point(270, y),
                Size = new Size(80, 24),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            btnClose.Click += (s, e) => Close();
            CancelButton = btnClose;

            Controls.Add(btnApply);
            Controls.Add(btnClose);

            ClientSize = new Size(390, y + 32);

            ResumeLayout(false);
            PerformLayout();

            Theme.ApplyDarkTitleBar(this);
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
                ForeColor = Theme.HintFg,
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
                JoystickDeviceId = _settings.JoystickDeviceId,
                JoystickDeviceName = _settings.JoystickDeviceName,
                JoystickMappings = _settings.JoystickMappings,

                // Editable settings
                SerialFormat = _cboSerialFormat.SelectedIndex switch
                {
                    1 => SerialFormat.R2D2,
                    2 => SerialFormat.EspBridge,
                    _ => SerialFormat.Auto,
                },
                MavlinkPort = int.TryParse(_txtMavlinkPort.Text, out int mp) && mp > 0 && mp <= 65535
                    ? mp : _settings.MavlinkPort,
                MavlinkSysId = int.TryParse(_txtMavlinkSysId.Text, out int sid) && sid >= 1 && sid <= 255
                    ? sid : _settings.MavlinkSysId,
                UdpListenPort = int.TryParse(_txtUdpPort.Text, out int up) && up > 0 && up <= 65535
                    ? up : _settings.UdpListenPort,
                RcSendRateHz = int.TryParse(_cboSendRate.SelectedItem?.ToString(), out int rr) ? rr : 20,
                SerialDtrRts = _chkDtrRtsFix.Checked,
                RcForwardEnabled = _chkRcForward.Checked,
                RcForwardIp = _txtRcForwardIp.Text.Trim(),
                RcForwardPort = int.TryParse(_txtRcForwardPort.Text, out int fp) && fp > 0 && fp <= 65535
                    ? fp : _settings.RcForwardPort,
                IgnoreDroneTelemetry = _chkIgnoreDrone.Checked,
                AdaptiveDpi = true,
                Language = lang,
                ThemeMode = _cboTheme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "auto" },
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

        private static void ComboDrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || sender is not ComboBox combo) return;
            e.DrawBackground();
            using var brush = new SolidBrush(
                (e.State & DrawItemState.Selected) != 0 ? Theme.MenuHighlight : Theme.InputBg);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, combo.Items[e.Index]?.ToString(),
                e.Font, e.Bounds, Theme.InputFg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
