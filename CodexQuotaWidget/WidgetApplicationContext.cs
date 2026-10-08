namespace CodexQuotaWidget;

internal sealed class WidgetApplicationContext : ApplicationContext
{
    private readonly AppSettings settings = SettingsStore.Load();
    private readonly WidgetForm form = new();
    private readonly NotifyIcon tray = new();
    private readonly CodexAppServerClient client = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly System.Windows.Forms.Timer countdownTimer = new() { Interval = 30_000 };
    private RegisteredWaitHandle? wakeWait;
    private readonly CancellationTokenSource lifetime = new();
    private volatile bool exiting;
    private readonly EventWaitHandle wakeEvent;
    private bool refreshing;
    private bool demo;
    private string? notifiedCycle;
    private QuotaSnapshot snapshot = QuotaSnapshot.Unavailable("正在读取…");
    private Icon? currentIcon;
    private int? iconPercent;

    public WidgetApplicationContext(bool demoMode, EventWaitHandle wakeSignal)
    {
        demo = demoMode;
        wakeEvent = wakeSignal;
        settings.StartWithWindows = StartupManager.IsEnabledForCurrentExecutable();
        form.ApplySettings(settings);
        form.Place(settings);
        form.FormClosed += (_, _) => ExitThread();
        form.LocationChanged += (_, _) => { if (form.Visible) { settings.X = form.Left; settings.Y = form.Top; } };
        form.HideRequested += HideWidget;
        form.RefreshRequested += async () => await RefreshAsync();
        form.BallModeChanged += value => { settings.BallMode = value; SettingsStore.Save(settings); BuildMenu(); };
        tray.Visible = true;
        tray.Text = "Codex 限额读取中";
        tray.DoubleClick += (_, _) => ShowWidget();
        BuildMenu();
        UpdateIcon(null);
        form.Show();
        refreshTimer.Interval = Math.Clamp(settings.RefreshSeconds, 15, 3600) * 1000;
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        countdownTimer.Tick += (_, _) => form.UpdateSnapshot(snapshot, settings);
        wakeWait = ThreadPool.RegisterWaitForSingleObject(wakeEvent, (_, _) =>
        {
            if (exiting) return;
            try { form.BeginInvoke((Action)(() => { if (!exiting) ShowWidget(); })); }
            catch (InvalidOperationException) { }
        }, null, Timeout.Infinite, false);
        refreshTimer.Start(); countdownTimer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (refreshing || exiting) return;
        refreshing = true;
        try
        {
            var next = demo ? DemoSnapshot() : await client.ReadAsync(lifetime.Token);
            if (exiting) return;
            snapshot = next.Error is not null && (snapshot.Weekly is not null || snapshot.FiveHour is not null)
                ? snapshot with { Error = next.Error } : next;
            form.UpdateSnapshot(snapshot, settings);
            var (primary, secondary, primaryName, secondaryName) = snapshot.Display(settings.DisplayMode);
            UpdateIcon(primary?.RemainingPercent);
            var pct = primary?.RemainingPercent;
            var secondaryPct = secondary?.RemainingPercent;
            var secondaryText = secondaryPct.HasValue ? $"{secondaryPct:0}%" : "--";
            tray.Text = pct.HasValue ? $"Codex {primaryName} {pct:0}%" + (snapshot.HideFiveHourRow(settings.DisplayMode) ? "" : $" · {secondaryName} {secondaryText}") : "Codex 额度暂不可用";
            if (snapshot.Error is not null && pct.HasValue) tray.Text += "（待刷新）";
            if (snapshot.Error is null && pct.HasValue && pct <= settings.LowQuotaThreshold)
            {
                var cycle = $"{primaryName}:{primary?.ResetsAt?.ToUnixTimeSeconds().ToString() ?? "unknown"}";
                if (notifiedCycle != cycle)
                {
                    tray.BalloonTipTitle = $"Codex {primaryName}额度偏低";
                    tray.BalloonTipText = $"当前剩余 {pct:0}%，{FormatReset(primary?.ResetsAt)}。";
                    tray.BalloonTipIcon = ToolTipIcon.Warning;
                    tray.ShowBalloonTip(6000);
                    notifiedCycle = cycle;
                }
            }
        }
        catch (Exception ex)
        {
            if (exiting) return;
            snapshot = snapshot with { Error = $"刷新失败：{ex.Message}" };
            UpdateDisplay();
        }
        finally { refreshing = false; }
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示悬浮窗", null, (_, _) => ShowWidget());
        menu.Items.Add("隐藏到托盘", null, (_, _) => HideWidget());
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(new ToolStripSeparator());
        var top = new ToolStripMenuItem("始终置顶") { Checked = settings.AlwaysOnTop, CheckOnClick = true };
        top.CheckedChanged += (_, _) => { settings.AlwaysOnTop = top.Checked; ApplyAndSave(); };
        var taskbar = new ToolStripMenuItem("任务栏显示百分比") { Checked = settings.ShowInTaskbar, CheckOnClick = true };
        taskbar.CheckedChanged += (_, _) => { settings.ShowInTaskbar = taskbar.Checked; ApplyAndSave(); };
        var compact = new ToolStripMenuItem("紧凑模式") { Checked = settings.Compact, CheckOnClick = true };
        compact.CheckedChanged += (_, _) => { settings.Compact = compact.Checked; ApplyAndSave(); };
        var ballMode = new ToolStripMenuItem("悬浮球模式") { Checked = settings.BallMode, CheckOnClick = true };
        ballMode.CheckedChanged += (_, _) => { settings.BallMode = ballMode.Checked; ApplyAndSave(); };
        var animations = new ToolStripMenuItem("平滑过渡（主题与水位）") { Checked = settings.Animations, CheckOnClick = true };
        animations.CheckedChanged += (_, _) => { settings.Animations = animations.Checked; ApplyAndSave(); };
        var waterWaves = new ToolStripMenuItem("水位轻微波动") { Checked = settings.WaterWaves, CheckOnClick = true };
        waterWaves.CheckedChanged += (_, _) => { settings.WaterWaves = waterWaves.Checked; ApplyAndSave(); };
        var appearance = new ToolStripMenuItem("外观主题");
        foreach (var (value, label) in new[] { (WidgetTheme.Light, "浅色"), (WidgetTheme.Dark, "深色") })
        {
            var item = new ToolStripMenuItem(label) { Checked = !settings.AutoTheme && settings.Theme == value };
            item.Click += (_, _) => { settings.Theme = value; settings.AutoTheme = false; ApplyAndSave(); BuildMenu(); };
            appearance.DropDownItems.Add(item);
        }
        var autoTheme = new ToolStripMenuItem("按时间自动切换") { Checked = settings.AutoTheme };
        autoTheme.Click += (_, _) =>
        {
            if (settings.AutoTheme) settings.Theme = ThemeSchedule.IsDark(settings, DateTime.Now) ? WidgetTheme.Dark : WidgetTheme.Light;
            settings.AutoTheme = !settings.AutoTheme; ApplyAndSave(); BuildMenu();
        };
        appearance.DropDownItems.Add(autoTheme);
        appearance.DropDownItems.Add("设置切换时间…", null, (_, _) => SetThemeTimes());
        var alignment = new ToolStripMenuItem("主额度位置");
        foreach (var (right, label) in new[] { (false, "靠左"), (true, "靠右") })
        {
            var item = new ToolStripMenuItem(label) { Checked = settings.MainQuotaRightAligned == right };
            item.Click += (_, _) => { settings.MainQuotaRightAligned = right; ApplyAndSave(); BuildMenu(); };
            alignment.DropDownItems.Add(item);
        }
        var startup = new ToolStripMenuItem("开机启动") { Checked = settings.StartWithWindows, CheckOnClick = true };
        startup.CheckedChanged += (_, _) => { try { StartupManager.Set(startup.Checked); settings.StartWithWindows = startup.Checked; ApplyAndSave(); } catch (Exception ex) { MessageBox.Show(ex.Message, "开机启动设置失败"); } };
        var demoItem = new ToolStripMenuItem("演示模式") { Checked = demo, CheckOnClick = true };
        demoItem.CheckedChanged += async (_, _) => { demo = demoItem.Checked; await RefreshAsync(); };
        var manualExpiry = new ToolStripMenuItem("设置完全重置到期时间…");
        manualExpiry.Click += (_, _) => SetManualExpiry();
        var displayMode = new ToolStripMenuItem("额度显示模式");
        foreach (var (mode, label) in new[] { (QuotaDisplayMode.Auto, "自动（有 5 小时额度则优先）"), (QuotaDisplayMode.Plus, "Plus：5 小时优先"), (QuotaDisplayMode.Pro, "Pro：每周优先") })
        {
            var item = new ToolStripMenuItem(label) { Checked = settings.DisplayMode == mode };
            item.Click += (_, _) => { settings.DisplayMode = mode; notifiedCycle = null; ApplyAndSave(); UpdateDisplay(); BuildMenu(); };
            displayMode.DropDownItems.Add(item);
        }
        var opacity = new ToolStripMenuItem("不透明度");
        foreach (var value in new[] { 100, 92, 80, 70, 60 })
        {
            var item = new ToolStripMenuItem($"{value}%") { Checked = settings.OpacityPercent == value };
            item.Click += (_, _) => { settings.OpacityPercent = value; ApplyAndSave(); BuildMenu(); };
            opacity.DropDownItems.Add(item);
        }
        var threshold = new ToolStripMenuItem("当前主额度低额度提醒");
        foreach (var value in new[] { 10, 20, 30 })
        {
            var item = new ToolStripMenuItem($"低于 {value}%") { Checked = settings.LowQuotaThreshold == value };
            item.Click += (_, _) => { settings.LowQuotaThreshold = value; notifiedCycle = null; ApplyAndSave(); BuildMenu(); };
            threshold.DropDownItems.Add(item);
        }
        var interval = new ToolStripMenuItem("刷新频率");
        foreach (var pair in new[] { (15, "15 秒"), (60, "1 分钟"), (300, "5 分钟") })
        {
            var item = new ToolStripMenuItem(pair.Item2) { Checked = settings.RefreshSeconds == pair.Item1 };
            item.Click += (_, _) => { settings.RefreshSeconds = pair.Item1; refreshTimer.Interval = pair.Item1 * 1000; ApplyAndSave(); BuildMenu(); };
            interval.DropDownItems.Add(item);
        }
        menu.Items.AddRange([ballMode]);
        menu.Items.Add("悬浮球大小…", null, (_, _) => SetWidgetSize(false));
        menu.Items.Add("展开面板大小…", null, (_, _) => SetWidgetSize(true));
        menu.Items.AddRange([alignment, appearance, waterWaves, animations, top, taskbar, compact, startup, displayMode, opacity, threshold, interval, manualExpiry, demoItem, new ToolStripSeparator()]);
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        var previous = tray.ContextMenuStrip;
        tray.ContextMenuStrip = menu;
        form.ContextMenuStrip = menu;
        if (previous is not null) form.BeginInvoke(previous.Dispose);
    }

    private void SetManualExpiry()
    {
        using var dialog = new Form { Text = "完全重置到期时间", Width = 365, Height = 160, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var picker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm", Width = 300, Location = new Point(22, 20), Value = settings.ManualResetExpiry?.LocalDateTime ?? DateTime.Now.AddDays(30) };
        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(166, 65), Width = 75 };
        var clear = new Button { Text = "清除", DialogResult = DialogResult.Ignore, Location = new Point(247, 65), Width = 75 };
        dialog.Controls.AddRange([picker, ok, clear]); dialog.AcceptButton = ok;
        var result = dialog.ShowDialog();
        if (result == DialogResult.OK) settings.ManualResetExpiry = new DateTimeOffset(picker.Value);
        else if (result == DialogResult.Ignore) settings.ManualResetExpiry = null;
        else return;
        ApplyAndSave(); form.UpdateSnapshot(snapshot, settings);
    }

    private void ApplyAndSave() { form.ApplySettings(settings); SettingsStore.Save(settings); }
    private void SetWidgetSize(bool panel)
    {
        var originalSize = panel ? settings.PanelScalePercent : settings.BallSize;
        var originalMode = settings.BallMode;
        var originalLocation = form.Location;
        var originalVisible = form.Visible;
        using var dialog = new Form { Text = panel ? "展开面板大小" : "悬浮球大小", ClientSize = new Size(350, 175), StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var label = new Label { Location = new Point(22, 15), Width = 305, Height = 25 };
        var slider = new TrackBar { Minimum = panel ? 75 : 48, Maximum = panel ? 150 : 160, TickFrequency = panel ? 25 : 16, SmallChange = 2, LargeChange = 8,
            Value = panel ? Math.Clamp(settings.PanelScalePercent, 75, 150) : Math.Clamp(settings.BallSize, 48, 160), Location = new Point(18, 45), Width = 310 };
        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(171, 123), Width = 75 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(252, 123), Width = 75 };
        settings.BallMode = !panel;
        form.ApplySettings(settings); form.Show();
        void Preview()
        {
            if (panel) settings.PanelScalePercent = slider.Value;
            else settings.BallSize = slider.Value;
            form.ApplySettings(settings);
            label.Text = panel ? $"缩放：{slider.Value}% · {form.Width} × {form.Height}" : $"大小：{slider.Value} 像素（48–160）";
        }
        slider.ValueChanged += (_, _) => Preview();
        Preview();
        dialog.Controls.AddRange([label, slider, ok, cancel]); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        var result = dialog.ShowDialog();
        if (result != DialogResult.OK)
        {
            if (panel) settings.PanelScalePercent = originalSize;
            else settings.BallSize = originalSize;
        }
        settings.BallMode = originalMode;
        form.ApplySettings(settings);
        if (result != DialogResult.OK) form.Location = originalLocation;
        if (!originalVisible) form.Hide();
        SettingsStore.Save(settings);
    }
    private void SetThemeTimes()
    {
        using var dialog = new Form { Text = "主题切换时间", ClientSize = new Size(330, 170), StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var light = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
            Location = new Point(160, 20), Width = 135, Value = DateTime.Today.AddMinutes(Math.Clamp(settings.LightStartMinute, 0, 1439)) };
        var dark = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
            Location = new Point(160, 62), Width = 135, Value = DateTime.Today.AddMinutes(Math.Clamp(settings.DarkStartMinute, 0, 1439)) };
        var ok = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(220, 118), Width = 75 };
        dialog.Controls.AddRange([new Label { Text = "浅色开始", Location = new Point(24, 24), AutoSize = true },
            new Label { Text = "深色开始", Location = new Point(24, 66), AutoSize = true }, light, dark, ok]);
        dialog.AcceptButton = ok;
        if (dialog.ShowDialog() != DialogResult.OK) return;
        var lightMinute = light.Value.Hour * 60 + light.Value.Minute;
        var darkMinute = dark.Value.Hour * 60 + dark.Value.Minute;
        if (lightMinute == darkMinute) { MessageBox.Show("浅色和深色的开始时间不能相同。", "切换时间"); return; }
        settings.LightStartMinute = lightMinute;
        settings.DarkStartMinute = darkMinute;
        ApplyAndSave();
    }
    private void UpdateDisplay()
    {
        form.UpdateSnapshot(snapshot, settings);
        var (primary, secondary, primaryName, secondaryName) = snapshot.Display(settings.DisplayMode);
        UpdateIcon(primary?.RemainingPercent);
        tray.Text = primary is null ? "Codex 额度暂不可用" : $"Codex {primaryName} {primary.RemainingPercent:0}%" + (snapshot.HideFiveHourRow(settings.DisplayMode) ? "" : $" · {secondaryName} {(secondary is null ? "--" : $"{secondary.RemainingPercent:0}%")}");
        if (snapshot.Error is not null && primary is not null) tray.Text += "（待刷新）";
    }
    private void HideWidget() { SettingsStore.Save(settings); form.Hide(); }
    private void ShowWidget() { form.Show(); form.WindowState = FormWindowState.Normal; form.Activate(); }

    private void UpdateIcon(double? pct)
    {
        var rounded = pct.HasValue ? (int?)Math.Round(pct.Value) : null;
        if (currentIcon is not null && rounded == iconPercent) return;
        iconPercent = rounded;
        var old = currentIcon; currentIcon = IconRenderer.Create(pct); tray.Icon = currentIcon; form.Icon = currentIcon; old?.Dispose();
    }

    private static string FormatReset(DateTimeOffset? dt) => dt is null ? "重置时间未知" : $"{dt.Value.LocalDateTime:MM/dd HH:mm} 重置";
    private static QuotaSnapshot DemoSnapshot() => new(
        new("weekly/10080", 45, DateTimeOffset.Now.AddDays(5).AddHours(8)),
        new("five-hour/300", 72, DateTimeOffset.Now.AddHours(3).AddMinutes(18)),
        1,
        [DateTimeOffset.Now.AddDays(29)],
        DateTimeOffset.Now);

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true; lifetime.Cancel(); wakeWait?.Unregister(null);
        settings.X = form.Left; settings.Y = form.Top; SettingsStore.Save(settings);
        tray.Visible = false; tray.Dispose(); currentIcon?.Dispose(); refreshTimer.Dispose(); countdownTimer.Dispose(); form.Dispose(); lifetime.Dispose();
        base.ExitThreadCore();
    }
}
