using System.IO;
using System.Text.Json;
using System.Windows;
using ClaudeUsageMonitor.Services;
using Microsoft.Web.WebView2.Core;
using WpfMessageBox = System.Windows.MessageBox;

namespace ClaudeUsageMonitor.Views;

public partial class ClaudeLoginWindow : Window
{
    public string  SessionKey    { get; private set; } = "";
    public string? UserEmail     { get; private set; }
    public string  AllCookies    { get; private set; } = "";
    public string? AccountJson   { get; private set; }
    public string? RateLimitsJson { get; private set; }

    private static readonly string _userDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ClaudeUsageMonitor", "WebView2");

    private const string FetchScript = """
        (async () => {
            try {
                const acctResp = await fetch('/api/account',
                    { headers: { 'Accept': 'application/json' } });
                if (!acctResp.ok) {
                    window.chrome.webview.postMessage({
                        type: 'error',
                        message: 'Account HTTP ' + acctResp.status
                    });
                    return;
                }
                const acct = await acctResp.json();

                const orgId = acct?.memberships?.[0]?.organization?.uuid
                           ?? acct?.organizations?.[0]?.uuid
                           ?? acct?.organization_uuid
                           ?? acct?.default_organization_uuid;

                if (!orgId) {
                    window.chrome.webview.postMessage({
                        type: 'error',
                        message: 'org ID not found. Keys: ' + Object.keys(acct || {}).join(', ')
                    });
                    return;
                }

                const limitsResp = await fetch(`/api/organizations/${orgId}/usage`,
                    { headers: { 'Accept': 'application/json' } });
                if (!limitsResp.ok) {
                    window.chrome.webview.postMessage({
                        type: 'error',
                        message: 'Usage HTTP ' + limitsResp.status
                    });
                    return;
                }
                const limits = await limitsResp.json();

                window.chrome.webview.postMessage({
                    type: 'data',
                    account: acct,
                    limits: limits
                });
            } catch(err) {
                window.chrome.webview.postMessage({
                    type: 'error',
                    message: String(err)
                });
            }
        })();
        """;

    private bool _fetchingData;

    public ClaudeLoginWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitAsync();
    }

    private static readonly LocalizationService _loc = LocalizationService.Instance;

    private async Task InitAsync()
    {
        try
        {
            SetStatus(_loc["login_starting_browser"]);
            var env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.NavigationCompleted += OnNavCompleted;

            WebView.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                WebView.CoreWebView2.Navigate(e.Uri);
            };

            LoadingText.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;

            WebView.CoreWebView2.Navigate("https://claude.ai/login");
            SetStatus(_loc["login_please_sign_in"]);
        }
        catch (Exception ex)
        {
            SetStatus(_loc.Format("login_webview_err_fmt", ex.Message));
            if (ex.Message.Contains("WebView2 Runtime") || ex.Message.Contains("Edge"))
            {
                WpfMessageBox.Show(
                    "El WebView2 Runtime de Microsoft Edge no está instalado.\n\n" +
                    "Descárgalo desde:\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                    "WebView2 requerido", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void OnNavCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_fetchingData) return;

        var url = WebView.Source?.ToString() ?? "";

        // Only attempt data fetch when on a claude.ai page that is NOT a login/auth/signup screen
        if (!url.StartsWith("https://claude.ai") ||
            url.Contains("/login") ||
            url.Contains("/signup") ||
            url.Contains("/auth"))
            return;

        _fetchingData = true;
        Dispatcher.Invoke(() => SetStatus(_loc["login_session_detected"]));

        var (acctJson, limitsJson, email) = await FetchDataViaWebViewAsync();

        if (acctJson == null)
        {
            // Fetch failed — user is not authenticated yet, or on an intermediate page; wait for next nav
            _fetchingData = false;
            Dispatcher.Invoke(() => SetStatus(_loc["login_please_sign_in"]));
            return;
        }

        // Data retrieved — capture all cookies and close
        var cookies = await WebView.CoreWebView2.CookieManager
                                   .GetCookiesAsync("https://claude.ai");
        var fullCookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));

        // Try to find any session-related cookie value to use as the stored key
        var sessionCookie = cookies.FirstOrDefault(c =>
            c.Name.Equals("sessionKey",  StringComparison.OrdinalIgnoreCase) ||
            c.Name.Equals("session_key", StringComparison.OrdinalIgnoreCase) ||
            c.Name.Equals("__session",   StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains("session",   StringComparison.OrdinalIgnoreCase));

        // Minimal sanitized diagnostic — never store account JSON (contains PII)
        // or cookie values. The Diagnostics → "Discover endpoint" flow is the
        // explicit, user-triggered tool for capturing raw responses.
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeUsageMonitor");
            Directory.CreateDirectory(dir);
            var cookieNames = string.Join(", ", cookies.Select(c => c.Name));
            File.WriteAllText(
                Path.Combine(dir, "debug_last_fetch.json"),
                $"{{\"timestamp\":\"{DateTime.Now:o}\"," +
                $"\"cookieNames\":\"{cookieNames}\"," +
                $"\"accountOk\":{(acctJson != null ? "true" : "false")}," +
                $"\"limitsOk\":{(limitsJson != null ? "true" : "false")}}}");
        }
        catch { }

        Dispatcher.Invoke(() =>
        {
            // Use actual session cookie value if found; otherwise use a sentinel to mark "webview2 auth"
            SessionKey     = sessionCookie?.Value ?? "webview2-authenticated";
            AllCookies     = fullCookieString;
            AccountJson    = acctJson;
            RateLimitsJson = limitsJson;
            UserEmail      = email;
            DialogResult   = true;
            Close();
        });
    }

    private async Task<(string? AccountJson, string? RateLimitsJson, string? Email)> FetchDataViaWebViewAsync()
    {
        var tcs = new TaskCompletionSource<(string?, string?, string?)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<CoreWebView2WebMessageReceivedEventArgs>? handler = null;
        handler = (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var type = doc.RootElement.GetProperty("type").GetString();

                if (type == "data")
                {
                    string? acct = doc.RootElement.TryGetProperty("account", out var a)
                                && a.ValueKind != JsonValueKind.Null ? a.GetRawText() : null;
                    string? limits = doc.RootElement.TryGetProperty("limits", out var l)
                                  && l.ValueKind != JsonValueKind.Null ? l.GetRawText() : null;

                    string? email = null;
                    if (acct != null)
                    {
                        try
                        {
                            using var ad = JsonDocument.Parse(acct);
                            if (ad.RootElement.TryGetProperty("email", out var em))
                                email = em.GetString();
                        }
                        catch { }
                    }

                    tcs.TrySetResult((acct, limits, email));
                }
                else
                {
                    // Error from script — not authenticated or org not found
                    tcs.TrySetResult((null, null, null));
                }
            }
            catch { tcs.TrySetResult((null, null, null)); }
            finally
            {
                WebView.CoreWebView2.WebMessageReceived -= handler;
            }
        };

        WebView.CoreWebView2.WebMessageReceived += handler;

        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(FetchScript);
        }
        catch
        {
            WebView.CoreWebView2.WebMessageReceived -= handler;
            return (null, null, null);
        }

        var winner = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        if (winner != tcs.Task)
        {
            WebView.CoreWebView2.WebMessageReceived -= handler;
            return (null, null, null);
        }

        return await tcs.Task;
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void OnDrag(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void OnCancel(object s, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
