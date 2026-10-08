using System.Threading;

namespace CodexQuotaWidget;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--benchmark-ui")
        {
            ApplicationConfiguration.Initialize();
            UiPerformance.Run(args[1]);
            return;
        }
        if (args.Length == 2 && args[0] == "--render-preview")
        {
            ApplicationConfiguration.Initialize();
            UiPreview.Render(args[1]);
            return;
        }
        using var wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexQuotaWidget5hWeekly-Wake-4D560AC3");
        using var mutex = new Mutex(true, "Local\\CodexQuotaWidget5hWeekly-4D560AC3", out var first);
        if (!first) { wakeEvent.Set(); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new WidgetApplicationContext(args.Contains("--demo", StringComparer.OrdinalIgnoreCase), wakeEvent));
    }
}
