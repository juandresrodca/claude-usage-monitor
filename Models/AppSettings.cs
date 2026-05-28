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
}
