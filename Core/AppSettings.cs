using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RcConnector.Core
{
    internal enum SourceMode { COM, BLE, UDP, Joystick }

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
        public int JoystickDeviceId { get; set; } = -1;
        public string? JoystickDeviceName { get; set; }
        public int JoystickPollHz { get; set; } = 20; // 1-50 Hz
        public JoystickMapping JoystickMapping { get; set; } = new();
        public Dictionary<string, JoystickMapping> JoystickMappings { get; set; } = new();

        // MAVLink output (listen port — replies to sender address)
        public int MavlinkPort { get; set; } = 14555;
        public int MavlinkSysId { get; set; } = 255;

        // RC forward: send parsed channels as "RC 1500,1500,...\n" via UDP
        public bool RcForwardEnabled { get; set; } = false;
        public string RcForwardIp { get; set; } = "127.0.0.1";
        public int RcForwardPort { get; set; } = 14560;

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
        public string ThemeMode { get; set; } = "auto"; // "auto", "light", "dark"

        /// <summary>
        /// Get mapping for a specific joystick device. Falls back to default JoystickMapping.
        /// </summary>
        public JoystickMapping GetJoystickMapping(string? deviceName)
        {
            if (deviceName != null && JoystickMappings.TryGetValue(deviceName, out var mapping))
                return mapping;
            return JoystickMapping;
        }

        /// <summary>
        /// Store mapping for a specific joystick device by name.
        /// </summary>
        public void SetJoystickMapping(string? deviceName, JoystickMapping mapping)
        {
            if (deviceName != null)
                JoystickMappings[deviceName] = mapping;
            else
                JoystickMapping = mapping;
        }

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
