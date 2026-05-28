using System.ComponentModel;
using System.Globalization;

namespace ClaudeUsageMonitor.Services;

public enum AppLanguage { Es, En }

// Lightweight singleton used by XAML bindings (via `{Binding [key], Source=…}`)
// and ViewModel code (via Loc.Instance["key"]). Switching language raises
// PropertyChanged("Item[]"), which is the magic name WPF uses to refresh every
// indexer binding bound to this instance.
public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private AppLanguage _current = AppLanguage.Es;
    public AppLanguage Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            CultureInfo.CurrentUICulture = value == AppLanguage.En
                ? new CultureInfo("en-US")
                : new CultureInfo("es-ES");
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged("Item[]");
        }
    }

    public string this[string key]
    {
        get
        {
            var table = _current == AppLanguage.En ? _en : _es;
            return table.TryGetValue(key, out var v) ? v : key;
        }
    }

    public string Format(string key, params object[] args) =>
        string.Format(this[key], args);

    public string ShortDay(DayOfWeek d)
    {
        var keys = new[]
        {
            "day_sun", "day_mon", "day_tue", "day_wed",
            "day_thu", "day_fri", "day_sat"
        };
        return this[keys[(int)d]];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private readonly Dictionary<string, string> _es = new()
    {
        // Dashboard
        ["dashboard_title"]            = "Límites de uso del plan",
        ["section_current_session"]    = "SESIÓN ACTUAL",
        ["section_weekly_limits"]      = "LÍMITES SEMANALES",
        ["section_additional_features"]= "FUNCIONES ADICIONALES",
        ["row_all_models"]             = "Todos los modelos",
        ["row_opus"]                   = "Opus",
        ["more_info"]                  = "Más información",
        ["no_additional_features"]     = "Sin funciones adicionales activas",
        ["last_updated"]               = "Última actualización: ",
        ["tip_sync"]                   = "Sincronizar con Claude",
        ["tip_settings"]               = "Configuración",
        ["tip_close"]                  = "Cerrar",
        ["tip_refresh"]                = "Actualizar",
        ["percent_used_fmt"]           = "{0:0}% usado",
        ["resets_in_h_m_fmt"]          = "Se restablece en {0} h {1} min",
        ["resets_in_m_fmt"]            = "Se restablece en {0} min",
        ["resets_soon"]                = "Se restablece pronto",
        ["resets_on_fmt"]              = "Se restablece {0}, {1}",
        ["opus_not_used"]              = "Aún no has usado Opus esta semana",
        ["just_now"]                   = "justo ahora",
        ["minutes_ago_fmt"]            = "hace {0} min",
        ["hours_ago_fmt"]              = "hace {0} h",
        ["connected_to_fmt"]           = "Conectado: {0}",
        ["claude_account"]             = "cuenta Claude",
        ["not_connected"]              = "Sin conexión — Abre Configuración para conectar",
        ["sync_configure_first"]       = "Configura tu cuenta primero.",
        ["syncing"]                    = "Sincronizando…",
        ["sync_err_account_fmt"]       = "Error: No se pudo obtener la cuenta. Limpia la sesión e inicia sesión de nuevo.",
        ["sync_err_org_fmt"]           = "Error: No se encontró el ID de organización. Limpia la sesión e inicia sesión.",
        ["sync_err_parse"]             = "Error: No se pudieron interpretar los datos de uso.",
        ["sync_err_generic_fmt"]       = "Error: {0}",

        // Settings dialog
        ["settings_title"]             = "Configuración",
        ["section_claude_account"]     = "CUENTA CLAUDE",
        ["browser_login_title"]        = "Inicio de sesión con navegador",
        ["browser_login_desc"]         = "Se abrirá una ventana del navegador. Inicia sesión con tu cuenta de Claude y la app se conectará automáticamente.",
        ["btn_login_with_claude"]      = "Iniciar sesión con Claude →",
        ["btn_clear_session"]          = "Limpiar sesión",
        ["tip_clear_session"]          = "Borra las cookies guardadas y te pide iniciar sesión de nuevo",
        ["section_diagnostics"]        = "Diagnóstico",
        ["diagnostics_desc"]           = "Abre claude.ai/settings/usage y captura todas las llamadas /api/ que hace la web para identificar el endpoint correcto del uso de 5 h.",
        ["btn_discover_endpoint"]      = "Descubrir endpoint de uso",
        ["section_manual_key"]         = "O INGRESA LA SESSION KEY MANUALMENTE",
        ["label_session_key"]          = "Session Key (desde DevTools → Application → Cookies → claude.ai)",
        ["btn_test"]                   = "Probar",
        ["section_auto_refresh"]       = "SINCRONIZACIÓN AUTOMÁTICA",
        ["label_refresh_every"]        = "Actualizar cada ",
        ["label_minutes"]              = " minutos",
        ["section_language"]           = "IDIOMA",
        ["lang_spanish"]               = "Español",
        ["lang_english"]               = "English",
        ["btn_cancel"]                 = "Cancelar",
        ["btn_save"]                   = "Guardar",
        ["btn_saving"]                 = "Guardando…",
        ["btn_login_opening"]          = "Abriendo navegador…",
        ["btn_discover_capturing"]     = "Capturando…",
        ["status_session_cleared"]     = "Sesión borrada. Inicia sesión de nuevo con el navegador.",
        ["status_enter_key_first"]     = "Ingresa una session key primero.",
        ["status_testing"]             = "Probando conexión…",
        ["status_conn_ok_fmt"]         = "Conexión exitosa — {0}",
        ["status_unknown_err"]         = "Error desconocido",
        ["status_discover_opening"]    = "Abriendo claude.ai/settings/usage para capturar llamadas /api/…",
        ["status_discover_err_fmt"]    = "Error en diagnóstico: {0}",
        ["status_discover_none"]       = "No se capturó ninguna llamada /api/. Asegúrate de haber iniciado sesión primero.",
        ["status_discover_ok_fmt"]     = "Capturadas {0} llamadas, {1} con datos de uso. Archivo guardado — abriéndolo…",

        // Login window
        ["login_title"]                = "Iniciar sesión en Claude",
        ["login_subtitle"]             = " — inicia sesión y la app se conectará automáticamente",
        ["login_loading"]              = "Cargando claude.ai…",
        ["login_waiting"]              = "Esperando inicio de sesión…",
        ["login_starting_browser"]     = "Iniciando navegador…",
        ["login_please_sign_in"]       = "Inicia sesión con tu cuenta de Claude…",
        ["login_session_detected"]     = "Sesión detectada, cargando datos de uso…",
        ["login_webview_err_fmt"]      = "Error al iniciar WebView2: {0}",

        // Tray
        ["tray_open_dashboard"]        = "Abrir Dashboard",
        ["tray_sync"]                  = "Sincronizar con Claude",
        ["tray_settings"]              = "Configuración",
        ["tray_start_with_windows"]    = "Iniciar con Windows",
        ["tray_exit"]                  = "Salir",

        // Days of week
        ["day_sun"] = "dom", ["day_mon"] = "lun", ["day_tue"] = "mar",
        ["day_wed"] = "mié", ["day_thu"] = "jue", ["day_fri"] = "vie",
        ["day_sat"] = "sáb",
    };

    private readonly Dictionary<string, string> _en = new()
    {
        // Dashboard
        ["dashboard_title"]            = "Plan usage limits",
        ["section_current_session"]    = "CURRENT SESSION",
        ["section_weekly_limits"]      = "WEEKLY LIMITS",
        ["section_additional_features"]= "ADDITIONAL FEATURES",
        ["row_all_models"]             = "All models",
        ["row_opus"]                   = "Opus",
        ["more_info"]                  = "More info",
        ["no_additional_features"]     = "No additional features active",
        ["last_updated"]               = "Last updated: ",
        ["tip_sync"]                   = "Sync with Claude",
        ["tip_settings"]               = "Settings",
        ["tip_close"]                  = "Close",
        ["tip_refresh"]                = "Refresh",
        ["percent_used_fmt"]           = "{0:0}% used",
        ["resets_in_h_m_fmt"]          = "Resets in {0} h {1} min",
        ["resets_in_m_fmt"]            = "Resets in {0} min",
        ["resets_soon"]                = "Resets soon",
        ["resets_on_fmt"]              = "Resets {0}, {1}",
        ["opus_not_used"]              = "You haven't used Opus this week",
        ["just_now"]                   = "just now",
        ["minutes_ago_fmt"]            = "{0} min ago",
        ["hours_ago_fmt"]              = "{0} h ago",
        ["connected_to_fmt"]           = "Connected: {0}",
        ["claude_account"]             = "Claude account",
        ["not_connected"]              = "Not connected — open Settings to connect",
        ["sync_configure_first"]       = "Configure your account first.",
        ["syncing"]                    = "Syncing…",
        ["sync_err_account_fmt"]       = "Error: Couldn't load account. Clear the session and sign in again.",
        ["sync_err_org_fmt"]           = "Error: Organization ID not found. Clear the session and sign in again.",
        ["sync_err_parse"]             = "Error: Couldn't parse usage data.",
        ["sync_err_generic_fmt"]       = "Error: {0}",

        // Settings dialog
        ["settings_title"]             = "Settings",
        ["section_claude_account"]     = "CLAUDE ACCOUNT",
        ["browser_login_title"]        = "Sign in with browser",
        ["browser_login_desc"]         = "A browser window will open. Sign in with your Claude account and the app will connect automatically.",
        ["btn_login_with_claude"]      = "Sign in with Claude →",
        ["btn_clear_session"]          = "Clear session",
        ["tip_clear_session"]          = "Wipes saved cookies and asks you to sign in again",
        ["section_diagnostics"]        = "Diagnostics",
        ["diagnostics_desc"]           = "Opens claude.ai/settings/usage and captures every /api/ call the web makes, to identify the correct 5-hour usage endpoint.",
        ["btn_discover_endpoint"]      = "Discover usage endpoint",
        ["section_manual_key"]         = "OR ENTER A SESSION KEY MANUALLY",
        ["label_session_key"]          = "Session Key (from DevTools → Application → Cookies → claude.ai)",
        ["btn_test"]                   = "Test",
        ["section_auto_refresh"]       = "AUTO REFRESH",
        ["label_refresh_every"]        = "Refresh every ",
        ["label_minutes"]              = " minutes",
        ["section_language"]           = "LANGUAGE",
        ["lang_spanish"]               = "Español",
        ["lang_english"]               = "English",
        ["btn_cancel"]                 = "Cancel",
        ["btn_save"]                   = "Save",
        ["btn_saving"]                 = "Saving…",
        ["btn_login_opening"]          = "Opening browser…",
        ["btn_discover_capturing"]     = "Capturing…",
        ["status_session_cleared"]     = "Session cleared. Sign in again with the browser.",
        ["status_enter_key_first"]     = "Enter a session key first.",
        ["status_testing"]             = "Testing connection…",
        ["status_conn_ok_fmt"]         = "Connected — {0}",
        ["status_unknown_err"]         = "Unknown error",
        ["status_discover_opening"]    = "Opening claude.ai/settings/usage to capture /api/ calls…",
        ["status_discover_err_fmt"]    = "Diagnostics error: {0}",
        ["status_discover_none"]       = "No /api/ calls captured. Make sure you're signed in first.",
        ["status_discover_ok_fmt"]     = "Captured {0} calls, {1} with usage data. File saved — opening it…",

        // Login window
        ["login_title"]                = "Sign in to Claude",
        ["login_subtitle"]             = " — sign in and the app will connect automatically",
        ["login_loading"]              = "Loading claude.ai…",
        ["login_waiting"]              = "Waiting for sign-in…",
        ["login_starting_browser"]     = "Starting browser…",
        ["login_please_sign_in"]       = "Sign in with your Claude account…",
        ["login_session_detected"]     = "Session detected, loading usage data…",
        ["login_webview_err_fmt"]      = "WebView2 error: {0}",

        // Tray
        ["tray_open_dashboard"]        = "Open Dashboard",
        ["tray_sync"]                  = "Sync with Claude",
        ["tray_settings"]              = "Settings",
        ["tray_start_with_windows"]    = "Start with Windows",
        ["tray_exit"]                  = "Exit",

        // Days of week
        ["day_sun"] = "Sun", ["day_mon"] = "Mon", ["day_tue"] = "Tue",
        ["day_wed"] = "Wed", ["day_thu"] = "Thu", ["day_fri"] = "Fri",
        ["day_sat"] = "Sat",
    };
}
