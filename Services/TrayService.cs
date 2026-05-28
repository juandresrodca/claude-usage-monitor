using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using ClaudeUsageMonitor.Views;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace ClaudeUsageMonitor.Services;

public class TrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private UsageDashboard? _dashboard;

    public readonly ClaudeApiService Api = new();
    public readonly StorageService Storage = new();
    public readonly WebInterceptService WebInterceptor = new();
    public readonly EndpointDiscoveryService Discovery = new();

    public void Initialize()
    {
        // Load and configure the API with saved settings
        var settings = Storage.LoadSettings();
        Api.Configure(settings);
        LocalizationService.Instance.Current = settings.Language;

        _notifyIcon = new NotifyIcon
        {
            Icon = BuildTrayIcon(),
            Visible = true,
            Text = "Claude Usage Monitor"
        };

        var menu = new ContextMenuStrip();
        var loc = LocalizationService.Instance;

        ToolStripMenuItem Add(string key, EventHandler handler)
        {
            var item = new ToolStripMenuItem(loc[key]) { Tag = key };
            item.Click += handler;
            menu.Items.Add(item);
            return item;
        }

        Add("tray_open_dashboard", (_, _) => ShowDashboard());
        Add("tray_sync", (_, _) => SyncFromApi());
        menu.Items.Add(new ToolStripSeparator());
        Add("tray_settings", (_, _) => OpenSettings());
        var startupItem = Add("tray_start_with_windows", (s, _) => ToggleStartup(s as ToolStripMenuItem));
        menu.Items.Add(new ToolStripSeparator());
        Add("tray_exit", (_, _) => ExitApplication());

        startupItem.Checked = IsStartupEnabled();

        // Refresh menu labels when the user switches language
        loc.PropertyChanged += (_, _) =>
        {
            foreach (var item in menu.Items)
                if (item is ToolStripMenuItem mi && mi.Tag is string k)
                    mi.Text = loc[k];
        };

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowDashboard();
        };
        _notifyIcon.DoubleClick += (_, _) => ShowDashboard();
    }

    // ── Dashboard ─────────────────────────────────────────────────────────

    public void ShowDashboard()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            bool isNew = _dashboard == null;

            if (_dashboard == null)
            {
                _dashboard = new UsageDashboard(this);
                _dashboard.Closed += (_, _) => _dashboard = null;
            }

            if (!_dashboard.IsVisible)
            {
                var wa = SystemParameters.WorkArea;
                _dashboard.Width  = 440;
                _dashboard.Height = 560;
                _dashboard.Left   = wa.Right  - _dashboard.Width  - 12;
                _dashboard.Top    = wa.Bottom - _dashboard.Height - 12;
                _dashboard.Show();

                // Always reload settings when showing so any credential changes take effect
                if (_dashboard.DataContext is ViewModels.UsageDashboardViewModel vm)
                {
                    vm.ReloadSettings();

                    // Auto-fetch data on first open (or after being hidden) when connected
                    if (vm.IsConnected)
                        _ = vm.FetchFromApiAsync();
                }
            }

            _dashboard.Activate();
            _dashboard.Focus();
        });
    }

    private void SyncFromApi()
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            if (_dashboard?.DataContext is ViewModels.UsageDashboardViewModel vm)
                await vm.FetchFromApiAsync();
            else
            {
                ShowDashboard();
                if (_dashboard?.DataContext is ViewModels.UsageDashboardViewModel vm2)
                    await vm2.FetchFromApiAsync();
            }
        });
    }

    public void OpenSettings()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var dlg = new SettingsDialog(this);
            dlg.Owner = _dashboard;
            // Centre on screen if no owner
            if (dlg.Owner == null)
            {
                var wa = SystemParameters.WorkArea;
                dlg.Left = wa.Left + (wa.Width - 480) / 2;
                dlg.Top  = wa.Top  + (wa.Height - 400) / 2;
            }
            dlg.ShowDialog();
        });
    }

    // ── Startup ───────────────────────────────────────────────────────────

    private static void ToggleStartup(ToolStripMenuItem? item)
    {
        if (item == null) return;
        if (IsStartupEnabled()) { DisableStartup(); item.Checked = false; }
        else                    { EnableStartup();  item.Checked = true;  }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue("ClaudeUsageMonitor") != null;
    }

    private static void EnableStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", true);
        var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (exe != null) key?.SetValue("ClaudeUsageMonitor", $"\"{exe}\"");
    }

    private static void DisableStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", true);
        key?.DeleteValue("ClaudeUsageMonitor", false);
    }

    private static void ExitApplication() =>
        Application.Current.Dispatcher.Invoke(Application.Current.Shutdown);

    // ── Tray icon ─────────────────────────────────────────────────────────

    private static Icon BuildTrayIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float cx = size / 2f, cy = size / 2f;
            const int petals = 8;
            const float innerR = 5f, outerR = 13f;

            using var pen = new Pen(Color.FromArgb(255, 255, 115, 75), 2.8f)
            {
                StartCap = LineCap.Round,
                EndCap   = LineCap.Round
            };

            for (int i = 0; i < petals; i++)
            {
                double a = Math.PI * 2 / petals * i - Math.PI / 2;
                g.DrawLine(pen,
                    cx + (float)(Math.Cos(a) * innerR), cy + (float)(Math.Sin(a) * innerR),
                    cx + (float)(Math.Cos(a) * outerR), cy + (float)(Math.Sin(a) * outerR));
            }

            using var brush = new SolidBrush(Color.FromArgb(255, 255, 140, 90));
            g.FillEllipse(brush, cx - 4, cy - 4, 8, 8);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose() => _notifyIcon?.Dispose();
}
