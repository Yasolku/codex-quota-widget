using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexQuotaWidget;

internal static class IconRenderer
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Create(double? percent, int size = 64)
    {
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        var value = percent.HasValue ? Math.Clamp((int)Math.Round(percent.Value), 0, 100) : -1;
        var color = value < 0 ? Color.FromArgb(130, 140, 155) : value <= 10 ? Color.FromArgb(244, 86, 92) : value <= 30 ? Color.FromArgb(245, 181, 71) : Color.FromArgb(63, 205, 143);
        using var bg = new SolidBrush(Color.FromArgb(28, 30, 38));
        using var border = new Pen(color, Math.Max(3, size / 16f));
        g.FillEllipse(bg, 2, 2, size - 4, size - 4);
        g.DrawEllipse(border, 4, 4, size - 8, size - 8);
        var text = value < 0 ? "--" : value.ToString();
        var fontSize = value >= 100 ? size * .27f : size * .34f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, new RectangleF(0, 0, size, size), format);
        var h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }
}
