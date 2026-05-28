using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using ClaudeUsageMonitor.Models;
using ClaudeUsageMonitor.Services;

namespace ClaudeUsageMonitor.ViewModels;

public class UsageDashboardViewModel : INotifyPropertyChanged
{
    private readonly StorageService _storage;
    private readonly ClaudeApiService _api;
    private readonly WebInterceptService _interceptor;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private UsageData _data;
    private AppSettings _settings;
    private readonly DispatcherTimer _clockTimer;
    private DateTime _lastRefresh = DateTime.Now;

    private bool _isSyncing;
    private string _syncStatus = "";

    public UsageDashboardViewModel(StorageService storage, ClaudeApiService api,
                                   WebInterceptService interceptor)
    {
        _storage = storage;
        _api = api;
        _interceptor = interceptor;
        _data = _storage.LoadUsage();
        _settings = _storage.LoadSettings();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(SessionResetText));
            OnPropertyChanged(nameof(LastRefreshText));
        };
        _clockTimer.Start();

        _loc.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    // ── Connection ────────────────────────────────────────────────────────
    public bool IsConnected => !string.IsNullOrWhiteSpace(_settings.SessionKey) ||
                               !string.IsNullOrWhiteSpace(_settings.AllCookies);
    public string ConnectionText => IsConnected
        ? _loc.Format("connected_to_fmt", _settings.UserEmail ?? _loc["claude_account"])
        : _loc["not_connected"];

    public bool IsSyncing
    {
        get => _isSyncing;
        private set { _isSyncing = value; OnPropertyChanged(); OnPropertyChanged(nameof(SyncButtonText)); }
    }

    public string SyncButtonText => IsSyncing ? "⏳" : "⟳";

    public string SyncStatus
    {
        get => _syncStatus;
        private set { _syncStatus = value; OnPropertyChanged(); }
    }

    public async Task FetchFromApiAsync()
    {
        if (!_api.IsConfigured) { SyncStatus = _loc["sync_configure_first"]; return; }

        IsSyncing = true;
        SyncStatus = _loc["syncing"];

        var (accountJson, rateLimitsJson, interceptError) = await _interceptor.FetchRawAsync();

        if (interceptError != null)
        {
            SyncStatus = _loc.Format("sync_err_generic_fmt", interceptError);
            IsSyncing = false;
            return;
        }

        if (accountJson == null)
        {
            SyncStatus = _loc["sync_err_account_fmt"];
            IsSyncing = false;
            return;
        }

        if (rateLimitsJson == null)
        {
            SyncStatus = _loc["sync_err_org_fmt"];
            IsSyncing = false;
            return;
        }

        ApplyFetchedData(accountJson, rateLimitsJson);
        IsSyncing = false;
    }

    // Called directly when ClaudeLoginWindow already fetched the data for us.
    public void ApplyFetchedData(string accountJson, string rateLimitsJson)
    {
        var (data, email) = _api.ParseInterceptedData(accountJson, rateLimitsJson);

        if (data == null)
        {
            SyncStatus = _loc["sync_err_parse"];
            return;
        }

        if (email != null && email != _settings.UserEmail)
        {
            _settings.UserEmail = email;
            _storage.SaveSettings(_settings);
        }

        _data.CurrentSessionPercent      = data.SessionPercent;
        _data.SessionResetTime           = data.SessionResetsAt;
        _data.AllModelsWeeklyPercent     = data.AllModelsPercent;
        _data.WeeklyAllModelsResetTime   = data.WeeklyResetsAt;
        _data.ClaudeDesignPercent        = data.OpusPercent;
        _data.ClaudeDesignUsed           = data.OpusUsed;
        _data.WeeklyDesignResetTime      = data.OpusResetsAt;
        _data.PlanTier                   = data.Plan;
        _storage.SaveUsage(_data);
        _lastRefresh = DateTime.Now;
        SyncStatus = "";
        OnPropertyChanged(string.Empty);
    }

    public void ReloadSettings()
    {
        _settings = _storage.LoadSettings();
        _api.Configure(_settings);
        OnPropertyChanged(string.Empty);
    }

    // ── Plan ──────────────────────────────────────────────────────────────
    public string PlanTier => _data.PlanTier;

    // ── Current session ───────────────────────────────────────────────────
    public double SessionPercent
    {
        get => _data.CurrentSessionPercent;
        set
        {
            _data.CurrentSessionPercent = Math.Clamp(value, 0, 100);
            _storage.SaveUsage(_data);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SessionPercentText));
        }
    }

    public string SessionPercentText => _loc.Format("percent_used_fmt", _data.CurrentSessionPercent);

    public string SessionResetText
    {
        get
        {
            var remaining = _data.SessionResetTime - DateTime.Now;
            if (remaining.TotalSeconds <= 0) return _loc["resets_soon"];
            if (remaining.TotalHours >= 1)
                return _loc.Format("resets_in_h_m_fmt", (int)remaining.TotalHours, remaining.Minutes);
            return _loc.Format("resets_in_m_fmt", remaining.Minutes);
        }
    }

    // ── Weekly — all models ───────────────────────────────────────────────
    public double AllModelsPercent
    {
        get => _data.AllModelsWeeklyPercent;
        set
        {
            _data.AllModelsWeeklyPercent = Math.Clamp(value, 0, 100);
            _storage.SaveUsage(_data);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AllModelsPercentText));
        }
    }

    public string AllModelsPercentText => _loc.Format("percent_used_fmt", _data.AllModelsWeeklyPercent);

    public string WeeklyAllModelsResetText
    {
        get
        {
            var d = _data.WeeklyAllModelsResetTime;
            return _loc.Format("resets_on_fmt", _loc.ShortDay(d.DayOfWeek), FormatTime(d));
        }
    }

    // ── Weekly — Opus (stored under legacy ClaudeDesign* field names) ─────
    public double ClaudeDesignPercent
    {
        get => _data.ClaudeDesignPercent;
        set
        {
            _data.ClaudeDesignPercent = Math.Clamp(value, 0, 100);
            _data.ClaudeDesignUsed = value > 0;
            _storage.SaveUsage(_data);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ClaudeDesignSubText));
            OnPropertyChanged(nameof(ClaudeDesignPercentText));
        }
    }

    public string ClaudeDesignPercentText => _loc.Format("percent_used_fmt", _data.ClaudeDesignPercent);

    public string ClaudeDesignSubText => _data.ClaudeDesignUsed
        ? _loc.Format("resets_on_fmt",
            _loc.ShortDay(_data.WeeklyDesignResetTime.DayOfWeek),
            FormatTime(_data.WeeklyDesignResetTime))
        : _loc["opus_not_used"];

    // ── Footer ────────────────────────────────────────────────────────────
    public string LastRefreshText
    {
        get
        {
            var e = DateTime.Now - _lastRefresh;
            if (e.TotalSeconds < 60) return _loc["just_now"];
            if (e.TotalMinutes < 60) return _loc.Format("minutes_ago_fmt", (int)e.TotalMinutes);
            return _loc.Format("hours_ago_fmt", (int)e.TotalHours);
        }
    }

    public void Refresh()
    {
        _data = _storage.LoadUsage();
        _lastRefresh = DateTime.Now;
        OnPropertyChanged(string.Empty);
    }

    private static string FormatTime(DateTime dt)
    {
        int h = dt.Hour % 12; if (h == 0) h = 12;
        return $"{h}:{dt.Minute:00} {(dt.Hour < 12 ? "a.m." : "p.m.")}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
