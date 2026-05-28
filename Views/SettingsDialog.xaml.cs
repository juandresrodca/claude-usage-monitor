using System.Windows;
using System.Windows.Media;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;
using WpfColor = System.Windows.Media.Color;

namespace ClaudeUsageMonitor.Views;

public partial class SettingsDialog : Window
{
    private readonly TrayService _tray;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private bool _sessionKeyChanged;
    private bool _loaded;

    public SettingsDialog(TrayService tray)
    {
        InitializeComponent();
        _tray = tray;

        var settings = tray.Storage.LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.SessionKey))
        {
            SessionKeyBox.Password = settings.SessionKey;
            ShowStatus(_loc.Format("status_conn_ok_fmt",
                settings.UserEmail ?? _loc["claude_account"]), ok: true);
        }

        if (int.TryParse(settings.AutoRefreshMinutes.ToString(), out _))
            RefreshMinBox.Text = settings.AutoRefreshMinutes.ToString();

        LangEsRadio.IsChecked = settings.Language == AppLanguage.Es;
        LangEnRadio.IsChecked = settings.Language == AppLanguage.En;
        _loaded = true;
    }

    // ── Browser login ─────────────────────────────────────────────────────

    private void OnLoginWithBrowser(object s, RoutedEventArgs e)
    {
        LoginBtn.IsEnabled = false;
        LoginBtn.Content = _loc["btn_login_opening"];

        var loginWin = new ClaudeLoginWindow { Owner = this };
        bool? result = loginWin.ShowDialog();

        LoginBtn.IsEnabled = true;
        LoginBtn.Content = _loc["btn_login_with_claude"];

        if (result == true && loginWin.AccountJson != null)
        {
            if (!string.IsNullOrWhiteSpace(loginWin.SessionKey))
                SessionKeyBox.Password = loginWin.SessionKey;
            _sessionKeyChanged = true;

            var settings = _tray.Storage.LoadSettings();
            settings.SessionKey = loginWin.SessionKey;
            settings.AllCookies = loginWin.AllCookies;
            if (loginWin.UserEmail != null) settings.UserEmail = loginWin.UserEmail;
            _tray.Storage.SaveSettings(settings);
            _tray.Api.Configure(settings);

            if (loginWin.RateLimitsJson != null)
                NotifyWithData(loginWin.AccountJson, loginWin.RateLimitsJson);
            else
                NotifyAndSync();

            ShowStatus(_loc.Format("status_conn_ok_fmt",
                loginWin.UserEmail ?? _loc["claude_account"]), ok: true);
        }
    }

    // ── Clear session & reconnect ─────────────────────────────────────────

    private void OnClearSession(object s, RoutedEventArgs e)
    {
        WebInterceptService.ClearSession();
        var settings = _tray.Storage.LoadSettings();
        settings.SessionKey = "";
        settings.AllCookies = "";
        settings.UserEmail  = "";
        settings.OrgId      = null;
        _tray.Storage.SaveSettings(settings);
        SessionKeyBox.Password = "";
        _sessionKeyChanged = false;

        ShowStatus(_loc["status_session_cleared"], ok: null);
        OnLoginWithBrowser(s, e);
    }

    // ── Endpoint discovery ────────────────────────────────────────────────

    private async void OnDiscoverEndpoints(object s, RoutedEventArgs e)
    {
        DiscoverBtn.IsEnabled = false;
        var originalContent = DiscoverBtn.Content;
        DiscoverBtn.Content = _loc["btn_discover_capturing"];
        ShowStatus(_loc["status_discover_opening"], ok: null);

        var result = await _tray.Discovery.DiscoverAsync();

        DiscoverBtn.IsEnabled = true;
        DiscoverBtn.Content = originalContent;

        if (result.Error != null)
        {
            ShowStatus(_loc.Format("status_discover_err_fmt", result.Error), ok: false);
            return;
        }

        if (result.FilePath == null || result.TotalCaptured == 0)
        {
            ShowStatus(_loc["status_discover_none"], ok: false);
            return;
        }

        ShowStatus(_loc.Format("status_discover_ok_fmt",
            result.TotalCaptured, result.InterestingCount), ok: true);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = result.FilePath,
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe",
                    $"/select,\"{result.FilePath}\"");
            }
            catch { }
        }
    }

    // ── Manual key test ───────────────────────────────────────────────────

    private void SessionKeyBox_Changed(object s, RoutedEventArgs e) => _sessionKeyChanged = true;

    private async void OnTestConnection(object s, RoutedEventArgs e)
    {
        var key = SessionKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowStatus(_loc["status_enter_key_first"], ok: false);
            return;
        }

        TestBtn.IsEnabled = false;
        TestBtn.Content = "…";
        ShowStatus(_loc["status_testing"], ok: null);

        var stored = _tray.Storage.LoadSettings();
        _tray.Api.Configure(new AppSettings { SessionKey = key, AllCookies = stored.AllCookies });
        var (ok, email, error) = await _tray.Api.TestConnectionAsync();

        TestBtn.IsEnabled = true;
        TestBtn.Content = _loc["btn_test"];

        if (ok)
        {
            ShowStatus(_loc.Format("status_conn_ok_fmt",
                email ?? _loc["claude_account"]), ok: true);
            var s2 = _tray.Storage.LoadSettings();
            s2.UserEmail = email;
            _tray.Storage.SaveSettings(s2);
        }
        else
        {
            ShowStatus(error ?? _loc["status_unknown_err"], ok: false);
        }
    }

    // ── Language switch ───────────────────────────────────────────────────

    private void OnLanguageChanged(object s, RoutedEventArgs e)
    {
        if (!_loaded) return;
        if (s is not System.Windows.Controls.RadioButton rb) return;
        if (rb.Tag is not string code) return;

        var lang = code == "En" ? AppLanguage.En : AppLanguage.Es;
        _loc.Current = lang;

        var settings = _tray.Storage.LoadSettings();
        settings.Language = lang;
        _tray.Storage.SaveSettings(settings);
    }

    // ── Save ──────────────────────────────────────────────────────────────

    private async void OnSave(object s, RoutedEventArgs e)
    {
        var settings = _tray.Storage.LoadSettings();
        var newKey = SessionKeyBox.Password.Trim();
        if (!string.IsNullOrWhiteSpace(newKey))
            settings.SessionKey = newKey;

        if (int.TryParse(RefreshMinBox.Text, out int mins) && mins > 0)
            settings.AutoRefreshMinutes = mins;

        settings.Language = LangEnRadio.IsChecked == true ? AppLanguage.En : AppLanguage.Es;

        if (_sessionKeyChanged && !string.IsNullOrWhiteSpace(settings.SessionKey))
        {
            SaveBtn.IsEnabled = false;
            SaveBtn.Content = _loc["btn_saving"];
            _tray.Api.Configure(settings);
            var (ok, email, _) = await _tray.Api.TestConnectionAsync();
            if (ok) settings.UserEmail = email;
            SaveBtn.IsEnabled = true;
            SaveBtn.Content = _loc["btn_save"];
        }

        _tray.Storage.SaveSettings(settings);
        _tray.Api.Configure(settings);
        NotifyAndSync();
        Close();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void NotifyAndSync()
    {
        if (System.Windows.Application.Current.Windows
                .OfType<UsageDashboard>()
                .FirstOrDefault()?.DataContext is ViewModels.UsageDashboardViewModel vm)
        {
            vm.ReloadSettings();
            _ = vm.FetchFromApiAsync();
        }
    }

    private void NotifyWithData(string accountJson, string rateLimitsJson)
    {
        if (System.Windows.Application.Current.Windows
                .OfType<UsageDashboard>()
                .FirstOrDefault()?.DataContext is ViewModels.UsageDashboardViewModel vm)
        {
            vm.ReloadSettings();
            vm.ApplyFetchedData(accountJson, rateLimitsJson);
        }
    }

    private void ShowStatus(string msg, bool? ok)
    {
        StatusBorder.Visibility = Visibility.Visible;
        StatusText.Text = msg;
        StatusBorder.Background = ok switch
        {
            true  => new SolidColorBrush(WpfColor.FromArgb(40,  60, 210, 100)),
            false => new SolidColorBrush(WpfColor.FromArgb(40, 210,  60,  60)),
            null  => new SolidColorBrush(WpfColor.FromArgb(40, 100, 100, 100))
        };
        StatusText.Foreground = ok switch
        {
            true  => new SolidColorBrush(WpfColor.FromArgb(255,  80, 200, 120)),
            false => new SolidColorBrush(WpfColor.FromArgb(255, 220,  80,  80)),
            null  => new SolidColorBrush(WpfColor.FromArgb(255, 160, 160, 160))
        };
    }

    private void OnDrag(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object s, RoutedEventArgs e) => Close();
}
