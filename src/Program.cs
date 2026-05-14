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
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

[assembly: AssemblyTitle("Ultimate JSON Mod Manager")]
[assembly: AssemblyDescription("Ultimate JSON Mod Manager for Crimson Desert")]
[assembly: AssemblyCompany("0xNobody")]
[assembly: AssemblyProduct("Ultimate JSON Mod Manager")]
[assembly: AssemblyFileVersion("1.5.2.0")]
[assembly: AssemblyVersion("1.5.2.0")]

namespace CdJsonModManager
{
    internal static class Program
    {
        public const string AppDisplayName = "Ultimate JSON Mod Manager";
        public const string AppShortName = "UJMM";
        public const string DonateUrl = "https://buymeacoffee.com/0xNobody";
        public const string BugReportRepo = "0xNobodyYT/ultimate-json-mod-manager";
        public const string UpdateRepo = "0xNobodyYT/ultimate-json-mod-manager";
        public const string AppVersion = "1.5.2";
        public const string NexusGameDomain = "crimsondesert";
        public const int NexusAppModId = 2454;
        public const string NexusAppPageUrl = "https://www.nexusmods.com/crimsondesert/mods/2454";
        public const string NexusAppFilesUrl = "https://www.nexusmods.com/crimsondesert/mods/2454?tab=files";
        public const string NexusSsoApplication = "0xnobody-ultimatejsonmodmanager"; // app slug used for SSO handshake - assigned by Nexus 2026-05-10
        public const string NxmScheme = "nxm";

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
            }
            catch { }

            string nxmUrl = null;
            if (args != null)
            {
                foreach (var a in args)
                {
                    if (!string.IsNullOrEmpty(a) && a.StartsWith(NxmScheme + "://", StringComparison.OrdinalIgnoreCase))
                    {
                        nxmUrl = a;
                        break;
                    }
                }
            }

            if (!SingleInstance.TryClaim())
            {
                if (!string.IsNullOrEmpty(nxmUrl))
                {
                    SingleInstance.ForwardUrlToExistingInstance(nxmUrl);
                }
                return;
            }

            try
            {
                if (!NxmProtocolHandler.IsRegistered()) NxmProtocolHandler.Register();
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += (sender, e) => CrashReporter.Capture(e.Exception, "ThreadException");
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject));
                CrashReporter.Capture(ex, "UnhandledException");
            };

            Application.Run(new ManagerForm(nxmUrl));
        }
    }

    internal sealed partial class ManagerForm : Form
    {
        private readonly string appDir = AppDomain.CurrentDomain.BaseDirectory;
        private readonly string modsDir;
        private readonly string enabledDir;
        private readonly string asiDir;
        private readonly string backupsDir;
        private readonly string cacheDir;
        private readonly string configPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        private readonly List<JsonMod> mods = new List<JsonMod>();
        private readonly Dictionary<string, FlatCheck> activeBoxes = new Dictionary<string, FlatCheck>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> groupBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RoundedPanel> presetCards = new Dictionary<string, RoundedPanel>(StringComparer.OrdinalIgnoreCase);
        private string activePreset = "";
        private string focusedModPath = "";
        private readonly Dictionary<string, RoundedPanel> modCards = new Dictionary<string, RoundedPanel>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NexusLink> nexusLinks = new Dictionary<string, NexusLink>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Pill> modCardPills = new Dictionary<string, Pill>(StringComparer.OrdinalIgnoreCase);
        private bool suppressSelectionPersist;
        private System.Windows.Forms.Timer nexusUpdateTimer;
        private readonly Dictionary<string, object> config;

        private Pill statusGamePill;
        private Pill statusModsPill;
        private Pill statusBuildPill;
        private TextBox gamePathText;
        private RoundedPanel dropZone;
        private Label dropTitle;
        private Label dropHint;
        private FlowLayoutPanel modCardsHost;
        private ListView patchList;
        private ListView asiList;
        private TextBox logBox;
        private Label loaderLabel;
        private Label workspaceCounter;
        private Label inspectorCounter;
        private Label summaryLabel;
        private CheckGridRow checkGuards;
        private CheckGridRow checkOverlay;
        private CheckGridRow checkConflicts;
        private CheckGridRow checkBackup;
        private RoundedPanel topBar;
        private RoundedPanel bottomBar;
        private RoundedPanel installPanel;
        private RoundedPanel workspacePanel;
        private RoundedPanel inspectorPanel;
        private FlowLayoutPanel themeSwatchHost;
        private FlowLayoutPanel presetRailHost;
        private DarkTabControl tabs;
        private string nexusApiKey;
        private string nexusUserName;
        private bool nexusIsPremium;
        private readonly ToolTip tipsHost = new ToolTip { AutoPopDelay = 12000, InitialDelay = 350, ReshowDelay = 350, ShowAlways = true };
        private Pill updatePill;
        private Pill nexusStatusPill;
        private GradientButton nexusConnectButton;
        private FlowLayoutPanel nexusFeedHost;
        private Label nexusUserLabel;
        private Label nexusRateLabel;
        private string gamePath;
        private Theme currentTheme;
        private bool operationRunning;

        private string pendingNxmUrl;

        public ManagerForm() : this(null) { }

        public ManagerForm(string initialNxmUrl)
        {
            pendingNxmUrl = initialNxmUrl;
            modsDir = Path.Combine(appDir, "mods");
            enabledDir = Path.Combine(modsDir, "enabled");
            asiDir = Path.Combine(modsDir, "_asi");
            backupsDir = Path.Combine(appDir, "backups");
            cacheDir = Path.Combine(appDir, ".cache", "extracted");
            configPath = Path.Combine(appDir, "config.json");

            EnsureLayout();
            config = LoadConfig();
            nexusApiKey = ConfigString("nexusApiKey");
            nexusUserName = ConfigString("nexusUserName");
            nexusIsPremium = ConfigBool("nexusIsPremium");
            gamePath = ConfigString("gamePath");
            if (string.IsNullOrWhiteSpace(gamePath) || !IsGameFolder(gamePath))
            {
                gamePath = DetectGameFolder();
            }

            currentTheme = ResolveSavedTheme();
            activePreset = ConfigString("activePreset");
            // Restore per-patch disabled set
            try
            {
                if (config.ContainsKey("disabledPatches") && config["disabledPatches"] is object[] arr)
                {
                    foreach (var s in arr) if (s != null) disabledPatches.Add(Convert.ToString(s));
                }
            }
            catch { }
            CrashReporter.StateProvider = () =>
            {
                var s = new Dictionary<string, object>();
                try
                {
                    s["theme"] = currentTheme == null ? "" : currentTheme.Name;
                    s["game_folder_set"] = !string.IsNullOrEmpty(gamePath);
                    s["game_folder_valid"] = IsGameFolder(gamePath);
                    s["mods_loaded"] = mods.Count;
                    s["mods_active"] = activeBoxes.Values.Count(b => b.Checked);
                    s["active_preset"] = activePreset ?? "";
                    s["backup_app"] = File.Exists(AppPapgtBackupPath());
                    s["backup_game"] = !string.IsNullOrEmpty(GamePapgtBackupPath()) && File.Exists(GamePapgtBackupPath());
                }
                catch { }
                return s;
            };
            BuildUi();
            ApplyTheme(currentTheme);
            LoadMods();
            RefreshAsi();
            if (gamePathText != null) gamePathText.Text = gamePath ?? "";
            UpdateStatusPills();
            UpdateInspectorBackup();
            Log("Native .NET manager ready.");
        }

        private void ApplyOverlayStub()
        {
            if (!IsGameFolder(gamePath))
            {
                MessageBox.Show("Set the Crimson Desert folder first.", "Game folder missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var selected = mods.Where(m => activeBoxes.ContainsKey(m.Path) && activeBoxes[m.Path].Checked).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Tick at least one mod's checkbox first.", "No mods active", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RunBackgroundOperation(() => ApplyByPazAppend(selected), "Applying " + selected.Count + " mod(s)...");
        }

        private void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private void Log(string message)
        {
            CrashReporter.RecordLog(message);
            if (logBox == null) return;
            if (logBox.InvokeRequired)
            {
                try { logBox.BeginInvoke(new Action<string>(Log), message); } catch { }
                return;
            }
            logBox.AppendText(message + Environment.NewLine);
        }

        private void RunBackgroundOperation(Action work, string label)
        {
            if (operationRunning)
            {
                MessageBox.Show("UJMM is already working. Please wait for the current operation to finish.", "Busy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            operationRunning = true;
            var oldCursor = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            Log(label);
            Task.Run(() =>
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    Log("Operation failed: " + ex.Message);
                    try
                    {
                        BeginInvoke(new Action(() =>
                            MessageBox.Show("Operation failed:\r\n\r\n" + ex.Message, "UJMM", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                    }
                    catch { }
                }
                finally
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            operationRunning = false;
                            Cursor.Current = oldCursor;
                            UpdateStatusPills();
                            UpdateBottomSummary();
                        }));
                    }
                    catch
                    {
                        operationRunning = false;
                    }
                }
            });
        }

        private static byte[] HexToBytes(string hex)
        {
            hex = Regex.Replace(hex, @"\s+", "");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private static string BytesToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
