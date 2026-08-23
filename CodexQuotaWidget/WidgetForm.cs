using System.Drawing.Drawing2D;

namespace CodexQuotaWidget;

internal sealed class WidgetForm : Form
{
    private readonly Label title = NewLabel("CODEX 限额", 11, FontStyle.Bold, Color.FromArgb(220, 225, 235));
    private readonly Label percent = NewLabel("--%", 20, FontStyle.Bold, Color.White);
    private readonly Label weekly = NewLabel("每周限额读取中…", 10, FontStyle.Regular, Color.FromArgb(188, 194, 207));
    private readonly Label reset = NewLabel("", 9, FontStyle.Regular, Color.FromArgb(145, 153, 170));
    private readonly Label credit = NewLabel("完全重置：读取中…", 9, FontStyle.Regular, Color.FromArgb(185, 191, 204));
    private readonly Label status = NewLabel("正在连接 Codex", 8, FontStyle.Regular, Color.FromArgb(112, 122, 143));
    private readonly Panel progress = new() { BackColor = Color.FromArgb(57, 61, 73), Height = 7 };
    private readonly Panel progressValue = new() { BackColor = Color.FromArgb(63, 205, 143), Height = 7 };
    private readonly Button minimize = NewButton("—");
    private readonly Button close = NewButton("×");
    private Point dragOrigin;
    private bool dragging;

    public event Action? RefreshRequested;
    public event Action? HideRequested;

    public WidgetForm()
    {
        Text = "Codex 限额";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(28, 30, 38);
        ClientSize = new Size(320, 205);
        MinimumSize = MaximumSize = Size;
        ShowInTaskbar = true;
        DoubleBuffered = true;
        Padding = new Padding(20);
        StartPosition = FormStartPosition.Manual;

        Controls.AddRange([title, percent, weekly, reset, credit, status, progress, minimize, close]);
        title.SetBounds(20, 18, 180, 24);
        minimize.SetBounds(250, 11, 28, 28);
        close.SetBounds(282, 11, 28, 28);
        percent.SetBounds(20, 51, 108, 45);
        weekly.SetBounds(130, 56, 170, 22);
        reset.SetBounds(130, 80, 170, 20);
        progress.SetBounds(20, 108, 280, 7);
        progressValue.SetBounds(0, 0, 0, 7);
        progress.Controls.Add(progressValue);
        credit.SetBounds(20, 130, 280, 25);
        status.SetBounds(20, 169, 280, 18);

        minimize.Click += (_, _) => HideRequested?.Invoke();
        close.Click += (_, _) => HideRequested?.Invoke();
        foreach (Control c in Controls) { c.MouseDown += DragDown; c.MouseMove += DragMove; c.MouseUp += DragUp; }
        DoubleClick += (_, _) => RefreshRequested?.Invoke();
        Resize += (_, _) => Invalidate();
    }

    public void UpdateSnapshot(QuotaSnapshot snapshot, AppSettings settings)
    {
        var pct = snapshot.Weekly?.RemainingPercent;
        percent.Text = pct.HasValue ? $"{pct:0}%" : "--%";
        percent.ForeColor = QuotaColor(pct);
        weekly.Text = snapshot.Weekly is null ? "每周使用限额" : $"每周剩余 {snapshot.Weekly.RemainingPercent:0}%";
        reset.Text = snapshot.Weekly?.ResetsAt is { } dt ? $"重置：{dt.LocalDateTime:MM/dd HH:mm}" : "重置时间不可用";
        progressValue.Width = pct.HasValue ? (int)(progress.Width * pct.Value / 100) : 0;
        progressValue.BackColor = QuotaColor(pct);
        var expiries = snapshot.ResetCreditExpiries.ToList();
        if (settings.ManualResetExpiry is { } manual && manual > DateTimeOffset.Now) expiries.Add(manual);
        var expiry = expiries.Where(x => x > DateTimeOffset.Now).Order().FirstOrDefault();
        credit.Text = expiry != default
            ? $"完全重置：{Math.Max(snapshot.ResetCredits, 1)} 次 · {expiry.LocalDateTime:MM/dd HH:mm} 到期"
            : snapshot.ResetCredits > 0 ? $"完全重置：{snapshot.ResetCredits} 次" : "完全重置：暂无或接口未提供";
        status.Text = snapshot.Error ?? $"已更新 {snapshot.UpdatedAt.LocalDateTime:HH:mm:ss} · 双击刷新";
        status.ForeColor = snapshot.Error is null ? Color.FromArgb(112, 122, 143) : Color.FromArgb(245, 181, 71);
    }

    public void ApplySettings(AppSettings settings)
    {
        TopMost = settings.AlwaysOnTop;
        ShowInTaskbar = settings.ShowInTaskbar;
        Opacity = Math.Clamp(settings.OpacityPercent / 100d, .5, 1);
        var h = settings.Compact ? 155 : 205;
        ClientSize = new Size(320, h);
        MinimumSize = MaximumSize = Size;
        credit.Visible = status.Visible = !settings.Compact;
    }

    public void Place(AppSettings settings)
    {
        if (settings.X != int.MinValue && settings.Y != int.MinValue)
        {
            var point = new Point(settings.X, settings.Y);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(point, Size)))) { Location = point; return; }
        }
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(62, 66, 79));
        e.Graphics.DrawRoundedRectangle(pen, new Rectangle(0, 0, Width - 1, Height - 1), 14);
        base.OnPaint(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideRequested?.Invoke(); return; }
        base.OnFormClosing(e);
    }

    private void DragDown(object? sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragOrigin = Cursor.Position; } }
    private void DragMove(object? sender, MouseEventArgs e) { if (!dragging) return; var now = Cursor.Position; Location = new Point(Left + now.X - dragOrigin.X, Top + now.Y - dragOrigin.Y); dragOrigin = now; }
    private void DragUp(object? sender, MouseEventArgs e) { dragging = false; }
    private static Color QuotaColor(double? p) => !p.HasValue ? Color.FromArgb(130, 140, 155) : p <= 10 ? Color.FromArgb(244, 86, 92) : p <= 30 ? Color.FromArgb(245, 181, 71) : Color.FromArgb(63, 205, 143);
    private static Label NewLabel(string text, float size, FontStyle style, Color color) => new() { Text = text, Font = new Font("Segoe UI", size, style), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true };
    private static Button NewButton(string text) => new() { Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(170, 177, 192), BackColor = Color.Transparent, Font = new Font("Segoe UI", 10), TabStop = false, Cursor = Cursors.Hand };
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); graphics.DrawPath(pen, path);
    }
}
