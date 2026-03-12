using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using RcConnector.Core;

namespace RcConnector
{
    /// <summary>
    /// Joystick channel mapping editor: 8 RC channels, each assignable to
    /// None / Axis (X-V) / Button Group. Includes PWM bar visualization.
    /// </summary>
    internal sealed class JoystickMappingForm : Form
    {
        private const int NUM_CH = JoystickMapping.NUM_MAPPED_CHANNELS;
        private const int ROW_H = 30;
        private const int PWM_BAR_W = 120;
        private const int PWM_BAR_H = 14;

        private readonly Dictionary<string, JoystickMapping> _savedMappings;
        private readonly ChannelRow[] _rows = new ChannelRow[NUM_CH];
        private readonly ComboBox _cboDevice;
        private readonly CheckBox _chkLive;
        private readonly Timer _liveTimer;

        private int _joystickDeviceId;
        private string? _selectedDeviceName;
        private int[] _livePwm = new int[NUM_CH]; // current PWM values for live preview

        /// <summary>Fired when user clicks Apply. Includes device name for per-device storage.</summary>
        public event Action<string?, JoystickMapping>? ApplyRequested;

        public JoystickMappingForm(JoystickMapping mapping, int joystickDeviceId,
            Dictionary<string, JoystickMapping> savedMappings, string? currentDeviceName)
        {
            _savedMappings = savedMappings;
            _joystickDeviceId = joystickDeviceId;
            _selectedDeviceName = currentDeviceName;

            SuspendLayout();

            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Theme.FormBg;
            ForeColor = Theme.FormFg;
            Text = L.Get("joymap_title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            int y = 8;

            // Device selector
            Controls.Add(new Label
            {
                Text = L.Get("joymap_device"),
                Location = new Point(8, y + 3),
                AutoSize = true,
            });
            _cboDevice = new ComboBox
            {
                Location = new Point(70, y),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                DrawMode = Theme.IsDark ? DrawMode.OwnerDrawFixed : DrawMode.Normal,
            };
            if (Theme.IsDark)
                _cboDevice.DrawItem += ComboDrawItem;
            PopulateDeviceList(joystickDeviceId);
            _cboDevice.SelectedIndexChanged += OnDeviceChanged;
            Controls.Add(_cboDevice);

            y += 28;

            // Header row
            AddHeaderLabel("CH", 8, y, 30);
            AddHeaderLabel(L.Get("joymap_source"), 42, y, 80);
            AddHeaderLabel("", 126, y, 70); // axis/detail column
            AddHeaderLabel(L.Get("joymap_invert"), 200, y, 30);
            AddHeaderLabel(L.Get("joymap_pwm"), 240, y, PWM_BAR_W + 50);
            y += 20;

            // Channel rows
            for (int i = 0; i < NUM_CH; i++)
            {
                _rows[i] = CreateChannelRow(i, mapping.Channels[i], y);
                y += ROW_H;
            }

            y += 6;

            // Separator
            Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Location = new Point(8, y), Size = new Size(420, 2) });
            y += 8;

            // Live preview checkbox
            _chkLive = new CheckBox
            {
                Text = L.Get("joymap_live"),
                Location = new Point(8, y),
                AutoSize = true,
                Checked = _joystickDeviceId >= 0,
                Enabled = _joystickDeviceId >= 0,
            };
            Controls.Add(_chkLive);

            y += 26;

            // Buttons: Save / Load / Defaults / Apply / Close
            var btnSave = new Button
            {
                Text = L.Get("joymap_save"),
                Location = new Point(8, y),
                Size = new Size(75, 24),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            btnSave.Click += OnSaveClick;

            var btnLoad = new Button
            {
                Text = L.Get("joymap_load"),
                Location = new Point(88, y),
                Size = new Size(75, 24),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            btnLoad.Click += OnLoadClick;

            var btnDefaults = new Button
            {
                Text = L.Get("joymap_defaults"),
                Location = new Point(214, y),
                Size = new Size(75, 24),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            btnDefaults.Click += OnDefaultsClick;

            var btnApply = new Button
            {
                Text = L.Get("settings_apply"),
                Location = new Point(296, y),
                Size = new Size(60, 24),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            btnApply.Click += OnApplyClick;

            var btnClose = new Button
            {
                Text = L.Get("settings_close"),
                Location = new Point(362, y),
                Size = new Size(60, 24),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            btnClose.Click += (s, e) => Close();
            CancelButton = btnClose;

            Controls.Add(btnSave);
            Controls.Add(btnLoad);
            Controls.Add(btnDefaults);
            Controls.Add(btnApply);
            Controls.Add(btnClose);

            ClientSize = new Size(434, y + 32);

            // Live preview timer (100ms = 10Hz)
            _liveTimer = new Timer { Interval = 100 };
            _liveTimer.Tick += OnLiveTick;
            if (_chkLive.Checked)
                _liveTimer.Start();

            _chkLive.CheckedChanged += (s, e) =>
            {
                if (_chkLive.Checked) _liveTimer.Start();
                else _liveTimer.Stop();
            };

            ResumeLayout(false);
            PerformLayout();
            Theme.ApplyDarkTitleBar(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _liveTimer.Stop();
            _liveTimer.Dispose();
            base.OnFormClosed(e);
        }

        // -------------------------------------------------------------------
        // Device selector
        // -------------------------------------------------------------------

        private struct DeviceEntry
        {
            public string Name;
            public int Id; // -1 = saved profile, not connected
        }

        private DeviceEntry[] _deviceEntries = Array.Empty<DeviceEntry>();

        private void PopulateDeviceList(int preselectedId)
        {
            _cboDevice.Items.Clear();
            _cboDevice.Items.Add(L.Get("joymap_no_device"));

            var entries = new List<DeviceEntry>();
            var connectedNames = new HashSet<string>();
            int selectedIdx = 0;

            // Connected devices
            var devices = Transport.JoystickTransport.ListDevices();
            for (int i = 0; i < devices.Length; i++)
            {
                entries.Add(new DeviceEntry { Name = devices[i].Name, Id = devices[i].Id });
                connectedNames.Add(devices[i].Name);
                _cboDevice.Items.Add($"{devices[i].Name} (id={devices[i].Id})");
                if (devices[i].Id == preselectedId)
                    selectedIdx = entries.Count; // index in combo (1-based due to "No device")
            }

            // Saved profiles not currently connected
            foreach (var name in _savedMappings.Keys.OrderBy(k => k))
            {
                if (connectedNames.Contains(name))
                    continue;
                entries.Add(new DeviceEntry { Name = name, Id = -1 });
                _cboDevice.Items.Add($"{name} ({L.Get("joymap_saved")})");
                // If this is the current device name and nothing was preselected by ID
                if (selectedIdx == 0 && name == _selectedDeviceName)
                    selectedIdx = entries.Count;
            }

            _deviceEntries = entries.ToArray();
            _cboDevice.SelectedIndex = selectedIdx;
            if (selectedIdx > 0)
            {
                var entry = _deviceEntries[selectedIdx - 1];
                _joystickDeviceId = entry.Id;
                _selectedDeviceName = entry.Name;
            }
            else
            {
                _joystickDeviceId = -1;
            }
        }

        private void OnDeviceChanged(object? sender, EventArgs e)
        {
            int idx = _cboDevice.SelectedIndex;
            if (idx <= 0)
            {
                _joystickDeviceId = -1;
                _selectedDeviceName = null;
                _chkLive.Checked = false;
                _chkLive.Enabled = false;
                _liveTimer.Stop();
            }
            else
            {
                var entry = _deviceEntries[idx - 1];
                _joystickDeviceId = entry.Id;
                _selectedDeviceName = entry.Name;
                _chkLive.Enabled = entry.Id >= 0;
                _chkLive.Checked = entry.Id >= 0;
                if (entry.Id < 0)
                    _liveTimer.Stop();

                // Load mapping for selected device
                if (_savedMappings.TryGetValue(entry.Name, out var mapping))
                    ApplyMappingToUI(mapping);
            }
        }

        // -------------------------------------------------------------------
        // Header
        // -------------------------------------------------------------------

        private void AddHeaderLabel(string text, int x, int y, int width)
        {
            Controls.Add(new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 16),
                ForeColor = Theme.HintFg,
                Font = new Font(Font.FontFamily, 7.5f),
            });
        }

        // -------------------------------------------------------------------
        // Channel row creation
        // -------------------------------------------------------------------

        private ChannelRow CreateChannelRow(int ch, ChannelMapping cfg, int y)
        {
            var row = new ChannelRow { ChannelIndex = ch };

            // Channel number label
            var lblCh = new Label
            {
                Text = L.Get("joymap_channel", ch + 1),
                Location = new Point(8, y + 6),
                Size = new Size(30, 16),
                ForeColor = Theme.ChannelNumFg,
            };
            Controls.Add(lblCh);

            // Source type combo
            row.CboSource = new ComboBox
            {
                Location = new Point(42, y + 2),
                Width = 80,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                DrawMode = Theme.IsDark ? DrawMode.OwnerDrawFixed : DrawMode.Normal,
            };
            if (Theme.IsDark)
                row.CboSource.DrawItem += ComboDrawItem;
            row.CboSource.Items.Add(L.Get("joymap_source_none"));
            row.CboSource.Items.Add(L.Get("joymap_source_axis"));
            row.CboSource.Items.Add(L.Get("joymap_source_buttons"));
            row.CboSource.SelectedIndex = (int)cfg.SourceType;
            Controls.Add(row.CboSource);

            // Axis selector (visible when source = Axis)
            row.CboAxis = new ComboBox
            {
                Location = new Point(126, y + 2),
                Width = 50,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                DrawMode = Theme.IsDark ? DrawMode.OwnerDrawFixed : DrawMode.Normal,
            };
            if (Theme.IsDark)
                row.CboAxis.DrawItem += ComboDrawItem;
            foreach (var ax in Enum.GetNames(typeof(JoystickAxis)))
                row.CboAxis.Items.Add(ax);
            row.CboAxis.SelectedIndex = (int)cfg.Axis;
            row.CboAxis.Visible = cfg.SourceType == ChannelSourceType.Axis;
            Controls.Add(row.CboAxis);

            // Button edit button (visible when source = ButtonGroup)
            row.BtnEditButtons = new Button
            {
                Text = cfg.Buttons.Length > 0
                    ? $"{cfg.Buttons.Length} btn"
                    : L.Get("joymap_buttons_edit"),
                Location = new Point(126, y + 2),
                Size = new Size(66, 22),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                Visible = cfg.SourceType == ChannelSourceType.ButtonGroup,
            };
            int chCapture = ch;
            row.BtnEditButtons.Click += (s, e) => EditButtonGroup(chCapture);
            Controls.Add(row.BtnEditButtons);

            // Invert checkbox
            row.ChkInvert = new CheckBox
            {
                Location = new Point(206, y + 4),
                Size = new Size(16, 16),
                Checked = cfg.Invert,
                Visible = cfg.SourceType == ChannelSourceType.Axis,
            };
            Controls.Add(row.ChkInvert);

            // PWM bar panel (custom painted)
            row.PwmPanel = new Panel
            {
                Location = new Point(240, y + 6),
                Size = new Size(PWM_BAR_W, PWM_BAR_H),
                BackColor = Theme.BarBg,
            };
            row.PwmPanel.Paint += (s, e) => PaintPwmBar(e.Graphics, row);
            Controls.Add(row.PwmPanel);

            // PWM value label
            row.LblPwm = new Label
            {
                Text = cfg.SourceType == ChannelSourceType.None
                    ? L.Get("joymap_passthrough")
                    : "1500",
                Location = new Point(240 + PWM_BAR_W + 4, y + 5),
                Size = new Size(70, 16),
                ForeColor = Theme.ChannelValFg,
                Font = new Font("Consolas", 8f),
            };
            Controls.Add(row.LblPwm);

            // Source change handler
            row.CboSource.SelectedIndexChanged += (s, e) =>
            {
                var src = (ChannelSourceType)row.CboSource.SelectedIndex;
                row.CboAxis.Visible = src == ChannelSourceType.Axis;
                row.BtnEditButtons.Visible = src == ChannelSourceType.ButtonGroup;
                row.ChkInvert.Visible = src == ChannelSourceType.Axis;
                row.LblPwm.Text = src == ChannelSourceType.None
                    ? L.Get("joymap_passthrough") : "1500";
                row.PwmPanel.Invalidate();
            };

            // Store button config
            row.Buttons = (int[])cfg.Buttons.Clone();

            return row;
        }

        // -------------------------------------------------------------------
        // PWM bar painting
        // -------------------------------------------------------------------

        private void PaintPwmBar(Graphics g, ChannelRow row)
        {
            var src = (ChannelSourceType)row.CboSource.SelectedIndex;
            if (src == ChannelSourceType.None)
                return;

            int actualW = row.PwmPanel.ClientSize.Width;
            int actualH = row.PwmPanel.ClientSize.Height;

            int pwm = _livePwm[row.ChannelIndex];
            if (pwm < 1000) pwm = 1500; // default center if no live data

            double frac = (pwm - 1000) / 1000.0;
            int barW = (int)(frac * actualW);

            bool extreme = pwm <= 1010 || pwm >= 1990;
            var color = extreme ? Theme.BarFgExtreme : Theme.BarFg;

            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, 0, 0, barW, actualH);

            // Draw center mark
            int centerX = actualW / 2;
            using var pen = new Pen(Theme.HintFg) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            g.DrawLine(pen, centerX, 0, centerX, actualH);

            // For button groups: draw position markers
            if (src == ChannelSourceType.ButtonGroup && row.Buttons.Length > 0)
            {
                int numPos = row.Buttons.Length + 1;
                using var markerPen = new Pen(Theme.HintFg);
                for (int i = 0; i < numPos; i++)
                {
                    double posF = (double)i / (numPos - 1);
                    int mx = (int)(posF * actualW);
                    g.DrawLine(markerPen, mx, 0, mx, actualH);
                }
            }
        }

        // -------------------------------------------------------------------
        // Button group editor dialog
        // -------------------------------------------------------------------

        private void EditButtonGroup(int chIndex)
        {
            var row = _rows[chIndex];
            using var dlg = new ButtonGroupDialog(chIndex, row.Buttons, _joystickDeviceId);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                row.Buttons = dlg.ResultButtons;
                row.BtnEditButtons.Text = row.Buttons.Length > 0
                    ? $"{row.Buttons.Length} btn"
                    : L.Get("joymap_buttons_edit");
                row.PwmPanel.Invalidate();
            }
        }

        // -------------------------------------------------------------------
        // Live preview
        // -------------------------------------------------------------------

        private void OnLiveTick(object? sender, EventArgs e)
        {
            if (!_chkLive.Checked || _joystickDeviceId < 0)
                return;

            // Read joystick state via JoystickTransport helper
            var joyData = ReadJoystickRaw();
            if (joyData == null)
                return;

            for (int i = 0; i < NUM_CH; i++)
            {
                var src = (ChannelSourceType)_rows[i].CboSource.SelectedIndex;
                int pwm;

                switch (src)
                {
                    case ChannelSourceType.Axis:
                        var axis = (JoystickAxis)_rows[i].CboAxis.SelectedIndex;
                        pwm = GetAxisPwm(joyData.Value, axis, _rows[i].ChkInvert.Checked);
                        break;

                    case ChannelSourceType.ButtonGroup:
                        pwm = JoystickMapping.ButtonGroupToPwm(_rows[i].Buttons, joyData.Value.Buttons);
                        break;

                    default:
                        pwm = 0;
                        break;
                }

                _livePwm[i] = pwm;
                _rows[i].LblPwm.Text = src == ChannelSourceType.None
                    ? L.Get("joymap_passthrough")
                    : pwm.ToString();
                _rows[i].PwmPanel.Invalidate();
            }
        }

        // -------------------------------------------------------------------
        // Raw joystick reading (P/Invoke reuse)
        // -------------------------------------------------------------------

        private JoyRawData? ReadJoystickRaw()
        {
            var info = new Transport.JoystickTransport.JOYINFOEX_Public
            {
                dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Transport.JoystickTransport.JOYINFOEX_Public>(),
                dwFlags = 0xFF, // JOY_RETURNALL
            };

            if (Transport.JoystickTransport.JoyGetPosEx((uint)_joystickDeviceId, ref info) != 0)
                return null;

            var caps = new Transport.JoystickTransport.JOYCAPS_Public();
            Transport.JoystickTransport.JoyGetDevCaps((uint)_joystickDeviceId, ref caps,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<Transport.JoystickTransport.JOYCAPS_Public>());

            return new JoyRawData
            {
                Axes = new uint[] { info.dwXpos, info.dwYpos, info.dwZpos, info.dwRpos, info.dwUpos, info.dwVpos },
                AxisMins = new uint[] { caps.wXmin, caps.wYmin, caps.wZmin, caps.wRmin, caps.wUmin, caps.wVmin },
                AxisMaxs = new uint[] { caps.wXmax, caps.wYmax, caps.wZmax, caps.wRmax, caps.wUmax, caps.wVmax },
                Buttons = info.dwButtons,
            };
        }

        private static int GetAxisPwm(JoyRawData data, JoystickAxis axis, bool invert)
        {
            int idx = (int)axis;
            uint val = data.Axes[idx];
            uint min = data.AxisMins[idx];
            uint max = data.AxisMaxs[idx];

            if (invert)
                val = max - (val - min);

            return AxisToPwm(val, min, max);
        }

        private static int AxisToPwm(uint value, uint min, uint max)
        {
            if (max <= min) return 1500;
            double norm = (double)(value - min) / (max - min);
            double centered = norm - 0.5;
            const double deadzone = 0.05;
            if (Math.Abs(centered) < deadzone)
                centered = 0;
            else
                centered = centered > 0
                    ? (centered - deadzone) / (0.5 - deadzone)
                    : (centered + deadzone) / (0.5 - deadzone);
            int pwm = 1500 + (int)(centered * 500.0);
            return Math.Clamp(pwm, 1000, 2000);
        }

        private struct JoyRawData
        {
            public uint[] Axes;
            public uint[] AxisMins;
            public uint[] AxisMaxs;
            public uint Buttons;
        }

        // -------------------------------------------------------------------
        // Save / Load mapping to file
        // -------------------------------------------------------------------

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        };

        private JoystickMapping BuildMappingFromUI()
        {
            var result = new JoystickMapping();
            for (int i = 0; i < NUM_CH; i++)
            {
                result.Channels[i] = new ChannelMapping
                {
                    SourceType = (ChannelSourceType)_rows[i].CboSource.SelectedIndex,
                    Axis = (JoystickAxis)_rows[i].CboAxis.SelectedIndex,
                    Invert = _rows[i].ChkInvert.Checked,
                    Buttons = (int[])_rows[i].Buttons.Clone(),
                };
            }
            return result;
        }

        private void ApplyMappingToUI(JoystickMapping mapping)
        {
            for (int i = 0; i < NUM_CH; i++)
            {
                var cfg = mapping.Channels[i];
                _rows[i].CboSource.SelectedIndex = (int)cfg.SourceType;
                _rows[i].CboAxis.SelectedIndex = (int)cfg.Axis;
                _rows[i].ChkInvert.Checked = cfg.Invert;
                _rows[i].Buttons = (int[])cfg.Buttons.Clone();
                _rows[i].BtnEditButtons.Text = cfg.Buttons.Length > 0
                    ? $"{cfg.Buttons.Length} btn"
                    : L.Get("joymap_buttons_edit");
            }
        }

        private void OnSaveClick(object? sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = L.Get("joymap_file_filter"),
                DefaultExt = "json",
                FileName = "joystick-mapping.json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            var mapping = BuildMappingFromUI();
            var json = JsonSerializer.Serialize(mapping, _jsonOptions);
            File.WriteAllText(dlg.FileName, json);
        }

        private void OnLoadClick(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = L.Get("joymap_file_filter"),
                DefaultExt = "json",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var mapping = JsonSerializer.Deserialize<JoystickMapping>(json, _jsonOptions);
                if (mapping?.Channels == null || mapping.Channels.Length != NUM_CH)
                    throw new InvalidDataException("Invalid channel count");

                ApplyMappingToUI(mapping);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.Get("joymap_load_error", ex.Message),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // -------------------------------------------------------------------
        // Apply / Defaults
        // -------------------------------------------------------------------

        private void OnApplyClick(object? sender, EventArgs e)
        {
            ApplyRequested?.Invoke(_selectedDeviceName, BuildMappingFromUI());
            Close();
        }

        private void OnDefaultsClick(object? sender, EventArgs e)
        {
            var defaults = new JoystickMapping();
            defaults.Channels = JoystickMapping.CreateDefault();
            ApplyMappingToUI(defaults);
        }

        // -------------------------------------------------------------------
        // ComboBox dark theme draw helper
        // -------------------------------------------------------------------

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

        // -------------------------------------------------------------------
        // Channel row data holder
        // -------------------------------------------------------------------

        private sealed class ChannelRow
        {
            public int ChannelIndex;
            public ComboBox CboSource = null!;
            public ComboBox CboAxis = null!;
            public Button BtnEditButtons = null!;
            public CheckBox ChkInvert = null!;
            public Panel PwmPanel = null!;
            public Label LblPwm = null!;
            public int[] Buttons = Array.Empty<int>();
        }
    }

    // ===================================================================
    // Button group editor sub-dialog
    // ===================================================================

    /// <summary>
    /// Dialog for assigning joystick buttons to a channel's button group.
    /// Shows current buttons, allows add (by pressing on joystick) and remove.
    /// Displays PWM position distribution.
    /// </summary>
    internal sealed class ButtonGroupDialog : Form
    {
        private readonly int _channelIndex;
        private readonly int _joystickDeviceId;
        private readonly ListBox _lstButtons;
        private readonly Label _lblPwmPositions;
        private readonly System.Collections.Generic.List<int> _buttons;
        private readonly Timer _captureTimer;
        private uint _prevButtonState;
        private bool _capturing;

        public int[] ResultButtons => _buttons.ToArray();

        public ButtonGroupDialog(int channelIndex, int[] currentButtons, int joystickDeviceId)
        {
            _channelIndex = channelIndex;
            _joystickDeviceId = joystickDeviceId;
            _buttons = new System.Collections.Generic.List<int>(currentButtons);

            SuspendLayout();

            AutoScaleDimensions = new SizeF(6F, 13F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Theme.FormBg;
            ForeColor = Theme.FormFg;
            Text = L.Get("joymap_btn_title", channelIndex + 1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            int y = 10;

            // Button list
            _lstButtons = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(160, 100),
                BackColor = Theme.InputBg,
                ForeColor = Theme.InputFg,
            };
            Controls.Add(_lstButtons);

            // Add button
            var btnAdd = new Button
            {
                Text = L.Get("joymap_btn_add"),
                Location = new Point(180, y),
                Size = new Size(90, 24),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
                Enabled = joystickDeviceId >= 0,
            };
            btnAdd.Click += OnAddButton;
            Controls.Add(btnAdd);

            // Remove button
            var btnRemove = new Button
            {
                Text = L.Get("joymap_btn_remove"),
                Location = new Point(180, y + 30),
                Size = new Size(90, 24),
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            btnRemove.Click += OnRemoveButton;
            Controls.Add(btnRemove);

            y += 108;

            // PWM positions info
            _lblPwmPositions = new Label
            {
                Location = new Point(10, y),
                Size = new Size(260, 32),
                ForeColor = Theme.LabelFg,
            };
            Controls.Add(_lblPwmPositions);

            y += 40;

            // OK / Cancel
            var btnOk = new Button
            {
                Text = "OK",
                Location = new Point(110, y),
                Size = new Size(60, 24),
                DialogResult = DialogResult.OK,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };
            var btnCancel = new Button
            {
                Text = L.Get("settings_close"),
                Location = new Point(180, y),
                Size = new Size(60, 24),
                DialogResult = DialogResult.Cancel,
                BackColor = Theme.ButtonBg,
                ForeColor = Theme.ButtonFg,
                FlatStyle = Theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard,
            };

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            ClientSize = new Size(280, y + 32);

            // Capture timer for detecting button presses
            _captureTimer = new Timer { Interval = 50 };
            _captureTimer.Tick += OnCaptureTick;

            RefreshList();

            ResumeLayout(false);
            PerformLayout();
            Theme.ApplyDarkTitleBar(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _captureTimer.Stop();
            _captureTimer.Dispose();
            base.OnFormClosed(e);
        }

        private void RefreshList()
        {
            _lstButtons.Items.Clear();
            for (int i = 0; i < _buttons.Count; i++)
                _lstButtons.Items.Add(L.Get("joymap_btn_number", _buttons[i]));

            // Update PWM positions text
            if (_buttons.Count == 0)
            {
                _lblPwmPositions.Text = L.Get("joymap_btn_pwm_positions") + " —";
            }
            else
            {
                int n = _buttons.Count + 1;
                var positions = new string[n];
                for (int i = 0; i < n; i++)
                {
                    double pwm = 1000.0 + (1000.0 * i / (n - 1));
                    positions[i] = ((int)Math.Round(pwm)).ToString();
                }
                _lblPwmPositions.Text = L.Get("joymap_btn_pwm_positions") + " " + string.Join(", ", positions);
            }
        }

        private void OnAddButton(object? sender, EventArgs e)
        {
            // Start capture mode — wait for user to press a button on joystick
            _capturing = true;
            _prevButtonState = ReadButtons();
            _captureTimer.Start();
            Text = L.Get("joymap_btn_press");
        }

        private void OnCaptureTick(object? sender, EventArgs e)
        {
            if (!_capturing) return;

            uint current = ReadButtons();
            uint newPressed = current & ~_prevButtonState; // newly pressed

            if (newPressed != 0)
            {
                // Find which button was pressed
                for (int b = 0; b < 32; b++)
                {
                    if ((newPressed & (1u << b)) != 0)
                    {
                        if (!_buttons.Contains(b))
                        {
                            _buttons.Add(b);
                            RefreshList();
                        }
                        break;
                    }
                }

                _capturing = false;
                _captureTimer.Stop();
                Text = L.Get("joymap_btn_title", _channelIndex + 1);
            }

            _prevButtonState = current;
        }

        private void OnRemoveButton(object? sender, EventArgs e)
        {
            int idx = _lstButtons.SelectedIndex;
            if (idx >= 0)
            {
                _buttons.RemoveAt(idx);
                RefreshList();
            }
        }

        private uint ReadButtons()
        {
            if (_joystickDeviceId < 0) return 0;

            var info = new Transport.JoystickTransport.JOYINFOEX_Public
            {
                dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Transport.JoystickTransport.JOYINFOEX_Public>(),
                dwFlags = 0xFF,
            };
            if (Transport.JoystickTransport.JoyGetPosEx((uint)_joystickDeviceId, ref info) != 0)
                return 0;

            return info.dwButtons;
        }
    }
}
