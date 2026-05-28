using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WpfApp = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHAlign = System.Windows.HorizontalAlignment;

namespace ClaudeUsageMonitor.Services;

// Opens claude.ai's own settings/usage page in a visible WebView2 (reusing the
// authenticated profile) and records every /api/ response the page makes.
// The captured bodies are dumped to a JSON file so we can identify which
// endpoint actually carries the 5-hour session / weekly usage numbers.
public class EndpointDiscoveryService
{
    private static readonly string _userDataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ClaudeUsageMonitor", "WebView2");

    private static readonly string _outputDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "ClaudeUsageMonitor");

    // If a response body contains any of these substrings, mark it as
    // "interesting" — these are the words that would appear in a payload
    // describing the user-facing usage UI.
    private static readonly string[] _interestingKeywords = new[]
    {
        "session", "weekly", "five_hour", "5_hour", "five-hour",
        "resets_at", "reset_at", "next_reset",
        "percent_used", "pct_used", "used", "consumed", "remaining",
        "rate_limit_status", "usage_quota", "usage_summary", "usage_limits",
        "claude_design"
    };

    public record DiscoveryResult(string? FilePath, int TotalCaptured, int InterestingCount, string? Error);

    public Task<DiscoveryResult> DiscoverAsync() =>
        WpfApp.Current.Dispatcher.InvokeAsync(DoDiscoverAsync).Task.Unwrap();

    private record CapturedResponse(
        string Url,
        int Status,
        string? ContentType,
        string? Body,
        bool Interesting,
        DateTime Timestamp);

    private async Task<DiscoveryResult> DoDiscoverAsync()
    {
        var captured = new List<CapturedResponse>();
        var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Window? host = null;
        try
        {
            host = BuildHostWindow(out var statusText, out var doneBtn, out var webView);
            doneBtn.Click += (_, _) => doneTcs.TrySetResult(true);
            host.Closed += (_, _) => doneTcs.TrySetResult(true);
            host.Show();

            var env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.WebResourceResponseReceived += async (_, e) =>
            {
                await OnResponseAsync(e, captured, statusText);
            };

            webView.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            webView.CoreWebView2.Navigate("https://claude.ai/settings/usage");

            // Wait until the user clicks "Terminar" or 90 seconds elapse —
            // whichever comes first. 90 s gives the page plenty of time to
            // run lazy XHRs and lets the user click around the usage section
            // if extra requests are needed.
            await Task.WhenAny(doneTcs.Task, Task.Delay(TimeSpan.FromSeconds(90)));

            return WriteOutput(captured);
        }
        catch (Exception ex)
        {
            return new DiscoveryResult(null, captured.Count, 0, ex.Message);
        }
        finally
        {
            host?.Close();
        }
    }

    private static async Task OnResponseAsync(
        CoreWebView2WebResourceResponseReceivedEventArgs e,
        List<CapturedResponse> captured,
        TextBlock statusText)
    {
        try
        {
            var url = e.Request.Uri;
            if (!url.Contains("claude.ai", StringComparison.OrdinalIgnoreCase)) return;
            if (!url.Contains("/api/", StringComparison.OrdinalIgnoreCase)) return;

            var resp = e.Response;
            string? contentType = null;
            foreach (var h in resp.Headers)
                if (string.Equals(h.Key, "content-type", StringComparison.OrdinalIgnoreCase))
                { contentType = h.Value; break; }

            // Only read text/JSON bodies — skip binary like images / fonts
            string? body = null;
            if (contentType == null ||
                contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var stream = await resp.GetContentAsync();
                    if (stream != null)
                    {
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        body = await reader.ReadToEndAsync();
                    }
                }
                catch { /* body unavailable — keep URL + status anyway */ }
            }

            bool interesting = body != null && _interestingKeywords.Any(k =>
                body.Contains(k, StringComparison.OrdinalIgnoreCase));

            int count;
            int interestingCount;
            lock (captured)
            {
                captured.Add(new CapturedResponse(
                    url, resp.StatusCode, contentType, body, interesting, DateTime.Now));
                count = captured.Count;
                interestingCount = captured.Count(c => c.Interesting);
            }

            WpfApp.Current.Dispatcher.Invoke(() =>
            {
                statusText.Text = $"Capturadas {count} llamadas /api/ — " +
                                  $"{interestingCount} contienen datos de uso. " +
                                  "Pulsa 'Terminar y guardar' cuando hayas visto la sección de uso.";
            });
        }
        catch { /* never let a single bad response stop discovery */ }
    }

    private static DiscoveryResult WriteOutput(List<CapturedResponse> captured)
    {
        Directory.CreateDirectory(_outputDir);
        var outPath = Path.Combine(_outputDir,
            $"endpoint_discovery_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        var interesting = captured.Where(c => c.Interesting).ToList();

        // Build a readable summary at the top, then full bodies below.
        var payload = new
        {
            instructions =
                "Open the 'interesting_urls' list first — those are the responses whose body " +
                "mentioned session/weekly/percent/resets_at/etc. The one that contains the " +
                "5-hour session percentage and reset time is the endpoint to wire up. " +
                "Then look at its body in 'responses' below to confirm field names.",
            timestamp = DateTime.Now.ToString("o"),
            total_captured = captured.Count,
            interesting_count = interesting.Count,
            interesting_urls = interesting.Select(c => new
            {
                url = c.Url,
                status = c.Status,
                content_type = c.ContentType
            }).ToList(),
            responses = captured.Select(c => new
            {
                url = c.Url,
                status = c.Status,
                content_type = c.ContentType,
                interesting = c.Interesting,
                timestamp = c.Timestamp.ToString("o"),
                // Keep first 8 KB of body — enough to identify the right
                // endpoint without bloating the file with huge bundles.
                body_preview = c.Body == null
                    ? null
                    : c.Body.Length > 8192
                        ? c.Body[..8192] + "\n\n…[truncated, original length=" + c.Body.Length + "]"
                        : c.Body
            }).ToList()
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(outPath, json);

        return new DiscoveryResult(outPath, captured.Count, interesting.Count, null);
    }

    private static Window BuildHostWindow(
        out TextBlock statusText, out WpfButton doneBtn, out WebView2 webView)
    {
        var win = new Window
        {
            Title = "Diagnóstico de endpoints — claude.ai/settings/usage",
            Width = 980,
            Height = 720,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(WpfColor.FromRgb(0x1c, 0x1c, 0x1c))
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        statusText = new TextBlock
        {
            Text = "Cargando claude.ai/settings/usage… las llamadas /api/ se están capturando en segundo plano.",
            Foreground = WpfBrushes.White,
            Background = new SolidColorBrush(WpfColor.FromRgb(0x22, 0x22, 0x22)),
            Padding = new Thickness(14, 10, 14, 10),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(statusText, 0);
        grid.Children.Add(statusText);

        webView = new WebView2();
        Grid.SetRow(webView, 1);
        grid.Children.Add(webView);

        doneBtn = new WpfButton
        {
            Content = "Terminar y guardar",
            Padding = new Thickness(20, 8, 20, 8),
            Margin = new Thickness(12),
            HorizontalAlignment = WpfHAlign.Right,
            Background = new SolidColorBrush(WpfColor.FromRgb(0xff, 0x8c, 0x55)),
            Foreground = WpfBrushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        Grid.SetRow(doneBtn, 2);
        grid.Children.Add(doneBtn);

        win.Content = grid;
        return win;
    }
}
