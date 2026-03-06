using System;
using System.Reflection;

namespace RcConnector.Core
{
    internal static class AppInfo
    {
        public const string AppName = "RC-Connector";
        private const string DefaultAuthor = "@zvldz & team";

        /// <summary>
        /// Author from author.txt (next to exe) if present, otherwise default.
        /// </summary>
        public static string Author { get; } = LoadAuthor();

        public static string Version
        {
            get
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "0.0.0";
            }
        }

        /// <summary>True if running from installed location (Uninstall.exe exists nearby).</summary>
        public static bool IsInstalled { get; } =
            System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Uninstall.exe"));

        public static string BuildDate
        {
            get
            {
                // Use exe file timestamp as build date
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                    return System.IO.File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd");

                return "unknown";
            }
        }

        private static string LoadAuthor()
        {
            try
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "author.txt");
                if (System.IO.File.Exists(path))
                {
                    string text = System.IO.File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(text))
                        return text;
                }
            }
            catch { }

            return DefaultAuthor;
        }
    }
}
