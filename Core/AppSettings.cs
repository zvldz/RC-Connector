using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RcConnector.Core
{
    internal enum SourceMode { COM, BLE, UDP }

    internal sealed class AppSettings
    {
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RC-Connector");
        private static readonly string SettingsPath =
            Path.Combine(SettingsDir, "settings.json");

        // Transport source
        public SourceMode SourceMode { get; set; } = SourceMode.COM;
        public string? ComPort { get; set; }
        public int UdpListenPort { get; set; } = 14552;
        public string? BleDeviceId { get; set; }
        public string? BleDeviceName { get; set; }

        // MAVLink output (listen port — replies to sender address)
        public int MavlinkPort { get; set; } = 14555;
        public int MavlinkSysId { get; set; } = 255;

        // Serial
        public bool SerialDtrRts { get; set; } = true;

        // UI
        public int WindowX { get; set; } = -1;
        public int WindowY { get; set; } = -1;
        public bool AlwaysOnTop { get; set; } = false;
        public bool AdaptiveDpi { get; set; } = true;
        public string Language { get; set; } = "auto";
        public bool RunAtStartup { get; set; } = false;
        public bool FirstRunDone { get; set; } = false;

        public static AppSettings Load()
        {
            AppSettings settings;
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    settings = new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Settings] Load failed: " + ex.Message);
                settings = new AppSettings();
            }

            // Sync RunAtStartup with actual registry state
            settings.RunAtStartup = IsStartupRegistrySet();
            return settings;
        }

        private static bool IsStartupRegistrySet()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("RC-Connector") != null;
            }
            catch { return false; }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
                Console.WriteLine("[Settings] Saved to " + SettingsPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Settings] Save failed: " + ex.Message);
            }
        }
    }
}
