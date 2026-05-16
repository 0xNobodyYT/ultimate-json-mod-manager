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
    internal static class UpdateChecker
    {
        public sealed class ReleaseInfo
        {
            public string TagName;
            public string Title;
            public string Body;
            public string HtmlUrl;
            public string DownloadUrl;
            public long DownloadSize;
            public string AssetName;
        }

        public static ReleaseInfo CheckLatest(out string error)
        {
            error = "";
            try
            {
                var url = "https://api.github.com/repos/" + Program.UpdateRepo + "/releases/latest";
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = Program.AppDisplayName + "/" + Program.AppVersion;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 15000;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var json = sr.ReadToEnd();
                    var d = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(json);
                    if (d == null) { error = "empty response"; return null; }

                    var info = new ReleaseInfo
                    {
                        TagName = d.ContainsKey("tag_name") ? Convert.ToString(d["tag_name"]) : "",
                        Title = d.ContainsKey("name") ? Convert.ToString(d["name"]) : "",
                        Body = d.ContainsKey("body") ? Convert.ToString(d["body"]) : "",
                        HtmlUrl = d.ContainsKey("html_url") ? Convert.ToString(d["html_url"]) : ""
                    };

                    if (d.ContainsKey("assets") && d["assets"] is object[] assets)
                    {
                        foreach (var a in assets)
                        {
                            var ad = a as Dictionary<string, object>;
                            if (ad == null) continue;
                            var name = ad.ContainsKey("name") ? Convert.ToString(ad["name"]) : "";
                            var lower = (name ?? "").ToLowerInvariant();
                            if (lower.EndsWith(".exe") || lower.EndsWith(".zip"))
                            {
                                info.AssetName = name;
                                info.DownloadUrl = ad.ContainsKey("browser_download_url") ? Convert.ToString(ad["browser_download_url"]) : "";
                                long size = 0;
                                if (ad.ContainsKey("size")) try { size = Convert.ToInt64(ad["size"]); } catch { }
                                info.DownloadSize = size;
                                if (lower.EndsWith(".exe")) break;
                            }
                        }
                    }
                    return info;
                }
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response is System.Net.HttpWebResponse r && (int)r.StatusCode == 404) error = "no releases yet";
                else error = wex.Message;
                return null;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static bool IsNewer(string remoteTag, string localVersion)
        {
            return CompareVersions(remoteTag, localVersion) > 0;
        }

        public static int CompareVersions(string remoteTag, string localVersion)
        {
            if (string.IsNullOrEmpty(remoteTag) || string.IsNullOrEmpty(localVersion)) return 0;
            var r = NormalizeVersion(remoteTag);
            var l = NormalizeVersion(localVersion);
            if (r == null || l == null)
            {
                return string.Compare(remoteTag.TrimStart('v', 'V'), localVersion.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
            }
            for (int i = 0; i < Math.Max(r.Length, l.Length); i++)
            {
                int rv = i < r.Length ? r[i] : 0;
                int lv = i < l.Length ? l[i] : 0;
                if (rv > lv) return 1;
                if (rv < lv) return -1;
            }
            return 0;
        }

        private static int[] NormalizeVersion(string v)
        {
            try
            {
                var s = (v ?? "").TrimStart('v', 'V').Trim();
                var parts = s.Split('.');
                var arr = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    int n; int.TryParse(new string(parts[i].TakeWhile(char.IsDigit).ToArray()), out n);
                    arr[i] = n;
                }
                return arr;
            }
            catch { return null; }
        }

    }

    internal sealed class UpdateDialog : Form
    {
        private readonly UpdateChecker.ReleaseInfo info;
        private readonly Label statusLabel;

        public UpdateDialog(UpdateChecker.ReleaseInfo info)
        {
            this.info = info;
            Text = "Update available";
            Width = 580;
            Height = 460;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 11);
            ForeColor = Color.FromArgb(244, 234, 209);
            Font = new Font("Segoe UI", 9.5f);
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Padding = new Padding(20);

            var t = new Label
            {
                Text = "New version available: " + info.TagName,
                Dock = DockStyle.Top,
                Height = 30,
                Font = new Font("Trebuchet MS", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent
            };
            Controls.Add(t);

            var sub = new Label
            {
                Text = "You're on " + Program.AppVersion + ". Open the Nexus files tab to download the latest build.",
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent
            };
            Controls.Add(sub);

            var notes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(9, 10, 8),
                ForeColor = Color.FromArgb(199, 187, 155),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9),
                Text = string.IsNullOrEmpty(info.Body) ? "(no release notes)" : info.Body
            };
            Controls.Add(notes);

            statusLabel = new Label
            {
                Text = "UJMM does not auto-replace itself. Updates are installed from the Nexus mod page.",
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent
            };
            Controls.Add(statusLabel);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.Transparent
            };
            var skip = new GradientButton { Text = "Later", Kind = GradientButton.Style.Default, Width = 100, Height = 36 };
            skip.Click += (s, e) => Close();
            buttons.Controls.Add(skip);

            var view = new GradientButton { Text = "Source on GitHub", Kind = GradientButton.Style.Default, Width = 150, Height = 36 };
            view.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(info.HtmlUrl)) try { Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true }); } catch { }
            };
            buttons.Controls.Add(view);

            var open = new GradientButton { Text = "Open Nexus Files", Kind = GradientButton.Style.Primary, Width = 180, Height = 36 };
            open.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(Program.NexusAppFilesUrl) { UseShellExecute = true }); } catch { }
            };
            buttons.Controls.Add(open);
            Controls.Add(buttons);
        }
    }

    internal sealed class NexusLinkDialog : Form
    {
        public int ResolvedModId { get; private set; }
        public int FileIdHint { get; private set; }
        public string InstalledVersion { get; private set; }

        public NexusLinkDialog(string modName, NexusLink existing)
        {
            Text = "Link to Nexus";
            Width = 540;
            Height = 290;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 11);
            ForeColor = Color.FromArgb(244, 234, 209);
            Font = new Font("Segoe UI", 9.5f);
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Padding = new Padding(20);

            var t = new Label
            {
                Text = "Link to Nexus mod page",
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Trebuchet MS", 12.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent
            };
            Controls.Add(t);

            var hint = new Label
            {
                Text = "Linking '" + modName + "' lets the manager check for updates of this mod.\r\nPaste the Nexus URL (e.g. https://www.nexusmods.com/crimsondesert/mods/1072) or just the mod ID.",
                Dock = DockStyle.Top,
                Height = 60,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent
            };
            Controls.Add(hint);

            var urlBox = new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(20, 21, 14),
                ForeColor = Color.FromArgb(244, 234, 209),
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 8, 0, 0),
                Height = 30
            };
            if (existing != null && existing.ModId > 0) urlBox.Text = existing.ModId.ToString();
            Controls.Add(urlBox);

            var verRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                Height = 32,
                BackColor = Color.Transparent
            };
            verRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            verRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            verRow.Controls.Add(new Label
            {
                Text = "Installed version:",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            var verBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 21, 14),
                ForeColor = Color.FromArgb(244, 234, 209),
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 4, 0, 4)
            };
            if (existing != null && !string.IsNullOrEmpty(existing.InstalledVersion)) verBox.Text = existing.InstalledVersion;
            verRow.Controls.Add(verBox, 1, 0);
            Controls.Add(verRow);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.Transparent
            };
            var ok = new GradientButton { Text = "Save Link", Kind = GradientButton.Style.Primary, Width = 120, Height = 36 };
            ok.Click += (s, e) =>
            {
                var modId = NexusLink.ParseModIdFromUrl(urlBox.Text);
                if (!modId.HasValue || modId.Value <= 0)
                {
                    UiSafe.Msg("Could not parse a Nexus mod ID from that input.\r\nExample: https://www.nexusmods.com/crimsondesert/mods/1072", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ResolvedModId = modId.Value;
                InstalledVersion = string.IsNullOrEmpty(verBox.Text) ? "?" : verBox.Text.Trim();
                FileIdHint = existing == null ? 0 : existing.FileId;
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = new GradientButton { Text = "Cancel", Kind = GradientButton.Style.Default, Width = 100, Height = 36 };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            Controls.Add(buttons);
        }
    }

    internal static class SingleInstance
    {
        private const string MutexName = "Global\\UltimateJsonModManager_SingleInstance_v1";
        private static System.Threading.Mutex mutex;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public static bool TryClaim()
        {
            try
            {
                bool createdNew;
                mutex = new System.Threading.Mutex(true, MutexName, out createdNew);
                return createdNew;
            }
            catch
            {
                return true;
            }
        }

        public static bool ForwardUrlToExistingInstance(string url)
        {
            try
            {
                var hwnd = FindWindow(null, Program.AppDisplayName);
                if (hwnd == IntPtr.Zero) return false;
                SetForegroundWindow(hwnd);
                var bytes = Encoding.UTF8.GetBytes(url);
                var ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    System.Runtime.InteropServices.Marshal.Copy(bytes, 0, ptr, bytes.Length);
                    var cds = new COPYDATASTRUCT
                    {
                        dwData = (IntPtr)0x4E58,
                        cbData = bytes.Length,
                        lpData = ptr
                    };
                    SendMessage(hwnd, 0x004A, IntPtr.Zero, ref cds);
                    return true;
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
                }
            }
            catch { return false; }
        }
    }

    internal static class NxmProtocolHandler
    {
        public static void Register()
        {
            try
            {
                var exePath = Assembly.GetExecutingAssembly().Location;
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm"))
                {
                    if (key == null) return;
                    key.SetValue("", "URL:Nexus Mod Manager Protocol");
                    key.SetValue("URL Protocol", "");
                    using (var icon = key.CreateSubKey("DefaultIcon"))
                    {
                        if (icon != null) icon.SetValue("", "\"" + exePath + "\",0");
                    }
                    using (var shell = key.CreateSubKey(@"shell\open\command"))
                    {
                        if (shell != null) shell.SetValue("", "\"" + exePath + "\" \"%1\"");
                    }
                }
            }
            catch { }
        }

        public static bool IsRegistered()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\nxm\shell\open\command"))
                {
                    if (key == null) return false;
                    var cmd = (string)key.GetValue("");
                    if (string.IsNullOrEmpty(cmd)) return false;
                    var exe = Assembly.GetExecutingAssembly().Location;
                    return cmd.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }
    }

    internal static class NexusClient
    {
        public const string ApiBase = "https://api.nexusmods.com/v1";
        public static string UserAgent { get { return Program.AppDisplayName + "/" + Program.AppVersion + " (+https://github.com/" + Program.UpdateRepo + ")"; } }

        private static System.Net.HttpWebRequest BuildGet(string apiKey, string path)
        {
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(ApiBase + path);
            req.Method = "GET";
            req.Headers["apikey"] = apiKey;
            req.Headers["Application-Name"] = Program.AppDisplayName;
            req.Headers["Application-Version"] = Program.AppVersion;
            req.UserAgent = UserAgent;
            req.Timeout = 25000;
            req.Accept = "application/json";
            return req;
        }

        private static string ReadBody(System.Net.HttpWebResponse resp)
        {
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        private static string Get(string apiKey, string path, out int status, out int hourlyRemaining, out int hourlyLimit, out string error)
        {
            status = 0;
            hourlyRemaining = -1;
            hourlyLimit = -1;
            error = "";
            try
            {
                var req = BuildGet(apiKey, path);
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    status = (int)resp.StatusCode;
                    int.TryParse(resp.Headers["x-rl-hourly-remaining"], out hourlyRemaining);
                    int.TryParse(resp.Headers["x-rl-hourly-limit"], out hourlyLimit);
                    return ReadBody(resp);
                }
            }
            catch (System.Net.WebException wex)
            {
                error = wex.Message;
                if (wex.Response is System.Net.HttpWebResponse r) status = (int)r.StatusCode;
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static Dictionary<string, object> Validate(string apiKey, out string error)
        {
            int status; int rem; int lim;
            var json = Get(apiKey, "/users/validate.json", out status, out rem, out lim, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static List<Dictionary<string, object>> LatestUpdated(string apiKey, string game, out string error, out int hourlyRemaining, out int hourlyLimit)
        {
            int status;
            var json = Get(apiKey, "/games/" + game + "/mods/latest_updated.json", out status, out hourlyRemaining, out hourlyLimit, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var arr = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<object[]>(json);
                var list = new List<Dictionary<string, object>>();
                foreach (var item in arr)
                {
                    var dict = item as Dictionary<string, object>;
                    if (dict != null) list.Add(dict);
                }
                return list;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static List<Tuple<int, long>> GetUpdatedMods(string apiKey, string game, string period, out string error)
        {
            int status; int rem; int lim;
            var json = Get(apiKey, "/games/" + game + "/mods/updated.json?period=" + Uri.EscapeDataString(period ?? "1w"), out status, out rem, out lim, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var arr = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<object[]>(json);
                var list = new List<Tuple<int, long>>();
                foreach (var item in arr)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;
                    int modId = 0; long ts = 0;
                    if (d.ContainsKey("mod_id")) try { modId = Convert.ToInt32(d["mod_id"]); } catch { }
                    if (d.ContainsKey("latest_file_update")) try { ts = Convert.ToInt64(d["latest_file_update"]); } catch { }
                    if (modId > 0) list.Add(Tuple.Create(modId, ts));
                }
                return list;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static Dictionary<string, object> GetMod(string apiKey, string game, int modId, out string error)
        {
            int status; int rem; int lim;
            var json = Get(apiKey, "/games/" + game + "/mods/" + modId + ".json", out status, out rem, out lim, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try { return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static List<Dictionary<string, object>> GetModFiles(string apiKey, string game, int modId, out string error)
        {
            int status; int rem; int lim;
            var json = Get(apiKey, "/games/" + game + "/mods/" + modId + "/files.json", out status, out rem, out lim, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = serializer.Deserialize<object>(json);
                var list = new List<Dictionary<string, object>>();
                Action<object> collect = null;
                collect = value =>
                {
                    var dict = value as Dictionary<string, object>;
                    if (dict != null)
                    {
                        if (dict.ContainsKey("version") || dict.ContainsKey("file_id") || dict.ContainsKey("file_name")) list.Add(dict);
                        foreach (var child in dict.Values) collect(child);
                        return;
                    }
                    var arr = value as object[];
                    if (arr != null)
                    {
                        foreach (var child in arr) collect(child);
                    }
                };
                collect(root);
                return list;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static string GetFileName(string apiKey, string game, int modId, int fileId, out string error)
        {
            int status; int rem; int lim;
            var json = Get(apiKey, "/games/" + game + "/mods/" + modId + "/files/" + fileId + ".json", out status, out rem, out lim, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (d == null) return null;
                if (d.ContainsKey("file_name") && d["file_name"] != null) return Convert.ToString(d["file_name"]);
                return null;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static string GetDownloadLink(string apiKey, string game, int modId, int fileId, string nxmKey, string nxmExpires, out string error)
        {
            var path = "/games/" + game + "/mods/" + modId + "/files/" + fileId + "/download_link.json";
            if (!string.IsNullOrEmpty(nxmKey))
            {
                path += "?key=" + Uri.EscapeDataString(nxmKey);
                if (!string.IsNullOrEmpty(nxmExpires)) path += "&expires=" + Uri.EscapeDataString(nxmExpires);
            }
            int status; int rem; int lim;
            var json = Get(apiKey, path, out status, out rem, out lim, out error);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var arr = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<object[]>(json);
                if (arr == null || arr.Length == 0) return null;
                var first = arr[0] as Dictionary<string, object>;
                if (first == null) return null;
                if (first.ContainsKey("URI") && first["URI"] != null) return Convert.ToString(first["URI"]);
                return null;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static bool DownloadFile(string url, string targetPath, Action<int> onProgress, out string error)
        {
            error = "";
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.UserAgent = UserAgent;
                req.Headers["Application-Name"] = Program.AppDisplayName;
                req.Headers["Application-Version"] = Program.AppVersion;
                req.Timeout = 60000;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    long total = resp.ContentLength;
                    long got = 0;
                    using (var src = resp.GetResponseStream())
                    using (var dst = File.Create(targetPath))
                    {
                        var buf = new byte[81920];
                        int read;
                        int lastPct = -1;
                        while ((read = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            dst.Write(buf, 0, read);
                            got += read;
                            if (total > 0 && onProgress != null)
                            {
                                int pct = (int)((got * 100L) / total);
                                if (pct != lastPct) { lastPct = pct; onProgress(pct); }
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }

    internal sealed class NexusSsoDialog : Form
    {
        public string ApiKey;
        public string ErrorMessage;
        public new DialogResult Result { get; private set; } = DialogResult.Cancel;

        private readonly Label statusLabel;
        private System.Threading.Thread worker;
        private bool cancelled;

        public NexusSsoDialog()
        {
            Text = "Sign in with Nexus";
            Width = 520;
            Height = 220;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 11);
            ForeColor = Color.FromArgb(244, 234, 209);
            Font = new Font("Segoe UI", 9.5f);
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Padding = new Padding(20);

            var title = new Label
            {
                Text = "Sign in with Nexus",
                Dock = DockStyle.Top,
                Height = 30,
                Font = new Font("Trebuchet MS", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent
            };
            Controls.Add(title);

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9.5f),
                ForeColor = Color.FromArgb(199, 187, 155),
                BackColor = Color.Transparent,
                Text = "Connecting to Nexus...",
                TextAlign = ContentAlignment.TopLeft
            };
            Controls.Add(statusLabel);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.Transparent
            };
            var cancel = new GradientButton { Text = "Cancel", Kind = GradientButton.Style.Default, Width = 110, Height = 36 };
            cancel.Click += (s, e) => { cancelled = true; Close(); };
            buttons.Controls.Add(cancel);
            Controls.Add(buttons);
            Controls.SetChildIndex(buttons, 0);

            Shown += (s, e) => StartFlow();
            FormClosing += (s, e) =>
            {
                cancelled = true;
                try { if (worker != null && worker.IsAlive) worker.Interrupt(); } catch { }
            };
        }

        public void SetStatus(string s)
        {
            try { Invoke(new Action(() => { statusLabel.Text = s; })); }
            catch { }
        }

        private void StartFlow()
        {
            worker = new System.Threading.Thread(() =>
            {
                try
                {
                    SetStatus("Opening connection to Nexus SSO...");
                    var conn = Guid.NewGuid().ToString();
                    using (var ws = new System.Net.WebSockets.ClientWebSocket())
                    {
                        var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
                        ws.ConnectAsync(new Uri("wss://sso.nexusmods.com"), cts.Token).GetAwaiter().GetResult();

                        var hello = "{\"id\":\"" + conn + "\",\"token\":null,\"protocol\":2,\"application\":\"" + Program.NexusSsoApplication + "\"}";
                        var helloBytes = Encoding.UTF8.GetBytes(hello);
                        ws.SendAsync(new ArraySegment<byte>(helloBytes), System.Net.WebSockets.WebSocketMessageType.Text, true, cts.Token).GetAwaiter().GetResult();

                        SetStatus("Opening browser to nexusmods.com to approve sign-in...");
                        var signInUrl = "https://www.nexusmods.com/sso?id=" + conn + "&application=" + Program.NexusSsoApplication;
                        try { Process.Start(new ProcessStartInfo(signInUrl) { UseShellExecute = true }); } catch { }

                        SetStatus("Waiting for sign-in approval in your browser...\r\n(this dialog will close automatically once you approve)");

                        var buf = new byte[8192];
                        var msg = new StringBuilder();
                        var apiKey = "";
                        while (!cts.IsCancellationRequested && !cancelled)
                        {
                            msg.Clear();
                            System.Net.WebSockets.WebSocketReceiveResult rcv;
                            do
                            {
                                rcv = ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token).GetAwaiter().GetResult();
                                msg.Append(Encoding.UTF8.GetString(buf, 0, rcv.Count));
                                if (rcv.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                            } while (!rcv.EndOfMessage);

                            if (rcv.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                            var text = msg.ToString();
                            apiKey = TryExtractApiKey(text);
                            if (!string.IsNullOrEmpty(apiKey)) break;
                        }

                        try { ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "done", System.Threading.CancellationToken.None).GetAwaiter().GetResult(); } catch { }

                        if (!string.IsNullOrEmpty(apiKey))
                        {
                            ApiKey = apiKey;
                            Result = DialogResult.OK;
                            try { Invoke(new Action(Close)); } catch { }
                        }
                        else if (!cancelled)
                        {
                            ErrorMessage = "Sign-in not completed (timed out or rejected).";
                            try { Invoke(new Action(Close)); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorMessage = ex.Message;
                    try { Invoke(new Action(Close)); } catch { }
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private static string TryExtractApiKey(string text)
        {
            try
            {
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);
                if (d == null) return null;
                if (d.ContainsKey("data"))
                {
                    var data = d["data"];
                    if (data is string s) return s;
                    var dd = data as Dictionary<string, object>;
                    if (dd != null)
                    {
                        if (dd.ContainsKey("api_key")) return Convert.ToString(dd["api_key"]);
                        if (dd.ContainsKey("apiKey")) return Convert.ToString(dd["apiKey"]);
                    }
                }
                if (d.ContainsKey("api_key")) return Convert.ToString(d["api_key"]);
            }
            catch { }
            return null;
        }
    }

    internal sealed class ApiKeyPasteDialog : Form
    {
        public string ApiKey { get; private set; }

        public ApiKeyPasteDialog()
        {
            Text = "Paste Nexus API Key";
            Width = 540;
            Height = 230;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 11);
            ForeColor = Color.FromArgb(244, 234, 209);
            Font = new Font("Segoe UI", 9.5f);
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Padding = new Padding(18);

            var title = new Label
            {
                Text = "Paste your Nexus API Key",
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Trebuchet MS", 12.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent
            };
            Controls.Add(title);

            var hint = new Label
            {
                Text = "Get your key from nexusmods.com -> Account Settings -> API Access. You only need to do this once.",
                Dock = DockStyle.Top,
                Height = 36,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent
            };
            Controls.Add(hint);

            var input = new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(20, 21, 14),
                ForeColor = Color.FromArgb(244, 234, 209),
                Font = new Font("Consolas", 10),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 8, 0, 0),
                Height = 30
            };
            Controls.Add(input);

            var openLink = new GradientButton
            {
                Text = "Open API page",
                Kind = GradientButton.Style.Default,
                Width = 130,
                Height = 32,
                Margin = new Padding(0, 8, 8, 0)
            };
            openLink.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("https://www.nexusmods.com/users/myaccount?tab=api") { UseShellExecute = true }); } catch { }
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.Transparent
            };
            var ok = new GradientButton { Text = "Save", Kind = GradientButton.Style.Primary, Width = 110, Height = 36 };
            ok.Click += (s, e) => { ApiKey = input.Text; DialogResult = DialogResult.OK; Close(); };
            var cancel = new GradientButton { Text = "Cancel", Kind = GradientButton.Style.Default, Width = 100, Height = 36 };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(openLink);
            Controls.Add(buttons);
        }
    }

    internal sealed class NexusDownloadDialog : Form
    {
        private readonly Label statusLabel;
        private readonly ProgressBar bar;

        public NexusDownloadDialog(string title, int modId)
        {
            Text = title;
            Width = 480;
            Height = 180;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 11);
            ForeColor = Color.FromArgb(244, 234, 209);
            Font = new Font("Segoe UI", 9.5f);
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            Padding = new Padding(20);

            var t = new Label
            {
                Text = "Nexus mod " + modId,
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font("Trebuchet MS", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent
            };
            Controls.Add(t);

            statusLabel = new Label
            {
                Text = "Starting...",
                Dock = DockStyle.Top,
                Height = 36,
                Font = new Font("Consolas", 9.5f),
                ForeColor = Color.FromArgb(199, 187, 155),
                BackColor = Color.Transparent
            };
            Controls.Add(statusLabel);

            bar = new ProgressBar { Dock = DockStyle.Top, Height = 24, Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100 };
            Controls.Add(bar);

            var close = new GradientButton { Text = "Close", Kind = GradientButton.Style.Default, Width = 110, Height = 32 };
            close.Click += (s, e) => Close();
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 38,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = Color.Transparent
            };
            buttons.Controls.Add(close);
            Controls.Add(buttons);
        }

        public void SetStatus(string s) { statusLabel.Text = s; }
        public void SetProgress(int pct) { try { bar.Value = Math.Max(0, Math.Min(100, pct)); } catch { } }
    }
}
