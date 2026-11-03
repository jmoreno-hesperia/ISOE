using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ISOE {
    public static class Brand {
        public static readonly Color Ink = Color.FromArgb(18, 52, 60);
        public static readonly Color Gold = Color.FromArgb(195, 157, 91);
        public static void DrawMark(Graphics graphics, RectangleF bounds) {
            var saved = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 64f, bounds.Height / 64f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(Ink)) using (var accent = new SolidBrush(Gold)) using (var white = new SolidBrush(Color.White))
            using (var shape = new GraphicsPath()) {
                shape.AddArc(0, 0, 24, 24, 180, 90); shape.AddArc(40, 0, 24, 24, 270, 90);
                shape.AddArc(40, 40, 24, 24, 0, 90); shape.AddArc(0, 40, 24, 24, 90, 90); shape.CloseFigure();
                graphics.FillPath(background, shape);
                graphics.FillRectangle(white, 17, 16, 8, 32); graphics.FillRectangle(white, 39, 16, 8, 32);
                graphics.FillRectangle(accent, 25, 28, 14, 8);
                graphics.FillRectangle(accent, 43, 8, 4, 4); graphics.FillRectangle(accent, 51, 8, 4, 4);
            }
            graphics.Restore(saved);
        }
        public static void DrawHeader(Graphics graphics, Rectangle bounds) {
            var saved = graphics.Save();
            float scale = bounds.Height / 80f;
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(scale, scale);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            DrawMark(graphics, new RectangleF(4, 12, 54, 54));
            using (var ink = new SolidBrush(Ink)) using (var gold = new SolidBrush(Gold))
            using (var world = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel)) {
                graphics.DrawString("Hesperia World", world, gold, 74, 14);
                const string title = "ISOE - Software para tu empresa";
                float available = bounds.Width / scale - 80;
                float size = 21;
                using (var measure = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                    size = Math.Min(size, size * available / graphics.MeasureString(title, measure).Width);
                using (var font = new Font("Segoe UI", Math.Max(13, size), FontStyle.Bold, GraphicsUnit.Pixel))
                    graphics.DrawString(title, font, ink, 72, 35);
            }
            graphics.Restore(saved);
        }
    }
    public sealed class BrandHeader : Control {
        public BrandHeader() { DoubleBuffered = true; AccessibleName = "Hesperia World · ISOE - Software para tu empresa"; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); Brand.DrawHeader(e.Graphics, ClientRectangle); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }
}
