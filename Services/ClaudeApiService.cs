using System.Net.Http;
using System.Text.Json;
using ClaudeUsageMonitor.Models;

namespace ClaudeUsageMonitor.Services;

public record UsageFetch(
    double SessionPercent,
    DateTime SessionResetsAt,
    double AllModelsPercent,
    DateTime WeeklyResetsAt,
    double OpusPercent,
    DateTime OpusResetsAt,
    bool OpusUsed,
    string Plan
);

public class ClaudeApiService
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false   // detect 302→/login instead of silently following
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private string _sessionKey  = "";
    private string _cookieHeader = "";  // full cookie jar when available
    private string? _orgId;

    // Configured when either a real session key OR a full cookie jar (WebView2 auth) is present
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_sessionKey) || !string.IsNullOrWhiteSpace(_cookieHeader);

    public void Configure(AppSettings settings)
    {
        _sessionKey = NormalizeKey(settings.SessionKey);
        _orgId      = settings.OrgId;
        // Prefer the full cookie jar captured by WebView2; fall back to session key only
        _cookieHeader = !string.IsNullOrWhiteSpace(settings.AllCookies)
            ? settings.AllCookies
            : $"sessionKey={_sessionKey}";
    }

    // Accept both "sk-ant-sid01-xxx" and "sessionKey=sk-ant-sid01-xxx"
    private static string NormalizeKey(string key)
    {
        key = key.Trim();
        if (key.StartsWith("sessionKey=", StringComparison.OrdinalIgnoreCase))
            key = key["sessionKey=".Length..].Trim();
        return key;
    }

    private HttpRequestMessage Req(string url) =>
        new(HttpMethod.Get, url)
        {
            Headers =
            {
                { "Cookie",           _cookieHeader },
                { "User-Agent",       "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36" },
                { "Accept",           "application/json, text/plain, */*" },
                { "Accept-Language",  "es-ES,es;q=0.9,en;q=0.8" },
                { "Origin",           "https://claude.ai" },
                { "Referer",          "https://claude.ai/" },
                { "Sec-Fetch-Dest",   "empty" },
                { "Sec-Fetch-Mode",   "cors" },
                { "Sec-Fetch-Site",   "same-origin" },
                { "sec-ch-ua-mobile", "?0" }
            }
        };

    // ── Public API ────────────────────────────────────────────────────────

    public async Task<(bool Ok, string? Email, string? Error)> TestConnectionAsync()
    {
        try
        {
            using var resp = await _http.SendAsync(Req("https://claude.ai/api/account"));
            var body = await resp.Content.ReadAsStringAsync();
            var status = (int)resp.StatusCode;

            // 3xx → redirected to login page — key is expired or wrong
            if (status is >= 300 and < 400)
                return (false, null, "Session key expirada o inválida — el servidor redirigió al login. Copia una nueva desde el navegador.");

            if (status is 401 or 403)
                return (false, null, $"Acceso denegado (HTTP {status}). Usa el botón 'Iniciar sesión con Claude' para conectar automáticamente con todas las cookies necesarias.");

            if (!resp.IsSuccessStatusCode)
                return (false, null, $"HTTP {status}: {Snip(body)}");

            // Guard against HTML responses (session cookie issue or redirect followed)
            if (!IsJson(resp, body))
                return (false, null, "El servidor devolvió HTML en vez de JSON. La session key puede estar caducada o ser incorrecta.");

            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? email = root.TryGetProperty("email", out var em) ? em.GetString() : null;
            _orgId = ExtractOrgId(root);

            return (true, email, null);
        }
        catch (HttpRequestException ex) { return (false, null, $"Sin conexión: {ex.Message}"); }
        catch (JsonException)           { return (false, null, "Respuesta inesperada del servidor (no es JSON válido)."); }
        catch (Exception ex)            { return (false, null, ex.Message); }
    }

    public async Task<(UsageFetch? Data, string? Error)> FetchUsageAsync()
    {
        if (!IsConfigured)
            return (null, "No hay session key configurada.");

        if (_orgId == null)
        {
            var (ok, _, err) = await TestConnectionAsync();
            if (!ok) return (null, err);
            if (_orgId == null) return (null, "No se pudo obtener el ID de organización desde /api/account.");
        }

        try
        {
            using var resp = await _http.SendAsync(
                Req($"https://claude.ai/api/organizations/{_orgId}/usage"));

            var body = await resp.Content.ReadAsStringAsync();
            var status = (int)resp.StatusCode;

            if (status is >= 300 and < 400)
                return (null, "Sesión caducada al obtener límites — vuelve a introducir tu session key.");

            if (!resp.IsSuccessStatusCode)
                return (null, $"Error HTTP {status} al obtener límites: {Snip(body)}");

            return (ParseLimits(body), null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }

    // ── Intercepted-data parser (used by WebInterceptService path) ───────────

    public (UsageFetch? Data, string? Email) ParseInterceptedData(
        string? accountJson, string? rateLimitsJson)
    {
        string? email = null;

        if (accountJson != null)
        {
            try
            {
                var doc  = JsonDocument.Parse(accountJson);
                var root = doc.RootElement;
                email  = root.TryGetProperty("email", out var em) ? em.GetString() : null;
                _orgId = ExtractOrgId(root) ?? _orgId;
            }
            catch { }
        }

        if (rateLimitsJson == null) return (null, email);

        try   { return (ParseLimits(rateLimitsJson), email); }
        catch { return (null, email); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsJson(HttpResponseMessage resp, string body)
    {
        var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (ct.Contains("json")) return true;
        var trimmed = body.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string Snip(string s) => s.Length > 250 ? s[..250] + "…" : s;

    // ── Org ID extraction ─────────────────────────────────────────────────

    private static string? ExtractOrgId(JsonElement root)
    {
        // memberships[].organization.uuid
        if (root.TryGetProperty("memberships", out var mems) && mems.ValueKind == JsonValueKind.Array)
            foreach (var m in mems.EnumerateArray())
                if (m.TryGetProperty("organization", out var org) && org.TryGetProperty("uuid", out var u))
                    return u.GetString();

        // organizations[].uuid
        if (root.TryGetProperty("organizations", out var orgs) && orgs.ValueKind == JsonValueKind.Array)
            foreach (var o in orgs.EnumerateArray())
                if (o.TryGetProperty("uuid", out var u)) return u.GetString();

        // direct scalar fields
        foreach (var key in new[] { "organization_uuid", "org_uuid", "default_organization_uuid" })
            if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();

        return null;
    }

    // ── Usage parsing ─────────────────────────────────────────────────────
    // Shape of /api/organizations/{org_id}/usage:
    //   { "five_hour":  { "utilization": 14.0, "resets_at": "2026-05-28T18:30:01+00:00" },
    //     "seven_day":  { "utilization": 32.0, "resets_at": "2026-05-31T11:00:00+00:00" },
    //     "seven_day_opus":   null | { ... },
    //     "seven_day_sonnet": null | { ... },  ...
    //     "extra_usage":      { ... } }

    private static UsageFetch ParseLimits(string json)
    {
        double sessionPct = 0, weeklyPct = 0, opusPct = 0;
        bool opusUsed = false;
        DateTime sessionReset = DateTime.Now.AddHours(5);
        DateTime weeklyReset = NextSunday();
        DateTime opusReset = NextSunday();

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Default();

            if (root.TryGetProperty("five_hour", out var fh) && fh.ValueKind == JsonValueKind.Object)
            {
                sessionPct = ReadUtilization(fh);
                sessionReset = ReadResetsAt(fh) ?? sessionReset;
            }
            if (root.TryGetProperty("seven_day", out var sd) && sd.ValueKind == JsonValueKind.Object)
            {
                weeklyPct = ReadUtilization(sd);
                weeklyReset = ReadResetsAt(sd) ?? weeklyReset;
            }
            if (root.TryGetProperty("seven_day_opus", out var op) && op.ValueKind == JsonValueKind.Object)
            {
                opusPct = ReadUtilization(op);
                opusUsed = opusPct > 0;
                opusReset = ReadResetsAt(op) ?? opusReset;
            }
        }
        catch { }

        return new UsageFetch(sessionPct, sessionReset, weeklyPct, weeklyReset,
                              opusPct, opusReset, opusUsed, "Pro");

        UsageFetch Default() => new(0, sessionReset, 0, weeklyReset, 0, opusReset, false, "Pro");
    }

    private static double ReadUtilization(JsonElement el)
    {
        if (el.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number)
            return Math.Round(u.GetDouble(), 1);
        return 0;
    }

    private static DateTime? ReadResetsAt(JsonElement el)
    {
        if (el.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
            && DateTime.TryParse(r.GetString(), out var dt))
            return dt.ToLocalTime();
        return null;
    }

    private static DateTime NextSunday()
    {
        var now = DateTime.Now;
        int d = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;
        return now.AddDays(d == 0 ? 7 : d).Date.AddHours(12);
    }
}
