using System.Threading;

namespace CodexQuotaWidget;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexQuotaWidget5hWeekly-Wake-4D560AC3");
        using var mutex = new Mutex(true, "Local\\CodexQuotaWidget5hWeekly-4D560AC3", out var first);
        if (!first) { wakeEvent.Set(); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new WidgetApplicationContext(args.Contains("--demo", StringComparer.OrdinalIgnoreCase), wakeEvent));
    }
}
