namespace CodexQuotaWidget;

internal sealed class WidgetApplicationContext : ApplicationContext
{
    private readonly AppSettings settings = SettingsStore.Load();
    private readonly WidgetForm form = new();
    private readonly NotifyIcon tray = new();
    private readonly CodexAppServerClient client = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly System.Windows.Forms.Timer countdownTimer = new() { Interval = 30_000 };
    private readonly System.Windows.Forms.Timer wakeTimer = new() { Interval = 300 };
    private readonly EventWaitHandle wakeEvent;
    private bool refreshing;
    private bool demo;
    private string? notifiedCycle;
    private QuotaSnapshot snapshot = QuotaSnapshot.Unavailable("正在读取…");
    private Icon? currentIcon;

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
        tray.Visible = true;
        tray.Text = "Codex 限额读取中";
        tray.DoubleClick += (_, _) => ShowWidget();
        BuildMenu();
        UpdateIcon(null);
        form.Show();
        refreshTimer.Interval = Math.Clamp(settings.RefreshSeconds, 15, 3600) * 1000;
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
        countdownTimer.Tick += (_, _) => form.UpdateSnapshot(snapshot, settings);
        wakeTimer.Tick += (_, _) => { if (wakeEvent.WaitOne(0)) ShowWidget(); };
        refreshTimer.Start(); countdownTimer.Start(); wakeTimer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            snapshot = demo ? DemoSnapshot() : await client.ReadAsync(CancellationToken.None);
            form.UpdateSnapshot(snapshot, settings);
            UpdateIcon(snapshot.Weekly?.RemainingPercent);
            var pct = snapshot.Weekly?.RemainingPercent;
            tray.Text = pct.HasValue ? $"Codex 每周剩余 {pct:0}% · {FormatReset(snapshot.Weekly?.ResetsAt)}" : "Codex 限额暂不可用";
            if (pct.HasValue && pct <= settings.LowQuotaThreshold)
            {
                var cycle = snapshot.Weekly?.ResetsAt?.ToUnixTimeSeconds().ToString() ?? "unknown";
                if (notifiedCycle != cycle)
                {
                    tray.BalloonTipTitle = "Codex 每周额度偏低";
                    tray.BalloonTipText = $"当前剩余 {pct:0}%，{FormatReset(snapshot.Weekly?.ResetsAt)}。";
                    tray.BalloonTipIcon = ToolTipIcon.Warning;
                    tray.ShowBalloonTip(6000);
                    notifiedCycle = cycle;
                }
            }
        }
        catch (Exception ex)
        {
            snapshot = QuotaSnapshot.Unavailable($"刷新失败：{ex.Message}");
            form.UpdateSnapshot(snapshot, settings);
            UpdateIcon(null);
        }
        finally { refreshing = false; }
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示悬浮窗", null, (_, _) => ShowWidget());
        menu.Items.Add("立即刷新", null, async (_, _) => await RefreshAsync());
        menu.Items.Add(new ToolStripSeparator());
        var top = new ToolStripMenuItem("始终置顶") { Checked = settings.AlwaysOnTop, CheckOnClick = true };
        top.CheckedChanged += (_, _) => { settings.AlwaysOnTop = top.Checked; ApplyAndSave(); };
        var taskbar = new ToolStripMenuItem("任务栏显示百分比") { Checked = settings.ShowInTaskbar, CheckOnClick = true };
        taskbar.CheckedChanged += (_, _) => { settings.ShowInTaskbar = taskbar.Checked; ApplyAndSave(); };
        var compact = new ToolStripMenuItem("紧凑模式") { Checked = settings.Compact, CheckOnClick = true };
        compact.CheckedChanged += (_, _) => { settings.Compact = compact.Checked; ApplyAndSave(); };
        var startup = new ToolStripMenuItem("开机启动") { Checked = settings.StartWithWindows, CheckOnClick = true };
        startup.CheckedChanged += (_, _) => { try { StartupManager.Set(startup.Checked); settings.StartWithWindows = startup.Checked; ApplyAndSave(); } catch (Exception ex) { MessageBox.Show(ex.Message, "开机启动设置失败"); } };
        var demoItem = new ToolStripMenuItem("演示模式") { Checked = demo, CheckOnClick = true };
        demoItem.CheckedChanged += async (_, _) => { demo = demoItem.Checked; await RefreshAsync(); };
        var manualExpiry = new ToolStripMenuItem("设置完全重置到期时间…");
        manualExpiry.Click += (_, _) => SetManualExpiry();
        var opacity = new ToolStripMenuItem("不透明度");
        foreach (var value in new[] { 100, 92, 80, 70, 60 })
        {
            var item = new ToolStripMenuItem($"{value}%") { Checked = settings.OpacityPercent == value };
            item.Click += (_, _) => { settings.OpacityPercent = value; ApplyAndSave(); BuildMenu(); };
            opacity.DropDownItems.Add(item);
        }
        var threshold = new ToolStripMenuItem("低额度提醒");
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
        menu.Items.AddRange([top, taskbar, compact, startup, opacity, threshold, interval, manualExpiry, demoItem, new ToolStripSeparator()]);
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        var previous = tray.ContextMenuStrip;
        tray.ContextMenuStrip = menu;
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
    private void HideWidget() { SettingsStore.Save(settings); form.Hide(); }
    private void ShowWidget() { form.Show(); form.WindowState = FormWindowState.Normal; form.Activate(); }

    private void UpdateIcon(double? pct)
    {
        var old = currentIcon; currentIcon = IconRenderer.Create(pct); tray.Icon = currentIcon; form.Icon = currentIcon; old?.Dispose();
    }

    private static string FormatReset(DateTimeOffset? dt) => dt is null ? "重置时间未知" : $"{dt.Value.LocalDateTime:MM/dd HH:mm} 重置";
    private static QuotaSnapshot DemoSnapshot() => new(new("weekly/10080", 45, DateTimeOffset.Now.AddDays(5).AddHours(8)), null, 1, [DateTimeOffset.Now.AddDays(29)], DateTimeOffset.Now);

    protected override void ExitThreadCore()
    {
        settings.X = form.Left; settings.Y = form.Top; SettingsStore.Save(settings);
        tray.Visible = false; tray.Dispose(); currentIcon?.Dispose(); refreshTimer.Dispose(); countdownTimer.Dispose(); wakeTimer.Dispose(); form.Dispose();
        base.ExitThreadCore();
    }
}
