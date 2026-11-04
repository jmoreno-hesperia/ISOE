using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ISOE {
    public sealed class InstalledProduct {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
    }
    public sealed class InstalledInventory {
        public readonly List<InstalledProduct> Products = new List<InstalledProduct>();
        public readonly List<string> OfficeProducts = new List<string>();
        public readonly HashSet<string> UpgradeCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool QuickSupportPrepared;
    }
    public static class InstalledSoftware {
        const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern uint MsiEnumRelatedProductsW(string upgradeCode, uint reserved, uint index, StringBuilder product);
        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int MsiQueryProductStateW(string product);

        public static InstalledProduct FindInstalled(AppEntry app) {
            return Match(app, Capture(app));
        }
        public static InstalledInventory Capture(AppEntry app) {
            var inventory = new InstalledInventory();
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    using (var root = RegistryKey.OpenBaseKey(hive, view)) {
                        using (var uninstall = root.OpenSubKey(UninstallPath, false)) {
                            if (uninstall != null) foreach (string code in uninstall.GetSubKeyNames()) {
                                try {
                                    using (var key = uninstall.OpenSubKey(code, false)) {
                                        if (key == null) continue;
                                        string name = Convert.ToString(key.GetValue("DisplayName"));
                                        if (String.IsNullOrWhiteSpace(name)) continue;
                                        inventory.Products.Add(new InstalledProduct { Code = code, Name = name,
                                            Publisher = Convert.ToString(key.GetValue("Publisher")), Version = Convert.ToString(key.GetValue("DisplayVersion")) });
                                    }
                                } catch (SecurityException) { } catch (UnauthorizedAccessException) { }
                            }
                        }
                        if (hive == RegistryHive.LocalMachine)
                            using (var office = root.OpenSubKey(@"SOFTWARE\Microsoft\Office\ClickToRun\Configuration", false))
                                if (office != null) inventory.OfficeProducts.AddRange(Convert.ToString(office.GetValue("ProductReleaseIds")).Split(','));
                    }
            foreach (string upgrade in app.upgradeCodes ?? new string[0]) {
                Guid id;
                if (!Guid.TryParse(upgrade, out id)) continue;
                string normalized = id.ToString("B");
                for (uint index = 0; index < 256; index++) {
                    var product = new StringBuilder(39);
                    if (MsiEnumRelatedProductsW(normalized, 0, index, product) != 0) break;
                    // Advertised MSI products are not installed and must not be skipped.
                    if (MsiQueryProductStateW(product.ToString()) == 5) { inventory.UpgradeCodes.Add(normalized); break; }
                }
            }
            if (app.id == "TeamViewer.TeamViewer.QuickSupport") {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ISOE", "Tools", app.id, "TeamViewerQS.exe");
                if (File.Exists(file)) {
                    try { InstallerSecurity.VerifyVendor(file, app.id); inventory.QuickSupportPrepared = true; }
                    catch (InvalidDataException) { } catch (System.Security.Cryptography.CryptographicException) { }
                }
            }
            return inventory;
        }
        public static InstalledProduct Match(AppEntry app, InstalledInventory inventory) {
            if (app.id == "Microsoft.Office") {
                if (inventory.OfficeProducts.Any(p => p.Trim().StartsWith("O365", StringComparison.OrdinalIgnoreCase)))
                    return new InstalledProduct { Name = "Microsoft 365", Publisher = "Microsoft" };
                return null;
            }
            if (app.id == "TeamViewer.TeamViewer.QuickSupport" && inventory.QuickSupportPrepared)
                return new InstalledProduct { Name = "TeamViewer QuickSupport", Publisher = "TeamViewer" };
            var codes = new HashSet<string>((app.productCodes ?? new string[0]).Select(NormalizeCode), StringComparer.OrdinalIgnoreCase);
            var byCode = inventory.Products.FirstOrDefault(p => !String.IsNullOrEmpty(p.Code) && codes.Contains(NormalizeCode(p.Code)));
            if (byCode != null) return byCode;
            if ((app.upgradeCodes ?? new string[0]).Any(c => inventory.UpgradeCodes.Any(i => NormalizeCode(i) == NormalizeCode(c))))
                return new InstalledProduct { Name = app.name, Publisher = app.publisher };
            foreach (var product in inventory.Products) {
                string pattern = null, publisher = null;
                switch (app.id) {
                    case "7zip.7zip": pattern = @"^7-Zip(?: \d+(?:\.\d+)*)?(?: \((?:x64|x86|arm64|32-bit|64-bit)(?: edition)?\))?$"; publisher = "Igor Pavlov"; break;
                    case "Adobe.Acrobat.Reader.64-bit": pattern = @"^Adobe (?:(?:Acrobat )?Reader(?: DC)?(?: \d+(?:\.\d+)*)?(?: \((?:32|64)-bit\))?(?: - .+)?|Acrobat(?: DC)?(?: \((?:32|64)-bit\))?)$"; publisher = "Adobe"; break;
                    case "Oracle.JavaRuntimeEnvironment": pattern = @"^Java(?:\(TM\))? (?:\d+ Update \d+|(?:SE )?Runtime Environment(?: \d[\w.]*)?)(?: \((?:32|64)-bit\))?$"; publisher = "Oracle"; break;
                    case "AnyDesk.AnyDesk": pattern = @"^AnyDesk(?: \d[\w.]*)?$"; publisher = "AnyDesk"; break;
                    case "TeamViewer.TeamViewer.QuickSupport": pattern = @"^TeamViewer QuickSupport(?: \d[\w.]*)?$"; publisher = "TeamViewer"; break;
                }
                if (pattern != null) {
                    if (Regex.IsMatch(product.Name ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                        (PublisherMatches(publisher, product.Publisher) || (app.id == "Oracle.JavaRuntimeEnvironment" && PublisherMatches("Sun Microsystems", product.Publisher)))) return product;
                    // Built-in identities must not match loosely by a friendly label.
                    continue;
                }
                var names = (app.detectionNames ?? new string[0]).Concat(new[] { app.name });
                var publishers = (app.detectionPublishers ?? new string[0]).Concat(new[] { app.publisher });
                if (names.Any(n => NormalizeName(n) == NormalizeName(product.Name)) &&
                    (!publishers.Any(p => !String.IsNullOrWhiteSpace(p)) || publishers.Any(p => PublisherMatches(p, product.Publisher)))) return product;
            }
            return null;
        }
        static string NormalizeCode(string code) { return (code ?? "").Trim().Trim('{', '}').ToUpperInvariant(); }
        static string NormalizeName(string name) {
            string value = Regex.Replace((name ?? "").Trim(), @"\s+\((?:x64|x86|64-bit|32-bit)\)$", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\s+v?\d+\.\d+[\w.\-]*$", "", RegexOptions.IgnoreCase);
            return Regex.Replace(value, @"\s+", " ").ToUpperInvariant();
        }
        static bool PublisherMatches(string expected, string actual) {
            if (String.IsNullOrWhiteSpace(expected) || String.IsNullOrWhiteSpace(actual)) return false;
            string first = Regex.Replace(expected.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim();
            string second = Regex.Replace(actual.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim();
            return first == second || second.StartsWith(first + " ", StringComparison.Ordinal) || first.StartsWith(second + " ", StringComparison.Ordinal);
        }
    }
}
