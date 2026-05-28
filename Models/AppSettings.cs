using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor.Models;

public class AppSettings
{
    public string SessionKey { get; set; } = "";
    public string? OrgId { get; set; }
    public string? UserEmail { get; set; }
    public int AutoRefreshMinutes { get; set; } = 10;
    // Full cookie jar captured from WebView2 login (includes sessionKey + Cloudflare cookies)
    public string AllCookies { get; set; } = "";
    public AppLanguage Language { get; set; } = AppLanguage.Es;
    // True after the most recent successful sync. Used to show "connected" on
    // the dashboard even if SessionKey/AllCookies were emptied by a partial
    // Clear Session while WebView2 still holds valid cookies.
    public bool LastSyncOk { get; set; }
}
