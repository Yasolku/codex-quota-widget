using System.Threading;

namespace CodexQuotaWidget;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexQuotaWidget-Wake-7B869AAC");
        using var mutex = new Mutex(true, "Local\\CodexQuotaWidget-7B869AAC", out var first);
        if (!first) { wakeEvent.Set(); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new WidgetApplicationContext(args.Contains("--demo", StringComparer.OrdinalIgnoreCase), wakeEvent));
    }
}
