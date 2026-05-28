using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor;

public partial class App : System.Windows.Application
{
    private TrayService? _trayService;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        _trayService = new TrayService();
        _trayService.Initialize();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayService?.Dispose();
        base.OnExit(e);
    }
}
