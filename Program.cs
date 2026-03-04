using System;
using System.Threading;
using System.Windows.Forms;

namespace RcConnector
{
    static class Program
    {
        private const string MutexName = "Global\\RcConnector_SingleInstance";

        [STAThread]
        static void Main()
        {
            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("RC-Connector is already running.", "RC-Connector",
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
