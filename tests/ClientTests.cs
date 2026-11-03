using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using ISOE;

class ClientTests {
    [STAThread] static int Main(string[] args) {
        var catalog = new JavaScriptSerializer().Deserialize<Catalog>(File.ReadAllText("catalog-v2.json"));
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
        valid.sha256 = new string('A', 64); valid.architecture = "x86"; valid.allowX86 = true;
        try { MainForm.Validate(valid); throw new Exception("Accepted arbitrary x86 exception"); } catch (InvalidDataException) { }
        valid.id = "AnyDesk.AnyDesk"; valid.type = "exe"; valid.url = "https://download.anydesk.com/AnyDesk.exe";
        MainForm.Validate(valid); valid.allowX86 = false;
        try { MainForm.Validate(valid); throw new Exception("Accepted unapproved AnyDesk x86"); } catch (InvalidDataException) { }
        foreach (string product in new[] { "O365BusinessRetail", "O365ProPlusRetail" }) {
            var xml = new XmlDocument(); xml.LoadXml(MainForm.BuildOfficeConfiguration(@"C:\Temp\ISOE\A & B", product));
            if (xml.SelectSingleNode("/Configuration/Add/@OfficeClientEdition").Value != "64" || xml.SelectSingleNode("/Configuration/Display/@Level").Value != "None") throw new Exception("Office must install x64 silently");
            if (xml.SelectSingleNode("/Configuration/Remove") != null || xml.SelectSingleNode("/Configuration/RemoveMSI") != null || xml.SelectSingleNode("/Configuration/Add/@MigrateArch") != null) throw new Exception("Office config must not remove existing installations");
            if (xml.SelectSingleNode("/Configuration/Add/@SourcePath").Value != @"C:\Temp\ISOE\A & B") throw new Exception("Office path not escaped");
        }
        try { MainForm.BuildOfficeConfiguration("C:\\Temp", "Injected<Product>"); throw new Exception("Office product injection accepted"); } catch (InvalidDataException) { }
        string portableFolder = Path.GetFullPath(Path.Combine("dist", "portable-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(portableFolder);
        string portableSource = Path.Combine(portableFolder, "fixture.txt"); File.WriteAllText(portableSource, "Non executable test fixture");
        string portableTarget = Path.Combine(portableFolder, "tools", "QuickSupport.exe");
        string portableLink = Path.Combine(portableFolder, "QuickSupport.lnk");
        MainForm.DeployPortable(portableSource, portableTarget, portableLink);
        if (!File.Exists(portableLink) || File.ReadAllText(portableTarget) != "Non executable test fixture") throw new Exception("Portable deployment failed");
        if (args.Contains("--vendor-test")) {
            InstallerSecurity.VerifyVendor(Path.GetFullPath(".venv/AnyDesk.exe"), "AnyDesk.AnyDesk");
            InstallerSecurity.VerifyVendor(Path.GetFullPath(".venv/TeamViewerQS_x64.exe"), "TeamViewer.TeamViewer.QuickSupport");
            InstallerSecurity.VerifyVendor(Path.GetFullPath(".venv/odt.exe"), "Microsoft.Office");
            Console.WriteLine("Vendor signatures verified without running any installers.");
        }
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
            var available = list.Items.Cast<ListViewItem>().First(i => ((AppEntry)i.Tag).available);
            string selectedId = ((AppEntry)available.Tag).id;
            available.Checked = true;
            typeof(MainForm).GetMethod("SetBusy", flags).Invoke(form, new object[] { true });
            typeof(MainForm).GetMethod("RenderApps", flags).Invoke(form, null);
            if (!list.Items.Cast<ListViewItem>().First(i => ((AppEntry)i.Tag).id == selectedId).Checked) throw new Exception("Selection lost on refresh");
            typeof(MainForm).GetMethod("SetBusy", flags).Invoke(form, new object[] { false });
            foreach (ListViewItem item in list.Items) if (!((AppEntry)item.Tag).available) {
                item.Checked = true;
                if (item.Checked) throw new Exception("Unavailable app selected");
            }
            if (args.Contains("--download-test")) {
                var app = catalog.apps.First(a => a.id == "7zip.7zip" && a.available);
                string file = Path.Combine("dist", "verified-7zip-" + Guid.NewGuid().ToString("N") + ".exe");
                Exception error = null;
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                var worker = new Thread(delegate() {
                    try { typeof(MainForm).GetMethod("Download", flags).Invoke(form, new object[] { app.url, file }); }
                    catch (Exception ex) { error = ex; }
                });
                worker.Start();
                while (worker.IsAlive) { Application.DoEvents(); Thread.Sleep(10); }
                if (error != null) throw error;
                using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create()) {
                    string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                    if (!String.Equals(hash, app.sha256, StringComparison.OrdinalIgnoreCase)) throw new Exception("Downloaded hash mismatch");
                }
                Console.WriteLine("Real x64 download and SHA-256 verified. Installer was NOT executed.");
            }
        }
        Console.WriteLine("Client validation and UI rendering passed. No installers executed.");
        return 0;
    }
}
