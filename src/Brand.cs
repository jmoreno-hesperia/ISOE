using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Windows.Forms;

namespace ISOE {
    public static class Brand {
        public static readonly Color Ink = Color.FromArgb(5, 28, 44);
        public static readonly Color Blue = Color.FromArgb(85, 132, 210);
        static readonly Image Wordmark = LoadOriginal("ISOE.brand.wordmark");
        static readonly Image Favicon = LoadOriginal("ISOE.brand.favicon");
        static Image LoadOriginal(string name) {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var original = Image.FromStream(stream)) return new Bitmap(original);
        }
        public static void DrawMark(Graphics graphics, RectangleF bounds) {
            var saved = graphics.Save();
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(Favicon, bounds);
            graphics.Restore(saved);
        }
        public static void DrawHeader(Graphics graphics, Rectangle bounds) {
            var saved = graphics.Save();
            float scale = bounds.Height / 80f;
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(scale, scale);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var background = new SolidBrush(Ink)) graphics.FillRectangle(background, 0, 0, bounds.Width / scale, 80);
            graphics.DrawImage(Wordmark, new RectangleF(12, 14, 182, 52));
            using (var white = new SolidBrush(Color.White)) {
                const string title = "ISOE - Software para tu empresa";
                float available = bounds.Width / scale - 220;
                float size = 20;
                using (var measure = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                    size = Math.Min(size, size * available / graphics.MeasureString(title, measure).Width);
                using (var font = new Font("Segoe UI", Math.Max(13, size), FontStyle.Bold, GraphicsUnit.Pixel))
                    graphics.DrawString(title, font, white, 208, (80 - font.GetHeight(graphics)) / 2);
            }
            using (var accent = new SolidBrush(Blue)) graphics.FillRectangle(accent, 0, 78, bounds.Width / scale, 2);
            graphics.Restore(saved);
        }
    }
    public sealed class BrandHeader : Control {
        public BrandHeader() { DoubleBuffered = true; AccessibleName = "Hesperia World · ISOE - Software para tu empresa"; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); Brand.DrawHeader(e.Graphics, ClientRectangle); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }
}
