using System.Diagnostics;
using System.Text.Json;

namespace CodexQuotaWidget;

// Measures the real UI message loop with synthetic data, without account reads or settings writes.
internal static class UiPerformance
{
    public static void Run(string output)
    {
        using var form = new WidgetForm();
        using var timer = new System.Windows.Forms.Timer { Interval = 250 };
        using var process = Process.GetCurrentProcess();
        var options = new AppSettings { AutoTheme = false, OpacityPercent = 100, AlwaysOnTop = false, ShowInTaskbar = false };
        var snapshot = new QuotaSnapshot(new("weekly/10080", 72, DateTimeOffset.Now.AddDays(3)), null,
            2, [], DateTimeOffset.Now, PlanType: "prolite");
        var cases = new[] { "ball-waves", "ball-static", "panel-static", "hidden" };
        var results = new List<object>();
        var index = -1;
        var clock = Stopwatch.StartNew();
        TimeSpan initialCpu = default;
        long initialAllocation = 0;
        var measuring = false;

        void BeginCase()
        {
            index++;
            options.BallMode = index != 2;
            options.WaterWaves = index == 0;
            options.Animations = true;
            form.ApplySettings(options); form.UpdateSnapshot(snapshot, options);
            var area = Screen.PrimaryScreen!.WorkingArea;
            form.Location = new Point(area.Left + 16, area.Bottom - form.Height - 16);
            if (index == 3) form.Hide(); else form.Show();
            measuring = false;
            clock.Restart();
        }

        form.Shown += (_, _) => { BeginCase(); timer.Start(); };
        timer.Tick += (_, _) =>
        {
            if (!measuring && clock.Elapsed.TotalSeconds >= 2)
            {
                process.Refresh();
                initialCpu = process.TotalProcessorTime;
                initialAllocation = GC.GetTotalAllocatedBytes();
                measuring = true;
                clock.Restart();
            }
            if (!measuring) return;
            if (clock.Elapsed.TotalSeconds < 6) return;
            var seconds = clock.Elapsed.TotalSeconds;
            // Query process counters only once per measurement, not on every timer tick:
            // frequent Windows process-memory queries would dominate idle UI CPU usage.
            process.Refresh();
            var cpu = (process.TotalProcessorTime - initialCpu).TotalSeconds / seconds * 100;
            var allocation = (GC.GetTotalAllocatedBytes() - initialAllocation) / seconds / 1024;
            results.Add(new { Scenario = cases[index], Seconds = Math.Round(seconds, 2),
                CpuOneCorePercent = Math.Round(cpu, 3),
                PrivateMb = Math.Round(process.PrivateMemorySize64 / 1048576d, 2), WorkingSetMb = Math.Round(process.WorkingSet64 / 1048576d, 2),
                AllocationKbPerSecond = Math.Round(allocation, 2) });
            if (index + 1 < cases.Length) BeginCase();
            else
            {
                timer.Stop(); form.Hide();
                File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                Application.ExitThread();
            }
        };
        Application.Run(form);
    }
}
