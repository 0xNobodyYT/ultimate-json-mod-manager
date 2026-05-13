using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CdJsonModManager
{
    internal static class CrashReporter
    {
        private static readonly object gate = new object();

        public static List<string> RecentLog = new List<string>();
        public static Func<Dictionary<string, object>> StateProvider = () => new Dictionary<string, object>();

        public static string LastCrashPath { get; private set; }

        public static void RecordLog(string line)
        {
            lock (gate)
            {
                RecentLog.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line);
                if (RecentLog.Count > 200) RecentLog.RemoveAt(0);
            }
        }

        public static void Capture(Exception ex, string source)
        {
            try
            {
                var payload = BuildPayload(ex, source);
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "crashes");
                Directory.CreateDirectory(dir);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var path = Path.Combine(dir, stamp + "-" + SafeFilePart(ex.GetType().Name) + ".json");
                var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(payload);
                File.WriteAllText(path, FormatJson(json), Encoding.UTF8);
                LastCrashPath = path;
                ShowReportDialog(ex, source, path, payload);
            }
            catch
            {
                try
                {
                    var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "crashes");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "fatal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"), ex.ToString());
                }
                catch { }
            }
        }

        public static Dictionary<string, object> BuildPayload(Exception ex, string source)
        {
            var p = new Dictionary<string, object>
            {
                ["timestamp"] = DateTime.UtcNow.ToString("u"),
                ["source"] = source,
                ["app_version"] = Program.AppVersion,
                ["build_date"] = AssemblyBuildDate(),
                ["os"] = Environment.OSVersion.VersionString,
                ["clr"] = Environment.Version.ToString(),
                ["dotnet"] = DetectFrameworkVersion(),
                ["exception_type"] = ex == null ? "" : ex.GetType().FullName,
                ["exception_message"] = Redact(ex == null ? "" : ex.Message),
                ["stack_trace"] = Redact(ex == null ? "" : (ex.ToString() ?? "")),
                ["recent_log"] = SnapshotLog()
            };
            try
            {
                var state = StateProvider == null ? new Dictionary<string, object>() : (StateProvider() ?? new Dictionary<string, object>());
                foreach (var kv in state)
                {
                    p["state_" + kv.Key] = kv.Value is string ? Redact((string)kv.Value) : kv.Value;
                }
            }
            catch { }
            return p;
        }

        public static List<string> SnapshotLog()
        {
            lock (gate)
            {
                var copy = new List<string>(RecentLog.Count);
                foreach (var l in RecentLog) copy.Add(Redact(l));
                return copy;
            }
        }

        private static string DetectFrameworkVersion()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key != null)
                    {
                        var rel = key.GetValue("Release");
                        if (rel != null) return "4.x release " + rel.ToString();
                    }
                }
            }
            catch { }
            return "unknown";
        }

        private static string AssemblyBuildDate()
        {
            try { return File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd"); }
            catch { return ""; }
        }

        public static string Redact(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                s = s.Replace(home, "<USER>");
                var name = Environment.UserName;
                if (!string.IsNullOrEmpty(name))
                {
                    s = Regex.Replace(s, Regex.Escape(name), "<USER>", RegexOptions.IgnoreCase);
                }
            }
            s = Regex.Replace(s, @"[A-Za-z0-9+/]{40,}--[A-Za-z0-9+/]+--[A-Za-z0-9+/]+={0,2}", "<NEXUS_KEY>");
            return s;
        }

        private static string SafeFilePart(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            foreach (var c in bad) s = s.Replace(c, '_');
            if (s.Length > 64) s = s.Substring(0, 64);
            return s;
        }

        private static string FormatJson(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                if (!inStr)
                {
                    if (c == '{' || c == '[') { sb.Append(c); sb.AppendLine(); indent++; sb.Append(new string(' ', indent * 2)); continue; }
                    if (c == '}' || c == ']') { sb.AppendLine(); indent--; sb.Append(new string(' ', indent * 2)); sb.Append(c); continue; }
                    if (c == ',') { sb.Append(c); sb.AppendLine(); sb.Append(new string(' ', indent * 2)); continue; }
                    if (c == ':') { sb.Append(": "); continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static void ShowReportDialog(Exception ex, string source, string crashPath, Dictionary<string, object> payload)
        {
            try
            {
                using (var dlg = new BugReportDialog(ex, source, crashPath, payload, isCrash: true))
                {
                    dlg.ShowDialog();
                }
            }
            catch
            {
                MessageBox.Show("The app encountered an unexpected error.\r\nA crash dump was saved to: " + crashPath, "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static void OpenManualReport(IWin32Window owner)
        {
            var payload = new Dictionary<string, object>
            {
                ["timestamp"] = DateTime.UtcNow.ToString("u"),
                ["source"] = "Manual",
                ["app_version"] = Program.AppVersion,
                ["build_date"] = AssemblyBuildDate(),
                ["os"] = Environment.OSVersion.VersionString,
                ["dotnet"] = DetectFrameworkVersion(),
                ["recent_log"] = SnapshotLog()
            };
            try
            {
                var state = StateProvider == null ? new Dictionary<string, object>() : (StateProvider() ?? new Dictionary<string, object>());
                foreach (var kv in state) payload["state_" + kv.Key] = kv.Value is string ? Redact((string)kv.Value) : kv.Value;
            }
            catch { }
            using (var dlg = new BugReportDialog(null, "Manual", "", payload, isCrash: false))
            {
                dlg.ShowDialog(owner);
            }
        }

        public static string BuildIssueUrl(Exception ex, Dictionary<string, object> payload, bool isCrash)
        {
            var owner = Program.BugReportRepo;
            var title = isCrash
                ? "[crash] " + (ex == null ? "Unknown" : ex.GetType().Name) + ": " + Truncate(ex == null ? "" : ex.Message, 80)
                : "[bug] ";
            var body = new StringBuilder();
            body.AppendLine("## What happened?");
            body.AppendLine();
            body.AppendLine(isCrash ? "_The app crashed with the error below._" : "_<describe the bug - what did you do, what did you expect, what happened instead?>_");
            body.AppendLine();
            body.AppendLine("## Steps to reproduce");
            body.AppendLine();
            body.AppendLine("1. ");
            body.AppendLine("2. ");
            body.AppendLine();
            body.AppendLine("## Diagnostics");
            body.AppendLine();
            body.AppendLine("```");
            foreach (var kv in payload)
            {
                if (kv.Key == "recent_log") continue;
                body.AppendLine(kv.Key + ": " + Truncate(Convert.ToString(kv.Value), 400));
            }
            body.AppendLine("```");
            body.AppendLine();
            body.AppendLine("## Recent log");
            body.AppendLine();
            body.AppendLine("```");
            var log = payload.ContainsKey("recent_log") ? payload["recent_log"] as List<string> : null;
            if (log != null)
            {
                int start = Math.Max(0, log.Count - 50);
                for (int i = start; i < log.Count; i++) body.AppendLine(log[i]);
            }
            body.AppendLine("```");

            var url = "https://github.com/" + owner + "/issues/new?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body.ToString());
            if (url.Length > 7800)
            {
                url = url.Substring(0, 7800) + Uri.EscapeDataString("\n\n...truncated. Full report saved locally.");
            }
            return url;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }
    }

    internal sealed class BugReportDialog : Form
    {
        public BugReportDialog(Exception ex, string source, string crashPath, Dictionary<string, object> payload, bool isCrash)
        {
            Text = isCrash ? "Unexpected Error" : "Report a Bug";
            Width = 820;
            Height = 640;
            MinimumSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 11);
            ForeColor = Color.FromArgb(244, 234, 209);
            Font = new Font("Segoe UI", 9.5f);
            ShowIcon = false;
            MaximizeBox = true;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Padding = new Padding(18);

            var title = new Label
            {
                Text = isCrash ? "The app encountered an error" : "Send us a bug report",
                Dock = DockStyle.Top,
                Height = 30,
                Font = new Font("Trebuchet MS", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent
            };
            Controls.Add(title);

            var sub = new Label
            {
                Text = isCrash
                    ? "A crash dump was saved locally. You can send a sanitized report to help us fix it."
                    : "Describe what went wrong. App diagnostics and recent log are attached automatically.",
                Dock = DockStyle.Top,
                Height = 38,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent
            };
            Controls.Add(sub);

            var preview = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                MaxLength = 1024 * 1024, // bump well past the 32 KB default - log + state can easily exceed it
                BackColor = Color.FromArgb(9, 10, 8),
                ForeColor = Color.FromArgb(199, 187, 155),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9),
                Dock = DockStyle.Fill
            };
            var sb = new StringBuilder();
            foreach (var kv in payload)
            {
                if (kv.Key == "recent_log") continue;
                sb.AppendLine(kv.Key + ": " + Convert.ToString(kv.Value));
            }
            sb.AppendLine();
            sb.AppendLine("--- recent log ---");
            var log = payload.ContainsKey("recent_log") ? payload["recent_log"] as List<string> : null;
            if (log != null) foreach (var l in log) sb.AppendLine(l);
            preview.Text = sb.ToString();
            Controls.Add(preview);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.Transparent
            };

            var skip = new GradientButton { Text = isCrash ? "Skip" : "Cancel", Kind = GradientButton.Style.Default, Width = 90, Height = 36 };
            skip.Click += (s, e) => Close();
            buttons.Controls.Add(skip);

            var openFolder = new GradientButton { Text = "Open Folder", Kind = GradientButton.Style.Default, Width = 110, Height = 36 };
            openFolder.Click += (s, e) =>
            {
                try
                {
                    var dir = string.IsNullOrEmpty(crashPath)
                        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "crashes")
                        : Path.GetDirectoryName(crashPath);
                    Directory.CreateDirectory(dir);
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                }
                catch { }
            };
            buttons.Controls.Add(openFolder);

            var send = new GradientButton { Text = "Send via GitHub", Kind = GradientButton.Style.Primary, Width = 170, Height = 36 };
            send.Click += (s, e) =>
            {
                try
                {
                    var url = CrashReporter.BuildIssueUrl(ex, payload, isCrash);
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception openErr)
                {
                    MessageBox.Show("Could not open GitHub: " + openErr.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Close();
            };
            buttons.Controls.Add(send);

            Controls.Add(buttons);
        }
    }
}
