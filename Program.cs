using System;
using System.Threading;
using System.Windows.Forms;
using RcConnector.Core;

namespace RcConnector
{
    static class Program
    {
        private const string MutexName = "Global\\RcConnector_SingleInstance";

        [STAThread]
        static void Main()
        {
            // Load settings early to init language before any UI
            var settings = AppSettings.Load();
            L.Init(settings.Language);

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
