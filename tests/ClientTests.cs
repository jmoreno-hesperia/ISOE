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
    static void CheckLayout(Form form, ListView list) {
        Application.DoEvents();
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        var buttons = Descendants(form).OfType<Button>().ToArray();
        if (buttons.Length != 4) throw new Exception("Missing queue buttons");
        foreach (var button in buttons) {
            Rectangle bounds = button.RectangleToScreen(button.ClientRectangle);
            if (!client.Contains(bounds) || button.Height < 20 || button.Width < 100) throw new Exception("Button clipped: " + button.Text);
        }
        if (list.Columns.Cast<ColumnHeader>().Sum(c => c.Width) > list.ClientSize.Width) throw new Exception("Horizontal catalog overflow");
        if (form.Width > Screen.FromControl(form).WorkingArea.Width || form.Height > Screen.FromControl(form).WorkingArea.Height) throw new Exception("Window larger than screen");
    }
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent) {
        foreach (Control child in parent.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    static void Snapshot(Form form, string path) {
        Application.DoEvents();
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path); }
    }
    static void CleanupTests() {
        string workspace = Path.GetFullPath(Path.Combine("dist", "cleanup-test-" + Guid.NewGuid().ToString("N")));
        string root = Path.Combine(workspace, "downloads"), folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Office365", "Office", "Data"));
        string preservedLog = Path.Combine(workspace, "ISOE.log"), installed = Path.Combine(workspace, "installed-tool.exe");
        File.WriteAllText(preservedLog, "Log to retain"); File.WriteAllText(installed, "Installed tool to retain");
        File.WriteAllText(Path.Combine(folder, "app.exe"), "Download");
        File.WriteAllText(Path.Combine(folder, "Office365", "Office", "Data", "payload.cab"), "Office data");
        string sibling = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sibling); File.WriteAllText(Path.Combine(sibling, "keep.txt"), "Other run");
        foreach (var invalid in new[] { root, workspace, Path.Combine(root, "not-a-run"), Path.Combine(workspace, Guid.NewGuid().ToString("N")) }) {
            try { MainForm.CleanRunDirectory(root, invalid); throw new Exception("Unsafe cleanup target accepted"); } catch (IOException) { }
        }
        MainForm.CleanRunDirectory(root, folder);
        if (Directory.Exists(folder) || !File.Exists(preservedLog) || !File.Exists(installed) || !File.Exists(Path.Combine(sibling, "keep.txt"))) throw new Exception("Cleanup did not preserve logs/other runs/installed tools");
        string locked = Path.Combine(sibling, "locked.exe"); File.WriteAllText(locked, "Busy download");
        using (var handle = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) {
            try { MainForm.CleanRunDirectory(root, sibling); throw new Exception("Locked download should be reported"); } catch (IOException) { }
            if (!File.Exists(preservedLog)) throw new Exception("Log lost after cleanup failure");
        }
        MainForm.CleanRunDirectory(root, sibling);
        if (Directory.Exists(root) || !File.Exists(preservedLog) || !File.Exists(installed)) throw new Exception("Final cleanup failed");
        string junctionRoot = Path.GetFullPath("dist/cleanup-junction-fixture/downloads");
        string junctionRun = Path.Combine(junctionRoot, "0123456789abcdef0123456789abcdef");
        if (Directory.Exists(junctionRun)) {
            try { MainForm.CleanRunDirectory(junctionRoot, junctionRun); throw new Exception("Junction cleanup accepted"); } catch (IOException) { }
            if (!File.Exists("dist/cleanup-junction-fixture/outside/keep.txt")) throw new Exception("Junction target changed");
        }
    }
    [STAThread] static int Main(string[] args) {
        CleanupTests();
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
            var list = (ListView)typeof(MainForm).GetField("list", flags).GetValue(form);
            if (form.Text != "ISOE - Software para tu empresa" || !Descendants(form).OfType<BrandHeader>().Any()) throw new Exception("Branding missing");
            CheckLayout(form, list); Snapshot(form, "dist/preview.png");
            form.Size = new Size(560, 450); CheckLayout(form, list); Snapshot(form, "dist/preview-small.png");
            form.Scale(new SizeF(1.5f, 1.5f)); form.FitToWorkingArea(); CheckLayout(form, list); Snapshot(form, "dist/preview-150.png");
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
