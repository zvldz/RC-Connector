using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using RcConnector.Core;

namespace RcConnector
{
    static class Program
    {
        private const string MutexName = "Global\\RcConnector_SingleInstance";

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleOutputCP(uint cp);

        [STAThread]
        static void Main()
        {
            try
            {
                SetConsoleOutputCP(65001);
                var utf8Writer = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(utf8Writer);
            }
            catch { }

            // Load settings early to init language before any UI
            var settings = AppSettings.Load();
            L.Init(settings.Language);
            Theme.Init(settings.ThemeMode);

            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(L.Get("already_running"), "RC-Connector",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            using var trayApp = new TrayApp();
            Application.Run();
        }
    }
}
