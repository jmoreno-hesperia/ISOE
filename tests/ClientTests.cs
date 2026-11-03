using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ISOE;

class ClientTests {
    [STAThread] static int Main(string[] args) {
        var catalog = new JavaScriptSerializer().Deserialize<Catalog>(File.ReadAllText("catalog.json"));
        foreach (var app in catalog.apps) if (app.available) MainForm.Validate(app);
        var valid = new AppEntry { id = "Test.App", architecture = "x64", sha256 = new string('A',64), type = "msi", arguments = "/qn", url = "https://example.com/app" };
        MainForm.Validate(valid);
        foreach (var arch in new[] { "x86", "neutral", "arm64" }) {
            valid.architecture = arch;
            try { MainForm.Validate(valid); throw new Exception("Accepted " + arch); } catch (InvalidDataException) { }
        }
        valid.architecture = "x64"; valid.url = "http://example.com/app";
        try { MainForm.Validate(valid); throw new Exception("Accepted HTTP"); } catch (InvalidDataException) { }
        valid.url = "https://example.com/app"; valid.sha256 = "BAD";
        try { MainForm.Validate(valid); throw new Exception("Accepted invalid hash"); } catch (InvalidDataException) { }
        Application.EnableVisualStyles();
        using (var form = new MainForm(true)) {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainForm).GetField("apps", flags).SetValue(form, catalog.apps);
            typeof(MainForm).GetMethod("RenderApps", flags).Invoke(form, null);
            typeof(MainForm).GetMethod("SetBusy", flags).Invoke(form, new object[] { false });
            var status = (Label)typeof(MainForm).GetField("status", flags).GetValue(form);
            status.Text = "Catálogo actualizado · " + catalog.apps.Length + " programas";
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save("dist/preview.png"); }
            var list = (ListView)typeof(MainForm).GetField("list", flags).GetValue(form);
            if (list.Items.Count != catalog.apps.Length) throw new Exception("Catalog UI missing rows");
        }
        Console.WriteLine("Client validation and UI rendering passed. No installers executed.");
        return 0;
    }
}
