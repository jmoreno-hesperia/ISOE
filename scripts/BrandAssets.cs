using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ISOE;

class BrandAssets {
    static int Main(string[] args) {
        string folder = args[0]; Directory.CreateDirectory(folder);
        using (var bitmap = new Bitmap(760, 100)) {
            using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); Brand.DrawHeader(g, new Rectangle(0, 0, 760, 100)); }
            bitmap.Save(Path.Combine(folder, "logo.png"), ImageFormat.Png);
        }
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) {
            using (var bitmap = new Bitmap(sizes[i], sizes[i])) using (var stream = new MemoryStream()) {
                using (var g = Graphics.FromImage(bitmap)) { g.Clear(Color.Transparent); Brand.DrawMark(g, new RectangleF(0, 0, sizes[i], sizes[i])); }
                bitmap.Save(stream, ImageFormat.Png); images[i] = stream.ToArray();
            }
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(folder, "ISOE.ico")))) {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++) {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (var bytes in images) writer.Write(bytes);
        }
        return 0;
    }
}
