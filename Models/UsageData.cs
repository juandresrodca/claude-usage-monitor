namespace ClaudeUsageMonitor.Models;

public class UsageData
{
    public string PlanTier { get; set; } = "Pro";

    // 5-hour session
    public double CurrentSessionPercent { get; set; } = 0;
    public DateTime SessionResetTime { get; set; } = DateTime.Now.AddHours(5);

    // Weekly — all models (7-day window)
    public double AllModelsWeeklyPercent { get; set; } = 0;
    public DateTime WeeklyAllModelsResetTime { get; set; } = GetNextWeekday(DayOfWeek.Sunday);

    // Weekly — Opus (was "Claude Design"; field name kept for backwards-compatible storage)
    public double ClaudeDesignPercent { get; set; } = 0;
    public bool ClaudeDesignUsed { get; set; } = false;
    public DateTime WeeklyDesignResetTime { get; set; } = GetNextWeekday(DayOfWeek.Sunday);

    private static DateTime GetNextWeekday(DayOfWeek day)
    {
        var now = DateTime.Now;
        int daysUntil = ((int)day - (int)now.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7;
        return now.AddDays(daysUntil).Date.AddHours(12);
    }
}
