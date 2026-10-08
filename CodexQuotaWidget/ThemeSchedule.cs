namespace CodexQuotaWidget;

internal static class ThemeSchedule
{
    public static bool IsDark(AppSettings settings, DateTime localTime)
    {
        if (!settings.AutoTheme) return settings.Theme == WidgetTheme.Dark;
        var start = Math.Clamp(settings.LightStartMinute, 0, 1439);
        var end = Math.Clamp(settings.DarkStartMinute, 0, 1439);
        var minute = localTime.Hour * 60 + localTime.Minute;
        var light = start == end || (start < end ? minute >= start && minute < end : minute >= start || minute < end);
        return !light;
    }
}
