using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CodexQuotaWidget;

internal sealed class WidgetForm : Form
{
    private Color Ink => ThemeColor(Color.FromArgb(36, 48, 65), Color.FromArgb(233, 242, 249));
    private Color Muted => ThemeColor(Color.FromArgb(96, 111, 131), Color.FromArgb(147, 166, 184));
    private bool dark, themeInitialized;
    private double themeMix;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 40 };
    private readonly System.Diagnostics.Stopwatch frameClock = new();
    private readonly Dictionary<(string Family, float Size, FontStyle Style), Font> fonts = new();
    private readonly StringFormat textLeft = CreateTextFormat(StringAlignment.Near);
    private readonly StringFormat textRight = CreateTextFormat(StringAlignment.Far);
    private readonly StringFormat textCenter = CreateTextFormat(StringAlignment.Center);
    private readonly GraphicsPath ballShape = new();
    private readonly GraphicsPath waterPath = new();
    private readonly PointF[] waterPoints = new PointF[48];
    private readonly Pen liquidEdge = new(Color.FromArgb(211, 223, 240));
    private readonly SolidBrush ballInk = new(Color.FromArgb(26, 73, 142));
    private LinearGradientBrush? glassBrush, waterBrushBack, waterBrushFront;
    private Color glassTop, glassBottom, cachedWaterColor;
    private string ballText = "--";
    private string? tooltipText;
    private readonly ToolTip tooltip = new() { AutoPopDelay = 20000, InitialDelay = 300 };
    private QuotaSnapshot snapshot = QuotaSnapshot.Unavailable("正在读取…");
    private AppSettings settings = new();
    private Point dragStart, windowStart;
    private Point ballAnchor;
    private bool restoreBallAnchor;
    private bool pressed, moved;
    private double phase, displayedPercent, targetPercent;
    private bool hasPercent, initializedLevel;
    private bool ball = true;
    private bool titleHovered;
    private float PanelScale => Math.Clamp(settings.PanelScalePercent, 75, 150) / 100f;
    private int PanelBaseHeight => (settings.Compact ? 240 : 306) - (snapshot.HideFiveHourRow(settings.DisplayMode) ? 56 : 0);
    private Point PanelPoint(Point point) => new((int)(point.X / PanelScale), (int)(point.Y / PanelScale));

    public event Action? RefreshRequested;
    public event Action? HideRequested;
    public event Action<bool>? BallModeChanged;

    public WidgetForm()
    {
        Text = "Codex 限额";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(16, 23, 34);
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        ballShape.AddEllipse(1.5f, 1.5f, 85, 85);
        animation.Tick += (_, _) =>
        {
            var elapsed = Math.Clamp(frameClock.Elapsed.TotalSeconds, .001, .1);
            frameClock.Restart();
            AdvanceAnimation(elapsed);
        };
        VisibleChanged += (_, _) => UpdateAnimation();
        SetBallMode(true, notify: false);
    }

    public void UpdateSnapshot(QuotaSnapshot value, AppSettings options)
    {
        var previousHide = snapshot.HideFiveHourRow(settings.DisplayMode);
        snapshot = value;
        settings = options;
        if (!ball && previousHide != snapshot.HideFiveHourRow(settings.DisplayMode)) SetBallMode(false, notify: false);
        RefreshTheme();
        var primary = snapshot.Display(settings.DisplayMode).Primary;
        hasPercent = primary is not null;
        targetPercent = primary?.RemainingPercent ?? 0;
        ballText = primary is null ? "--" : $"{primary.RemainingPercent:0}";
        if (!initializedLevel || !settings.Animations)
        {
            displayedPercent = targetPercent;
            initializedLevel = hasPercent;
        }
        var tip = snapshot.Error is not null
            ? $"{snapshot.Error}\n最近数据：{snapshot.UpdatedAt.LocalDateTime:MM/dd HH:mm:ss}"
            : ball ? "单击展开 · 拖动移动 · 右键设置" : "点击 CODEX 或右上角减号收起 · 右键设置";
        if (tip != tooltipText) { tooltipText = tip; tooltip.SetToolTip(this, tip); }
        UpdateAnimation();
        Invalidate();
    }

    public void ApplySettings(AppSettings options)
    {
        settings = options;
        RefreshTheme();
        TopMost = settings.AlwaysOnTop;
        ShowInTaskbar = settings.ShowInTaskbar;
        Opacity = Math.Clamp(settings.OpacityPercent / 100d, .6, 1);
        SetBallMode(settings.BallMode, notify: false);
        UpdateAnimation();
        Invalidate();
    }

    public void SetBallMode(bool value, bool notify = true)
    {
        if (ball && !value && IsHandleCreated) { ballAnchor = Location; restoreBallAnchor = true; }
        var restore = !ball && value && restoreBallAnchor;
        ball = value;
        MinimumSize = Size.Empty;
        MaximumSize = Size.Empty;
        var ballSize = Math.Clamp(settings.BallSize, 48, 160);
        ClientSize = ball ? new Size(ballSize, ballSize) : new Size((int)Math.Round(340 * PanelScale), (int)Math.Round(PanelBaseHeight * PanelScale));
        MinimumSize = MaximumSize = Size;
        var oldRegion = Region;
        using var path = new GraphicsPath();
        if (ball) path.AddEllipse(1, 1, Width - 2, Height - 2);
        else AddRoundedRectangle(path, new RectangleF(0, 0, Width, Height), 22 * PanelScale);
        Region = new Region(path);
        oldRegion?.Dispose();
        if (restore) { Location = ballAnchor; restoreBallAnchor = false; }
        if (IsHandleCreated) KeepOnScreen();
        UpdateAnimation();
        Invalidate();
        if (notify) BallModeChanged?.Invoke(value);
    }

    public void Place(AppSettings options)
    {
        if (options.X != int.MinValue && options.Y != int.MinValue)
            Location = new Point(options.X, options.Y);
        else
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
            Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);
        }
        KeepOnScreen();
    }

    private void KeepOnScreen()
    {
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    public void RefreshTheme()
    {
        var next = ThemeSchedule.IsDark(settings, DateTime.Now);
        var changed = !themeInitialized || dark != next;
        dark = next;
        if (!themeInitialized || !settings.Animations || !Visible) themeMix = dark ? 1 : 0;
        themeInitialized = true;
        UpdateAnimation();
        if (changed) Invalidate();
    }

    private void UpdateAnimation()
    {
        var waveMoving = ball && hasPercent && settings.WaterWaves && targetPercent > .5 && targetPercent < 99.5;
        var easing = settings.Animations && (Math.Abs(themeMix - (dark ? 1 : 0)) > .001 || (ball && Math.Abs(displayedPercent - targetPercent) > .01));
        var run = Visible && WindowState != FormWindowState.Minimized && (waveMoving || easing);
        var interval = easing ? 25 : 40;
        if (animation.Interval != interval) animation.Interval = interval;
        if (run && !animation.Enabled) frameClock.Restart();
        animation.Enabled = run;
        if (!settings.Animations) { displayedPercent = targetPercent; themeMix = dark ? 1 : 0; }
    }

    private void AdvanceAnimation(double seconds)
    {
        if (settings.WaterWaves) phase = (phase + seconds * 1.4) % (Math.PI * 2);
        var themeTarget = dark ? 1d : 0d;
        themeMix = Math.Clamp(themeMix + Math.Sign(themeTarget - themeMix) * seconds / .5, 0, 1);
        if (Math.Abs(themeTarget - themeMix) < .001) themeMix = themeTarget;
        displayedPercent += (targetPercent - displayedPercent) * (1 - Math.Exp(-seconds / .2));
        if (Math.Abs(targetPercent - displayedPercent) < .01) displayedPercent = targetPercent;
        Invalidate();
        UpdateAnimation();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        if (ball) PaintBall(g);
        else
        {
            var state = g.Save();
            g.ScaleTransform(PanelScale, PanelScale);
            PaintPanel(g);
            g.Restore(state);
        }
        base.OnPaint(e);
    }

    private void PaintBall(Graphics g)
    {
        var state = g.Save();
        g.ScaleTransform(Width / 88f, Height / 88f);
        DrawLiquid(g, new RectangleF(1.5f, 1.5f, 85, 85));
        var pct = snapshot.Display(settings.DisplayMode).Primary?.RemainingPercent;
        var font = GetFont("Segoe UI Variable Display", pct >= 99.5 ? 36 : 43, FontStyle.Bold);
        var color = ThemeColor(Color.FromArgb(26, 73, 142), Color.FromArgb(191, 216, 255));
        if (ballInk.Color != color) ballInk.Color = color;
        g.DrawString(ballText, font, ballInk, new RectangleF(2, 17, 84, 54), textCenter);
        if (snapshot.Error is not null)
        {
            using var warning = new SolidBrush(Color.FromArgb(217, 142, 32));
            g.FillEllipse(warning, 65, 13, 5, 5);
        }
        g.Restore(state);
    }

    private void PaintPanel(Graphics g)
    {
        using var background = new LinearGradientBrush(new Rectangle(0, 0, 340, PanelBaseHeight), ThemeColor(Color.FromArgb(250, 252, 255), Color.FromArgb(27, 36, 49)), ThemeColor(Color.FromArgb(240, 245, 252), Color.FromArgb(17, 24, 34)), 90f);
        g.FillRectangle(background, new Rectangle(0, 0, 340, PanelBaseHeight));
        using var border = new Pen(ThemeColor(Color.FromArgb(210, 221, 235), Color.FromArgb(58, 70, 86)));
        using var outline = new GraphicsPath();
        AddRoundedRectangle(outline, new RectangleF(.5f, .5f, 339, PanelBaseHeight - 1), 22);
        g.DrawPath(border, outline);
        DrawText(g, "CODEX", 11, FontStyle.Bold, titleHovered ? (ThemeColor(Color.FromArgb(26, 115, 232), Color.FromArgb(145, 190, 255))) : Ink, new RectangleF(20, 14, 120, 28));
        DrawAction(g, new RectangleF(252, 14, 30, 30), "↻");
        DrawAction(g, new RectangleF(294, 14, 30, 30), "−");

        var (primary, secondary, primaryName, secondaryName) = snapshot.Display(settings.DisplayMode);
        DrawText(g, $"{primaryName}剩余额度", 11, FontStyle.Regular, Muted, new RectangleF(20, 55, 300, 24), right: settings.MainQuotaRightAligned);
        var rightAligned = settings.MainQuotaRightAligned;
        DrawText(g, primary is null ? "--%" : $"{primary.RemainingPercent:0}%", primary?.RemainingPercent >= 99.5 ? 30 : 36, FontStyle.Bold, Ink,
            new RectangleF(rightAligned ? 150 : 15, 87, 175, 70), right: rightAligned);
        var resetX = rightAligned ? 20 : 197;
        DrawText(g, "下次重置", 9, FontStyle.Regular, Muted, new RectangleF(resetX, 82, 123, 20), right: !rightAligned);
        if (primary?.ResetsAt is { } dt)
        {
            DrawText(g, $"{dt.LocalDateTime:MM/dd}", 17, FontStyle.Regular, Muted, new RectangleF(resetX, 103, 123, 31), right: !rightAligned);
            DrawText(g, $"{dt.LocalDateTime:HH:mm}", 23, FontStyle.Regular, Muted, new RectangleF(resetX, 130, 123, 35), right: !rightAligned);
        }
        else DrawText(g, "暂不可用", 11, FontStyle.Regular, Muted, new RectangleF(resetX, 119, 123, 28), right: !rightAligned);
        DrawCard(g, new RectangleF(20, 172, 300, 4));
        if (primary is not null && primary.RemainingPercent > 0)
        {
            using var progress = new SolidBrush(QuotaColor(primary.RemainingPercent));
            g.FillRectangle(progress, 20, 172, (float)(300 * primary.RemainingPercent / 100), 4);
        }

        var hideFiveHour = snapshot.HideFiveHourRow(settings.DisplayMode);
        if (!hideFiveHour)
        {
            DrawCard(g, new RectangleF(16, 186, 308, 48));
            DrawText(g, $"{secondaryName}额度", 11, FontStyle.Regular, Muted, new RectangleF(28, 190, 150, 23));
            DrawText(g, secondary is null ? "未提供" : $"{secondary.RemainingPercent:0}%",
                12, FontStyle.Bold, secondary is null ? Muted : Ink, new RectangleF(180, 190, 131, 23), right: true);
            if (secondary?.ResetsAt is { } otherReset)
                DrawText(g, $"{otherReset.LocalDateTime:MM/dd HH:mm} 重置", 10, FontStyle.Regular, Muted, new RectangleF(28, 212, 283, 20));
        }

        if (settings.Compact) return;
        var expiries = snapshot.ResetCreditExpiries.Where(x => x > DateTimeOffset.Now).ToList();
        if (settings.ManualResetExpiry is { } manual && manual > DateTimeOffset.Now) expiries.Add(manual);
        var expiry = expiries.Order().FirstOrDefault();
        var offset = hideFiveHour ? 56 : 0;
        DrawCard(g, new RectangleF(16, 242 - offset, 308, 32));
        DrawText(g, "完全重置", 10, FontStyle.Regular, Muted, new RectangleF(28, 247 - offset, 91, 22));
        var creditText = expiry != default ? $"{Math.Max(snapshot.ResetCredits, 1)} 次 · {expiry.LocalDateTime:MM/dd} 到期"
            : snapshot.ResetCredits > 0 ? $"{snapshot.ResetCredits} 次" : "未提供";
        DrawText(g, creditText, 10, FontStyle.Bold, Ink, new RectangleF(120, 247 - offset, 191, 22), right: true);
        using var dot = new SolidBrush(snapshot.Error is null ? Color.FromArgb(100, 219, 181) : Color.FromArgb(246, 189, 98));
        g.FillEllipse(dot, 22, 288 - offset, 4, 4);
        DrawText(g, snapshot.Error is null ? $"已同步 {snapshot.UpdatedAt.LocalDateTime:HH:mm}" : "连接暂时失败 · 悬停查看详情",
            9, FontStyle.Regular, Muted, new RectangleF(34, 280 - offset, 285, 20));
    }

    private void DrawLiquid(Graphics g, RectangleF bounds)
    {
        var top = ThemeColor(Color.FromArgb(250, 252, 255), Color.FromArgb(28, 39, 55));
        var bottom = ThemeColor(Color.FromArgb(243, 247, 253), Color.FromArgb(18, 27, 41));
        if (glassBrush is null || glassTop != top || glassBottom != bottom)
        {
            glassBrush?.Dispose();
            glassBrush = new LinearGradientBrush(bounds, top, bottom, 90f);
            glassTop = top; glassBottom = bottom;
        }
        var state = g.Save();
        g.SetClip(ballShape);
        g.FillPath(glassBrush, ballShape);
        if (hasPercent && displayedPercent > 0)
        {
            var color = ThemeColor(
                targetPercent <= 10 ? Color.FromArgb(250, 210, 211) : targetPercent <= 30 ? Color.FromArgb(255, 235, 188) : Color.FromArgb(190, 218, 255),
                targetPercent <= 10 ? Color.FromArgb(119, 61, 74) : targetPercent <= 30 ? Color.FromArgb(121, 95, 53) : Color.FromArgb(52, 91, 143));
            if (waterBrushBack is null || cachedWaterColor != color)
            {
                waterBrushBack?.Dispose(); waterBrushFront?.Dispose();
                waterBrushBack = new LinearGradientBrush(bounds, Color.FromArgb(90, color), Color.FromArgb(245, color), 90f);
                waterBrushFront = new LinearGradientBrush(bounds, Color.FromArgb(220, color), Color.FromArgb(245, color), 90f);
                cachedWaterColor = color;
            }
            var level = bounds.Bottom - (float)(displayedPercent / 100 * bounds.Height);
            var amplitude = !settings.WaterWaves || displayedPercent >= 99.5 || displayedPercent <= .5 ? 0 : 1.8f;
            for (var layer = 0; layer < 2; layer++)
            {
                for (var i = 0; i < 46; i++)
                {
                    var x = bounds.Left - 2 + i * 2;
                    waterPoints[i] = new PointF(x, level + amplitude * (float)Math.Sin((x - bounds.Left) / bounds.Width * Math.PI * 2 + phase + layer * 1.8));
                }
                waterPoints[46] = new PointF(bounds.Right + 3, bounds.Bottom + 3);
                waterPoints[47] = new PointF(bounds.Left - 2, bounds.Bottom + 3);
                waterPath.Reset(); waterPath.AddPolygon(waterPoints);
                g.FillPath(layer == 0 ? waterBrushBack : waterBrushFront!, waterPath);
            }
        }
        g.Restore(state);
        var edge = ThemeColor(Color.FromArgb(211, 223, 240), Color.FromArgb(56, 75, 100));
        if (liquidEdge.Color != edge) liquidEdge.Color = edge;
        g.DrawPath(liquidEdge, ballShape);
    }

    private void DrawText(Graphics g, string text, float size, FontStyle style, Color color, RectangleF bounds, bool centered = false, bool right = false)
    {
        // Pixel fonts keep this owner-drawn layout consistent at different display DPI.
        var font = GetFont("Microsoft YaHei UI", size * 1.65f, style);
        using var brush = new SolidBrush(color);
        var format = centered ? textCenter : right ? textRight : textLeft;
        g.DrawString(text, font, brush, bounds, format);
    }

    private Font GetFont(string family, float size, FontStyle style)
    {
        var key = (family, size, style);
        if (!fonts.TryGetValue(key, out var font))
        {
            font = new Font(family, size, style, GraphicsUnit.Pixel);
            fonts.Add(key, font);
        }
        return font;
    }

    private static StringFormat CreateTextFormat(StringAlignment alignment) => new()
    {
        Alignment = alignment, LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap
    };

    private void DrawCard(Graphics g, RectangleF bounds)
    {
        using var path = new GraphicsPath();
        AddRoundedRectangle(path, bounds, Math.Min(12, bounds.Height / 2));
        using var fill = new SolidBrush(ThemeColor(Color.FromArgb(225, 233, 245), Color.FromArgb(35, 49, 65)));
        g.FillPath(fill, path);
    }

    private void DrawAction(Graphics g, RectangleF bounds, string label)
    {
        DrawCard(g, bounds);
        DrawText(g, label, 12, FontStyle.Regular, Muted, bounds, centered: true);
    }

    private static void AddRoundedRectangle(GraphicsPath path, RectangleF b, float radius)
    {
        var d = radius * 2;
        path.AddArc(b.Left, b.Top, d, d, 180, 90);
        path.AddArc(b.Right - d, b.Top, d, d, 270, 90);
        path.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
        path.AddArc(b.Left, b.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        pressed = true; moved = false; dragStart = Cursor.Position; windowStart = Location; Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hovered = !ball && new Rectangle(16, 12, 120, 32).Contains(PanelPoint(e.Location));
        if (hovered != titleHovered) { titleHovered = hovered; Invalidate(); }
        Cursor = ball || hovered || (!ball && new Rectangle(252, 14, 72, 30).Contains(PanelPoint(e.Location))) ? Cursors.Hand : Cursors.Default;
        if (!pressed) return;
        var delta = new Size(Cursor.Position.X - dragStart.X, Cursor.Position.Y - dragStart.Y);
        if (Math.Abs(delta.Width) + Math.Abs(delta.Height) >= 5) moved = true;
        if (moved) { restoreBallAnchor = false; Location = windowStart + delta; }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !pressed) return;
        pressed = false; Capture = false;
        if (moved) { KeepOnScreen(); return; }
        if (ball) SetBallMode(false);
        else if (new Rectangle(16, 12, 120, 32).Contains(PanelPoint(e.Location)) || new Rectangle(294, 14, 30, 30).Contains(PanelPoint(e.Location))) SetBallMode(true);
        else if (new Rectangle(252, 14, 30, 30).Contains(PanelPoint(e.Location))) RefreshRequested?.Invoke();
    }

    protected override void OnMouseLeave(EventArgs e) { titleHovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; base.OnMouseCaptureChanged(e); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData is Keys.Enter or Keys.Space) { SetBallMode(!ball); return true; }
        if (keyData == Keys.Escape) { SetBallMode(true); return true; }
        if (keyData == Keys.R) { RefreshRequested?.Invoke(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideRequested?.Invoke(); return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animation.Dispose(); tooltip.Dispose();
            foreach (var font in fonts.Values) font.Dispose();
            fonts.Clear();
            textLeft.Dispose(); textRight.Dispose(); textCenter.Dispose();
            ballShape.Dispose(); waterPath.Dispose(); liquidEdge.Dispose(); ballInk.Dispose();
            glassBrush?.Dispose(); waterBrushBack?.Dispose(); waterBrushFront?.Dispose();
        }
        base.Dispose(disposing);
    }

    private Color ThemeColor(Color light, Color night)
    {
        var t = themeMix * themeMix * (3 - 2 * themeMix);
        return Color.FromArgb((int)Math.Round(light.R + (night.R - light.R) * t),
            (int)Math.Round(light.G + (night.G - light.G) * t),
            (int)Math.Round(light.B + (night.B - light.B) * t));
    }

    private static Color QuotaColor(double p) => p <= 10 ? Color.FromArgb(244, 111, 122) : p <= 30 ? Color.FromArgb(240, 190, 105) : Color.FromArgb(86, 220, 190);
}
