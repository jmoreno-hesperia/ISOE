using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ISOE {
    public sealed class AppEntry {
        public string id { get; set; }
        public string name { get; set; }
        public string version { get; set; }
        public bool available { get; set; }
        public string reason { get; set; }
        public string architecture { get; set; }
        public string url { get; set; }
        public string sha256 { get; set; }
        public string type { get; set; }
        public string arguments { get; set; }
        public int[] successCodes { get; set; }
    }
    public sealed class Catalog {
        public int schemaVersion { get; set; }
        public string updatedAt { get; set; }
        public AppEntry[] apps { get; set; }
    }
    public sealed class HttpsClient : WebClient {
        protected override WebRequest GetWebRequest(Uri address) {
            if (address.Scheme != "https") throw new InvalidOperationException("Solo se permiten descargas HTTPS.");
            var request = base.GetWebRequest(address);
            request.Timeout = 60000;
            return request;
        }
        protected override WebResponse GetWebResponse(WebRequest request) {
            var response = base.GetWebResponse(request);
            if (response.ResponseUri.Scheme != "https") { response.Close(); throw new InvalidOperationException("Redirección sin HTTPS."); }
            return response;
        }
    }
    public sealed class MainForm : Form {
        const string Repository = "https://github.com/jmoreno-hesperia/ISOE";
        const string CatalogUrl = "https://raw.githubusercontent.com/jmoreno-hesperia/ISOE/main/catalog.json";
        readonly ListView list = new ListView();
        readonly TextBox search = new TextBox();
        readonly TextBox log = new TextBox();
        readonly Label status = new Label();
        readonly ProgressBar current = new ProgressBar();
        readonly ProgressBar total = new ProgressBar();
        readonly Button install = new Button();
        readonly Button refresh = new Button();
        readonly Button cancel = new Button();
        readonly Label count = new Label();
        readonly HashSet<string> selected = new HashSet<string>();
        AppEntry[] apps = new AppEntry[0];
        volatile bool busy, stop;
        bool rendering;
        string runFolder;
        string catalogDate;
        bool reboot;

        public MainForm() : this(false) { }
        public MainForm(bool preview) {
            Text = "ISOE · Install Software on Enterprises";
            Size = new Size(960, 760); MinimumSize = new Size(780, 650);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 251);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 9 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            var header = new Label { Text = "ISOE\nSoftware para tu empresa · Solo 64 bits", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 17, FontStyle.Bold), ForeColor = Color.FromArgb(22, 47, 78) };
            search.Dock = DockStyle.Fill;
            search.TextChanged += delegate { RenderApps(); };
            list.Dock = DockStyle.Fill; list.View = View.Details; list.CheckBoxes = true; list.FullRowSelect = true;
            list.Columns.Add("Programa", 240); list.Columns.Add("Versión", 130); list.Columns.Add("Disponibilidad / resultado", 450);
            list.ItemCheck += delegate(object sender, ItemCheckEventArgs e) {
                if (rendering) return;
                var app = (AppEntry)list.Items[e.Index].Tag;
                if (!app.available || busy) { e.NewValue = e.CurrentValue; return; }
                if (e.NewValue == CheckState.Checked) selected.Add(app.id); else selected.Remove(app.id);
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            install.Text = "Instalar selección"; install.AutoSize = true; install.Enabled = false; install.Click += delegate { StartQueue(); };
            refresh.Text = "Actualizar catálogo"; refresh.AutoSize = true; refresh.Click += delegate { LoadCatalog(); };
            cancel.Text = "Detener cola"; cancel.AutoSize = true; cancel.Enabled = false; cancel.Click += delegate { stop = true; cancel.Enabled = false; WriteLog("Se detendrá al finalizar la instalación activa; las descargas se cancelan."); };
            var add = new Button { Text = "Añadir software", AutoSize = true };
            add.Click += delegate { Process.Start(new ProcessStartInfo(Repository + "/issues/new?template=add-app.yml") { UseShellExecute = true }); };
            buttons.Controls.AddRange(new Control[] { install, refresh, cancel, add });
            status.Dock = DockStyle.Fill; status.Text = "Cargando catálogo…";
            current.Dock = DockStyle.Fill; total.Dock = DockStyle.Fill;
            log.Dock = DockStyle.Fill; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Color.White; log.Font = new Font("Consolas", 9);
            count.Dock = DockStyle.Fill; count.Text = "Buscar por nombre o ID de WinGet en el campo superior.";
            layout.Controls.Add(header); layout.Controls.Add(search); layout.Controls.Add(list); layout.Controls.Add(buttons);
            layout.Controls.Add(status); layout.Controls.Add(current); layout.Controls.Add(total); layout.Controls.Add(log); layout.Controls.Add(count);
            Controls.Add(layout);
            if (!preview) Shown += delegate { LoadCatalog(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; stop = true; WriteLog("Espera al fin de la operación activa antes de cerrar."); }
            };
        }
        void UI(Action action) { if (!IsDisposed) Invoke(action); }
        void WriteLog(string message) {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine;
            if (runFolder != null) {
                try { File.AppendAllText(Path.Combine(runFolder, "ISOE.log"), line, Encoding.UTF8); }
                catch (IOException) { line += "No se pudo escribir el registro en disco." + Environment.NewLine; }
                catch (UnauthorizedAccessException) { line += "Sin permiso para escribir el registro en disco." + Environment.NewLine; }
            }
            UI(delegate { log.AppendText(line); });
        }
        void SetBusy(bool value) {
            busy = value; install.Enabled = !value && apps.Any(a => a.available); refresh.Enabled = !value;
            search.Enabled = !value; list.Enabled = !value; cancel.Enabled = value; 
        }
        void RenderApps() {
            rendering = true; list.BeginUpdate(); list.Items.Clear();
            foreach (var app in apps.Where(a => ((a.name ?? "") + " " + a.id).IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0)) {
                var item = new ListViewItem(app.name) { Tag = app };
                item.SubItems.Add(app.version ?? "—");
                item.SubItems.Add(app.available ? "Listo · x64" : app.reason ?? "No disponible");
                if (!app.available) item.ForeColor = Color.Gray;
                list.Items.Add(item); item.Checked = selected.Contains(app.id) && app.available;
            }
            list.EndUpdate(); rendering = false;
        }
        void LoadCatalog() {
            SetBusy(true); cancel.Enabled = false;
            new Thread(delegate() {
                try {
                    string json;
                    using (var client = new HttpsClient()) { client.Encoding = Encoding.UTF8; json = client.DownloadString(CatalogUrl + "?t=" + DateTime.UtcNow.Ticks); }
                    var catalog = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Deserialize<Catalog>(json);
                    if (catalog == null || catalog.schemaVersion != 1 || catalog.apps == null) throw new InvalidDataException("Formato de catálogo no compatible.");
                    foreach (var app in catalog.apps) if (app.available) Validate(app);
                    UI(delegate { apps = catalog.apps; catalogDate = catalog.updatedAt; selected.RemoveWhere(id => !apps.Any(a => a.id == id && a.available)); RenderApps(); status.Text = "Catálogo actualizado: " + catalogDate; });
                } catch (Exception ex) {
                    WriteLog("No se pudo consultar GitHub: " + ex.Message);
                    try {
                        if (apps.Length == 0) {
                            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ISOE.catalog.json"))
                            using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                                var embedded = new JavaScriptSerializer().Deserialize<Catalog>(reader.ReadToEnd());
                                foreach (var app in embedded.apps) if (app.available) Validate(app);
                                UI(delegate { apps = embedded.apps; catalogDate = embedded.updatedAt; RenderApps(); });
                            }
                            WriteLog("Se utiliza el catálogo integrado: " + catalogDate + ". Los enlaces pueden haber cambiado; se verificará el SHA-256.");
                        }
                        UI(delegate { status.Text = "Sin actualización de GitHub. Catálogo conservado: " + catalogDate; });
                    } catch (Exception fallback) {
                        WriteLog(fallback.Message);
                        UI(delegate { status.Text = "No hay catálogo disponible. Pulsa Actualizar para reintentar."; });
                    }
                } finally { UI(delegate { SetBusy(false); cancel.Enabled = false; }); }
            }) { IsBackground = true }.Start();
        }
        public static void Validate(AppEntry app) {
            if (app.architecture != "x64" || !Regex.IsMatch(app.id ?? "", @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z") ||
                !Regex.IsMatch(app.sha256 ?? "", @"\A[0-9a-fA-F]{64}\z") ||
                !new[] { "msi", "wix", "exe", "burn", "inno", "nullsoft" }.Contains(app.type) ||
                String.IsNullOrWhiteSpace(app.arguments) || app.arguments.IndexOfAny(new[] { '\r', '\n', '<', '>' }) >= 0)
                throw new InvalidDataException("Instalador no compatible: " + app.id);
            Uri uri;
            if (!Uri.TryCreate(app.url, UriKind.Absolute, out uri) || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("URL no válida");
        }
        void StartQueue() {
            var queue = apps.Where(a => a.available && selected.Contains(a.id)).ToArray();
            if (queue.Length == 0) { status.Text = "Selecciona al menos un programa."; return; }
            if (MessageBox.Show("Se instalarán " + queue.Length + " programas en modo silencioso. Al continuar aceptas sus licencias.\n\nLos instaladores se guardarán en C:\\Temp\\ISOE. ¿Continuar?", "ISOE", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            stop = false; reboot = false; SetBusy(true); total.Maximum = queue.Length; total.Value = 0;
            new Thread(delegate() {
                int ok = 0, failed = 0, completed = 0;
                try {
                    runFolder = Path.Combine(@"C:\Temp\ISOE", Guid.NewGuid().ToString("N"));
                    var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
                    foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                        security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    foreach (string directory in new[] { @"C:\Temp", @"C:\Temp\ISOE" }) {
                        if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("La carpeta temporal no puede ser un enlace: " + directory);
                    }
                    Directory.CreateDirectory(@"C:\Temp\ISOE", security);
                    Directory.SetAccessControl(@"C:\Temp\ISOE", security);
                    Directory.CreateDirectory(runFolder, security);
                    WriteLog("Registro: " + runFolder + "\\ISOE.log");
                    foreach (var app in queue) {
                        if (stop) break;
                        try {
                            Validate(app);
                            UI(delegate { current.Value = 0; current.Style = ProgressBarStyle.Continuous; status.Text = "Descargando " + app.name + "…"; });
                            WriteLog("Descargando " + app.name + " " + app.version + " (x64)");
                            string file = Path.Combine(runFolder, app.id + (app.type == "msi" || app.type == "wix" ? ".msi" : ".exe"));
                            Download(app.url, file);
                            if (stop) break;
                            string actual;
                            using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                            if (!String.Equals(actual, app.sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(file); throw new InvalidDataException("SHA-256 incorrecto: archivo eliminado."); }
                            if (stop) break;
                            UI(delegate { current.Style = ProgressBarStyle.Marquee; status.Text = "Instalando " + app.name + "…"; });
                            var info = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runFolder };
                            if (app.type == "msi" || app.type == "wix") { info.FileName = Path.Combine(Environment.SystemDirectory, "msiexec.exe"); info.Arguments = "/i \"" + file + "\" " + app.arguments; }
                            else { info.FileName = file; info.Arguments = app.arguments; }
                            int code;
                            using (var process = Process.Start(info)) { process.WaitForExit(); code = process.ExitCode; }
                            if (!(app.successCodes ?? new[] { 0, 1641, 3010 }).Contains(code)) throw new InvalidOperationException("Instalador devolvió " + code + ".");
                            reboot |= code == 1641 || code == 3010;
                            ok++; SetResult(app.id, "Instalado" + (code == 3010 || code == 1641 ? " · reinicio pendiente" : ""));
                            WriteLog(app.name + ": instalado (código " + code + ").");
                        } catch (OperationCanceledException) { break;
                        } catch (Exception ex) { failed++; SetResult(app.id, "Error: " + ex.Message); WriteLog(app.name + ": " + ex.Message); }
                        completed++;
                        UI(delegate { total.Value = completed; current.Style = ProgressBarStyle.Continuous; current.Value = 100; });
                    }
                    WriteLog("Instalados: " + ok + ". Errores: " + failed + ". Sin procesar: " + (queue.Length - completed) + (reboot ? ". Reinicio pendiente." : "."));
                    UI(delegate { status.Text = (stop ? "Cola detenida. " : "Cola finalizada. ") + ok + " instalados, " + failed + " errores." + (reboot ? " Reinicia Windows cuando puedas." : ""); });
                } catch (Exception ex) { WriteLog("No se pudo continuar: " + ex.Message); UI(delegate { status.Text = "Error al preparar la cola. Revisa el registro."; });
                } finally { UI(delegate { current.Style = ProgressBarStyle.Continuous; SetBusy(false); cancel.Enabled = false; }); }
            }) { IsBackground = true }.Start();
        }
        void SetResult(string id, string text) { UI(delegate { foreach (ListViewItem item in list.Items) if (((AppEntry)item.Tag).id == id) item.SubItems[2].Text = text; }); }
        void Download(string url, string destination) {
            // Synchronous stream stays on the worker; UI progress is marshalled explicitly.
            var request = (HttpWebRequest)WebRequest.Create(url); request.Timeout = 60000; request.ReadWriteTimeout = 60000; request.UserAgent = "ISOE/1.0";
            using (var response = (HttpWebResponse)request.GetResponse()) {
                if (response.ResponseUri.Scheme != "https") throw new InvalidDataException("Redirección sin HTTPS.");
                using (var input = response.GetResponseStream()) using (var output = new FileStream(destination, FileMode.CreateNew)) {
                    var buffer = new byte[131072]; long received = 0; int length; var watch = Stopwatch.StartNew();
                    while ((length = input.Read(buffer, 0, buffer.Length)) > 0) {
                        if (stop) throw new OperationCanceledException();
                        output.Write(buffer, 0, length); received += length;
                        if (watch.ElapsedMilliseconds > 150) {
                            long bytes = received, size = response.ContentLength;
                            UI(delegate { current.Style = size > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee; if (size > 0) current.Value = (int)Math.Min(100, bytes * 100 / size); }); watch.Restart();
                        }
                    }
                }
            }
        }
    }
    static class Program {
        [STAThread] static void Main(string[] args) {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Contains("--self-test")) {
                var valid = new AppEntry { id = "Test.App", architecture = "x64", sha256 = new string('A', 64), type = "msi", arguments = "/qn /norestart", url = "https://example.com/test.msi" };
                MainForm.Validate(valid); valid.architecture = "x86";
                try { MainForm.Validate(valid); Environment.Exit(1); } catch (InvalidDataException) { }
                valid.architecture = "x64"; valid.url = "http://example.com/test.msi";
                try { MainForm.Validate(valid); Environment.Exit(2); } catch (InvalidDataException) { }
                return;
            }
            if (!Environment.Is64BitOperatingSystem) { MessageBox.Show("ISOE requiere Windows de 64 bits."); return; }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm());
        }
    }
}
