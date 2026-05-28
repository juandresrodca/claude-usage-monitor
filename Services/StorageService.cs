using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClaudeUsageMonitor.Models;
using Newtonsoft.Json;

namespace ClaudeUsageMonitor.Services;

public class StorageService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageMonitor");

    private static readonly string UsagePath   = Path.Combine(AppDataDir, "config.json");
    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    private const string DpapiPrefix = "DPAPI:";

    // ── Usage data ────────────────────────────────────────────────────────
    public UsageData LoadUsage()
    {
        return LoadFile<UsageData>(UsagePath) ?? new UsageData();
    }

    public void SaveUsage(UsageData data) => SaveFile(UsagePath, data);

    // ── App settings ──────────────────────────────────────────────────────
    // Session credentials are encrypted with Windows DPAPI scoped to the
    // current user — the ciphertext can't be decrypted from another user
    // account on the same machine or moved to a different machine.
    public AppSettings LoadSettings()
    {
        var s = LoadFile<AppSettings>(SettingsPath) ?? new AppSettings();
        s.SessionKey = Unprotect(s.SessionKey);
        s.AllCookies = Unprotect(s.AllCookies);
        return s;
    }

    public void SaveSettings(AppSettings settings)
    {
        // Round-trip through JSON to avoid mutating the caller's instance —
        // we don't want to leave DPAPI ciphertext sitting in the live AppSettings
        // object after a save.
        var copy = JsonConvert.DeserializeObject<AppSettings>(
            JsonConvert.SerializeObject(settings)) ?? new AppSettings();
        copy.SessionKey = Protect(copy.SessionKey);
        copy.AllCookies = Protect(copy.AllCookies);
        SaveFile(SettingsPath, copy);
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static T? LoadFile<T>(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch { }
        return default;
    }

    private static void SaveFile<T>(string path, T obj)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(path, JsonConvert.SerializeObject(obj, Formatting.Indented));
        }
        catch { }
    }

    private static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (plaintext.StartsWith(DpapiPrefix)) return plaintext;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var enc = ProtectedData.Protect(bytes, optionalEntropy: null,
                                            DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(enc);
        }
        catch
        {
            return plaintext;
        }
    }

    private static string Unprotect(string maybeEncrypted)
    {
        if (string.IsNullOrEmpty(maybeEncrypted)) return maybeEncrypted;
        if (!maybeEncrypted.StartsWith(DpapiPrefix)) return maybeEncrypted;
        try
        {
            var bytes = Convert.FromBase64String(maybeEncrypted[DpapiPrefix.Length..]);
            var dec = ProtectedData.Unprotect(bytes, optionalEntropy: null,
                                              DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch
        {
            return "";
        }
    }
}
