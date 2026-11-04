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
    static void UpdateTests() {
        var app = new AppEntry { id = "7zip.7zip", version = "26.05" };
        if (UpdatePolicy.Decide(app, null) != AppAction.Install) throw new Exception("Missing product must install");
        if (UpdatePolicy.Decide(app, new InstalledProduct { Version = "19.00" }) != AppAction.Update) throw new Exception("Older product must update");
        if (UpdatePolicy.Decide(app, new InstalledProduct { Version = "26.5.0" }) != AppAction.SkipCurrent) throw new Exception("Equivalent version must not reinstall");
        if (UpdatePolicy.Decide(app, new InstalledProduct { Version = "27.0" }) != AppAction.SkipCurrent) throw new Exception("Newer version must not downgrade");
        if (UpdatePolicy.Decide(app, new InstalledProduct { Version = "unknown" }) != AppAction.SkipUnknownVersion) throw new Exception("Unknown version must not trigger blind reinstall");
        int comparison;
        if (!UpdatePolicy.TryCompare("Example.Tool", "1.10", "1.9", out comparison) || comparison <= 0) throw new Exception("Versions sorted lexically instead of numerically");
        if (!UpdatePolicy.TryCompare("Example.Tool", "1.0.0-rc.1", "1.0.0", out comparison) || comparison >= 0) throw new Exception("Prerelease ordering failed");
        if (!UpdatePolicy.TryCompare("Example.Tool", "1.0.0", "1.0.0-beta.2", out comparison) || comparison <= 0) throw new Exception("Stable version would downgrade to beta");
        if (!UpdatePolicy.TryCompare("Oracle.JavaRuntimeEnvironment", "1.8.0_511-b32", "8.0.5110.32", out comparison) || comparison != 0) throw new Exception("Equivalent Java versions must not reinstall");
        if (!UpdatePolicy.TryCompare("Oracle.JavaRuntimeEnvironment", "8.0.4710.9", "8.0.5110.32", out comparison) || comparison >= 0) throw new Exception("Older Java update not recognized");
        var inventory = new InstalledInventory();
        inventory.Products.Add(new InstalledProduct { Name = "7-Zip 19.00 (x86)", Publisher = "Igor Pavlov", Version = "19.00" });
        inventory.Products.Add(new InstalledProduct { Name = "7-Zip 27.00 (x64)", Publisher = "Igor Pavlov", Version = "27.00" });
        if (InstalledSoftware.Match(app, inventory).Version != "27.00") throw new Exception("A newer installed copy was ignored");
        var portable = new AppEntry { id = "TeamViewer.TeamViewer.QuickSupport", sha256 = new string('A', 64) };
        if (UpdatePolicy.Decide(portable, new InstalledProduct { FileHash = new string('a', 64) }) != AppAction.SkipCurrent) throw new Exception("Identical QuickSupport must be skipped");
        if (UpdatePolicy.Decide(portable, new InstalledProduct { FileHash = new string('B', 64) }) != AppAction.Update) throw new Exception("Changed QuickSupport must update");
        if (UpdatePolicy.Decide(new AppEntry { id = "Microsoft.Office", version = "ODT 999.0" }, new InstalledProduct { Version = "16.0.1" }) != AppAction.OfficeUpdate) throw new Exception("ODT version must not be compared with Office version");
        if (!UpdatePolicy.OfficeUpdateArguments.Contains("forceappshutdown=false") || UpdatePolicy.OfficeUpdateArguments.Contains("updatetoversion") || UpdatePolicy.OfficeUpdateArguments.Contains("/changesetting")) throw new Exception("Office update must preserve user's applications and channel");
    }
    static void DetectionTests() {
        Func<string, string, string, bool> matches = delegate(string id, string name, string publisher) {
            var inventory = new InstalledInventory();
            inventory.Products.Add(new InstalledProduct { Name = name, Publisher = publisher, Version = "old", Code = "unrelated" });
            return InstalledSoftware.Match(new AppEntry { id = id, name = id }, inventory) != null;
        };
        if (!matches("7zip.7zip", "7-Zip 19.00 (x86)", "Igor Pavlov")) throw new Exception("Old 32-bit 7-Zip not recognized");
        if (matches("7zip.7zip", "7-Zip 19.00 Help", "Igor Pavlov")) throw new Exception("7-Zip helper confused with full product");
        if (matches("7zip.7zip", "7-Zip 19.00 (x86)", "Unrelated Publisher")) throw new Exception("Wrong publisher matched");
        if (!matches("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader DC", "Adobe Systems Incorporated")) throw new Exception("Reader not recognized");
        if (matches("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Update Service", "Adobe Inc.") || matches("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader Updater", "Adobe Inc.")) throw new Exception("Adobe updater confused with Reader");
        if (!matches("Oracle.JavaRuntimeEnvironment", "Java 8 Update 45 (64-bit)", "Oracle Corporation")) throw new Exception("Old Java runtime not recognized");
        if (!matches("Oracle.JavaRuntimeEnvironment", "Java(TM) 6 Update 45", "Sun Microsystems, Inc.")) throw new Exception("Legacy Java runtime not recognized");
        if (matches("Oracle.JavaRuntimeEnvironment", "Java Auto Updater", "Oracle Corporation") || matches("Oracle.JavaRuntimeEnvironment", "Java SE Development Kit 21", "Oracle Corporation")) throw new Exception("Updater/JDK confused with JRE");
        if (!matches("AnyDesk.AnyDesk", "AnyDesk", "AnyDesk Software GmbH")) throw new Exception("AnyDesk not recognized");
        if (matches("TeamViewer.TeamViewer.QuickSupport", "TeamViewer", "TeamViewer Germany GmbH")) throw new Exception("Full TeamViewer confused with QuickSupport");
        var office = new AppEntry { id = "Microsoft.Office", name = "Office" };
        var officeInventory = new InstalledInventory(); officeInventory.OfficeProducts.Add("VisioProRetail");
        if (InstalledSoftware.Match(office, officeInventory) != null) throw new Exception("Visio confused with Microsoft 365");
        officeInventory.OfficeProducts.Add("O365ProPlusRetail");
        if (InstalledSoftware.Match(office, officeInventory) == null) throw new Exception("Existing Microsoft 365 not recognized");
        var portableInventory = new InstalledInventory { QuickSupportPrepared = true };
        if (InstalledSoftware.Match(new AppEntry { id = "TeamViewer.TeamViewer.QuickSupport" }, portableInventory) == null) throw new Exception("Prepared QuickSupport not recognized");
        var generic = new AppEntry { id = "Example.Tool", name = "Example Tool", publisher = "Example" };
        var genericInventory = new InstalledInventory();
        genericInventory.Products.Add(new InstalledProduct { Code = "{11111111-1111-1111-1111-111111111111}", Name = "Example Tool Helper", Publisher = "Example Ltd" });
        if (InstalledSoftware.Match(generic, genericInventory) != null) throw new Exception("Helper confused with product");
        generic.productCodes = new[] { "{11111111-1111-1111-1111-111111111111}" };
        if (InstalledSoftware.Match(generic, genericInventory) == null) throw new Exception("Product code match failed");
        generic.productCodes = new string[0]; genericInventory.Products[0].Name = "Example Tool 1.2 (x86)";
        if (InstalledSoftware.Match(generic, genericInventory) == null) throw new Exception("Generic old version match failed");
        genericInventory.Products.Clear(); generic.upgradeCodes = new[] { "{22222222-2222-2222-2222-222222222222}" };
        if (InstalledSoftware.Match(generic, genericInventory) != null) throw new Exception("Missing MSI confused with installed");
        genericInventory.UpgradeCodes.Add(generic.upgradeCodes[0]);
        if (InstalledSoftware.Match(generic, genericInventory) == null) throw new Exception("Related installed MSI not recognized");
    }
    static void CheckLayout(Form form, ListView list) {
        Application.DoEvents();
        Rectangle client = form.RectangleToScreen(form.ClientRectangle);
        var buttons = Descendants(form).OfType<Button>().ToArray();
        if (buttons.Length != 5) throw new Exception("Missing action or exit button");
        if (buttons.Select(b => b.PointToScreen(Point.Empty).Y).Distinct().Count() != 1) throw new Exception("Buttons must be in a single row");
        foreach (var button in buttons) {
            Rectangle bounds = button.RectangleToScreen(button.ClientRectangle);
            Size text = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (!client.Contains(bounds) || button.Height < text.Height + 8 || button.Width < text.Width + button.Padding.Horizontal + 4 || button.TextAlign != ContentAlignment.MiddleCenter) throw new Exception("Button text clipped or misaligned: " + button.Text);
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
        UpdateTests();
        DetectionTests();
        CleanupTests();
        var catalog = new JavaScriptSerializer().Deserialize<Catalog>(File.ReadAllText("catalog-v2.json"));
        foreach (var app in catalog.apps) if (app.available) MainForm.Validate(app);
        if (args.Contains("--inventory-test")) {
            foreach (var app in catalog.apps) {
                var existing = InstalledSoftware.FindInstalled(app);
                Console.WriteLine(app.id + ": " + (existing == null ? "not detected" : "already installed"));
            }
        }
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
        using (var skipForm = new MainForm(true, delegate(AppEntry app) { return app.id == "7zip.7zip" ? new InstalledProduct { Name = "7-Zip", Version = "19.00" } : null; })) {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var registered = catalog.apps.First(a => a.id == "7zip.7zip");
            typeof(MainForm).GetField("apps", flags).SetValue(skipForm, new[] { registered });
            string skipLog = Path.GetFullPath(Path.Combine("dist", "skip-installed-" + Guid.NewGuid().ToString("N") + ".log"));
            typeof(MainForm).GetField("logFile", flags).SetValue(skipForm, skipLog);
            typeof(MainForm).GetMethod("RenderApps", flags).Invoke(skipForm, null);
            skipForm.Show(); Application.DoEvents();
            var existing = new InstalledProduct { Name = "7-Zip", Version = "19.00" };
            var skipMethod = typeof(MainForm).GetMethod("SkipIfCurrent", flags);
            if ((bool)skipMethod.Invoke(skipForm, new object[] { registered, existing, UpdatePolicy.Decide(registered, existing) })) throw new Exception("Older app incorrectly skipped instead of updating");
            existing.Version = registered.version;
            bool skipped = (bool)skipMethod.Invoke(skipForm, new object[] { registered, existing, UpdatePolicy.Decide(registered, existing) });
            var skipList = (ListView)typeof(MainForm).GetField("list", flags).GetValue(skipForm);
            if (!skipped || !skipList.Items[0].SubItems[3].Text.Contains("Omitido") || !File.ReadAllText(skipLog).Contains(registered.version)) throw new Exception("Current product was not skipped and logged");
            if ((bool)skipMethod.Invoke(skipForm, new object[] { registered, null, AppAction.Install })) throw new Exception("Missing app was skipped");
        }
        bool javaNeedsUpdate = false;
        Func<AppEntry, InstalledProduct> previewDetector = delegate(AppEntry app) {
            if (app.id == "7zip.7zip") return new InstalledProduct { Name = "7-Zip", Version = "19.00" };
            if (app.id == "Oracle.JavaRuntimeEnvironment") return new InstalledProduct { Name = "Java", Version = javaNeedsUpdate ? "1.0" : app.version };
            if (app.id == "Microsoft.Office") return new InstalledProduct { Name = "Microsoft 365", Version = "16.0.19231.20194" };
            if (app.id == "TeamViewer.TeamViewer.QuickSupport") return new InstalledProduct { Name = "QuickSupport", FileHash = app.sha256 };
            return null;
        };
        using (var form = new MainForm(true, previewDetector)) {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Action apply = delegate {
                var results = typeof(MainForm).GetMethod("InspectApplications", flags).Invoke(form, new object[] { catalog.apps });
                typeof(MainForm).GetMethod("ApplyCatalog", flags).Invoke(form, new object[] { catalog.apps, results, catalog.updatedAt });
            };
            apply();
            typeof(MainForm).GetMethod("SetBusy", flags).Invoke(form, new object[] { false });
            var status = (Label)typeof(MainForm).GetField("status", flags).GetValue(form);
            status.Text = "Catálogo actualizado · " + catalog.apps.Length + " programas";
            form.Show(); Application.DoEvents();
            var list = (ListView)typeof(MainForm).GetField("list", flags).GetValue(form);
            Func<string, ListViewItem> row = delegate(string id) { return list.Items.Cast<ListViewItem>().First(i => ((AppEntry)i.Tag).id == id); };
            if (list.Columns.Count != 4 || !row("Adobe.Acrobat.Reader.64-bit").Checked || !row("7zip.7zip").Checked || row("Oracle.JavaRuntimeEnvironment").Checked || row("TeamViewer.TeamViewer.QuickSupport").Checked) throw new Exception("Automatic selection must select missing/outdated apps only");
            if (row("7zip.7zip").SubItems[1].Text != "19.00" || row("7zip.7zip").SubItems[2].Text != catalog.apps.First(a => a.id == "7zip.7zip").version || row("7zip.7zip").SubItems[3].Text != "Actualizar") throw new Exception("Installed/available comparison missing");
            row("Adobe.Acrobat.Reader.64-bit").Checked = false; apply();
            if (row("Adobe.Acrobat.Reader.64-bit").Checked) throw new Exception("Refresh lost the user's unchecked selection");
            javaNeedsUpdate = true; apply();
            if (!row("Oracle.JavaRuntimeEnvironment").Checked || row("Oracle.JavaRuntimeEnvironment").SubItems[3].Text != "Actualizar") throw new Exception("Newly available update not selected");
            javaNeedsUpdate = false; apply();
            if (row("Oracle.JavaRuntimeEnvironment").Checked) throw new Exception("Current app should be deselected after refresh");
            row("Adobe.Acrobat.Reader.64-bit").Checked = true;
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
        using (var failedDetection = new MainForm(true, delegate { throw new IOException("Fixture detection failure"); })) {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var results = typeof(MainForm).GetMethod("InspectApplications", flags).Invoke(failedDetection, new object[] { catalog.apps });
            typeof(MainForm).GetMethod("ApplyCatalog", flags).Invoke(failedDetection, new object[] { catalog.apps, results, "test" });
            var list = (ListView)typeof(MainForm).GetField("list", flags).GetValue(failedDetection);
            if (list.Items.Cast<ListViewItem>().Any(i => i.Checked)) throw new Exception("Detection errors must not select apps for installation");
        }
        using (var exitForm = new MainForm(true)) {
            bool closed = false; exitForm.FormClosed += delegate { closed = true; };
            exitForm.Show(); Application.DoEvents();
            Descendants(exitForm).OfType<Button>().Single(b => b.Text == "Salir").PerformClick();
            if (!closed || !exitForm.IsDisposed) throw new Exception("Exit button did not close an idle app");
        }
        using (var closingForm = new MainForm(true)) {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string closingLog = Path.GetFullPath(Path.Combine("dist", "autoclose-" + Guid.NewGuid().ToString("N") + ".log"));
            typeof(MainForm).GetField("logFile", flags).SetValue(closingForm, closingLog);
            bool closed = false;
            closingForm.FormClosed += delegate { closed = true; };
            closingForm.Show(); Application.DoEvents();
            typeof(MainForm).GetMethod("SetBusy", flags).Invoke(closingForm, new object[] { true });
            typeof(MainForm).GetField("queueRunning", flags).SetValue(closingForm, true);
            Descendants(closingForm).OfType<Button>().Single(b => b.Text == "Salir").PerformClick();
            if (closed || closingForm.IsDisposed || !(bool)typeof(MainForm).GetField("stop", flags).GetValue(closingForm)) throw new Exception("Exit must wait for the active installer and request a safe stop");
            typeof(MainForm).GetMethod("CloseAfterQueue", flags).Invoke(closingForm, null);
            Application.DoEvents();
            if (!closed || !closingForm.IsDisposed) throw new Exception("Queue completion did not close the busy window");
            if (!File.Exists(closingLog) || !File.ReadAllText(closingLog).Contains("se cierra automáticamente")) throw new Exception("Final log was not saved before closing");
        }
        Console.WriteLine("Client validation, cleanup, UI and automatic close passed. No installers executed.");
        return 0;
    }
}
