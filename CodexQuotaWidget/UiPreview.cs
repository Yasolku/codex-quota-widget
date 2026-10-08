namespace CodexQuotaWidget;

// Local design checks use synthetic data and never read account or user settings.
internal static class UiPreview
{
    public static void Render(string directory)
    {
        Directory.CreateDirectory(directory);
        using var form = new WidgetForm();
        var options = new AppSettings { Animations = false, OpacityPercent = 100, AutoTheme = false };
        foreach (var pct in new[] { 0d, 10d, 50d, 100d })
        {
            var sample = new QuotaSnapshot(new("weekly/10080", 45, DateTimeOffset.Now.AddDays(4)),
                new("five-hour/300", pct, DateTimeOffset.Now.AddHours(3)), 2,
                [DateTimeOffset.Now.AddDays(12)], DateTimeOffset.Now);
            options.BallMode = true;
            form.ApplySettings(options);
            form.UpdateSnapshot(sample, options);
            Save(form, Path.Combine(directory, $"ball-{pct:0}.png"));
            options.BallMode = false;
            form.ApplySettings(options);
            Save(form, Path.Combine(directory, $"panel-{pct:0}.png"));
        }
        options.DisplayMode = QuotaDisplayMode.Pro;
        options.BallMode = false;
        var pro = new QuotaSnapshot(new("weekly/10080", 83, DateTimeOffset.Now.AddDays(4)), null,
            2, [DateTimeOffset.Now.AddDays(12)], DateTimeOffset.Now);
        form.ApplySettings(options);
        form.UpdateSnapshot(pro, options);
        Save(form, Path.Combine(directory, "panel-pro.png"));
        form.UpdateSnapshot(pro with { Error = "连接失败：示例错误，用于验证错误状态提示。" }, options);
        Save(form, Path.Combine(directory, "panel-stale.png"));
        options.Compact = true;
        form.ApplySettings(options);
        Save(form, Path.Combine(directory, "panel-compact.png"));
        form.UpdateSnapshot(QuotaSnapshot.Unavailable("正在读取…"), options);
        options.BallMode = true;
        form.ApplySettings(options);
        Save(form, Path.Combine(directory, "ball-unavailable.png"));
        // Exercise the actual click handlers rather than only setting the layout directly.
        var toggles = 0;
        var refreshes = 0;
        form.BallModeChanged += _ => toggles++;
        form.RefreshRequested += () => refreshes++;
        Click(form, new Point(44, 44));
        if (form.Width != 340 || toggles != 1) throw new InvalidOperationException("Ball click did not expand.");
        Click(form, new Point(267, 29));
        if (refreshes != 1) throw new InvalidOperationException("Refresh action did not fire.");
        Click(form, new Point(309, 29));
        if (form.Width != 88 || toggles != 2) throw new InvalidOperationException("Collapse action did not return to ball.");
        Click(form, new Point(44, 44));
        Click(form, new Point(60, 28));
        if (form.Width != 88 || toggles != 4) throw new InvalidOperationException("CODEX click did not return to ball.");
        var themed = new QuotaSnapshot(new("weekly/10080", 45, DateTimeOffset.Now.AddDays(4)),
            new("five-hour/300", 50, DateTimeOffset.Now.AddHours(3)), 2, [DateTimeOffset.Now.AddDays(12)], DateTimeOffset.Now);
        options.Compact = false;
        options.DisplayMode = QuotaDisplayMode.Auto;
        foreach (var theme in new[] { WidgetTheme.Light, WidgetTheme.Dark })
        {
            options.Theme = theme;
            options.BallMode = true;
            form.ApplySettings(options); form.UpdateSnapshot(themed, options);
            Save(form, Path.Combine(directory, $"ball-{theme.ToString().ToLowerInvariant()}.png"));
            options.BallMode = false;
            form.ApplySettings(options);
            Save(form, Path.Combine(directory, $"panel-{theme.ToString().ToLowerInvariant()}.png"));
        }
        var schedule = new AppSettings();
        foreach (var (hour, minute, expected) in new[] { (0, 0, true), (6, 59, true), (7, 0, false), (17, 59, false), (18, 0, true), (23, 59, true) })
            if (ThemeSchedule.IsDark(schedule, DateTime.Today.AddHours(hour).AddMinutes(minute)) != expected)
                throw new InvalidOperationException("Theme time boundary failed.");
        schedule.LightStartMinute = 18 * 60; schedule.DarkStartMinute = 7 * 60;
        if (ThemeSchedule.IsDark(schedule, DateTime.Today.AddHours(23)) || !ThemeSchedule.IsDark(schedule, DateTime.Today.AddHours(12)))
            throw new InvalidOperationException("Overnight schedule failed.");
        // Check the real timer callback's interpolation and final target without opening a window.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var mix = typeof(WidgetForm).GetField("themeMix", flags)!;
        var advance = typeof(WidgetForm).GetMethod("AdvanceAnimation", flags)!;
        options.Animations = true;
        form.ApplySettings(options);
        mix.SetValue(form, 0d);
        for (var i = 0; i < 10; i++) advance.Invoke(form, [.025d]);
        if (Math.Abs((double)mix.GetValue(form)! - .5) > .001) throw new InvalidOperationException("Theme midpoint failed.");
        Save(form, Path.Combine(directory, "panel-transition.png"));
        for (var i = 0; i < 10; i++) advance.Invoke(form, [.025d]);
        if (Math.Abs((double)mix.GetValue(form)! - 1) > .001) throw new InvalidOperationException("Theme animation did not complete.");
        options.BallMode = true;
        options.Theme = WidgetTheme.Light;
        options.Animations = false;
        foreach (var size in new[] { 48, 88, 160 })
        {
            options.BallSize = size;
            form.ApplySettings(options); form.UpdateSnapshot(themed, options);
            if (form.Width != size || form.Height != size) throw new InvalidOperationException("Ball resize failed.");
            Save(form, Path.Combine(directory, $"ball-size-{size}.png"));
        }
        options.BallSize = 88;
        options.WaterWaves = false;
        form.ApplySettings(options);
        var phase = typeof(WidgetForm).GetField("phase", flags)!;
        var before = (double)phase.GetValue(form)!;
        advance.Invoke(form, [.025d]);
        if ((double)phase.GetValue(form)! != before) throw new InvalidOperationException("Disabled waves still moved.");
        Save(form, Path.Combine(directory, "ball-waves-off.png"));
        options.WaterWaves = true;
        form.ApplySettings(options);
        advance.Invoke(form, [.025d]);
        if ((double)phase.GetValue(form)! <= before) throw new InvalidOperationException("Enabled waves did not move.");
        using var proJson = System.Text.Json.JsonDocument.Parse("""
            {"result":{"rateLimits":{"planType":"prolite","primary":{"usedPercent":17,"windowDurationMins":10080,"resetsAt":2000000000}},
             "rateLimitsByLimitId":{"other":{"planType":"plus","primary":{"usedPercent":99,"windowDurationMins":10080,"resetsAt":2100000000}}}}}
            """);
        var parse = typeof(CodexAppServerClient).GetMethod("Parse", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var detectedPro = (QuotaSnapshot)parse.Invoke(null, [proJson.RootElement])!;
        if (!detectedPro.IsPro || !detectedPro.HideFiveHourRow(QuotaDisplayMode.Auto) || detectedPro.Weekly?.RemainingPercent != 83)
            throw new InvalidOperationException("Pro plan detection failed.");
        options.DisplayMode = QuotaDisplayMode.Auto;
        options.Compact = false;
        options.Animations = false;
        foreach (var planSample in new[] { themed with { PlanType = "plus" }, detectedPro })
        {
            foreach (var scale in new[] { 75, 100, 150 })
            {
                options.PanelScalePercent = scale;
                options.BallMode = false;
                form.ApplySettings(options); form.UpdateSnapshot(planSample, options);
                var expectedHeight = (int)Math.Round((planSample.IsPro ? 250 : 306) * scale / 100d);
                if (form.Width != (int)Math.Round(340 * scale / 100d) || form.Height != expectedHeight)
                    throw new InvalidOperationException("Scaled plan layout failed.");
                Save(form, Path.Combine(directory, $"panel-{(planSample.IsPro ? "pro" : "plus")}-scale-{scale}.png"));
                var count = refreshes;
                Click(form, new Point((int)(267 * scale / 100d), (int)(29 * scale / 100d)));
                if (refreshes != count + 1) throw new InvalidOperationException("Scaled refresh hit area failed.");
                Click(form, new Point((int)(60 * scale / 100d), (int)(28 * scale / 100d)));
                if (form.Width != 88) throw new InvalidOperationException("Scaled CODEX hit area failed.");
            }
        }
        options.PanelScalePercent = 100;
        options.BallMode = false;
        options.Theme = WidgetTheme.Dark;
        foreach (var right in new[] { false, true })
        {
            options.MainQuotaRightAligned = right;
            form.ApplySettings(options); form.UpdateSnapshot(detectedPro, options);
            Save(form, Path.Combine(directory, $"panel-pro-{(right ? "right" : "left")}.png"));
        }
    }

    private static void Save(WidgetForm form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void Click(WidgetForm form, Point point)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var mouse = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
        typeof(WidgetForm).GetMethod("OnMouseDown", flags)!.Invoke(form, [mouse]);
        typeof(WidgetForm).GetMethod("OnMouseUp", flags)!.Invoke(form, [mouse]);
    }
}
