using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ISOE {
    public enum AppAction { Install, Update, SkipCurrent, SkipUnknownVersion, OfficeUpdate }
    public static class UpdatePolicy {
        public static AppAction Decide(AppEntry app, InstalledProduct installed) {
            if (installed == null) return AppAction.Install;
            if (app.id == "Microsoft.Office") return AppAction.OfficeUpdate;
            if (app.id == "TeamViewer.TeamViewer.QuickSupport" && !String.IsNullOrEmpty(installed.FileHash))
                return String.Equals(installed.FileHash, app.sha256, StringComparison.OrdinalIgnoreCase) ? AppAction.SkipCurrent : AppAction.Update;
            int comparison;
            if (!TryCompare(app.id, installed.Version, app.version, out comparison)) return AppAction.SkipUnknownVersion;
            return comparison >= 0 ? AppAction.SkipCurrent : AppAction.Update;
        }
        public static bool TryCompare(string id, string installed, string target, out int comparison) {
            comparison = 0;
            List<string> first, second; string firstPre, secondPre;
            if (!Parse(id, installed, out first, out firstPre) || !Parse(id, target, out second, out secondPre)) return false;
            // Old Java version strings can omit the build number; compare their known update.
            int length = id == "Oracle.JavaRuntimeEnvironment" && (first.Count < 4 || second.Count < 4) ? 3 : Math.Max(first.Count, second.Count);
            for (int i = 0; i < length; i++) {
                int value = CompareNumber(i < first.Count ? first[i] : "0", i < second.Count ? second[i] : "0");
                if (value != 0) { comparison = value; return true; }
            }
            if (firstPre == secondPre) return true;
            if (firstPre.Length == 0 || secondPre.Length == 0) { comparison = firstPre.Length == 0 ? 1 : -1; return true; }
            string[] left = firstPre.Split('.'), right = secondPre.Split('.');
            for (int i = 0; i < Math.Min(left.Length, right.Length); i++) {
                bool leftNumber = left[i].All(Char.IsDigit), rightNumber = right[i].All(Char.IsDigit);
                int value = leftNumber && rightNumber ? CompareNumber(left[i], right[i]) : leftNumber != rightNumber ? (leftNumber ? -1 : 1) : String.Compare(left[i], right[i], StringComparison.OrdinalIgnoreCase);
                if (value != 0) { comparison = Math.Sign(value); return true; }
            }
            comparison = Math.Sign(left.Length.CompareTo(right.Length)); return true;
        }
        static int CompareNumber(string left, string right) {
            left = left.TrimStart('0'); right = right.TrimStart('0');
            if (left.Length != right.Length) return Math.Sign(left.Length.CompareTo(right.Length));
            return Math.Sign(String.Compare(left, right, StringComparison.Ordinal));
        }
        static bool Parse(string id, string version, out List<string> parts, out string prerelease) {
            parts = new List<string>(); prerelease = "";
            if (String.IsNullOrWhiteSpace(version)) return false;
            string value = version.Trim();
            if (id == "Oracle.JavaRuntimeEnvironment") {
                var legacyJava = Regex.Match(value, @"^1\.(\d+)\.0_(\d+)(?:-b(\d+))?$");
                if (legacyJava.Success) {
                    parts.AddRange(new[] { legacyJava.Groups[1].Value, "0", legacyJava.Groups[2].Value });
                    if (legacyJava.Groups[3].Success) parts.Add(legacyJava.Groups[3].Value);
                    return true;
                }
            }
            var match = Regex.Match(value, @"^[vV]?(\d+(?:[._,]\d+)*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$");
            if (!match.Success) return false;
            parts = match.Groups[1].Value.Split('.', '_', ',').ToList(); prerelease = match.Groups[2].Value;
            if (id == "Oracle.JavaRuntimeEnvironment") {
                if (parts.Count >= 4 && parts[0] == "1") parts = new List<string> { parts[1], parts[2], parts[3] }.Concat(parts.Skip(4)).ToList();
                else if (parts.Count >= 3 && parts[2].Length >= 3 && parts[2].EndsWith("0", StringComparison.Ordinal)) parts[2] = parts[2].Substring(0, parts[2].Length - 1);
            }
            return true;
        }
        public static string OfficeUpdateArguments {
            get { return "/update user displaylevel=false forceappshutdown=false updatepromptuser=false"; }
        }
    }
}
