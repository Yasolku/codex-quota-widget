using System.Text.Json;

namespace CodexQuotaWidget;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(AppSettings.FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettings.FilePath), Options) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppSettings.DirectoryPath);
        File.WriteAllText(AppSettings.FilePath, JsonSerializer.Serialize(settings, Options));
    }
}
