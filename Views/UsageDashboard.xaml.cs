using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using ClaudeUsageMonitor.Services;
using ClaudeUsageMonitor.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;

namespace ClaudeUsageMonitor.Views;

public partial class UsageDashboard : Window
{
    private readonly TrayService _tray;
    private UsageDashboardViewModel Vm => (UsageDashboardViewModel)DataContext;

    public UsageDashboard(TrayService tray)
    {
        _tray = tray;
        InitializeComponent();
        DataContext = new UsageDashboardViewModel(tray.Storage, tray.Api, tray.WebInterceptor);

        var vm = (UsageDashboardViewModel)DataContext;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UsageDashboardViewModel.SyncStatus))
                SyncStatusText.Visibility = string.IsNullOrEmpty(Vm.SyncStatus)
                    ? Visibility.Collapsed : Visibility.Visible;

            if (e.PropertyName is nameof(UsageDashboardViewModel.IsConnected) or "")
                UpdateConnectionDot();
        };
        UpdateConnectionDot();
    }

    private void UpdateConnectionDot()
    {
        ConnectionDot.Fill = new System.Windows.Media.SolidColorBrush(
            Vm.IsConnected
                ? System.Windows.Media.Color.FromRgb(62, 207, 94)
                : System.Windows.Media.Color.FromRgb(80, 80, 80));
    }

    // ── Window chrome ─────────────────────────────────────────────────────
    private void OnDragMove(object s, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object s, RoutedEventArgs e) => Hide();

    private void OnRefresh(object s, RoutedEventArgs e) => Vm.Refresh();

    private async void OnSync(object s, RoutedEventArgs e)
    {
        await Vm.FetchFromApiAsync();
    }

    private void OnOpenSettings(object s, RoutedEventArgs e) => _tray.OpenSettings();

    private void Hyperlink_Navigate(object s, RequestNavigateEventArgs e)
    {
        // Only allow http/https — guards against `file:`, `javascript:`,
        // `ms-settings:`, etc. being passed to ShellExecute.
        if (e.Uri.Scheme is "http" or "https")
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // ── Inline edit helpers ───────────────────────────────────────────────
    private static void BeginEdit(System.Windows.Controls.TextBlock lbl,
                                  System.Windows.Controls.TextBox box,
                                  double value)
    {
        box.Text = ((int)value).ToString();
        lbl.Visibility = Visibility.Collapsed;
        box.Visibility = Visibility.Visible;
        box.Focus();
        box.SelectAll();
    }

    private static void CommitEdit(System.Windows.Controls.TextBlock lbl,
                                   System.Windows.Controls.TextBox box,
                                   Action<double> setter)
    {
        if (double.TryParse(box.Text.Replace("%", "").Trim(), out double v))
            setter(v);
        box.Visibility = Visibility.Collapsed;
        lbl.Visibility = Visibility.Visible;
    }

    private static void CancelEdit(System.Windows.Controls.TextBlock lbl,
                                   System.Windows.Controls.TextBox box)
    {
        box.Visibility = Visibility.Collapsed;
        lbl.Visibility = Visibility.Visible;
    }

    // ── Session ───────────────────────────────────────────────────────────
    private void SessionPct_Edit(object s, MouseButtonEventArgs e)
        => BeginEdit(SessionPctLabel, SessionPctEdit, Vm.SessionPercent);
    private void SessionPctEdit_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)  CommitEdit(SessionPctLabel, SessionPctEdit, v => Vm.SessionPercent = v);
        if (e.Key == System.Windows.Input.Key.Escape) CancelEdit(SessionPctLabel, SessionPctEdit);
    }
    private void SessionPctEdit_LostFocus(object s, RoutedEventArgs e)
        => CommitEdit(SessionPctLabel, SessionPctEdit, v => Vm.SessionPercent = v);

    // ── All Models ────────────────────────────────────────────────────────
    private void AllModelsPct_Edit(object s, MouseButtonEventArgs e)
        => BeginEdit(AllModelsPctLabel, AllModelsPctEdit, Vm.AllModelsPercent);
    private void AllModelsPctEdit_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)  CommitEdit(AllModelsPctLabel, AllModelsPctEdit, v => Vm.AllModelsPercent = v);
        if (e.Key == System.Windows.Input.Key.Escape) CancelEdit(AllModelsPctLabel, AllModelsPctEdit);
    }
    private void AllModelsPctEdit_LostFocus(object s, RoutedEventArgs e)
        => CommitEdit(AllModelsPctLabel, AllModelsPctEdit, v => Vm.AllModelsPercent = v);

    // ── Claude Design ─────────────────────────────────────────────────────
    private void DesignPct_Edit(object s, MouseButtonEventArgs e)
        => BeginEdit(DesignPctLabel, DesignPctEdit, Vm.ClaudeDesignPercent);
    private void DesignPctEdit_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)  CommitEdit(DesignPctLabel, DesignPctEdit, v => Vm.ClaudeDesignPercent = v);
        if (e.Key == System.Windows.Input.Key.Escape) CancelEdit(DesignPctLabel, DesignPctEdit);
    }
    private void DesignPctEdit_LostFocus(object s, RoutedEventArgs e)
        => CommitEdit(DesignPctLabel, DesignPctEdit, v => Vm.ClaudeDesignPercent = v);
}
