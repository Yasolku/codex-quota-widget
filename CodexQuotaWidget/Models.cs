using System.Text.Json.Serialization;

namespace CodexQuotaWidget;

internal sealed record UsageWindow(string Name, double RemainingPercent, DateTimeOffset? ResetsAt);

internal enum QuotaDisplayMode { Auto, Plus, Pro }
internal enum WidgetTheme { Light, Dark }

internal sealed record QuotaSnapshot(
    UsageWindow? Weekly,
    UsageWindow? FiveHour,
    int ResetCredits,
    IReadOnlyList<DateTimeOffset> ResetCreditExpiries,
    DateTimeOffset UpdatedAt,
    string? Error = null,
    string? PlanType = null)
{
    public static QuotaSnapshot Unavailable(string message) => new(null, null, 0, [], DateTimeOffset.Now, message);
    public bool IsPro => PlanType?.StartsWith("pro", StringComparison.OrdinalIgnoreCase) == true;
    public bool HideFiveHourRow(QuotaDisplayMode mode) => IsPro || mode == QuotaDisplayMode.Pro;

    public (UsageWindow? Primary, UsageWindow? Secondary, string PrimaryName, string SecondaryName) Display(QuotaDisplayMode mode)
    {
        if (HideFiveHourRow(mode)) return (Weekly, null, "每周", "5 小时");
        var weeklyFirst = mode == QuotaDisplayMode.Pro || (mode == QuotaDisplayMode.Auto && FiveHour is null);
        if (weeklyFirst && Weekly is not null) return (Weekly, FiveHour, "每周", "5 小时");
        if (FiveHour is not null) return (FiveHour, Weekly, "5 小时", "每周");
        return (Weekly, null, "每周", "5 小时");
    }
}

internal sealed class AppSettings
{
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    public bool AlwaysOnTop { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ShowInTaskbar { get; set; } = true;
    public bool Compact { get; set; }
    public bool BallMode { get; set; } = true;
    public int BallSize { get; set; } = 88;
    public int PanelScalePercent { get; set; } = 100;
    public bool MainQuotaRightAligned { get; set; }
    public bool WaterWaves { get; set; } = true;
    public bool Animations { get; set; } = true;
    public WidgetTheme Theme { get; set; } = WidgetTheme.Light;
    public bool AutoTheme { get; set; } = true;
    public int LightStartMinute { get; set; } = 7 * 60;
    public int DarkStartMinute { get; set; } = 18 * 60;
    public int OpacityPercent { get; set; } = 92;
    public int RefreshSeconds { get; set; } = 60;
    public int LowQuotaThreshold { get; set; } = 20;
    public QuotaDisplayMode DisplayMode { get; set; } = QuotaDisplayMode.Auto;
    public DateTimeOffset? ManualResetExpiry { get; set; }

    [JsonIgnore]
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaWidget5hWeekly");
    [JsonIgnore]
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
}
