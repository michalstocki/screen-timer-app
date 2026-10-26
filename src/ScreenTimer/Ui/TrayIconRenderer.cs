using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ScreenTimer.Native;

namespace ScreenTimer.Ui;

/// <summary>Draws the remaining time into the tray icon, since a tray icon cannot carry a text label.</summary>
internal static class TrayIconRenderer
{
    public static readonly Color Normal = Color.FromArgb(0x2D, 0x6C, 0xDF);
    public static readonly Color Soon = Color.FromArgb(0xE8, 0x89, 0x0C);
    public static readonly Color Urgent = Color.FromArgb(0xD3, 0x2F, 0x2F);
    public static readonly Color Break = Color.FromArgb(0x2E, 0x8B, 0x57);
    public static readonly Color Idle = Color.FromArgb(0x6B, 0x72, 0x80);

    public static Icon Render(string text, Color background)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using (var path = RoundedRect(new RectangleF(0, 0, size, size), 7))
            using (var fill = new SolidBrush(background))
                g.FillPath(fill, path);

            var fontSize = text.Length switch { <= 2 => 19f, 3 => 15f, _ => 12f };
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 1, size, size), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
