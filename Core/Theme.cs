using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RcConnector.Core
{
    /// <summary>
    /// Detects Windows dark/light theme and provides matching colors.
    /// </summary>
    internal static class Theme
    {
        public static bool IsDark { get; private set; } = DetectDarkMode();

        /// <summary>
        /// Initialize theme from settings. Call once at startup before creating any forms.
        /// </summary>
        public static void Init(string themeMode)
        {
            IsDark = themeMode switch
            {
                "light" => false,
                "dark" => true,
                _ => DetectDarkMode(), // "auto"
            };
        }

        // Form backgrounds
        public static Color FormBg => IsDark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
        public static Color FormFg => IsDark ? Color.FromArgb(230, 230, 230) : SystemColors.ControlText;

        // Panel / tab backgrounds
        public static Color PanelBg => IsDark ? Color.FromArgb(40, 40, 40) : SystemColors.Control;
        public static Color TabBg => IsDark ? Color.FromArgb(45, 45, 45) : SystemColors.Control;

        // Input controls (TextBox, ComboBox)
        public static Color InputBg => IsDark ? Color.FromArgb(55, 55, 55) : SystemColors.Window;
        public static Color InputFg => IsDark ? Color.FromArgb(230, 230, 230) : SystemColors.WindowText;

        // Labels
        public static Color LabelFg => IsDark ? Color.FromArgb(210, 210, 210) : SystemColors.ControlText;
        public static Color HintFg => IsDark ? Color.FromArgb(140, 140, 140) : Color.Gray;

        // Buttons
        public static Color ButtonBg => IsDark ? Color.FromArgb(60, 60, 60) : SystemColors.Control;
        public static Color ButtonFg => IsDark ? Color.FromArgb(230, 230, 230) : SystemColors.ControlText;

        // Grid (About tab)
        public static Color GridBg => IsDark ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
        public static Color GridHeaderBg => IsDark ? Color.FromArgb(55, 55, 60) : Color.FromArgb(240, 240, 240);
        public static Color GridAltBg => IsDark ? Color.FromArgb(50, 50, 55) : Color.FromArgb(245, 245, 250);
        public static Color GridLine => IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);

        // Channel bars
        public static Color BarBg => IsDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220);
        public static Color BarFg => Color.FromArgb(60, 150, 60); // green — same in both
        public static Color BarFgExtreme => Color.FromArgb(200, 50, 50); // red — same in both
        public static Color ChannelNumFg => IsDark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(80, 80, 80);
        public static Color ChannelValFg => IsDark ? Color.FromArgb(210, 210, 210) : Color.Black;
        public static Color ChannelValExtremeFg => Color.FromArgb(255, 80, 80); // red in both

        // Log
        public static Color LogBg => Color.FromArgb(30, 30, 30); // always dark
        public static Color LogFg => Color.LightGray;
        public static Color LogAltBg => Color.FromArgb(50, 50, 58);

        // Link
        public static Color LinkFg => IsDark ? Color.FromArgb(80, 160, 255) : Color.FromArgb(0, 102, 204);

        // Separator
        public static Color SepColor => IsDark ? Color.FromArgb(70, 70, 70) : SystemColors.ControlDark;

        // CheckBox / text on forms
        public static Color CheckFg => LabelFg;

        // Context menu
        public static Color MenuBg => IsDark ? Color.FromArgb(43, 43, 43) : SystemColors.Control;
        public static Color MenuFg => IsDark ? Color.FromArgb(230, 230, 230) : SystemColors.ControlText;
        public static Color MenuHighlight => IsDark ? Color.FromArgb(65, 65, 65) : SystemColors.Highlight;
        public static Color MenuBorder => IsDark ? Color.FromArgb(70, 70, 70) : SystemColors.ControlDark;
        public static Color MenuSep => IsDark ? Color.FromArgb(60, 60, 60) : SystemColors.ControlDark;

        /// <summary>Apply dark theme to a ContextMenuStrip.</summary>
        public static void Apply(ContextMenuStrip menu)
        {
            if (!IsDark) return;
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = MenuBg;
            menu.ForeColor = MenuFg;
        }

        /// <summary>Enable dark title bar on Windows 10/11.</summary>
        public static void ApplyDarkTitleBar(Form form)
        {
            if (!IsDark) return;
            try
            {
                // DWMWA_USE_IMMERSIVE_DARK_MODE = 20
                int val = 1;
                DwmSetWindowAttribute(form.Handle, 20, ref val, sizeof(int));
            }
            catch { }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);

        private static bool DetectDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                return val is int i && i == 0;
            }
            catch { return false; }
        }
    }

    /// <summary>Custom renderer for dark context menus.</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rc = new Rectangle(Point.Empty, e.Item.Size);
            if (e.Item.Selected && e.Item.Enabled)
            {
                using var brush = new SolidBrush(Theme.MenuHighlight);
                e.Graphics.FillRectangle(brush, rc);
            }
            else
            {
                using var brush = new SolidBrush(Theme.MenuBg);
                e.Graphics.FillRectangle(brush, rc);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.MenuFg : Theme.HintFg;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var pen = new Pen(Theme.MenuSep);
            e.Graphics.DrawLine(pen, 30, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.MenuFg;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(Theme.MenuBg);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }
    }

    internal sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder => Theme.MenuBorder;
        public override Color MenuItemBorder => Theme.MenuHighlight;
        public override Color MenuItemSelected => Theme.MenuHighlight;
        public override Color MenuStripGradientBegin => Theme.MenuBg;
        public override Color MenuStripGradientEnd => Theme.MenuBg;
        public override Color MenuItemSelectedGradientBegin => Theme.MenuHighlight;
        public override Color MenuItemSelectedGradientEnd => Theme.MenuHighlight;
        public override Color MenuItemPressedGradientBegin => Theme.MenuHighlight;
        public override Color MenuItemPressedGradientEnd => Theme.MenuHighlight;
        public override Color ToolStripDropDownBackground => Theme.MenuBg;
        public override Color ImageMarginGradientBegin => Theme.MenuBg;
        public override Color ImageMarginGradientMiddle => Theme.MenuBg;
        public override Color ImageMarginGradientEnd => Theme.MenuBg;
        public override Color SeparatorDark => Theme.MenuSep;
        public override Color SeparatorLight => Theme.MenuBg;
    }
}
