using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Ventilo;

/// <summary>Dessine l'icône de la barre des tâches : hélice + pastille de vitesse.</summary>
internal static class IconRenderer
{
    /// <param name="size">Taille en pixels demandée par Windows (petite icône à la DPI courante).</param>
    public static IntPtr Render(int size, bool on, int speed, bool lightTaskbar, byte ar, byte ag, byte ab)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            int alpha = on ? 245 : 120;
            var fan = lightTaskbar ? Color.FromArgb(alpha, 20, 20, 20) : Color.FromArgb(alpha, 255, 255, 255);

            using (var blade = new GraphicsPath())
            {
                // Pales élargies pour rester lisibles à 16–24 px ; repère 100×100, bord à bord
                blade.AddBezier(50, 46, 38, 36, 34, 14, 50, 2);
                blade.AddBezier(50, 2, 66, 14, 62, 36, 50, 46);
                blade.CloseFigure();
                using var brush = new SolidBrush(fan);
                float scale = size / 100f;
                for (int i = 0; i < 3; i++)
                {
                    using var m = new Matrix();
                    m.Scale(scale, scale);
                    m.RotateAt(i * 120, new PointF(50, 50));
                    g.Transform = m;
                    g.FillPath(brush, blade);
                }
                g.ResetTransform();
                float hub = size * 0.26f;
                g.FillEllipse(brush, (size - hub) / 2, (size - hub) / 2, hub, hub);
            }

            if (on)
            {
                var accent = Color.FromArgb(255, ar, ag, ab);
                double lum = (0.299 * ar + 0.587 * ag + 0.114 * ab) / 255;
                var onAccent = lum > 0.55 ? Color.FromArgb(255, 0, 0, 0) : Color.White;
                float d = size * 0.56f;
                using (var b = new SolidBrush(accent))
                    g.FillEllipse(b, size - d, size - d, d, d);
                using var font = new Font("Segoe UI", d * 0.78f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var tb = new SolidBrush(onAccent);
                using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(speed.ToString(), font, tb, new RectangleF(size - d, size - d + d * 0.04f, d, d), fmt);
            }
        }
        return bmp.GetHicon();
    }
}
