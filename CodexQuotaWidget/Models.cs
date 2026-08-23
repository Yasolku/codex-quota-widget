using System.Text.Json.Serialization;

namespace CodexQuotaWidget;

internal sealed record UsageWindow(string Name, double RemainingPercent, DateTimeOffset? ResetsAt);

internal sealed record QuotaSnapshot(
    UsageWindow? Weekly,
    UsageWindow? Short,
    int ResetCredits,
    IReadOnlyList<DateTimeOffset> ResetCreditExpiries,
    DateTimeOffset UpdatedAt,
    string? Error = null)
{
    public static QuotaSnapshot Unavailable(string message) => new(null, null, 0, [], DateTimeOffset.Now, message);
}

internal sealed class AppSettings
{
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    public bool AlwaysOnTop { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ShowInTaskbar { get; set; } = true;
    public bool Compact { get; set; }
    public int OpacityPercent { get; set; } = 92;
    public int RefreshSeconds { get; set; } = 60;
    public int LowQuotaThreshold { get; set; } = 20;
    public DateTimeOffset? ManualResetExpiry { get; set; }

    [JsonIgnore]
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaWidget");
    [JsonIgnore]
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
}
