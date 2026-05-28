using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using WpfApp = System.Windows.Application;

namespace ClaudeUsageMonitor.Services;

public class WebInterceptService
{
    private static readonly string _userDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ClaudeUsageMonitor", "WebView2");

    // After the page loads we explicitly fetch the data ourselves.
    // The browser is in the claude.ai origin so cookies are sent automatically —
    // no HttpClient / TLS fingerprint involved.
    private const string FetchScript = """
        (async () => {
            try {
                const acctResp = await fetch('/api/account',
                    { headers: { 'Accept': 'application/json' } });
                if (!acctResp.ok) {
                    window.chrome.webview.postMessage({
                        type: 'error',
                        message: 'Account API returned HTTP ' + acctResp.status
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
                        message: 'org ID no encontrado. Claves disponibles: ' + Object.keys(acct || {}).join(', ')
                    });
                    return;
                }

                const limitsResp = await fetch(`/api/organizations/${orgId}/usage`,
                    { headers: { 'Accept': 'application/json' } });
                if (!limitsResp.ok) {
                    window.chrome.webview.postMessage({
                        type: 'error',
                        message: 'Usage API returned HTTP ' + limitsResp.status
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

    public async Task<(string? AccountJson, string? RateLimitsJson, string? Error)> FetchRawAsync()
    {
        try
        {
            return await WpfApp.Current.Dispatcher
                .InvokeAsync(DoFetchAsync).Task.Unwrap();
        }
        catch (Exception ex)
        {
            return (null, null, $"Error al iniciar navegador: {ex.Message}");
        }
    }

    // Wipes the WebView2 profile so the next login starts with a clean slate.
    public static void ClearSession()
    {
        try
        {
            if (Directory.Exists(_userDataFolder))
                Directory.Delete(_userDataFolder, recursive: true);
        }
        catch { /* best-effort */ }
    }

    // ── Internal ──────────────────────────────────────────────────────────

    private async Task<(string?, string?, string?)> DoFetchAsync()
    {
        Window? host = null;
        try
        {
            host = new Window
            {
                Width = 2, Height = 2,
                Left = -9999, Top = -9999,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize
            };

            var webView = new WebView2();
            host.Content = webView;
            host.Show();

            var env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            var resultTcs = new TaskCompletionSource<(string?, string?, string?)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            // Receive the result from the JS fetch script
            webView.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var type = doc.RootElement.GetProperty("type").GetString();

                    if (type == "data")
                    {
                        string? acct = doc.RootElement.TryGetProperty("account", out var a)
                                    && a.ValueKind != JsonValueKind.Null
                                       ? a.GetRawText() : null;
                        string? limits = doc.RootElement.TryGetProperty("limits", out var l)
                                      && l.ValueKind != JsonValueKind.Null
                                         ? l.GetRawText() : null;

                        // Sanitized diagnostic only — see ClaudeLoginWindow for the rationale.
                        try
                        {
                            var dir = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "ClaudeUsageMonitor");
                            Directory.CreateDirectory(dir);
                            File.WriteAllText(
                                Path.Combine(dir, "debug_last_fetch.json"),
                                $"{{\"timestamp\":\"{DateTime.Now:o}\"," +
                                $"\"accountOk\":{(acct != null ? "true" : "false")}," +
                                $"\"limitsOk\":{(limits != null ? "true" : "false")}}}");
                        }
                        catch { }

                        resultTcs.TrySetResult((acct, limits, null));
                    }
                    else if (type == "error")
                    {
                        var msg = doc.RootElement.TryGetProperty("message", out var m)
                                ? m.GetString() : "error desconocido";
                        resultTcs.TrySetResult((null, null,
                            $"Error al consultar la API: {msg}"));
                    }
                }
                catch { }
            };

            // After the page finishes loading, run the explicit fetch script
            webView.CoreWebView2.NavigationCompleted += async (_, nav) =>
            {
                if (resultTcs.Task.IsCompleted) return;

                var src = webView.Source?.ToString() ?? "";

                if (src.Contains("/login"))
                {
                    resultTcs.TrySetResult((null, null,
                        "Sesión expirada — usa 'Limpiar sesión' en Configuración y vuelve a iniciar sesión."));
                    return;
                }

                if (!nav.IsSuccess)
                {
                    resultTcs.TrySetResult((null, null,
                        "Sin conexión a internet o error de navegación."));
                    return;
                }

                try
                {
                    await webView.CoreWebView2.ExecuteScriptAsync(FetchScript);
                }
                catch (Exception ex)
                {
                    resultTcs.TrySetResult((null, null,
                        $"Error al ejecutar script: {ex.Message}"));
                }
            };

            webView.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            webView.CoreWebView2.Navigate("https://claude.ai/new");

            var winner = await Task.WhenAny(resultTcs.Task, Task.Delay(TimeSpan.FromSeconds(25)));

            if (winner != resultTcs.Task)
                return (null, null,
                    "Sin respuesta (25s). Revisa tu conexión o usa 'Limpiar sesión' en Configuración.");

            return await resultTcs.Task;
        }
        finally
        {
            host?.Close();
        }
    }
}
