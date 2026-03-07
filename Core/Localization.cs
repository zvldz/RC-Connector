using System.Collections.Generic;
using System.Globalization;

namespace RcConnector.Core
{
    /// <summary>
    /// Simple localization: English (en) and Ukrainian (uk).
    /// Usage: L.Get("key") or L._("key")
    /// </summary>
    internal static class L
    {
        private static string _lang = "en";

        private static readonly Dictionary<string, string> EN = new()
        {
            // Program.cs
            ["already_running"] = "RC-Connector is already running.",

            // Tray menu
            ["menu_connect"] = "Connect",
            ["menu_disconnect"] = "Disconnect",
            ["menu_show"] = "Show",
            ["menu_always_on_top"] = "Always on Top",
            ["menu_settings"] = "Settings...",
            ["menu_exit"] = "Exit",
            ["menu_no_ports"] = "No ports",
            ["menu_refresh"] = "Refresh",
            ["menu_scanning"] = "Scanning...",
            ["menu_no_joysticks"] = "No joysticks",
            ["menu_joystick_mapping"] = "Joystick Mapping...",

            // Tray tooltips
            ["tip_disconnected"] = "RC-Connector: Disconnected",
            ["tip_disconnected_drone_ok"] = "RC-Connector: Disconnected, drone OK",
            ["tip_ble_auth_failed"] = "RC-Connector: BLE auth failed. Re-pair device.",
            ["tip_connecting"] = "RC-Connector: Connecting...",
            ["tip_ok"] = "RC-Connector: OK {0}Hz",
            ["tip_ok_armed"] = "RC-Connector: OK {0}Hz ARMED",
            ["tip_rc_ok_no_drone"] = "RC-Connector: RC OK, no drone",
            ["tip_no_rc_drone_ok"] = "RC-Connector: No RC data, drone OK",
            ["tip_connected_no_data"] = "RC-Connector: Connected, no data",

            // Log messages
            ["log_mavlink_started"] = "MAVLink started: port={0}, sysid={1}",
            ["log_mavlink_start_failed"] = "MAVLink start failed: {0}",
            ["log_mavlink_restarted"] = "MAVLink restarted: port={0}, sysid={1}",
            ["log_mavlink_restart_failed"] = "MAVLink restart failed: {0}",
            ["log_connected_to"] = "Connected to {0}",
            ["log_connect_failed"] = "Connect failed: {0}",
            ["log_connecting_ble"] = "Connecting to BLE: {0}",
            ["log_ble_auth_failed"] = "BLE auth failed: {0}",
            ["log_ble_found"] = "BLE: found {0} device(s)",
            ["log_listening_udp"] = "Listening for ESP32 on UDP:{0}",
            ["log_joystick_connected"] = "Joystick connected: {0}",
            ["log_disconnected"] = "Disconnected",
            ["log_disconnected_reason"] = "Disconnected: {0}",
            ["log_drone_connected"] = "Drone connected (sysid={0})",
            ["log_drone_disconnected"] = "Drone disconnected",
            ["log_settings_updated"] = "Settings updated",

            // MainForm
            ["form_title"] = "RC-Connector",
            ["status_disconnected"] = "Disconnected",
            ["status_no_telemetry"] = "No telemetry",
            ["status_no_rc"] = "No RC data",
            ["status_no_drone"] = "No drone",
            ["status_armed"] = " ARMED ",
            ["status_disarmed"] = " DISARMED ",
            ["tab_channels"] = "Channels",
            ["tab_log"] = "Log",
            ["tab_about"] = "About",
            ["btn_clear"] = "Clear",
            ["about_app"] = "App",
            ["about_version"] = "Version",
            ["about_build"] = "Build",
            ["about_author"] = "Author",
            ["about_latest"] = "Latest",
            ["about_check_update"] = "Check for updates",

            // SettingsForm
            ["settings_title"] = "RC-Connector Settings",
            ["settings_mavlink_port"] = "MAVLink port:",
            ["settings_mavlink_port_hint"] = "\u26A0 not 14550 (GCS default)",
            ["settings_mavlink_sysid"] = "MAVLink sysid:",
            ["settings_mavlink_sysid_hint"] = "\u26A0 must match SYSID_MYGCS",
            ["settings_udp_port"] = "UDP ESP port:",
            ["settings_udp_port_hint"] = "\u2139 ESP32 WiFi source port",
            ["settings_joystick_rate"] = "Joystick rate (Hz):",
            ["settings_dtr_rts"] = "Enable DTR/RTS on serial connect",
            ["settings_dtr_rts_hint"] = "\u2139 resets ESP32 on connect — needed for some boards",
            ["settings_adaptive_dpi"] = "Adaptive UI scaling",
            ["settings_language"] = "Language:",
            ["settings_lang_auto"] = "Auto",
            ["settings_startup"] = "Run at Windows startup",
            ["settings_theme"] = "Theme:",
            ["settings_theme_auto"] = "Auto",
            ["settings_theme_light"] = "Light",
            ["settings_theme_dark"] = "Dark",
            ["settings_theme_hint"] = "Restart app to apply",
            ["settings_apply"] = "Apply",
            ["settings_close"] = "Close",

            // JoystickMappingForm
            ["joymap_title"] = "Joystick Channel Mapping",
            ["joymap_channel"] = "CH{0}",
            ["joymap_source"] = "Source",
            ["joymap_source_none"] = "None",
            ["joymap_source_axis"] = "Axis",
            ["joymap_source_buttons"] = "Buttons",
            ["joymap_invert"] = "Inv",
            ["joymap_buttons_edit"] = "Edit...",
            ["joymap_pwm"] = "PWM",
            ["joymap_device"] = "Device:",
            ["joymap_no_device"] = "No joystick",
            ["joymap_live"] = "Live preview",
            ["joymap_defaults"] = "Defaults",
            ["joymap_btn_title"] = "Button Group — CH{0}",
            ["joymap_btn_add"] = "Add button",
            ["joymap_btn_remove"] = "Remove",
            ["joymap_btn_press"] = "Press button on joystick...",
            ["joymap_btn_number"] = "Btn {0}",
            ["joymap_btn_pwm_positions"] = "PWM positions:",
            ["joymap_passthrough"] = "passthrough",

            // First-run tip
            ["tip_pin_icon"] = "Tip: pin RC-Connector icon to taskbar for easy access.\nRight-click taskbar → Taskbar settings → Select which icons appear.",

            // Update
            ["update_available_title"] = "Update Available",
            ["update_available"] = "Version {0} is available. Click to update.",
            ["update_downloading"] = "Downloading update...",
            ["update_failed"] = "Update download failed. Opening release page...",
            ["log_update_available"] = "Update available: {0}",
            ["log_update_downloading"] = "Downloading update {0}...",
        };

        private static readonly Dictionary<string, string> UK = new()
        {
            // Program.cs
            ["already_running"] = "RC-Connector вже запущено.",

            // Tray menu
            ["menu_connect"] = "З'єднати",
            ["menu_disconnect"] = "Від'єднати",
            ["menu_show"] = "Показати",
            ["menu_always_on_top"] = "Завжди зверху",
            ["menu_settings"] = "Налаштування...",
            ["menu_exit"] = "Вихід",
            ["menu_no_ports"] = "Немає портів",
            ["menu_refresh"] = "Оновити",
            ["menu_scanning"] = "Сканування...",
            ["menu_no_joysticks"] = "Немає джойстиків",
            ["menu_joystick_mapping"] = "Налаштування каналів...",

            // Tray tooltips
            ["tip_disconnected"] = "RC-Connector: Від'єднано",
            ["tip_disconnected_drone_ok"] = "RC-Connector: Від'єднано, дрон OK",
            ["tip_ble_auth_failed"] = "RC-Connector: Помилка BLE авторизації. Перепаруйте пристрій.",
            ["tip_connecting"] = "RC-Connector: З'єднання...",
            ["tip_ok"] = "RC-Connector: OK {0}Hz",
            ["tip_ok_armed"] = "RC-Connector: OK {0}Hz ARMED",
            ["tip_rc_ok_no_drone"] = "RC-Connector: RC OK, немає дрона",
            ["tip_no_rc_drone_ok"] = "RC-Connector: Немає RC даних, дрон OK",
            ["tip_connected_no_data"] = "RC-Connector: З'єднано, немає даних",

            // Log messages
            ["log_mavlink_started"] = "MAVLink запущено: порт={0}, sysid={1}",
            ["log_mavlink_start_failed"] = "MAVLink не вдалося запустити: {0}",
            ["log_mavlink_restarted"] = "MAVLink перезапущено: порт={0}, sysid={1}",
            ["log_mavlink_restart_failed"] = "MAVLink не вдалося перезапустити: {0}",
            ["log_connected_to"] = "З'єднано з {0}",
            ["log_connect_failed"] = "Помилка з'єднання: {0}",
            ["log_connecting_ble"] = "З'єднання з BLE: {0}",
            ["log_ble_auth_failed"] = "Помилка BLE авторизації: {0}",
            ["log_ble_found"] = "BLE: знайдено {0} пристрій(ів)",
            ["log_listening_udp"] = "Очікування ESP32 на UDP:{0}",
            ["log_joystick_connected"] = "Джойстик підключено: {0}",
            ["log_disconnected"] = "Від'єднано",
            ["log_disconnected_reason"] = "Від'єднано: {0}",
            ["log_drone_connected"] = "Дрон з'єднано (sysid={0})",
            ["log_drone_disconnected"] = "Дрон від'єднано",
            ["log_settings_updated"] = "Налаштування оновлено",

            // MainForm
            ["form_title"] = "RC-Connector",
            ["status_disconnected"] = "Від'єднано",
            ["status_no_telemetry"] = "Немає телеметрії",
            ["status_no_rc"] = "Немає RC даних",
            ["status_no_drone"] = "Немає дрона",
            ["status_armed"] = " ARMED ",
            ["status_disarmed"] = " DISARMED ",
            ["tab_channels"] = "Канали",
            ["tab_log"] = "Лог",
            ["tab_about"] = "Про програму",
            ["btn_clear"] = "Очистити",
            ["about_app"] = "Програма",
            ["about_version"] = "Версія",
            ["about_build"] = "Збірка",
            ["about_author"] = "Автор",
            ["about_latest"] = "Остання",
            ["about_check_update"] = "Перевірити оновлення",

            // SettingsForm
            ["settings_title"] = "RC-Connector Налаштування",
            ["settings_mavlink_port"] = "MAVLink порт:",
            ["settings_mavlink_port_hint"] = "\u26A0 не 14550 (порт GCS)",
            ["settings_mavlink_sysid"] = "MAVLink sysid:",
            ["settings_mavlink_sysid_hint"] = "\u26A0 має збігатись з SYSID_MYGCS",
            ["settings_udp_port"] = "UDP ESP порт:",
            ["settings_udp_port_hint"] = "\u2139 Порт ESP32 WiFi джерела",
            ["settings_joystick_rate"] = "Частота джойстика (Hz):",
            ["settings_dtr_rts"] = "Увімкнути DTR/RTS при з'єднанні",
            ["settings_dtr_rts_hint"] = "\u2139 перезавантажує ESP32 — потрібно для деяких плат",
            ["settings_adaptive_dpi"] = "Адаптивне масштабування",
            ["settings_language"] = "Мова:",
            ["settings_lang_auto"] = "Авто",
            ["settings_startup"] = "Запускати з Windows",
            ["settings_theme"] = "Тема:",
            ["settings_theme_auto"] = "Авто",
            ["settings_theme_light"] = "Світла",
            ["settings_theme_dark"] = "Темна",
            ["settings_theme_hint"] = "Перезапустіть для застосування",
            ["settings_apply"] = "Застосувати",
            ["settings_close"] = "Закрити",

            // JoystickMappingForm
            ["joymap_title"] = "Налаштування каналів джойстика",
            ["joymap_channel"] = "КН{0}",
            ["joymap_source"] = "Джерело",
            ["joymap_source_none"] = "Немає",
            ["joymap_source_axis"] = "Вісь",
            ["joymap_source_buttons"] = "Кнопки",
            ["joymap_invert"] = "Інв",
            ["joymap_buttons_edit"] = "Редагувати...",
            ["joymap_pwm"] = "PWM",
            ["joymap_device"] = "Пристрій:",
            ["joymap_no_device"] = "Немає джойстика",
            ["joymap_live"] = "Живий перегляд",
            ["joymap_defaults"] = "За замовч.",
            ["joymap_btn_title"] = "Група кнопок — КН{0}",
            ["joymap_btn_add"] = "Додати кнопку",
            ["joymap_btn_remove"] = "Видалити",
            ["joymap_btn_press"] = "Натисніть кнопку на джойстику...",
            ["joymap_btn_number"] = "Кн {0}",
            ["joymap_btn_pwm_positions"] = "Позиції PWM:",
            ["joymap_passthrough"] = "прохідний",

            // First-run tip
            ["tip_pin_icon"] = "Порада: закріпіть іконку RC-Connector на панелі завдань.\nПКМ панель завдань → Параметри панелі → Виберіть іконки.",

            // Update
            ["update_available_title"] = "Оновлення доступне",
            ["update_available"] = "Доступна версія {0}. Натисніть для оновлення.",
            ["update_downloading"] = "Завантаження оновлення...",
            ["update_failed"] = "Не вдалося завантажити оновлення. Відкриваємо сторінку релізу...",
            ["log_update_available"] = "Доступне оновлення: {0}",
            ["log_update_downloading"] = "Завантаження оновлення {0}...",
        };

        /// <summary>
        /// Initialize language. Call once at startup after loading settings.
        /// </summary>
        public static void Init(string language)
        {
            if (language == "auto" || string.IsNullOrEmpty(language))
            {
                var culture = CultureInfo.CurrentUICulture;
                _lang = culture.TwoLetterISOLanguageName == "uk" ? "uk" : "en";
            }
            else
            {
                _lang = language;
            }
        }

        public static string CurrentLanguage => _lang;

        /// <summary>Get localized string by key. Falls back to English, then returns key.</summary>
        public static string Get(string key)
        {
            var dict = _lang == "uk" ? UK : EN;
            if (dict.TryGetValue(key, out var val))
                return val;
            if (_lang != "en" && EN.TryGetValue(key, out val))
                return val;
            return key;
        }

        /// <summary>Get localized string with format arguments.</summary>
        public static string Get(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        /// <summary>Shorthand alias for Get().</summary>
        public static string _(string key) => Get(key);
        public static string _(string key, params object[] args) => Get(key, args);
    }
}
