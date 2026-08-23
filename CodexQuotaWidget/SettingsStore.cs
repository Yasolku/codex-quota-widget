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

    public static bool Save(AppSettings settings)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(AppSettings.DirectoryPath);
            temporaryPath = Path.Combine(
                AppSettings.DirectoryPath,
                $"settings.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options));
            if (File.Exists(AppSettings.FilePath))
                File.Replace(temporaryPath, AppSettings.FilePath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, AppSettings.FilePath);
            return true;
        }
        catch
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
            return false;
        }
    }
}
