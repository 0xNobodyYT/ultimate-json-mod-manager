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

[assembly: AssemblyTitle("Ultimate JSON Mod Manager")]
[assembly: AssemblyDescription("Ultimate JSON Mod Manager for Crimson Desert")]
[assembly: AssemblyCompany("0xNobody")]
[assembly: AssemblyProduct("Ultimate JSON Mod Manager")]
[assembly: AssemblyFileVersion("1.3.4.0")]
[assembly: AssemblyVersion("1.3.4.0")]

namespace CdJsonModManager
{
    internal static class Program
    {
        public const string AppDisplayName = "Ultimate JSON Mod Manager";
        public const string AppShortName = "UJMM";
        public const string DonateUrl = "https://buymeacoffee.com/0xNobody";
        public const string BugReportRepo = "0xNobodyYT/ultimate-json-mod-manager";
        public const string UpdateRepo = "0xNobodyYT/ultimate-json-mod-manager";
        public const string AppVersion = "1.3.4";
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

    internal sealed class ManagerForm : Form
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

        private Theme ResolveSavedTheme()
        {
            var name = ConfigString("theme");
            switch ((name ?? "").ToLowerInvariant())
            {
                case "ember": return Theme.Ember();
                case "frost": return Theme.Frost();
                case "forest": return Theme.Forest();
                case "custom":
                    var hex = ConfigString("customAccent");
                    if (!string.IsNullOrEmpty(hex))
                    {
                        try { return Theme.Custom(ColorTranslator.FromHtml(hex)); } catch { }
                    }
                    return Theme.Gilded();
                default: return Theme.Gilded();
            }
        }

        private void EnsureLayout()
        {
            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(enabledDir);
            Directory.CreateDirectory(asiDir);
            Directory.CreateDirectory(Path.Combine(modsDir, "_enabled"));
            Directory.CreateDirectory(Path.Combine(modsDir, "_lang"));
            Directory.CreateDirectory(backupsDir);
            Directory.CreateDirectory(cacheDir);

            var state = Path.Combine(backupsDir, "state.json");
            if (!File.Exists(state))
            {
                File.WriteAllText(state, "{\r\n  \"overlay_groups\": [],\r\n  \"applied_mods\": []\r\n}", Encoding.UTF8);
            }

            var loadOrder = Path.Combine(enabledDir, "_load_order.json");
            if (!File.Exists(loadOrder))
            {
                File.WriteAllText(loadOrder, "[]\r\n", Encoding.UTF8);
            }
        }

        private Dictionary<string, object> LoadConfig()
        {
            if (!File.Exists(configPath))
            {
                var fresh = DefaultConfig();
                SaveConfig(fresh);
                return fresh;
            }

            try
            {
                var parsed = json.DeserializeObject(File.ReadAllText(configPath, Encoding.UTF8)) as Dictionary<string, object>;
                var merged = DefaultConfig();
                if (parsed != null)
                {
                    foreach (var pair in parsed)
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
                merged["modsPath"] = modsDir;
                return merged;
            }
            catch
            {
                return DefaultConfig();
            }
        }

        private Dictionary<string, object> DefaultConfig()
        {
            return new Dictionary<string, object>
            {
                ["gamePath"] = "",
                ["modsPath"] = modsDir,
                ["activeMods"] = new object[0],
                ["activeAsiMods"] = new object[0],
                ["activeLangMod"] = null,
                ["selectedLanguage"] = "english",
                ["uiLang"] = "en",
                ["modsApplied"] = false,
                ["modsInstalled"] = false,
                ["devMode"] = true,
                ["windowWidth"] = 1320,
                ["windowHeight"] = 820,
                ["windowLeft"] = null,
                ["windowTop"] = null,
                ["windowMaximized"] = false,
                ["colModsWidth"] = 320,
                ["colPatchesWidth"] = 700,
                ["colLogWidth"] = 360,
                ["theme"] = "Gilded",
                ["selectedGroups"] = new Dictionary<string, object>(),
                ["modOrder"] = new object[0]
            };
        }

        private string ConfigString(string key)
        {
            return config != null && config.ContainsKey(key) && config[key] != null ? Convert.ToString(config[key]) : "";
        }

        private bool ConfigBool(string key)
        {
            if (config == null || !config.ContainsKey(key) || config[key] == null) return false;
            try { return Convert.ToBoolean(config[key]); } catch { return false; }
        }

        private HashSet<string> ConfigStringSet(string key)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!config.ContainsKey(key) || config[key] == null)
            {
                return set;
            }

            if (config[key] is object[] array)
            {
                foreach (var item in array) set.Add(Convert.ToString(item));
            }
            else if (config[key] is System.Collections.ArrayList list)
            {
                foreach (var item in list) set.Add(Convert.ToString(item));
            }
            return set;
        }

        private List<string> ConfigStringList(string key)
        {
            var listOut = new List<string>();
            if (!config.ContainsKey(key) || config[key] == null)
            {
                return listOut;
            }

            if (config[key] is object[] array)
            {
                foreach (var item in array)
                {
                    var text = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(text)) listOut.Add(text);
                }
            }
            else if (config[key] is System.Collections.ArrayList list)
            {
                foreach (var item in list)
                {
                    var text = Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(text)) listOut.Add(text);
                }
            }
            return listOut;
        }

        private Dictionary<string, object> ConfigDict(string key)
        {
            if (config.ContainsKey(key) && config[key] is Dictionary<string, object> dict)
            {
                return dict;
            }
            return new Dictionary<string, object>();
        }

        private void SaveConfig(Dictionary<string, object> data)
        {
            data["modsPath"] = modsDir;
            File.WriteAllText(configPath, json.Serialize(data), Encoding.UTF8);
        }

        // ---------------------------------------------------------- UI BUILD

        private void BuildUi()
        {
            Text = Program.AppDisplayName;
            Width = Convert.ToInt32(config["windowWidth"]);
            Height = Convert.ToInt32(config["windowHeight"]);
            MinimumSize = new Size(1080, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(8, 9, 7);
            ForeColor = Color.FromArgb(244, 234, 209);
            DoubleBuffered = true;
            Padding = new Padding(8, 8, 8, 8);

            bottomBar = BuildBottomBar();
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 66;
            bottomBar.Margin = new Padding(0, 6, 0, 0);
            Controls.Add(bottomBar);

            var bottomGap = new Panel { Dock = DockStyle.Bottom, Height = 6, BackColor = Color.Transparent };
            Controls.Add(bottomGap);

            topBar = BuildTopBar();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 72;
            Controls.Add(topBar);

            var topGap = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent };
            Controls.Add(topGap);

            var mainGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 348));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(mainGrid);
            mainGrid.BringToFront();

            installPanel = BuildInstallPanel();
            installPanel.Margin = new Padding(0, 0, 10, 0);
            installPanel.Dock = DockStyle.Fill;
            mainGrid.Controls.Add(installPanel, 0, 0);

            workspacePanel = BuildWorkspacePanel();
            workspacePanel.Margin = new Padding(10, 0, 10, 0);
            workspacePanel.Dock = DockStyle.Fill;
            mainGrid.Controls.Add(workspacePanel, 1, 0);

            inspectorPanel = BuildInspectorPanel();
            inspectorPanel.Margin = new Padding(10, 0, 0, 0);
            inspectorPanel.Dock = DockStyle.Fill;
            mainGrid.Controls.Add(inspectorPanel, 2, 0);
        }

        private RoundedPanel BuildTopBar()
        {
            var bar = new RoundedPanel { CornerRadius = 14, BorderWidth = 1, Padding = new Padding(14, 8, 14, 8) };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 530));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bar.Controls.Add(grid);

            var brandRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var mark = new BrandMark { Width = 48, Height = 48, Anchor = AnchorStyles.Left, Margin = new Padding(0, 2, 10, 2) };
            brandRow.Controls.Add(mark, 0, 0);

            var titleStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            titleStack.Controls.Add(new Label
            {
                Text = Program.AppDisplayName,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Trebuchet MS", 15.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 234, 209),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 1, 0, 0)
            }, 0, 0);
            titleStack.Controls.Add(new Label
            {
                Text = "Crimson Desert - overlay-safe patching, byte-guard validation, Nexus integration",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
            titleStack.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            titleStack.Height = 46;
            titleStack.Margin = new Padding(0, 2, 0, 2);
            brandRow.RowStyles.Clear();
            brandRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            brandRow.Controls.Add(titleStack, 1, 0);
            grid.Controls.Add(brandRow, 0, 0);

            var pillRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 6, 0, 0)
            };
            pillRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            pillRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
            pillRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            pillRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            pillRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            pillRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            statusGamePill = new Pill { Text = "Game", DotColor = Color.FromArgb(216, 166, 64), Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            statusModsPill = new Pill { Text = "0 active", DotColor = Color.FromArgb(216, 166, 64), Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            statusBuildPill = new Pill { Text = "v" + Program.AppVersion, Cursor = Cursors.Hand, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            tipsHost.SetToolTip(statusBuildPill, "Application version. Click to check for updates.");
            statusBuildPill.Click += (s, e) => CheckForUpdatesInteractive();
            pillRow.Controls.Add(statusGamePill, 0, 0);
            pillRow.Controls.Add(statusModsPill, 1, 0);
            pillRow.Controls.Add(statusBuildPill, 2, 0);

            updatePill = new Pill { Text = "Up to date", DotColor = Color.FromArgb(101, 197, 134), Visible = false, Cursor = Cursors.Hand };
            tipsHost.SetToolTip(updatePill, "Update available - click to open the UJMM Nexus files page.");
            updatePill.Click += (s, e) => CheckForUpdatesInteractive();

            var donateBtn = new GradientButton
            {
                Text = "\u2665 Coffee",
                Kind = GradientButton.Style.Donate,
                Dock = DockStyle.Fill,
                Height = 30,
                Margin = new Padding(0, 0, 6, 0)
            };
            donateBtn.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(Program.DonateUrl) { UseShellExecute = true }); }
                catch (Exception ex) { Log("Could not open donate link: " + ex.Message); }
            };
            pillRow.Controls.Add(donateBtn, 3, 0);

            var reportBtn = new GradientButton
            {
                Text = "Report",
                Kind = GradientButton.Style.Default,
                Dock = DockStyle.Fill,
                Height = 30,
                Margin = new Padding(0)
            };
            reportBtn.Click += (s, e) => CrashReporter.OpenManualReport(this);
            pillRow.Controls.Add(reportBtn, 4, 0);

            grid.Controls.Add(pillRow, 1, 0);

            return bar;
        }

        private RoundedPanel BuildBottomBar()
        {
            var bar = new RoundedPanel { CornerRadius = 12, BorderWidth = 1, Padding = new Padding(10, 8, 10, 8) };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bar.Controls.Add(grid);

            summaryLabel = new Label
            {
                Text = "Preview: 0 active mods, no game files touched.",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            grid.Controls.Add(summaryLabel, 0, 0);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0)
            };

            var backup = NewGradientButton("Backup", GradientButton.Style.Safe, 96, CreateFullBackup);
            tipsHost.SetToolTip(backup, "Save the current game state as the revert point. Backs up meta\\0.papgt, 0008\\0.pamt, and the byte lengths of each .paz file. Run this after a Crimson Desert update (or after Steam -> Verify Integrity of Game Files) so 'Restore Backup' has fresh backups to restore from.");
            var dryRun = NewGradientButton("Check", GradientButton.Style.Default, 86, RunValidation);
            tipsHost.SetToolTip(dryRun, "Check that selected mods match the current game version (no changes applied). Confirms each patch's 'original' bytes match what's actually in your installed Crimson Desert. Useful after a Steam game update.");
            var apply = NewGradientButton("Apply", GradientButton.Style.Primary, 92, ApplyOverlayStub);
            tipsHost.SetToolTip(apply, "Apply selected mods. Modded bytes are appended to the .paz archive (original data never overwritten) and the .pamt index is patched to point at them. Click 'Restore Backup' to fully revert.");
            var uninstall = NewGradientButton("Restore", GradientButton.Style.Danger, 96, DisableAllMods);
            tipsHost.SetToolTip(uninstall, "Restore the game to your saved backup state: restores the .pamt and truncates each .paz back to its recorded length. Uses the backup created by 'Create Backup' (or, if you skipped that step, the one auto-created by Apply Mods).");
            actions.Controls.Add(backup);
            actions.Controls.Add(dryRun);
            actions.Controls.Add(apply);
            actions.Controls.Add(uninstall);
            grid.Controls.Add(actions, 1, 0);

            return bar;
        }

        private RoundedPanel BuildInstallPanel()
        {
            var panel = new RoundedPanel { CornerRadius = 22, BorderWidth = 1 };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(layout);

            layout.Controls.Add(BuildPanelHeader("INSTALL", "Steam"), 0, 0);
            layout.Controls.Add(BuildDropZone(), 0, 1);
            layout.Controls.Add(BuildInstallToolsRow(), 0, 2);

            // BufferedScrollPanel double-buffers the install body so scrolling the mod card list
            // doesn't flicker / leave brief paint artefacts.
            var body = new BufferedScrollPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 0, 14, 10),
                AutoScroll = true
            };
            layout.Controls.Add(body, 0, 3);

            modCardsHost = new BufferedFlowPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 8, 0, 0)
            };
            body.Controls.Add(modCardsHost);

            return panel;
        }

        private RoundedPanel BuildPathCard()
        {
            var card = new RoundedPanel
            {
                CornerRadius = 16,
                BorderWidth = 1,
                AutoSize = false,
                Height = 158,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(14, 12, 14, 14)
            };
            card.GradientTopOverride = Color.FromArgb(40, 0, 0, 0);
            card.GradientBottomOverride = Color.FromArgb(70, 0, 0, 0);
            card.Dock = DockStyle.Top;

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(stack);

            stack.Controls.Add(new Label
            {
                Text = "GAME FOLDER",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(112, 104, 79),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);

            gamePathText = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Multiline = false,
                Font = new Font("Consolas", 9f),
                BackColor = Color.FromArgb(20, 21, 14),
                ForeColor = Color.FromArgb(244, 234, 209),
                Margin = new Padding(0, 4, 0, 8)
            };
            stack.Controls.Add(gamePathText, 0, 1);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 6, 0, 0)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            var browseBtn = NewGradientButton("Browse", GradientButton.Style.Default, 0, BrowseGameFolder);
            tipsHost.SetToolTip(browseBtn, "Pick the Crimson Desert folder (the one that contains bin64 and 0008).");
            var detectBtn = NewGradientButton("Detect", GradientButton.Style.Default, 0, DetectGameFolderClicked);
            tipsHost.SetToolTip(detectBtn, "Auto-detect Crimson Desert in common Steam locations.");
            var saveBtn = NewGradientButton("Save", GradientButton.Style.Default, 0, SaveGamePath);
            tipsHost.SetToolTip(saveBtn, "Save the path above. Browse and Detect save automatically.");
            buttons.Controls.Add(browseBtn, 0, 0);
            buttons.Controls.Add(detectBtn, 1, 0);
            buttons.Controls.Add(saveBtn, 2, 0);
            stack.Controls.Add(buttons, 0, 2);

            return card;
        }

        private void DetectGameFolderClicked()
        {
            var found = DetectGameFolder();
            if (string.IsNullOrEmpty(found))
            {
                MessageBox.Show("Could not find Crimson Desert in any standard Steam path. Click Browse and pick the folder manually.", "Not detected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            gamePathText.Text = found;
            SaveGamePath();
        }

        private FlowLayoutPanel BuildThemeSwatches()
        {
            themeSwatchHost = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true, // wrap to a second row on narrow panels so all 5 swatches stay reachable
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 12)
            };

            themeSwatchHost.Controls.Add(MakeSwatch(Theme.Gilded()));
            themeSwatchHost.Controls.Add(MakeSwatch(Theme.Ember()));
            themeSwatchHost.Controls.Add(MakeSwatch(Theme.Frost()));
            themeSwatchHost.Controls.Add(MakeSwatch(Theme.Forest()));
            themeSwatchHost.Controls.Add(MakeCustomSwatch());
            return themeSwatchHost;
        }

        private ThemeSwatch MakeSwatch(Theme theme)
        {
            var sw = new ThemeSwatch(theme)
            {
                Width = 50,
                Height = 44,
                Margin = new Padding(0, 0, 6, 6),
                Cursor = Cursors.Hand
            };
            var tooltip = new ToolTip();
            tooltip.SetToolTip(sw, theme.Name);
            sw.Click += (sender, args) => ApplyTheme(theme);
            return sw;
        }

        // Custom swatch: shows the saved custom colour (or a faint placeholder pattern if none yet).
        // Click -> opens ColorDialog; selection saves to config and applies immediately.
        private ThemeSwatch MakeCustomSwatch()
        {
            var saved = ConfigString("customAccent");
            bool hasSaved = false;
            Theme baseTheme = Theme.Custom(Color.FromArgb(216, 166, 64)); // fallback if parse fails
            if (!string.IsNullOrEmpty(saved))
            {
                try { baseTheme = Theme.Custom(ColorTranslator.FromHtml(saved)); hasSaved = true; } catch { }
            }
            var sw = new ThemeSwatch(baseTheme)
            {
                Width = 50,
                Height = 44,
                Margin = new Padding(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                // Render as a dashed "+" placeholder until the user picks a colour for the first time.
                IsEmptyPlaceholder = !hasSaved
            };
            tipsHost.SetToolTip(sw, "Custom theme - click to pick your own accent colour. Saved across launches.");
            sw.Click += (sender, args) =>
            {
                using (var cd = new ColorDialog { FullOpen = true, AnyColor = true })
                {
                    var current = ConfigString("customAccent");
                    if (!string.IsNullOrEmpty(current))
                    {
                        try { cd.Color = ColorTranslator.FromHtml(current); } catch { }
                    }
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        var hex = "#" + cd.Color.R.ToString("X2") + cd.Color.G.ToString("X2") + cd.Color.B.ToString("X2");
                        config["customAccent"] = hex;
                        SaveConfig(config);
                        // ApplyTheme re-renders the custom swatch (with the new saved colour or the placeholder).
                        ApplyTheme(Theme.Custom(cd.Color));
                    }
                }
            };
            return sw;
        }

        private void ShowSettingsDialog()
        {
            using (var dialog = new Form
            {
                Text = "UJMM Settings",
                StartPosition = FormStartPosition.CenterParent,
                Width = 520,
                Height = 360,
                MinimizeBox = false,
                MaximizeBox = false,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = currentTheme.Panel,
                ForeColor = currentTheme.Text,
                Padding = new Padding(14)
            })
            {
                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 4,
                    BackColor = Color.Transparent
                };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                dialog.Controls.Add(layout);

                layout.Controls.Add(new Label
                {
                    Text = "SETTINGS",
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 10, FontStyle.Bold),
                    ForeColor = currentTheme.Accent2,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft
                }, 0, 0);

                var pathCard = BuildPathCard();
                pathCard.Margin = new Padding(0, 0, 0, 10);
                layout.Controls.Add(pathCard, 0, 1);

                var swatches = BuildThemeSwatches();
                swatches.Margin = new Padding(0, 4, 0, 8);
                layout.Controls.Add(swatches, 0, 2);

                var close = NewGradientButton("Close", GradientButton.Style.Primary, 100, () => dialog.Close());
                close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
                var closeHost = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    BackColor = Color.Transparent
                };
                closeHost.Controls.Add(close);
                layout.Controls.Add(closeHost, 0, 3);

                if (gamePathText != null) gamePathText.Text = gamePath ?? "";
                dialog.ShowDialog(this);
                gamePathText = null;
                themeSwatchHost = null;
            }
        }

        private RoundedPanel BuildDropZone()
        {
            var panel = new RoundedPanel
            {
                CornerRadius = 0,
                BorderWidth = 1,
                AutoSize = false,
                Height = 84,
                Margin = new Padding(12, 8, 12, 6),
                Padding = new Padding(12, 8, 12, 8),
                AllowDrop = true,
                Dashed = true
            };
            panel.GradientTopOverride = Color.FromArgb(28, 216, 166, 64);
            panel.GradientBottomOverride = Color.FromArgb(36, 0, 0, 0);
            panel.Dock = DockStyle.Top;

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.Controls.Add(stack);

            dropTitle = new Label
            {
                Text = "Drop or click to import",
                Dock = DockStyle.Fill,
                Font = new Font("Trebuchet MS", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 234, 209),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomCenter
            };
            stack.Controls.Add(dropTitle, 0, 0);

            dropHint = new Label
            {
                Text = "Files: JSON/FIELDS, ZIP/7Z/RAR, ASI/DLL/INI. Folders: RAW, Browser/UI.",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 7.5f),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopCenter
            };
            stack.Controls.Add(dropHint, 0, 1);

            panel.DragEnter += DropZoneDragEnter;
            panel.DragOver += DropZoneDragEnter;
            panel.DragLeave += (sender, args) => { panel.PulseAccent = false; panel.Invalidate(); };
            panel.DragDrop += DropZoneDragDrop;

            // Click anywhere on the drop zone to browse for either files or package folders.
            panel.Click += (sender, args) => ShowImportMenu(panel);
            dropTitle.Click += (sender, args) => ShowImportMenu(panel);
            dropHint.Click += (sender, args) => ShowImportMenu(panel);
            stack.Click += (sender, args) => ShowImportMenu(panel);

            dropZone = panel;
            return panel;
        }

        private Control BuildInstallToolsRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 4, 14, 4)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));

            var game = IsGameFolder(gamePath) ? ShortPathLabel(gamePath) : "Game folder missing";
            var gameLabel = new Label
            {
                Text = game,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Consolas", 8),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            tipsHost.SetToolTip(gameLabel, gamePath ?? "");
            row.Controls.Add(gameLabel, 0, 0);

            var settings = NewGradientButton("Settings", GradientButton.Style.Default, 100, ShowSettingsDialog);
            settings.Height = 30;
            tipsHost.SetToolTip(settings, "Game folder and color theme.");
            row.Controls.Add(settings, 1, 0);

            var refresh = NewGradientButton("Refresh", GradientButton.Style.Default, 100, RefreshModsFromDisk);
            refresh.Height = 30;
            tipsHost.SetToolTip(refresh, "Reload the mods folder after editing JSON files or copying files manually into mods/.");
            row.Controls.Add(refresh, 2, 0);

            return row;
        }

        private string ShortPathLabel(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) return path;
            return "Game: " + name;
        }

        private Control BuildRefreshModsRow()
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 8, 0, 0)
            };
            var refresh = NewGradientButton("Refresh Mods", GradientButton.Style.Default, 128, RefreshModsFromDisk);
            refresh.Height = 30;
            refresh.Margin = new Padding(0);
            tipsHost.SetToolTip(refresh, "Reload the mods folder after editing JSON files or copying files manually into mods/.");
            row.Controls.Add(refresh);
            return row;
        }

        private RoundedPanel BuildWorkspacePanel()
        {
            var panel = new RoundedPanel { CornerRadius = 22, BorderWidth = 1 };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(layout);

            layout.Controls.Add(BuildPanelHeader("PATCH BOARD", "0 enabled", out workspaceCounter), 0, 0);

            tabs = new DarkTabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.FlatButtons,
                SizeMode = TabSizeMode.Normal,
                ItemSize = new Size(120, 30),
                Padding = new Point(14, 6),
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Margin = new Padding(12, 8, 12, 12),
                BackColor = Color.FromArgb(14, 15, 11),
                StripColor = Color.FromArgb(14, 15, 11)
            };
            tabs.DrawItem += DrawTabItem;

            var jsonTab = new TabPage("Presets") { BackColor = Color.FromArgb(14, 15, 11) };
            BuildJsonTab(jsonTab);
            tabs.TabPages.Add(jsonTab);

            var asiTab = new TabPage("ASI Mods") { BackColor = Color.FromArgb(14, 15, 11) };
            BuildAsiTab(asiTab);
            tabs.TabPages.Add(asiTab);

            var nexusTab = new TabPage("Nexus") { BackColor = Color.FromArgb(14, 15, 11) };
            BuildNexusTab(nexusTab);
            tabs.TabPages.Add(nexusTab);

            tabs.TabPages.Add(BuildPlaceholderTab("Conflicts", "Conflict detection ships in a later phase.\r\nWhen multiple enabled mods touch the same byte, this view will show the clash and let you pick the winner."));
            tabs.TabPages.Add(BuildPlaceholderTab("Files", "Files browser ships in a later phase.\r\nThis view will list every game file currently extracted into .cache/extracted/ for inspection."));

            var tabsHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 8), BackColor = Color.Transparent };
            tabsHost.Controls.Add(tabs);
            layout.Controls.Add(tabsHost, 0, 1);

            return panel;
        }

        private void BuildJsonTab(TabPage tab)
        {
            tab.Padding = new Padding(8, 6, 8, 8);
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tab.Controls.Add(grid);

            var railOuter = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(2, 2, 10, 2),
                AutoScroll = true
            };
            presetRailHost = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent
            };
            railOuter.Controls.Add(presetRailHost);
            grid.Controls.Add(railOuter, 0, 0);

            var listHost = new RoundedPanel
            {
                CornerRadius = 16,
                BorderWidth = 1,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 8, 8, 8)
            };
            listHost.GradientTopOverride = Color.FromArgb(180, 0, 0, 0);
            listHost.GradientBottomOverride = Color.FromArgb(220, 0, 0, 0);
            listHost.BorderColor = Color.FromArgb(36, 255, 255, 255);

            var listLayoutInner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            listLayoutInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            listLayoutInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            listHost.Controls.Add(listLayoutInner);

            // Top row: [search box] [All On] [All Off] - "All On"/"All Off" bulk-toggle every patch
            // currently visible in the list (so they respect the active search filter).
            var topRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(2, 2, 2, 6)
            };
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            patchSearchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9),
                BackColor = Color.FromArgb(20, 21, 14),
                ForeColor = Color.FromArgb(244, 234, 209)
            };
            // Placeholder via a Label overlay (TextBox lacks native placeholder support in .NET FW).
            var ph = new Label
            {
                Text = "  Search patches...",
                Dock = DockStyle.Fill,
                Font = patchSearchBox.Font,
                ForeColor = Color.FromArgb(112, 104, 79),
                BackColor = patchSearchBox.BackColor,
                TextAlign = ContentAlignment.MiddleLeft
            };
            ph.Click += (s, e) => patchSearchBox.Focus();
            patchSearchBox.GotFocus += (s, e) => ph.Visible = false;
            patchSearchBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(patchSearchBox.Text)) ph.Visible = true; };
            patchSearchBox.TextChanged += (s, e) => RefreshPatchList();
            var searchHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0, 0, 6, 0) };
            searchHost.Controls.Add(patchSearchBox);
            searchHost.Controls.Add(ph);
            ph.BringToFront();
            topRow.Controls.Add(searchHost, 0, 0);

            var allOnBtn = NewGradientButton("All On", GradientButton.Style.Safe, 70, () => BulkTogglePatches(true));
            allOnBtn.Height = 28;
            allOnBtn.Margin = new Padding(0, 0, 4, 0);
            tipsHost.SetToolTip(allOnBtn, "Enable every patch currently shown in the list (respects the search filter).");
            topRow.Controls.Add(allOnBtn, 1, 0);

            var allOffBtn = NewGradientButton("All Off", GradientButton.Style.Default, 70, () => BulkTogglePatches(false));
            allOffBtn.Height = 28;
            allOffBtn.Margin = new Padding(0);
            tipsHost.SetToolTip(allOffBtn, "Disable every patch currently shown in the list (respects the search filter). Combine with the search box: 'All Off', then search for the few you want and tick them.");
            topRow.Controls.Add(allOffBtn, 2, 0);

            listLayoutInner.Controls.Add(topRow, 0, 0);

            patchList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                CheckBoxes = true,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(12, 13, 10),
                ForeColor = Color.FromArgb(244, 234, 209)
            };
            patchList.Columns.Add("Patch", 200);
            patchList.Columns.Add("Target", 200);
            patchList.SizeChanged += (s, e) => FitListColumns(patchList, new[] { 0.50f, 0.50f });
            patchList.ItemChecked += PatchList_ItemChecked;
            listLayoutInner.Controls.Add(patchList, 0, 1);
            grid.Controls.Add(listHost, 1, 0);
        }

        // Tracks patches the user disabled per-mod. Key: mod filename + "|" + change index.
        private readonly HashSet<string> disabledPatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool patchListSyncing = false;
        private TextBox patchSearchBox;

        // Key per-patch state by (mod, group, index). Group is part of the key so disabling a patch
        // under preset 0% no longer also disables the corresponding patch under 5%/10%/etc.
        private static string PatchKey(JsonMod mod, string group, int changeIndex)
        {
            return Path.GetFileName(mod.Path) + "|" + (group ?? "") + "|" + changeIndex;
        }

        // Flip every currently-visible patch to enabled/disabled in one batch (no per-row config saves).
        private void BulkTogglePatches(bool enable)
        {
            if (patchList == null || patchList.Items.Count == 0) return;
            patchListSyncing = true;
            patchList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in patchList.Items)
                {
                    var key = item.Tag as string;
                    if (string.IsNullOrEmpty(key)) continue;
                    if (enable) disabledPatches.Remove(key);
                    else disabledPatches.Add(key);
                    item.Checked = enable;
                }
            }
            finally
            {
                patchList.EndUpdate();
                patchListSyncing = false;
            }
            config["disabledPatches"] = disabledPatches.ToArray();
            SaveConfig(config);
            UpdateBottomSummary();
            // Refresh counter line in the panel header.
            if (workspaceCounter != null)
            {
                int total = 0; foreach (var mod in mods) total += mod.Changes.Count;
                int active = 0;
                foreach (var mod in mods)
                {
                    if (!activeBoxes.ContainsKey(mod.Path) || !activeBoxes[mod.Path].Checked) continue;
                    var grp = GroupFor(mod);
                    int idx = 0;
                    foreach (var c in mod.ChangesForGroup(grp))
                    {
                        if (!disabledPatches.Contains(PatchKey(mod, grp, idx))) active++;
                        idx++;
                    }
                }
                workspaceCounter.Text = active + " / " + total + " will apply";
            }
        }

        private void PatchList_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (patchListSyncing) return;
            var item = e.Item;
            if (item == null || item.Tag == null) return;
            var key = item.Tag as string;
            if (string.IsNullOrEmpty(key)) return;
            if (item.Checked) disabledPatches.Remove(key);
            else disabledPatches.Add(key);
            // Persist immediately to config.
            config["disabledPatches"] = disabledPatches.ToArray();
            SaveConfig(config);
            UpdateBottomSummary();
        }

        private void FitListColumns(ListView list, float[] weights)
        {
            int total = list.ClientSize.Width;
            if (total <= 0 || list.Columns.Count == 0) return;
            int sb = SystemInformation.VerticalScrollBarWidth + 2;
            int avail = Math.Max(0, total - sb);
            for (int i = 0; i < list.Columns.Count && i < weights.Length; i++)
            {
                list.Columns[i].Width = Math.Max(40, (int)(avail * weights[i]));
            }
        }

        private void BuildAsiTab(TabPage tab)
        {
            tab.Padding = new Padding(8, 6, 8, 8);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tab.Controls.Add(layout);

            var cardsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            cardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            cardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            cardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            cardsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            cardsRow.Controls.Add(BuildAsiCard("ASI Loader",
                "Detects Ultimate ASI Loader in bin64. Install or remove the loader separately from mods.",
                ("Detect", GradientButton.Style.Safe, (Action)RefreshAsi),
                ("Open bin64", GradientButton.Style.Default, (Action)(() => { if (!string.IsNullOrEmpty(gamePath)) OpenFolder(Path.Combine(gamePath, "bin64")); }))
            ), 0, 0);

            cardsRow.Controls.Add(BuildAsiCard("Drop ASI Mods",
                "Accepts .asi, .dll, .ini files. Each runtime mod stays in a managed list with enable / disable.",
                ("Add ASI", GradientButton.Style.Default, (Action)AddAsiFiles),
                ("Toggle Selected", GradientButton.Style.Default, (Action)ToggleSelectedAsi)
            ), 1, 0);

            cardsRow.Controls.Add(BuildAsiCard("Runtime Safety",
                "Lists files that load at startup, config files beside them, and warns when duplicate loaders are detected.",
                ("Scan", GradientButton.Style.Default, (Action)RefreshAsi),
                ("Disable All", GradientButton.Style.Danger, (Action)DisableAllAsi)
            ), 2, 0);

            layout.Controls.Add(cardsRow, 0, 0);

            var listHost = new RoundedPanel
            {
                CornerRadius = 16,
                BorderWidth = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0)
            };
            listHost.GradientTopOverride = Color.FromArgb(180, 0, 0, 0);
            listHost.GradientBottomOverride = Color.FromArgb(220, 0, 0, 0);
            listHost.BorderColor = Color.FromArgb(36, 255, 255, 255);

            var listLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            listLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            listHost.Controls.Add(listLayout);

            loaderLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Consolas", 9),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(169, 157, 124),
                Padding = new Padding(12, 0, 12, 0)
            };
            listLayout.Controls.Add(loaderLabel, 0, 0);

            asiList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(12, 13, 10),
                ForeColor = Color.FromArgb(244, 234, 209)
            };
            asiList.Columns.Add("File", 380);
            asiList.Columns.Add("Status", 130);
            asiList.Columns.Add("Size", 100);
            asiList.SizeChanged += (s, e) => FitListColumns(asiList, new[] { 0.6f, 0.25f, 0.15f });
            listLayout.Controls.Add(asiList, 0, 1);

            layout.Controls.Add(listHost, 0, 1);
        }

        private RoundedPanel BuildAsiCard(string title, string body, params (string Label, GradientButton.Style Style, Action OnClick)[] actions)
        {
            var card = new RoundedPanel
            {
                CornerRadius = 16,
                BorderWidth = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 6, 0),
                Padding = new Padding(14, 12, 14, 12)
            };
            card.GradientTopOverride = Color.FromArgb(80, 0, 0, 0);
            card.GradientBottomOverride = Color.FromArgb(140, 0, 0, 0);
            card.BorderColor = Color.FromArgb(36, 255, 255, 255);

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            card.Controls.Add(stack);

            stack.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = new Font("Trebuchet MS", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 234, 209),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            stack.Controls.Add(new Label
            {
                Text = body,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);

            var btns = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0)
            };
            foreach (var a in actions)
            {
                btns.Controls.Add(NewGradientButton(a.Label, a.Style, 0, a.OnClick));
            }
            stack.Controls.Add(btns, 0, 2);

            return card;
        }

        private void DisableAllAsi()
        {
            if (!IsGameFolder(gamePath))
            {
                MessageBox.Show("Set the Crimson Desert folder first.", "Game folder missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var bin64 = Path.Combine(gamePath, "bin64");
            int n = 0;
            foreach (var f in Directory.GetFiles(bin64, "*.asi"))
            {
                File.Move(f, f + ".disabled");
                n++;
            }
            RefreshAsi();
            Log("Disabled " + n + " .asi file(s).");
        }

        private TabPage BuildPlaceholderTab(string name, string body)
        {
            var page = new TabPage(name) { BackColor = Color.FromArgb(14, 15, 11), Padding = new Padding(24) };
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = body,
                Font = new Font("Consolas", 10.5f),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            page.Controls.Add(label);
            return page;
        }

        private void BuildNexusTab(TabPage tab)
        {
            tab.Padding = new Padding(14, 10, 14, 10);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116)); // status card
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));  // actions row
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // feed header
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // feed
            tab.Controls.Add(layout);

            // Status card
            var statusCard = new RoundedPanel
            {
                CornerRadius = 16,
                BorderWidth = 1,
                Padding = new Padding(18, 12, 18, 12),
                Margin = new Padding(0, 0, 0, 8),
                Dock = DockStyle.Fill
            };
            statusCard.GradientTopOverride = Color.FromArgb(80, 0, 0, 0);
            statusCard.GradientBottomOverride = Color.FromArgb(140, 0, 0, 0);
            statusCard.BorderColor = Color.FromArgb(36, 255, 255, 255);

            var statusInner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            statusInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusInner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            statusInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusCard.Controls.Add(statusInner);

            statusInner.Controls.Add(new Label
            {
                Text = "NEXUS MODS",
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            nexusStatusPill = new Pill { Text = "Not connected", DotColor = Color.FromArgb(216, 92, 76), Anchor = AnchorStyles.Right, Margin = new Padding(0, 4, 0, 8) };
            statusInner.Controls.Add(nexusStatusPill, 1, 0);

            nexusUserLabel = new Label
            {
                Text = "Sign in with your Nexus account to enable one-click downloads from any Crimson Desert mod page and update detection for installed mods.",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopLeft
            };
            statusInner.SetColumnSpan(nexusUserLabel, 2);
            statusInner.Controls.Add(nexusUserLabel, 0, 1);

            layout.Controls.Add(statusCard, 0, 0);

            // SSO is the primary sign-in path. Keep this block scroll-free so fallback controls stay visible.
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Padding = new Padding(0, 6, 0, 6)
            };

            nexusConnectButton = new GradientButton
            {
                Text = "Sign in with Nexus",
                Kind = GradientButton.Style.Primary,
                Width = 134,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0)
            };
            tipsHost.SetToolTip(nexusConnectButton, "One-click sign-in via your browser. No password or key required - just approve the app on nexusmods.com.");
            nexusConnectButton.Click += (s, e) => OnNexusConnectClick();
            actions.Controls.Add(nexusConnectButton);

            var openBtn = NewGradientButton("Browse Mods", GradientButton.Style.Default, 126, () => SafeOpenUrl("https://www.nexusmods.com/" + Program.NexusGameDomain));
            openBtn.Height = 32;
            tipsHost.SetToolTip(openBtn, "Open the Crimson Desert mods page on Nexus in your browser.");
            actions.Controls.Add(openBtn);

            var refreshBtn = NewGradientButton("Refresh", GradientButton.Style.Default, 88, RefreshNexusFeed);
            refreshBtn.Height = 32;
            tipsHost.SetToolTip(refreshBtn, "Reload the recently-updated mods list from Nexus.");
            actions.Controls.Add(refreshBtn);

            nexusRateLabel = new Label
            {
                Text = "",
                AutoSize = true,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(112, 104, 79),
                BackColor = Color.Transparent,
                Padding = new Padding(6, 9, 0, 0),
                Margin = new Padding(0, 0, 8, 0)
            };
            actions.Controls.Add(nexusRateLabel);

            var devLink = new Label
            {
                Text = "API key",
                AutoSize = true,
                Font = new Font("Consolas", 8.5f, FontStyle.Underline),
                ForeColor = Color.FromArgb(110, 100, 75),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Padding(0, 10, 0, 0)
            };
            tipsHost.SetToolTip(devLink, "Developer/testing only - manually paste a Nexus API key. Regular users sign in with the button above.");
            devLink.Click += (s, e) => PromptPasteApiKey();
            actions.Controls.Add(devLink);

            layout.Controls.Add(actions, 0, 1);

            // Feed header
            layout.Controls.Add(new Label
            {
                Text = "RECENTLY UPDATED ON CRIMSON DESERT",
                Font = new Font("Consolas", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(2, 0, 0, 4)
            }, 0, 2);

            // Feed scroller
            var feedScroller = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                AutoScroll = true,
                Padding = new Padding(0, 4, 8, 4)
            };
            nexusFeedHost = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent
            };
            feedScroller.Controls.Add(nexusFeedHost);
            layout.Controls.Add(feedScroller, 0, 3);
        }

        private void RefreshNexusStatus()
        {
            var connected = !string.IsNullOrEmpty(nexusApiKey);
            if (nexusStatusPill != null)
            {
                nexusStatusPill.Text = connected ? "Connected" : "Not connected";
                nexusStatusPill.DotColor = connected ? Color.FromArgb(101, 197, 134) : Color.FromArgb(216, 92, 76);
            }
            if (nexusConnectButton != null)
            {
                nexusConnectButton.Text = connected ? "Sign out" : "Sign in with Nexus";
                nexusConnectButton.Kind = connected ? GradientButton.Style.Default : GradientButton.Style.Primary;
                nexusConnectButton.Invalidate();
            }
            if (nexusUserLabel != null)
            {
                if (connected)
                {
                    var status = nexusIsPremium ? "Premium" : "Standard";
                    nexusUserLabel.Text = "Signed in as " + (string.IsNullOrEmpty(nexusUserName) ? "your Nexus account" : nexusUserName) + " - " + status + ".\r\nYou can now click 'Mod Manager Download' on any Crimson Desert mod page and the file lands here automatically.";
                }
                else
                {
                    nexusUserLabel.Text = "Sign in with your Nexus account to enable one-click downloads from any Crimson Desert mod page (the 'Mod Manager Download' button) and automatic update detection.\r\nThe sign-in flow opens your browser - no password or key required, just one click.";
                }
            }
        }

        private void OnNexusConnectClick()
        {
            if (!string.IsNullOrEmpty(nexusApiKey))
            {
                var ans = MessageBox.Show("Disconnect from Nexus? You can reconnect any time.", "Disconnect", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;
                nexusApiKey = "";
                nexusUserName = "";
                nexusIsPremium = false;
                config["nexusApiKey"] = "";
                config["nexusUserName"] = "";
                config["nexusIsPremium"] = false;
                SaveConfig(config);
                RefreshNexusStatus();
                Log("Disconnected from Nexus.");
                return;
            }
            ConnectViaSso();
        }

        private void ConnectViaSso()
        {
            using (var dlg = new NexusSsoDialog())
            {
                dlg.ShowDialog(this);
                if (dlg.Result == DialogResult.OK && !string.IsNullOrEmpty(dlg.ApiKey))
                {
                    SaveAndValidateApiKey(dlg.ApiKey);
                }
                else if (!string.IsNullOrEmpty(dlg.ErrorMessage))
                {
                    var err = dlg.ErrorMessage ?? "";
                    var lower = err.ToLowerInvariant();
                    if (lower.Contains("application id was invalid") || lower.Contains("application id"))
                    {
                        MessageBox.Show(
                            "Nexus sign-in is not yet enabled for this app.\r\n\r\n" +
                            "We're waiting for Nexus Mods to register Ultimate JSON Mod Manager so the browser sign-in can work. " +
                            "Once it's approved, this button will Just Work - no key pasting needed.\r\n\r\n" +
                            "In the meantime you can still browse mods on Nexus normally.",
                            "Sign-in coming soon",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Sign-in did not complete: " + err, "Sign-in failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void PromptPasteApiKey()
        {
            using (var dlg = new ApiKeyPasteDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.ApiKey))
                {
                    SaveAndValidateApiKey(dlg.ApiKey.Trim());
                }
            }
        }

        private void SaveAndValidateApiKey(string key)
        {
            string error;
            var info = NexusClient.Validate(key, out error);
            if (info == null || !info.ContainsKey("name"))
            {
                MessageBox.Show("That key didn't validate against Nexus.\r\n\r\n" + error, "Invalid key", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            nexusApiKey = key;
            nexusUserName = Convert.ToString(info["name"]);
            nexusIsPremium = info.ContainsKey("is_premium") && info["is_premium"] is bool && (bool)info["is_premium"];
            config["nexusApiKey"] = key;
            config["nexusUserName"] = nexusUserName;
            config["nexusIsPremium"] = nexusIsPremium;
            SaveConfig(config);
            RefreshNexusStatus();
            RefreshNexusFeed();
            Log("Connected to Nexus as " + nexusUserName + (nexusIsPremium ? " (Premium)." : "."));
        }

        private void RefreshNexusFeed()
        {
            if (nexusFeedHost == null) return;
            nexusFeedHost.Controls.Clear();
            if (string.IsNullOrEmpty(nexusApiKey))
            {
                var hint = new Label
                {
                    Text = "Sign in to load the Nexus feed.",
                    Font = new Font("Consolas", 9),
                    ForeColor = Color.FromArgb(112, 104, 79),
                    BackColor = Color.Transparent,
                    Padding = new Padding(8, 12, 8, 8),
                    AutoSize = true
                };
                nexusFeedHost.Controls.Add(hint);
                return;
            }

            var loading = new Label
            {
                Text = "Loading...",
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                Padding = new Padding(8, 12, 8, 8),
                AutoSize = true
            };
            nexusFeedHost.Controls.Add(loading);

            var key = nexusApiKey;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string error;
                int hourlyRemaining = -1;
                int hourlyLimit = -1;
                var feed = NexusClient.LatestUpdated(key, Program.NexusGameDomain, out error, out hourlyRemaining, out hourlyLimit);
                if (IsDisposed || Disposing) return;
                BeginInvoke(new Action(() =>
                {
                    nexusFeedHost.Controls.Clear();
                    if (hourlyLimit > 0)
                    {
                        nexusRateLabel.Text = "Nexus rate: " + hourlyRemaining + "/" + hourlyLimit + " hourly";
                    }
                    if (feed == null)
                    {
                        var err = new Label
                        {
                            Text = "Could not load feed: " + (error ?? ""),
                            Font = new Font("Consolas", 9),
                            ForeColor = Color.FromArgb(216, 92, 76),
                            BackColor = Color.Transparent,
                            Padding = new Padding(8, 8, 8, 8),
                            AutoSize = true
                        };
                        nexusFeedHost.Controls.Add(err);
                        return;
                    }
                    foreach (var mod in feed.Take(15))
                    {
                        nexusFeedHost.Controls.Add(BuildNexusFeedCard(mod));
                    }
                }));
            });
        }

        private RoundedPanel BuildNexusFeedCard(Dictionary<string, object> mod)
        {
            int modId = 0;
            try { modId = Convert.ToInt32(mod["mod_id"]); } catch { }
            var name = Convert.ToString(mod.ContainsKey("name") ? mod["name"] : "");
            var summary = Convert.ToString(mod.ContainsKey("summary") ? mod["summary"] : "");
            var version = Convert.ToString(mod.ContainsKey("version") ? mod["version"] : "");
            int downloads = 0; try { downloads = Convert.ToInt32(mod["mod_downloads"]); } catch { }

            var card = new RoundedPanel
            {
                CornerRadius = 14,
                BorderWidth = 1,
                Width = Math.Max(420, nexusFeedHost.ClientSize.Width - 4),
                Height = 78,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(14, 10, 14, 10),
                Cursor = Cursors.Hand
            };
            card.GradientTopOverride = Color.FromArgb(56, 0, 0, 0);
            card.GradientBottomOverride = Color.FromArgb(80, 0, 0, 0);
            card.BorderColor = Color.FromArgb(36, 255, 255, 255);

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(stack);

            var titleRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var nameLabel = new Label
            {
                Text = name,
                Dock = DockStyle.Fill,
                Font = new Font("Trebuchet MS", 11f, FontStyle.Bold),
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            titleRow.Controls.Add(nameLabel, 0, 0);

            var meta = new Label
            {
                Text = "v" + version + " - " + downloads.ToString("N0") + " downloads",
                AutoSize = true,
                Font = new Font("Consolas", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(112, 104, 79),
                BackColor = Color.Transparent,
                Padding = new Padding(8, 4, 0, 0)
            };
            titleRow.Controls.Add(meta, 1, 0);
            stack.Controls.Add(titleRow, 0, 0);

            var sub = new Label
            {
                Text = summary,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            stack.Controls.Add(sub, 0, 1);

            var url = "https://www.nexusmods.com/" + Program.NexusGameDomain + "/mods/" + modId;
            EventHandler open = (s, e) => SafeOpenUrl(url);
            card.Click += open;
            stack.Click += open;
            titleRow.Click += open;
            nameLabel.Click += open;
            sub.Click += open;
            meta.Click += open;
            return card;
        }

        private void SafeOpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log("Could not open URL: " + ex.Message); }
        }

        // ---------------------------------------------------------- nxm:// handling

        protected override void WndProc(ref Message m)
        {
            const int WM_COPYDATA = 0x004A;
            if (m.Msg == WM_COPYDATA)
            {
                try
                {
                    var cds = (SingleInstance.COPYDATASTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(SingleInstance.COPYDATASTRUCT));
                    if (cds.dwData == (IntPtr)0x4E58 && cds.cbData > 0)
                    {
                        var bytes = new byte[cds.cbData];
                        System.Runtime.InteropServices.Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
                        var url = Encoding.UTF8.GetString(bytes);
                        BeginInvoke(new Action(() => HandleNxmUrl(url)));
                    }
                }
                catch (Exception ex) { Log("WM_COPYDATA error: " + ex.Message); }
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshNexusStatus();
            if (!string.IsNullOrEmpty(nexusApiKey)) RefreshNexusFeed();
            if (Environment.GetEnvironmentVariable("UJMM_DEFAULT_TAB") == "nexus" && tabs != null && tabs.TabPages.Count >= 3)
            {
                try { tabs.SelectedIndex = 2; } catch { }
            }
            if (!string.IsNullOrEmpty(pendingNxmUrl))
            {
                var url = pendingNxmUrl;
                pendingNxmUrl = null;
                BeginInvoke(new Action(() => HandleNxmUrl(url)));
            }
            CheckForUpdatesBackground();
            StartNexusUpdateTimer();
        }

        private UpdateChecker.ReleaseInfo cachedRelease;

        private void CheckForUpdatesBackground()
        {
            if (string.IsNullOrEmpty(nexusApiKey)) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string err;
                    var info = CheckLatestAppVersionFromNexus(out err);
                    if (info == null || string.IsNullOrEmpty(info.TagName)) return;
                    cachedRelease = info;
                    if (UpdateChecker.IsNewer(info.TagName, Program.AppVersion))
                    {
                        if (IsDisposed || Disposing) return;
                        BeginInvoke(new Action(() =>
                        {
                            if (updatePill != null)
                            {
                                updatePill.Text = "Update " + info.TagName.TrimStart('v', 'V') + " available";
                                updatePill.DotColor = Color.FromArgb(244, 199, 103);
                                updatePill.Visible = true;
                            }
                            Log("Update available: " + info.TagName + " (you're on " + Program.AppVersion + ")");
                        }));
                    }
                }
                catch { }
            });
        }

        private void CheckForUpdatesInteractive()
        {
            UpdateChecker.ReleaseInfo info = cachedRelease;
            string err = "";
            if (info == null) info = CheckLatestAppVersionFromNexus(out err);
            if (info == null || string.IsNullOrEmpty(info.TagName))
            {
                MessageBox.Show("No Nexus update info available.\r\n\r\n" + (string.IsNullOrEmpty(err) ? "Sign in with Nexus, or open the UJMM Nexus page manually." : err), "No update info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!UpdateChecker.IsNewer(info.TagName, Program.AppVersion))
            {
                MessageBox.Show("You're on the latest version (" + Program.AppVersion + ").", "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new UpdateDialog(info))
            {
                dlg.ShowDialog(this);
            }
        }

        private UpdateChecker.ReleaseInfo CheckLatestAppVersionFromNexus(out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(nexusApiKey))
            {
                error = "Nexus sign-in is required for automatic UJMM update detection.";
                return null;
            }

            var modInfo = NexusClient.GetMod(nexusApiKey, Program.NexusGameDomain, Program.NexusAppModId, out error);
            if (modInfo == null) return null;

            var version = modInfo.ContainsKey("version") ? Convert.ToString(modInfo["version"]) : "";
            var name = modInfo.ContainsKey("name") ? Convert.ToString(modInfo["name"]) : Program.AppDisplayName;
            var summary = modInfo.ContainsKey("summary") ? Convert.ToString(modInfo["summary"]) : "";
            return new UpdateChecker.ReleaseInfo
            {
                TagName = version,
                Title = string.IsNullOrEmpty(name) ? Program.AppDisplayName : name,
                Body = string.IsNullOrEmpty(summary) ? "Open the Nexus files tab to download the latest UJMM build." : summary,
                HtmlUrl = Program.NexusAppFilesUrl
            };
        }

        private void HandleNxmUrl(string url)
        {
            try
            {
                Activate();
                BringToFront();
                if (string.IsNullOrEmpty(nexusApiKey))
                {
                    var ans = MessageBox.Show("A Nexus download was triggered, but you're not signed in yet.\r\n\r\nSign in now?", "Sign in required", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (ans == DialogResult.Yes) ConnectViaSso();
                    if (string.IsNullOrEmpty(nexusApiKey)) return;
                }

                var parsed = NxmUrl.Parse(url);
                if (parsed == null)
                {
                    Log("Could not parse nxm URL: " + url);
                    return;
                }
                if (!string.Equals(parsed.Game, Program.NexusGameDomain, StringComparison.OrdinalIgnoreCase))
                {
                    var ans = MessageBox.Show("This download is for game '" + parsed.Game + "'. This manager only supports '" + Program.NexusGameDomain + "'.\r\n\r\nIgnore?", "Wrong game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Log("Nexus download requested: mod " + parsed.ModId + " file " + parsed.FileId);
                StartNxmDownload(parsed);
            }
            catch (Exception ex)
            {
                Log("HandleNxmUrl error: " + ex.Message);
            }
        }

        private void StartNxmDownload(NxmUrl parsed)
        {
            var key = nexusApiKey;
            var modsDirCopy = modsDir;
            var asiDirCopy = asiDir;
            var gamePathCopy = gamePath;
            var bin64 = !string.IsNullOrEmpty(gamePath) ? Path.Combine(gamePath, "bin64") : "";
            var prog = new NexusDownloadDialog("Downloading from Nexus", parsed.ModId);
            prog.Show(this);

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string error;
                Action<string> setStatus = msg =>
                {
                    if (prog.IsDisposed) return;
                    try { prog.BeginInvoke(new Action(() => prog.SetStatus(msg))); } catch { }
                };
                Action<int> setPct = pct =>
                {
                    if (prog.IsDisposed) return;
                    try { prog.BeginInvoke(new Action(() => prog.SetProgress(pct))); } catch { }
                };
                try
                {
                    setStatus("Fetching download link...");
                    var dl = NexusClient.GetDownloadLink(key, parsed.Game, parsed.ModId, parsed.FileId, parsed.Key, parsed.Expires, out error);
                    if (dl == null)
                    {
                        Log("Could not get download link: " + error);
                        setStatus("Failed: " + (error ?? "no link"));
                        return;
                    }

                    setStatus("Fetching file metadata...");
                    string fileName = NexusClient.GetFileName(key, parsed.Game, parsed.ModId, parsed.FileId, out error) ?? ("nexus_" + parsed.ModId + "_" + parsed.FileId);
                    var tmpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".cache", "downloads");
                    Directory.CreateDirectory(tmpDir);
                    var tmpPath = Path.Combine(tmpDir, fileName);

                    setStatus("Downloading " + fileName + "...");
                    var ok = NexusClient.DownloadFile(dl, tmpPath, setPct, out error);
                    if (!ok)
                    {
                        Log("Download failed: " + error);
                        setStatus("Failed: " + (error ?? "download"));
                        return;
                    }

                    setStatus("Importing...");
                    var imported = ImportDownloadedFile(tmpPath, modsDirCopy, asiDirCopy, bin64);
                    Log("Nexus download imported: " + imported);

                    // Write Nexus sidecars for any imported JSON files that map to this nxm:// URL
                    try
                    {
                        var modIdLocal = parsed.ModId;
                        var fileIdLocal = parsed.FileId;
                        string installedVersion = "";
                        try
                        {
                            string vErr;
                            var info = NexusClient.GetMod(key, parsed.Game, modIdLocal, out vErr);
                            if (info != null && info.ContainsKey("version")) installedVersion = Convert.ToString(info["version"]);
                        }
                        catch { }
                        BeginInvoke(new Action(() =>
                        {
                            foreach (var jsonPath in EnumerateRecentJsonFromImport(modsDirCopy, imported))
                            {
                                var link = new NexusLink
                                {
                                    ModId = modIdLocal,
                                    FileId = fileIdLocal,
                                    InstalledVersion = string.IsNullOrEmpty(installedVersion) ? "?" : installedVersion,
                                    ModName = "",
                                    LastCheckUtc = DateTime.UtcNow.ToString("u")
                                };
                                try { link.Save(jsonPath); } catch { }
                            }
                            LoadMods();
                            RefreshAsi();
                            CheckUpdatesNow();
                        }));
                        return;
                    }
                    catch { }

                    setStatus("Done - " + imported);
                    BeginInvoke(new Action(() =>
                    {
                        LoadMods();
                        RefreshAsi();
                    }));
                }
                catch (Exception ex)
                {
                    Log("Nexus download error: " + ex.Message);
                    setStatus("Error: " + ex.Message);
                }
            });
        }

        private IEnumerable<string> EnumerateRecentJsonFromImport(string modsDirLocal, string importedDescription)
        {
            // Heuristic: pick all .json files in modsDirLocal modified in the last 30s.
            var cutoff = DateTime.Now.AddSeconds(-30);
            try
            {
                foreach (var f in Directory.GetFiles(modsDirLocal, "*.json", SearchOption.TopDirectoryOnly))
                {
                    if (File.GetLastWriteTime(f) >= cutoff) yield return f;
                }
            }
            finally { }
        }

        private string ImportDownloadedFile(string path, string modsDirLocal, string asiDirLocal, string bin64Path)
        {
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ext == ".zip" || ext == ".7z" || ext == ".rar")
            {
                var stage = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_extracted");
                Directory.CreateDirectory(stage);
                ExtractArchiveToDirectory(path, stage);
                return ImportFolder(stage, modsDirLocal, asiDirLocal, bin64Path);
            }
            return ImportSingleFile(path, modsDirLocal, asiDirLocal, bin64Path);
        }

        private string ImportFolder(string folder, string modsDirLocal, string asiDirLocal, string bin64Path)
        {
            int j = 0, a = 0;
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                var ext = (Path.GetExtension(file) ?? "").ToLowerInvariant();
                if (ext == ".json")
                {
                    File.Copy(file, Path.Combine(modsDirLocal, Path.GetFileName(file)), true);
                    j++;
                }
                else if (ext == ".asi" || ext == ".dll" || ext == ".ini")
                {
                    if (!string.IsNullOrEmpty(bin64Path) && Directory.Exists(bin64Path))
                    {
                        File.Copy(file, Path.Combine(bin64Path, Path.GetFileName(file)), true);
                        File.Copy(file, Path.Combine(asiDirLocal, Path.GetFileName(file)), true);
                        a++;
                    }
                }
            }
            return j + " json, " + a + " asi/dll/ini";
        }

        private string ImportSingleFile(string path, string modsDirLocal, string asiDirLocal, string bin64Path)
        {
            var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ext == ".json")
            {
                File.Copy(path, Path.Combine(modsDirLocal, Path.GetFileName(path)), true);
                return Path.GetFileName(path);
            }
            if ((ext == ".asi" || ext == ".dll" || ext == ".ini") && !string.IsNullOrEmpty(bin64Path) && Directory.Exists(bin64Path))
            {
                File.Copy(path, Path.Combine(bin64Path, Path.GetFileName(path)), true);
                File.Copy(path, Path.Combine(asiDirLocal, Path.GetFileName(path)), true);
                return Path.GetFileName(path);
            }
            return "unsupported: " + Path.GetFileName(path);
        }

        private void DrawTabItem(object sender, DrawItemEventArgs e)
        {
            var tc = (TabControl)sender;
            var page = tc.TabPages[e.Index];
            var rect = tc.GetTabRect(e.Index);
            var isActive = (tc.SelectedIndex == e.Index);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Paint the strip background dark first - the default TabControl background is system grey and bleeds through under FlatButtons.
            using (var stripBrush = new SolidBrush(Color.FromArgb(14, 15, 11)))
                g.FillRectangle(stripBrush, rect);

            var pad = new Rectangle(rect.X + 6, rect.Y + 2, rect.Width - 12, rect.Height - 3);
            using (var path = RoundedPanel.RoundedRect(pad, 10))
            {
                if (isActive)
                {
                    // Match panel-header style: dark fill with a thin gold border + theme accent text.
                    using (var brush = new LinearGradientBrush(pad, Color.FromArgb(36, 38, 28), Color.FromArgb(22, 24, 18), 90f))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(140, currentTheme.Accent), 1f))
                        g.DrawPath(pen, path);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(20, 21, 16)))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(36, 32, 22), 1f))
                        g.DrawPath(pen, path);
                }
            }
            var fg = isActive ? currentTheme.Accent2 : Color.FromArgb(169, 157, 124);
            TextRenderer.DrawText(g, page.Text, tc.Font, pad, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private RoundedPanel BuildInspectorPanel()
        {
            var panel = new RoundedPanel { CornerRadius = 22, BorderWidth = 1 };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(layout);

            layout.Controls.Add(BuildPanelHeader("INSPECTOR", "Match ready", out inspectorCounter), 0, 0);

            var checkHost = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 4,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 14, 14, 6)
            };
            checkHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) checkHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            checkGuards = new CheckGridRow("Original bytes", "ready", BadgeKind.Neutral) { Margin = new Padding(0, 0, 0, 6), Dock = DockStyle.Fill };
            checkOverlay = new CheckGridRow("Overlay target", "0036", BadgeKind.Neutral) { Margin = new Padding(0, 0, 0, 6), Dock = DockStyle.Fill };
            checkConflicts = new CheckGridRow("Conflicts", "-", BadgeKind.Neutral) { Margin = new Padding(0, 0, 0, 6), Dock = DockStyle.Fill };
            checkBackup = new CheckGridRow("Backup", "missing", BadgeKind.Warn) { Margin = new Padding(0, 0, 0, 0), Dock = DockStyle.Fill };
            checkHost.Controls.Add(checkGuards, 0, 0);
            checkHost.Controls.Add(checkOverlay, 0, 1);
            checkHost.Controls.Add(checkConflicts, 0, 2);
            checkHost.Controls.Add(checkBackup, 0, 3);
            layout.Controls.Add(checkHost, 0, 1);

            // (Inspector backup button removed - Create Backup is now in the bottom action bar.)

            logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(9, 10, 8),
                ForeColor = Color.FromArgb(199, 187, 155),
                Font = new Font("Consolas", 9),
                Dock = DockStyle.Fill
            };
            var logHost = new RoundedPanel
            {
                CornerRadius = 14,
                BorderWidth = 1,
                Padding = new Padding(10, 8, 10, 8),
                Dock = DockStyle.Fill,
                Margin = new Padding(14, 0, 14, 14)
            };
            logHost.GradientTopOverride = Color.FromArgb(245, 9, 10, 8);
            logHost.GradientBottomOverride = Color.FromArgb(245, 9, 10, 8);
            logHost.Controls.Add(logBox);
            layout.Controls.Add(logHost, 0, 3);

            return panel;
        }

        // ---------------------------------------------------------- HELPERS

        private RoundedPanel BuildPanelHeader(string title, string counterText)
        {
            Label discard;
            return BuildPanelHeader(title, counterText, out discard);
        }

        private RoundedPanel BuildPanelHeader(string title, string counterText, out Label counter)
        {
            var header = new RoundedPanel
            {
                CornerRadius = 0,
                BorderWidth = 0,
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 0, 18, 0)
            };
            header.GradientTopOverride = Color.FromArgb(28, 255, 255, 255);
            header.GradientBottomOverride = Color.FromArgb(10, 255, 255, 255);
            header.BottomBorderColor = Color.FromArgb(40, 216, 166, 64);

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.Controls.Add(grid);

            grid.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            counter = new Label
            {
                Text = counterText,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(112, 104, 79),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            grid.Controls.Add(counter, 1, 0);
            return header;
        }

        private GradientButton NewGradientButton(string text, GradientButton.Style style, int width, Action onClick)
        {
            var btn = new GradientButton
            {
                Text = text,
                Kind = style,
                Margin = new Padding(0, 0, 8, 0),
                Height = 36
            };
            if (width > 0)
            {
                btn.Width = width;
            }
            else
            {
                btn.Dock = DockStyle.Fill;
            }
            btn.Click += (sender, args) => onClick();
            return btn;
        }

        private string AssemblyBuildDate()
        {
            try
            {
                var path = Assembly.GetExecutingAssembly().Location;
                return File.GetLastWriteTime(path).ToString("yyyy.MM.dd");
            }
            catch
            {
                return DateTime.Now.ToString("yyyy.MM.dd");
            }
        }

        // ---------------------------------------------------------- THEME

        private void ApplyTheme(Theme theme)
        {
            currentTheme = theme;
            BackColor = theme.Background;
            ForeColor = theme.Text;

            if (topBar != null) StyleMainPanel(topBar, theme);
            if (bottomBar != null) StyleMainPanel(bottomBar, theme);
            if (installPanel != null) StyleMainPanel(installPanel, theme);
            if (workspacePanel != null) StyleMainPanel(workspacePanel, theme);
            if (inspectorPanel != null) StyleMainPanel(inspectorPanel, theme);

            // If we're switching AWAY from the custom theme, forget the saved colour so the next
            // click on the custom swatch opens a fresh picker (the swatch reverts to placeholder).
            bool toCustom = string.Equals(theme.Name, "Custom", StringComparison.OrdinalIgnoreCase);
            if (!toCustom && config.ContainsKey("customAccent"))
            {
                config.Remove("customAccent");
            }

            if (themeSwatchHost != null && !themeSwatchHost.IsDisposed)
            {
                foreach (Control c in themeSwatchHost.Controls)
                {
                    var sw = c as ThemeSwatch;
                    if (sw != null) sw.IsActive = string.Equals(sw.SwatchTheme.Name, theme.Name, StringComparison.OrdinalIgnoreCase);
                }

                // Re-render the custom swatch so its placeholder state matches the (possibly cleared) customAccent.
                for (int i = 0; i < themeSwatchHost.Controls.Count; i++)
                {
                    var sw = themeSwatchHost.Controls[i] as ThemeSwatch;
                    if (sw != null && string.Equals(sw.SwatchTheme.Name, "Custom", StringComparison.OrdinalIgnoreCase))
                    {
                        themeSwatchHost.Controls.RemoveAt(i);
                        sw.Dispose();
                        var refreshed = MakeCustomSwatch();
                        refreshed.IsActive = toCustom;
                        themeSwatchHost.Controls.Add(refreshed);
                        themeSwatchHost.Controls.SetChildIndex(refreshed, i);
                        break;
                    }
                }
            }

            if (statusGamePill != null) statusGamePill.WarnColor = theme.Accent;
            if (statusModsPill != null) statusModsPill.WarnColor = theme.Accent;

            if (gamePathText != null && !gamePathText.IsDisposed)
            {
                gamePathText.BackColor = theme.Panel2;
                gamePathText.ForeColor = theme.Text;
            }
            if (logBox != null)
            {
                logBox.BackColor = Color.FromArgb(Math.Max(0, theme.Background.R - 4), Math.Max(0, theme.Background.G - 4), Math.Max(0, theme.Background.B - 4));
                logBox.ForeColor = theme.Muted;
            }

            if (patchList != null) StyleListView(patchList, theme);
            if (asiList != null) StyleListView(asiList, theme);

            config["theme"] = theme.Name;
            SaveConfig(config);

            RepaintAllRoundedPanels(this);
            RepaintModCards();
            if (tabs != null) tabs.Invalidate();
            Invalidate(true);
        }

        private void StyleMainPanel(RoundedPanel panel, Theme theme)
        {
            panel.GradientTopOverride = Color.FromArgb(245, theme.Panel);
            panel.GradientBottomOverride = Color.FromArgb(245, theme.Panel2);
            panel.BorderColor = Color.FromArgb(80, theme.Accent);
        }

        private void StyleListView(ListView list, Theme theme)
        {
            list.BackColor = Color.FromArgb(theme.Panel.R / 2, theme.Panel.G / 2, theme.Panel.B / 2);
            list.ForeColor = theme.Text;
        }

        private void RepaintAllRoundedPanels(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is RoundedPanel rp)
                {
                    rp.Invalidate();
                }
                if (c.HasChildren)
                {
                    RepaintAllRoundedPanels(c);
                }
            }
        }

        // ---------------------------------------------------------- MODS

        private void LoadMods()
        {
            mods.Clear();
            activeBoxes.Clear();
            groupBy.Clear();
            modCards.Clear();
            modCardPills.Clear();
            nexusLinks.Clear();
            // Preserve focus across reload only if the focused mod's file/folder still exists.
            if (!string.IsNullOrEmpty(focusedModPath) && !File.Exists(focusedModPath) && !Directory.Exists(focusedModPath)) focusedModPath = "";
            if (modCardsHost == null) return;
            modCardsHost.Controls.Clear();

            foreach (var file in Directory.GetFiles(modsDir, "*.json", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
            {
                try
                {
                    mods.Add(JsonMod.Load(file, json));
                }
                catch (Exception ex)
                {
                    Log("Skipped " + Path.GetFileName(file) + ": " + ex.Message);
                }
            }
            foreach (var dir in Directory.GetDirectories(modsDir).OrderBy(Path.GetFileName))
            {
                try
                {
                    if (IsRawOverlayDirectory(dir)) mods.Add(JsonMod.LoadOverlayDirectory(dir, json, "RAW"));
                    else if (IsBrowserModDirectory(dir)) mods.Add(JsonMod.LoadOverlayDirectory(dir, json, "BROWSER"));
                }
                catch (Exception ex)
                {
                    Log("Skipped " + Path.GetFileName(dir) + ": " + ex.Message);
                }
            }
            ApplySavedModOrder();

            var active = ConfigStringSet("activeMods");
            var selectedGroups = ConfigDict("selectedGroups");
            foreach (var mod in mods)
            {
                var capturedMod = mod;
                var card = new RoundedPanel
                {
                    CornerRadius = 16,
                    BorderWidth = 1,
                    Width = Math.Max(260, modCardsHost.ClientSize.Width - 4),
                    Height = 96,
                    Margin = new Padding(0, 0, 0, 8),
                    Padding = new Padding(12, 9, 12, 9),
                    Cursor = Cursors.Hand
                };
                card.GradientTopOverride = Color.FromArgb(56, 0, 0, 0);
                card.GradientBottomOverride = Color.FromArgb(80, 0, 0, 0);

                var stack = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    BackColor = Color.Transparent
                };
                // Title row is tall enough to fully contain the 20px FlatCheck plus a couple of pixels
                // of breathing room on top/bottom - the previous 24px caused the box to be clipped where
                // the description row began.
                stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                card.Controls.Add(stack);

                var titleRow = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 3,
                    RowCount = 1,
                    BackColor = Color.Transparent
                };
                titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
                titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                var check = new FlatCheck
                {
                    Width = 20,
                    Height = 20,
                    Anchor = AnchorStyles.None,
                    Margin = new Padding(0, 0, 6, 0),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand,
                    CheckedFill = currentTheme.Accent,
                    CheckedBorder = currentTheme.Accent2
                };
                titleRow.Controls.Add(check, 0, 0);

                var nameLabel = new Label
                {
                    Text = mod.Name,
                    Dock = DockStyle.Fill,
                    Font = new Font("Trebuchet MS", 9.5f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand
                };
                titleRow.Controls.Add(nameLabel, 1, 0);

                var tag = new Pill
                {
                    Text = mod.FormatTag,
                    DotColor = Color.Empty,
                    BorderlessTag = true,
                    Font = new Font("Consolas", 7.5f, FontStyle.Bold),
                    Height = 18,
                    Anchor = AnchorStyles.Right,
                    Margin = new Padding(0, 4, 0, 0),
                    Cursor = Cursors.Hand
                };
                ApplyFormatPillStyle(tag, mod.FormatTag);
                titleRow.Controls.Add(tag, 2, 0);
                stack.Controls.Add(titleRow, 0, 0);
                modCardPills[mod.Path] = tag;

                var link = NexusLink.Load(mod.Path);
                if (link != null) nexusLinks[mod.Path] = link;
                tag.Click += (s, ea) =>
                {
                    if (nexusLinks.ContainsKey(capturedMod.Path))
                    {
                        var nl = nexusLinks[capturedMod.Path];
                        SafeOpenUrl("https://www.nexusmods.com/" + Program.NexusGameDomain + "/mods/" + nl.ModId);
                    }
                };

                var metaLabel = new Label
                {
                    Text = !string.IsNullOrWhiteSpace(mod.Description) ? mod.Description :
                           (string.IsNullOrWhiteSpace(mod.Version + mod.Author) ? mod.FormatTag + " mod" : (mod.Version + " by " + mod.Author).Trim()),
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 7.5f),
                    ForeColor = Color.FromArgb(169, 157, 124),
                    BackColor = Color.Transparent,
                    AutoEllipsis = true,
                    AutoSize = false,
                    TextAlign = ContentAlignment.TopLeft,
                    Cursor = Cursors.Hand
                };
                stack.Controls.Add(metaLabel, 0, 1);

                check.Checked = active.Contains(Path.GetFileName(mod.Path)) || active.Contains(mod.Name);
                tipsHost.SetToolTip(check, "Select / deselect this mod. Multiple mods can be selected - the patch list shows what would apply.");

                var menu = new ContextMenuStrip();
                menu.Items.Add("Uninstall / Disable", null, (s, e) => DisableMod(capturedMod));
                menu.Items.Add("Open Folder", null, (s, e) => OpenFolder(Path.GetDirectoryName(capturedMod.Path)));
                menu.Items.Add("-");
                menu.Items.Add("Priority: Move Up", null, (s, e) => MoveModPriority(capturedMod, -1));
                menu.Items.Add("Priority: Move Down", null, (s, e) => MoveModPriority(capturedMod, 1));
                menu.Items.Add("Priority: Move to Bottom (highest)", null, (s, e) => MoveModPriority(capturedMod, int.MaxValue));
                menu.Items.Add("-");
                menu.Items.Add("Link to Nexus...", null, (s, e) => PromptLinkToNexus(capturedMod));
                if (nexusLinks.ContainsKey(mod.Path))
                {
                    menu.Items.Add("Open on Nexus", null, (s, e) =>
                    {
                        if (nexusLinks.ContainsKey(capturedMod.Path))
                            SafeOpenUrl("https://www.nexusmods.com/" + Program.NexusGameDomain + "/mods/" + nexusLinks[capturedMod.Path].ModId);
                    });
                    menu.Items.Add("Unlink from Nexus", null, (s, e) =>
                    {
                        NexusLink.Delete(capturedMod.Path);
                        nexusLinks.Remove(capturedMod.Path);
                        UpdateModPillForLink(capturedMod);
                    });
                }
                menu.Items.Add("-");
                menu.Items.Add("Delete from manager", null, (s, e) => DeleteMod(capturedMod));
                card.ContextMenuStrip = menu;
                nameLabel.ContextMenuStrip = menu;
                metaLabel.ContextMenuStrip = menu;

                MouseEventHandler focusHandler = (sender, args) =>
                {
                    if (args.Button != MouseButtons.Left) return;
                    FocusMod(capturedMod);
                };
                card.MouseClick += focusHandler;
                stack.MouseClick += focusHandler;
                titleRow.MouseClick += focusHandler;
                nameLabel.MouseClick += focusHandler;
                metaLabel.MouseClick += focusHandler;
                tag.MouseClick += focusHandler;
                check.Click += (s, e) => FocusMod(capturedMod);
                check.CheckedChanged += (sender, args) =>
                {
                    if (suppressSelectionPersist) return;
                    UpdateModCardVisual(capturedMod);
                    PersistSelectionAndRefresh();
                };

                modCardsHost.Controls.Add(card);
                UpdateModPillForLink(mod);

                var fileName = Path.GetFileName(mod.Path);
                var chosenGroup = selectedGroups.ContainsKey(fileName) ? Convert.ToString(selectedGroups[fileName]) : mod.Groups.FirstOrDefault();
                if (string.IsNullOrEmpty(chosenGroup) || !mod.Groups.Contains(chosenGroup)) chosenGroup = mod.Groups.FirstOrDefault() ?? "All";
                groupBy[mod.Path] = chosenGroup;

                activeBoxes[mod.Path] = check;
                modCards[mod.Path] = card;
                UpdateModCardVisual(mod);
            }

            RebuildPresetRail();

            RefreshPatchList();
            UpdateStatusPills();
            UpdateBottomSummary();
            Log("Loaded " + mods.Count + " JSON mod file(s).");
        }

        private void RefreshModsFromDisk()
        {
            var before = mods.Count;
            LoadMods();
            RefreshAsi();
            Log("Refreshed mods folder: " + before + " -> " + mods.Count + " mod(s).");
        }

        private void ApplySavedModOrder()
        {
            var saved = ConfigStringList("modOrder");
            if (saved.Count == 0) return;
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < saved.Count; i++)
            {
                if (!rank.ContainsKey(saved[i])) rank[saved[i]] = i;
            }
            mods.Sort((a, b) =>
            {
                int ai, bi;
                var an = Path.GetFileName(a.Path);
                var bn = Path.GetFileName(b.Path);
                var ah = rank.TryGetValue(an, out ai);
                var bh = rank.TryGetValue(bn, out bi);
                if (ah && bh) return ai.CompareTo(bi);
                if (ah) return -1;
                if (bh) return 1;
                return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void SaveModOrder()
        {
            config["modOrder"] = mods.Select(mod => (object)Path.GetFileName(mod.Path)).ToArray();
            SaveConfig(config);
        }

        private void MoveModPriority(JsonMod mod, int delta)
        {
            var oldIndex = mods.FindIndex(m => string.Equals(m.Path, mod.Path, StringComparison.OrdinalIgnoreCase));
            if (oldIndex < 0) return;
            var newIndex = delta == int.MaxValue ? mods.Count - 1 : Math.Max(0, Math.Min(mods.Count - 1, oldIndex + delta));
            if (newIndex == oldIndex) return;

            mods.RemoveAt(oldIndex);
            mods.Insert(newIndex, mod);

            suppressSelectionPersist = true;
            try
            {
                modCardsHost.Controls.Clear();
                foreach (var m in mods)
                {
                    if (modCards.ContainsKey(m.Path)) modCardsHost.Controls.Add(modCards[m.Path]);
                }
            }
            finally
            {
                suppressSelectionPersist = false;
            }

            SaveModOrder();
            PersistSelectionAndRefresh();
            FocusMod(mod);
            Log("Moved " + mod.Name + " priority to " + (newIndex + 1) + "/" + mods.Count + ". Lower cards apply later and win conflicts.");
        }

        private void UpdateModPillForLink(JsonMod mod)
        {
            if (!modCardPills.ContainsKey(mod.Path)) return;
            var pill = modCardPills[mod.Path];
            if (!nexusLinks.ContainsKey(mod.Path))
            {
                pill.Text = mod.FormatTag;
                ApplyFormatPillStyle(pill, mod.FormatTag);
                pill.Width = 0;
                pill.Invalidate();
                return;
            }
            var link = nexusLinks[mod.Path];
            if (link.UpdateAvailable)
            {
                pill.Text = "UPDATE";
                pill.PillFillColor = Color.FromArgb(216, 92, 76);
                pill.PillTextColor = Color.White;
            }
            else
            {
                pill.Text = "v" + (string.IsNullOrEmpty(link.InstalledVersion) ? "?" : link.InstalledVersion);
                pill.PillFillColor = Color.FromArgb(101, 197, 134);
                pill.PillTextColor = Color.FromArgb(8, 19, 13);
            }
            pill.Width = 0;
            pill.Invalidate();
        }

        private static void ApplyFormatPillStyle(Pill pill, string tag)
        {
            var normalized = (tag ?? "JSON").ToUpperInvariant();
            if (normalized == "FIELDS")
            {
                pill.PillFillColor = Color.FromArgb(126, 216, 194);
                pill.PillTextColor = Color.FromArgb(8, 28, 24);
            }
            else if (normalized == "RAW")
            {
                pill.PillFillColor = Color.FromArgb(178, 134, 216);
                pill.PillTextColor = Color.FromArgb(24, 12, 33);
            }
            else if (normalized == "BROWSER")
            {
                pill.PillFillColor = Color.FromArgb(140, 199, 221);
                pill.PillTextColor = Color.FromArgb(8, 22, 31);
            }
            else
            {
                pill.PillFillColor = Color.FromArgb(216, 166, 64);
                pill.PillTextColor = Color.FromArgb(21, 15, 8);
            }
        }

        private void PromptLinkToNexus(JsonMod mod)
        {
            using (var dlg = new NexusLinkDialog(mod.Name, nexusLinks.ContainsKey(mod.Path) ? nexusLinks[mod.Path] : null))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var modId = dlg.ResolvedModId;
                    if (modId <= 0) return;
                    var link = new NexusLink
                    {
                        ModId = modId,
                        FileId = dlg.FileIdHint,
                        InstalledVersion = string.IsNullOrEmpty(dlg.InstalledVersion) ? "?" : dlg.InstalledVersion,
                        ModName = mod.Name,
                        LastCheckUtc = ""
                    };
                    nexusLinks[mod.Path] = link;
                    link.Save(mod.Path);
                    UpdateModPillForLink(mod);
                    Log("Linked " + mod.Name + " to Nexus mod " + modId);
                    if (!string.IsNullOrEmpty(nexusApiKey))
                    {
                        CheckUpdatesNow();
                    }
                }
            }
        }

        private void StartNexusUpdateTimer()
        {
            // Nexus rate-limit guidance (per their May 2026 reply): poll mod updates AT MOST once per session.
            // We do a single startup check 8s after the form is up - never on a recurring timer - and
            // refreshing the Nexus tab triggers an explicit on-demand check via the "Refresh Feed" button.
            if (nexusUpdateTimer != null) return;
            var kickoff = new System.Windows.Forms.Timer { Interval = 8000 };
            kickoff.Tick += (s, e) =>
            {
                kickoff.Stop();
                kickoff.Dispose();
                if (!string.IsNullOrEmpty(nexusApiKey)) CheckUpdatesNow();
            };
            kickoff.Start();
        }

        private bool updateCheckRunning;
        private void CheckUpdatesNow()
        {
            if (updateCheckRunning) return;
            if (string.IsNullOrEmpty(nexusApiKey)) return;
            if (nexusLinks.Count == 0) return;
            updateCheckRunning = true;
            var key = nexusApiKey;
            var localLinks = new Dictionary<string, NexusLink>(nexusLinks, StringComparer.OrdinalIgnoreCase);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string err;
                    var updates = NexusClient.GetUpdatedMods(key, Program.NexusGameDomain, "1m", out err);
                    if (updates == null) { Log("Nexus update check failed: " + err); return; }
                    var byId = new Dictionary<int, long>();
                    foreach (var u in updates) byId[u.Item1] = u.Item2;

                    var changed = new List<string>();
                    foreach (var pair in localLinks)
                    {
                        var link = pair.Value;
                        if (link.ModId <= 0) continue;
                        if (!byId.ContainsKey(link.ModId)) continue;
                        var newest = byId[link.ModId];
                        if (newest <= link.LatestFileTimestamp && link.LatestFileTimestamp != 0) continue;
                        var modInfo = NexusClient.GetMod(key, Program.NexusGameDomain, link.ModId, out err);
                        if (modInfo == null) continue;
                        var latestVer = modInfo.ContainsKey("version") ? Convert.ToString(modInfo["version"]) : "";
                        link.LatestVersion = latestVer ?? "";
                        link.LatestFileTimestamp = newest;
                        link.LastCheckUtc = DateTime.UtcNow.ToString("u");
                        link.UpdateAvailable = !string.IsNullOrEmpty(latestVer) && !string.Equals(latestVer, link.InstalledVersion, StringComparison.OrdinalIgnoreCase);
                        try { link.Save(pair.Key); } catch { }
                        if (link.UpdateAvailable) changed.Add(pair.Key);
                    }

                    if (IsDisposed || Disposing) return;
                    BeginInvoke(new Action(() =>
                    {
                        foreach (var path in changed)
                        {
                            var mod = mods.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
                            if (mod != null) UpdateModPillForLink(mod);
                        }
                        if (changed.Count > 0) Log("Nexus update check: " + changed.Count + " mod(s) have updates available.");
                    }));
                }
                catch (Exception ex) { Log("Nexus update check error: " + ex.Message); }
                finally { updateCheckRunning = false; }
            });
        }

        private void UpdateModCardVisual(JsonMod mod)
        {
            if (!modCards.ContainsKey(mod.Path) || !activeBoxes.ContainsKey(mod.Path)) return;
            var card = modCards[mod.Path];
            var on = activeBoxes[mod.Path].Checked;
            var isFocused = string.Equals(focusedModPath, mod.Path, StringComparison.OrdinalIgnoreCase);
            if (on)
            {
                var a = currentTheme.Accent;
                card.GradientTopOverride = Color.FromArgb(160, a.R, a.G, a.B);
                card.GradientBottomOverride = Color.FromArgb(220, Math.Min(60, a.R / 4), Math.Min(40, a.G / 4), Math.Min(20, a.B / 4));
                card.BorderColor = currentTheme.Accent2;
                card.BorderWidth = isFocused ? 3 : 2;
            }
            else if (isFocused)
            {
                card.GradientTopOverride = Color.FromArgb(70, currentTheme.Accent);
                card.GradientBottomOverride = Color.FromArgb(140, 0, 0, 0);
                card.BorderColor = currentTheme.Accent2;
                card.BorderWidth = 2;
            }
            else
            {
                card.GradientTopOverride = Color.FromArgb(56, 0, 0, 0);
                card.GradientBottomOverride = Color.FromArgb(110, 0, 0, 0);
                card.BorderColor = Color.FromArgb(38, 255, 255, 255);
                card.BorderWidth = 1;
            }
            card.Invalidate();
        }

        private void RepaintModCards()
        {
            foreach (var mod in mods) UpdateModCardVisual(mod);
        }

        private string GroupFor(JsonMod mod)
        {
            if (groupBy.TryGetValue(mod.Path, out var g) && !string.IsNullOrEmpty(g)) return g;
            return mod.Groups.FirstOrDefault() ?? "All";
        }

        private List<string> ComputeAllGroups()
        {
            return ComputeGroups(false);
        }

        private List<string> ComputeGroups(bool activeOnly)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            foreach (var m in mods)
            {
                if (activeOnly)
                {
                    if (!activeBoxes.ContainsKey(m.Path) || !activeBoxes[m.Path].Checked) continue;
                }
                foreach (var g in m.Groups)
                {
                    if (string.IsNullOrEmpty(g)) continue;
                    if (seen.Add(g)) ordered.Add(g);
                }
            }
            ordered.Sort((a, b) =>
            {
                double pa, pb;
                bool ap = TryParsePercent(a, out pa);
                bool bp = TryParsePercent(b, out pb);
                if (ap && bp) return pa.CompareTo(pb);
                if (ap) return -1;
                if (bp) return 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return ordered;
        }

        private bool AnyModActive()
        {
            foreach (var b in activeBoxes.Values) if (b.Checked) return true;
            return false;
        }

        private void FocusMod(JsonMod mod)
        {
            if (mod == null) return;
            var prev = focusedModPath;
            focusedModPath = mod.Path;
            // Repaint the previously- and now-focused cards so the ring updates
            if (!string.IsNullOrEmpty(prev) && modCards.ContainsKey(prev))
            {
                var prevMod = mods.FirstOrDefault(m => string.Equals(m.Path, prev, StringComparison.OrdinalIgnoreCase));
                if (prevMod != null) UpdateModCardVisual(prevMod);
            }
            UpdateModCardVisual(mod);
            RebuildPresetRail();
            RefreshPatchList(); // so preview rows track the focused mod
        }

        private JsonMod FocusedMod()
        {
            if (string.IsNullOrEmpty(focusedModPath)) return null;
            return mods.FirstOrDefault(m => string.Equals(m.Path, focusedModPath, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryParsePercent(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var t = s.Trim().TrimEnd('%').Trim();
            return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        private void RebuildPresetRail()
        {
            if (presetRailHost == null) return;
            presetRailHost.Controls.Clear();
            presetCards.Clear();

            var focused = FocusedMod();
            if (focused == null)
            {
                var empty = new Label
                {
                    Text = mods.Count == 0
                        ? "Add JSON mods to see presets here."
                        : "Click a mod on the left\r\nto see its preset groups.",
                    Dock = DockStyle.Top,
                    Font = new Font("Consolas", 8.5f),
                    ForeColor = Color.FromArgb(112, 104, 79),
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Padding = new Padding(8, 8, 8, 8)
                };
                presetRailHost.Controls.Add(empty);
                return;
            }

            // Header: which mod's presets we're showing. AutoEllipsis + smaller font + 2-line height so
            // long mod names like "RESOURCE COSTS-JSON" don't get clipped on the narrow preset rail.
            var header = new Label
            {
                Text = focused.Name.ToUpperInvariant(),
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 38,
                Font = new Font("Consolas", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft,
                AutoEllipsis = true,
                Padding = new Padding(2, 0, 4, 4),
                Margin = new Padding(0, 0, 0, 4)
            };
            tipsHost.SetToolTip(header, focused.Name);
            presetRailHost.Controls.Add(header);

            if (IsOverlayMod(focused))
            {
                var card = new RoundedPanel
                {
                    CornerRadius = 14,
                    BorderWidth = 1,
                    Width = 152,
                    Height = 70,
                    Margin = new Padding(0, 0, 0, 8),
                    Padding = new Padding(11, 8, 11, 8)
                };
                card.GradientTopOverride = Color.FromArgb(60, 101, 197, 134);
                card.GradientBottomOverride = Color.FromArgb(110, 0, 0, 0);
                card.BorderColor = Color.FromArgb(150, 101, 197, 134);

                var inner = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    BackColor = Color.Transparent
                };
                inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                card.Controls.Add(inner);

                inner.Controls.Add(new Label
                {
                    Text = focused.FormatTag + " overlay",
                    Dock = DockStyle.Fill,
                    Font = new Font("Trebuchet MS", 11.5f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(244, 234, 209),
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                }, 0, 0);
                inner.Controls.Add(new Label
                {
                    Text = OverlayUnitText(focused),
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 8f),
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(169, 157, 124),
                    TextAlign = ContentAlignment.MiddleLeft
                }, 0, 1);
                tipsHost.SetToolTip(card, "This mod installs overlay folders, not JSON byte patches.");
                presetRailHost.Controls.Add(card);
                return;
            }

            var groups = focused.Groups ?? new List<string>();
            // Sort by parsed % when applicable, else lexicographic
            groups = groups.OrderBy(g => g, Comparer<string>.Create((a, b) =>
            {
                double pa, pb;
                bool ap = TryParsePercent(a, out pa);
                bool bp = TryParsePercent(b, out pb);
                if (ap && bp) return pa.CompareTo(pb);
                if (ap) return -1;
                if (bp) return 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            })).ToList();

            var showSingleAllAsCard = string.Equals(focused.FormatTag, "FIELDS", StringComparison.OrdinalIgnoreCase);
            if (groups.Count == 0 || (!showSingleAllAsCard && groups.Count == 1 && string.Equals(groups[0], "All", StringComparison.OrdinalIgnoreCase)))
            {
                presetRailHost.Controls.Add(new Label
                {
                    Text = "This mod has no preset variants.\r\nIts " + focused.Changes.Count + " patch" + (focused.Changes.Count == 1 ? "" : "es") + "\r\nwill all apply when active.",
                    Dock = DockStyle.Top,
                    Font = new Font("Consolas", 8.5f),
                    ForeColor = Color.FromArgb(112, 104, 79),
                    BackColor = Color.Transparent,
                    AutoSize = true,
                    Padding = new Padding(8, 8, 8, 8)
                });
                return;
            }

            foreach (var g in groups)
            {
                var captured = g;
                int patchCount = focused.Changes.Count(c => string.Equals(c.Group, g, StringComparison.OrdinalIgnoreCase));
                var displayName = showSingleAllAsCard && string.Equals(g, "All", StringComparison.OrdinalIgnoreCase)
                    ? focused.Name
                    : g;

                var card = new RoundedPanel
                {
                    CornerRadius = 14,
                    BorderWidth = 1,
                    Width = 152,
                    Height = 62,
                    Margin = new Padding(0, 0, 0, 8),
                    Padding = new Padding(11, 7, 11, 7),
                    Cursor = Cursors.Hand
                };
                card.GradientTopOverride = Color.FromArgb(46, 0, 0, 0);
                card.GradientBottomOverride = Color.FromArgb(80, 0, 0, 0);
                card.BorderColor = Color.FromArgb(36, 255, 255, 255);

                var inner = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 2,
                    BackColor = Color.Transparent
                };
                inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
                inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                card.Controls.Add(inner);

                var titleLabel = new Label
                {
                    Text = displayName,
                    Dock = DockStyle.Fill,
                    Font = new Font("Trebuchet MS", 12.5f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(244, 234, 209),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand,
                    AutoEllipsis = true
                };
                tipsHost.SetToolTip(titleLabel, displayName);
                inner.Controls.Add(titleLabel, 0, 0);

                var subLabel = new Label
                {
                    Text = patchCount + " patch" + (patchCount == 1 ? "" : "es"),
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 8f),
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(169, 157, 124),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand
                };
                inner.Controls.Add(subLabel, 0, 1);

                MouseEventHandler clickHandler = (s, args) => SelectPreset(captured);
                card.MouseClick += clickHandler;
                titleLabel.MouseClick += clickHandler;
                subLabel.MouseClick += clickHandler;
                inner.MouseClick += clickHandler;

                presetRailHost.Controls.Add(card);
                presetCards[g] = card;
            }
            UpdatePresetVisuals();
        }

        private void SelectPreset(string preset)
        {
            var focused = FocusedMod();
            if (focused == null) return;
            if (!focused.Groups.Contains(preset, StringComparer.OrdinalIgnoreCase)) return;
            groupBy[focused.Path] = focused.Groups.First(x => string.Equals(x, preset, StringComparison.OrdinalIgnoreCase));
            activePreset = preset;
            config["activePreset"] = preset;
            UpdatePresetVisuals();
            PersistSelectionAndRefresh();
        }

        private void UpdatePresetVisuals()
        {
            var focused = FocusedMod();
            var selected = focused != null && groupBy.ContainsKey(focused.Path) ? groupBy[focused.Path] : "";
            foreach (var pair in presetCards)
            {
                var isSelectedForFocused = !string.IsNullOrEmpty(selected) && string.Equals(pair.Key, selected, StringComparison.OrdinalIgnoreCase);
                var c = pair.Value;
                if (isSelectedForFocused)
                {
                    c.GradientTopOverride = Color.FromArgb(60, 101, 197, 134);
                    c.GradientBottomOverride = Color.FromArgb(110, 0, 0, 0);
                    c.BorderColor = Color.FromArgb(150, 101, 197, 134);
                }
                else
                {
                    c.GradientTopOverride = Color.FromArgb(46, 0, 0, 0);
                    c.GradientBottomOverride = Color.FromArgb(80, 0, 0, 0);
                    c.BorderColor = Color.FromArgb(36, 255, 255, 255);
                }
                c.Invalidate();
            }
        }

        private void RefreshPatchList()
        {
            if (patchList == null) return;

            string filter = patchSearchBox != null ? (patchSearchBox.Text ?? "").Trim().ToLowerInvariant() : "";
            // Show all active (checked) mods' patches first - these will actually apply.
            // Then, if the focused mod isn't already active, show its patches as a preview.
            var pending = new List<ListViewItem>(256);
            int activeCount = 0;
            int activeAppliedCount = 0;
            int activeOverlayCount = 0;
            var focused = FocusedMod();
            bool focusedAlreadyActive = false;
            foreach (var mod in mods)
            {
                if (!activeBoxes.ContainsKey(mod.Path) || !activeBoxes[mod.Path].Checked) continue;
                if (focused != null && string.Equals(mod.Path, focused.Path, StringComparison.OrdinalIgnoreCase)) focusedAlreadyActive = true;
                if (IsOverlayMod(mod))
                {
                    foreach (var folder in mod.OverlayFolders)
                    {
                        foreach (var file in OverlayFiles(folder))
                        {
                            var label = OverlayFileLabel(folder, file);
                            var target = OverlayTargetDisplay(mod, folder);
                            activeOverlayCount++;
                            if (filter.Length > 0
                                && !label.ToLowerInvariant().Contains(filter)
                                && !target.ToLowerInvariant().Contains(filter)
                                && !mod.Name.ToLowerInvariant().Contains(filter))
                            {
                                continue;
                            }
                            var item = new ListViewItem(label);
                            item.SubItems.Add(target);
                            item.Tag = null;
                            item.Checked = true;
                            pending.Add(item);
                        }
                    }
                    continue;
                }
                var group = GroupFor(mod);
                int idx = 0;
                foreach (var change in mod.ChangesForGroup(group))
                {
                    var label = string.IsNullOrEmpty(change.CleanLabel) ? "(unnamed patch)" : change.CleanLabel;
                    var target = PatchTargetDisplay(change);
                    activeCount++;
                    var key = PatchKey(mod, group, idx);
                    bool patchEnabled = !disabledPatches.Contains(key);
                    if (patchEnabled) activeAppliedCount++;
                    idx++;
                    if (filter.Length > 0
                        && !label.ToLowerInvariant().Contains(filter)
                        && !target.ToLowerInvariant().Contains(filter)
                        && !mod.Name.ToLowerInvariant().Contains(filter))
                    {
                        continue;
                    }
                    var item = new ListViewItem(label);
                    item.SubItems.Add(target);
                    item.Tag = key;
                    item.Checked = patchEnabled;
                    pending.Add(item);
                }
            }

            int previewCount = 0;
            int previewOverlayCount = 0;
            if (focused != null && !focusedAlreadyActive)
            {
                if (IsOverlayMod(focused))
                {
                    foreach (var folder in focused.OverlayFolders)
                    {
                        foreach (var file in OverlayFiles(folder))
                        {
                            var label = "preview - " + OverlayFileLabel(folder, file);
                            var target = OverlayTargetDisplay(focused, folder);
                            if (filter.Length > 0
                                && !label.ToLowerInvariant().Contains(filter)
                                && !target.ToLowerInvariant().Contains(filter)
                                && !focused.Name.ToLowerInvariant().Contains(filter))
                            {
                                continue;
                            }
                            var item = new ListViewItem(label);
                            item.SubItems.Add(target);
                            item.ForeColor = Color.FromArgb(150, 138, 100);
                            item.Tag = null;
                            item.Checked = true;
                            pending.Add(item);
                            previewOverlayCount++;
                        }
                    }
                }
                else
                {
                var group = GroupFor(focused);
                int idx = 0;
                foreach (var change in focused.ChangesForGroup(group))
                {
                    var label = string.IsNullOrEmpty(change.CleanLabel) ? "(unnamed patch)" : change.CleanLabel;
                    var target = PatchTargetDisplay(change);
                    var key = PatchKey(focused, group, idx);
                    bool patchEnabled = !disabledPatches.Contains(key);
                    idx++;
                    if (filter.Length > 0
                        && !label.ToLowerInvariant().Contains(filter)
                        && !target.ToLowerInvariant().Contains(filter)
                        && !focused.Name.ToLowerInvariant().Contains(filter))
                    {
                        continue;
                    }
                    var item = new ListViewItem("preview - " + label);
                    item.SubItems.Add(target);
                    item.ForeColor = Color.FromArgb(150, 138, 100);
                    item.Tag = key;
                    item.Checked = patchEnabled;
                    pending.Add(item);
                    previewCount++;
                }
                }
            }

            patchListSyncing = true;
            patchList.BeginUpdate();
            try
            {
                patchList.Items.Clear();
                if (pending.Count > 0) patchList.Items.AddRange(pending.ToArray());
            }
            finally
            {
                patchList.EndUpdate();
                patchListSyncing = false;
            }

            if (workspaceCounter != null)
            {
                int total = 0;
                foreach (var mod in mods) total += mod.Changes.Count;
                var lead = activeAppliedCount + " / " + total + " will apply";
                if (activeOverlayCount > 0) lead += " + " + activeOverlayCount + " overlay file" + (activeOverlayCount == 1 ? "" : "s");
                if (previewCount > 0) lead += " - " + previewCount + " preview";
                if (filter.Length > 0) lead += " - filtered: \"" + filter + "\"";
                if (previewOverlayCount > 0) lead += " - " + previewOverlayCount + " overlay file preview" + (previewOverlayCount == 1 ? "" : "s");
                workspaceCounter.Text = lead;
            }
        }

        private static bool IsOverlayMod(JsonMod mod)
        {
            return mod != null
                && (string.Equals(mod.FormatTag, "RAW", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mod.FormatTag, "BROWSER", StringComparison.OrdinalIgnoreCase))
                && mod.OverlayFolders != null
                && mod.OverlayFolders.Count > 0;
        }

        private static string OverlayUnitText(JsonMod mod)
        {
            var count = mod != null && mod.OverlayFolders != null ? mod.OverlayFolders.Count : 0;
            return count + " folder" + (count == 1 ? "" : "s") + " to copy/register";
        }

        private static string OverlayTargetDisplay(JsonMod mod, string folder)
        {
            var slot = Path.GetFileName(folder);
            var pamt = Path.Combine(folder, "0.pamt");
            if (File.Exists(pamt)) return slot + " -> game folder + meta\\0.papgt";
            return slot + " -> game folder";
        }

        private static IEnumerable<string> OverlayFiles(string folder)
        {
            if (!Directory.Exists(folder)) return Enumerable.Empty<string>();
            var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count > 0) return files;
            return new[] { folder };
        }

        private static string OverlayFileLabel(string folder, string file)
        {
            if (!File.Exists(file)) return Path.GetFileName(folder);
            var rel = file.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return rel.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }

        private static string PatchTargetDisplay(PatchChange change)
        {
            if (!string.IsNullOrWhiteSpace(change.TargetDisplay)) return change.TargetDisplay;
            var fileName = string.IsNullOrEmpty(change.GameFile) ? "" : Path.GetFileName(change.GameFile);
            var patchedPreview = !string.IsNullOrEmpty(change.Patched)
                ? "  -> " + change.Patched.Replace(" ", "").ToUpperInvariant()
                : "";
            if (patchedPreview.Length > 18) patchedPreview = patchedPreview.Substring(0, 18) + "...";
            return fileName + "  +0x" + change.Offset.ToString("X") + patchedPreview;
        }

        private void PersistSelectionAndRefresh()
        {
            var active = new List<object>();
            var groups = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods)
            {
                var fileName = Path.GetFileName(mod.Path);
                groups[fileName] = GroupFor(mod);
                if (activeBoxes[mod.Path].Checked) active.Add(fileName);
            }
            config["activeMods"] = active.ToArray();
            config["selectedGroups"] = groups;
            config["modOrder"] = mods.Select(mod => (object)Path.GetFileName(mod.Path)).ToArray();
            SaveConfig(config);
            File.WriteAllText(Path.Combine(enabledDir, "_load_order.json"), json.Serialize(active.ToArray()), Encoding.UTF8);
            UpdatePresetVisuals();
            RefreshPatchList();
            UpdateStatusPills();
            UpdateBottomSummary();
        }

        private void DisableMod(JsonMod mod)
        {
            if (activeBoxes.ContainsKey(mod.Path))
            {
                activeBoxes[mod.Path].Checked = false;
                Log("Uninstalled/deactivated " + mod.Name + ".");
            }
        }

        private void DeleteMod(JsonMod mod)
        {
            var answer = MessageBox.Show("Delete this JSON mod from the manager?\r\n\r\n" + mod.Name + "\r\n\r\nThis removes it from mods/ but does not edit game files.", "Delete mod", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            try
            {
                if (File.Exists(mod.Path)) File.Delete(mod.Path);
                else if (Directory.Exists(mod.Path)) Directory.Delete(mod.Path, true);
                Log("Deleted mod: " + mod.Name + ".");

                // Surgical UI update - drop just this card instead of rebuilding the whole list (which flickers + scroll-resets).
                if (modCards.ContainsKey(mod.Path))
                {
                    var card = modCards[mod.Path];
                    if (modCardsHost != null) modCardsHost.Controls.Remove(card);
                    card.Dispose();
                    modCards.Remove(mod.Path);
                }
                modCardPills.Remove(mod.Path);
                activeBoxes.Remove(mod.Path);
                groupBy.Remove(mod.Path);
                nexusLinks.Remove(mod.Path);
                mods.RemoveAll(m => string.Equals(m.Path, mod.Path, StringComparison.OrdinalIgnoreCase));
                if (string.Equals(focusedModPath, mod.Path, StringComparison.OrdinalIgnoreCase)) focusedModPath = "";

                // Drop any per-patch toggles that belonged to this mod from the persisted set.
                var deadPrefix = Path.GetFileName(mod.Path) + "|";
                disabledPatches.RemoveWhere(k => k.StartsWith(deadPrefix, StringComparison.OrdinalIgnoreCase));
                config["disabledPatches"] = disabledPatches.ToArray();
                // Drop from active mods list in config too.
                var activeNames = (config.ContainsKey("activeMods") && config["activeMods"] is object[] aArr ? aArr : new object[0])
                    .Select(o => Convert.ToString(o))
                    .Where(s => !string.Equals(s, Path.GetFileName(mod.Path), StringComparison.OrdinalIgnoreCase))
                    .Cast<object>()
                    .ToArray();
                config["activeMods"] = activeNames;
                SaveConfig(config);

                RebuildPresetRail();
                RefreshPatchList();
                UpdateStatusPills();
                UpdateBottomSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not delete mod: " + ex.Message, "Delete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisableAllMods()
        {
            // Revert in priority order: real paz/pamt apply first, then any legacy loose-file writes.
            try { RevertPazAppend(); } catch (Exception ex) { Log("Paz/pamt revert: " + ex.Message); }
            try { RevertLooseFileApply(); } catch (Exception ex) { Log("Loose-file revert: " + ex.Message); }
            foreach (var box in activeBoxes.Values)
            {
                box.Checked = false;
            }
            Log("Uninstalled/deactivated all active JSON mods.");
        }

        private void UpdateStatusPills()
        {
            if (statusGamePill != null)
            {
                var ok = IsGameFolder(gamePath);
                statusGamePill.Text = ok ? "Game OK" : "No game";
                statusGamePill.DotColor = ok ? Color.FromArgb(101, 197, 134) : currentTheme.Accent;
            }
            if (statusModsPill != null)
            {
                var n = activeBoxes.Values.Count(box => box.Checked);
                statusModsPill.Text = n + " active";
                statusModsPill.DotColor = n > 0 ? currentTheme.Accent : Color.FromArgb(112, 104, 79);
            }
        }

        private void UpdateBottomSummary()
        {
            if (summaryLabel == null) return;
            int activeCount = activeBoxes.Values.Count(b => b.Checked);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods)
            {
                if (!activeBoxes.ContainsKey(mod.Path) || !activeBoxes[mod.Path].Checked) continue;
                var group = GroupFor(mod);
                foreach (var c in mod.ChangesForGroup(group))
                {
                    if (!string.IsNullOrEmpty(c.GameFile)) files.Add(c.GameFile);
                }
            }
            summaryLabel.Text = "Preview: " + activeCount + " active mod" + (activeCount == 1 ? "" : "s") +
                                ", " + files.Count + " game file" + (files.Count == 1 ? "" : "s") + " touched, overlay install only, originals remain untouched.";
        }

        private void UpdateInspectorBackup()
        {
            if (checkBackup == null) return;
            var hasAppBackup = File.Exists(AppPapgtBackupPath());
            var hasGameBackup = !string.IsNullOrEmpty(GamePapgtBackupPath()) && File.Exists(GamePapgtBackupPath());
            if (hasAppBackup || hasGameBackup)
            {
                checkBackup.SetState("ready", BadgeKind.Ok);
            }
            else
            {
                checkBackup.SetState("missing", BadgeKind.Warn);
            }
        }

        // ---------------------------------------------------------- GAME FOLDER

        private void BrowseGameFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose Crimson Desert folder, not bin64" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    gamePathText.Text = dialog.SelectedPath;
                    SaveGamePath();
                }
            }
        }

        private void SaveGamePath()
        {
            var path = NormalizeGameFolderInput(gamePathText.Text);
            if (!IsGameFolder(path))
            {
                MessageBox.Show("Choose the Crimson Desert folder that contains bin64 and 0008.\r\n\r\nLinux/Wine users can paste the full path manually, including dot folders such as ~/.steam.", "Invalid folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            gamePath = path;
            gamePathText.Text = path;
            config["gamePath"] = path;
            SaveConfig(config);
            EnsurePapgtBackup(false);
            RefreshAsi();
            UpdateStatusPills();
            UpdateInspectorBackup();
            Log("Saved game folder: " + path);
        }

        private void AddJsonMods()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "Mods (*.json;*.zip;*.7z;*.rar;*.asi;*.dll;*.ini)|*.json;*.zip;*.7z;*.rar;*.asi;*.dll;*.ini|JSON mods (*.json)|*.json|Archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|ASI/DLL/INI (*.asi;*.dll;*.ini)|*.asi;*.dll;*.ini",
                Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ImportPaths(dialog.FileNames);
            }
        }

        private void AddModFolder()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Choose a mod folder: RAW, Browser/UI, or a folder containing JSON/ZIP/ASI files"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ImportPaths(new[] { dialog.SelectedPath });
            }
        }

        private void ShowImportMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Import files...", null, (sender, args) => AddJsonMods());
            menu.Items.Add("Import folder...", null, (sender, args) => AddModFolder());
            var point = anchor.PointToClient(Cursor.Position);
            if (!anchor.ClientRectangle.Contains(point))
                point = new Point(anchor.Width / 2, anchor.Height / 2);
            menu.Show(anchor, point);
        }

        private void DropZoneDragEnter(object sender, DragEventArgs args)
        {
            if (!args.Data.GetDataPresent(DataFormats.FileDrop))
            {
                args.Effect = DragDropEffects.None;
                return;
            }

            var paths = args.Data.GetData(DataFormats.FileDrop) as string[];
            args.Effect = paths != null && paths.Any(IsImportCandidate) ? DragDropEffects.Copy : DragDropEffects.None;
            if (args.Effect == DragDropEffects.Copy && dropZone != null)
            {
                dropZone.PulseAccent = true;
                dropZone.AccentColor = currentTheme.Accent;
                dropZone.Invalidate();
            }
        }

        private void DropZoneDragDrop(object sender, DragEventArgs args)
        {
            if (dropZone != null)
            {
                dropZone.PulseAccent = false;
                dropZone.Invalidate();
            }

            var paths = args.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;
            ImportPaths(paths);
        }

        private bool IsImportCandidate(string path)
        {
            if (File.Exists(path))
            {
                var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                return ext == ".json" || IsSupportedArchiveExtension(ext) || ext == ".asi" || ext == ".dll" || ext == ".ini";
            }
            if (Directory.Exists(path))
            {
                if (IsRawOverlayDirectory(path) || IsBrowserModDirectory(path)) return true;
                if (Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).Any()) return true;
                if (Directory.GetFiles(path, "*.zip", SearchOption.AllDirectories).Any()) return true;
                if (Directory.GetFiles(path, "*.7z", SearchOption.AllDirectories).Any()) return true;
                if (Directory.GetFiles(path, "*.rar", SearchOption.AllDirectories).Any()) return true;
                if (Directory.GetFiles(path, "*.asi", SearchOption.AllDirectories).Any()) return true;
                if (Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories).Any()) return true;
                if (Directory.GetFiles(path, "*.ini", SearchOption.AllDirectories).Any()) return true;
            }
            return false;
        }

        private void ImportPaths(IEnumerable<string> paths)
        {
            var jsonCandidates = new List<string>();
            var asiCandidates = new List<string>();
            var packageCandidates = new List<string>();
            var archiveCandidates = new List<string>();

            foreach (var path in paths)
            {
                CollectImportCandidates(path, jsonCandidates, asiCandidates, archiveCandidates, packageCandidates);
            }

            foreach (var archive in archiveCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var extractRoot = ExtractArchiveForImport(archive);
                    CollectImportCandidates(extractRoot, jsonCandidates, asiCandidates, new List<string>(), packageCandidates);
                    AddExtractedPackageCandidates(extractRoot, packageCandidates);
                    Log("Unpacked archive mod: " + Path.GetFileName(archive));
                }
                catch (Exception ex)
                {
                    packageCandidates.Add(archive);
                    Log("Could not unpack archive mod " + Path.GetFileName(archive) + ": " + ex.Message);
                }
            }

            var imported = 0;
            var skipped = 0;
            foreach (var file in jsonCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    JsonMod.Load(file, json);
                    var target = Path.Combine(modsDir, Path.GetFileName(file));
                    if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(file, target, true);
                    }
                    imported++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    Log("Skipped invalid JSON mod " + Path.GetFileName(file) + ": " + ex.Message);
                }
            }

            int packagesSkipped = 0;
            foreach (var package in packageCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(package) && IsSupportedArchiveExtension(Path.GetExtension(package))) continue;
                    if (Directory.Exists(package) && IsRawOverlayDirectory(package))
                    {
                        CopyDirectory(package, Path.Combine(modsDir, SafePackageName(Path.GetFileName(package))));
                        imported++;
                        Log("Imported RAW overlay mod: " + Path.GetFileName(package));
                    }
                    else if (Directory.Exists(package) && IsBrowserModDirectory(package))
                    {
                        CopyDirectory(package, Path.Combine(modsDir, SafePackageName(Path.GetFileName(package))));
                        imported++;
                        Log("Imported Browser/UI mod: " + Path.GetFileName(package));
                    }
                    else
                    {
                        packagesSkipped++;
                        Log("Unrecognised mod package: " + Path.GetFileName(package));
                    }
                }
                catch (Exception ex)
                {
                    packagesSkipped++;
                    Log("Could not import package " + Path.GetFileName(package) + ": " + ex.Message);
                }
            }

            int asiImported = 0;
            if (asiCandidates.Count > 0)
            {
                if (!IsGameFolder(gamePath))
                {
                    Log("ASI/DLL/INI files dropped but game folder is not set. Set Crimson Desert folder, then drop again.");
                }
                else
                {
                    var bin64 = Path.Combine(gamePath, "bin64");
                    foreach (var file in asiCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Copy(file, Path.Combine(bin64, Path.GetFileName(file)), true);
                            File.Copy(file, Path.Combine(asiDir, Path.GetFileName(file)), true);
                            asiImported++;
                        }
                        catch (Exception ex)
                        {
                            Log("Could not import " + Path.GetFileName(file) + ": " + ex.Message);
                        }
                    }
                }
            }

            LoadMods();
            if (asiImported > 0) RefreshAsi();
            Log("Imported " + imported + " JSON, " + asiImported + " ASI/DLL/INI" + (packagesSkipped > 0 ? ", detected " + packagesSkipped + " package(s) not yet apply-ready" : "") + (skipped > 0 ? ", skipped " + skipped + "." : "."));
        }

        private void AddExtractedPackageCandidates(string extractRoot, List<string> packageCandidates)
        {
            if (!Directory.Exists(extractRoot)) return;

            foreach (var dir in Directory.GetDirectories(extractRoot, "*", SearchOption.AllDirectories)
                .OrderBy(d => d.Length))
            {
                if (packageCandidates.Any(existing => string.Equals(existing, dir, StringComparison.OrdinalIgnoreCase))) continue;
                if (IsRawOverlayDirectory(dir) || IsBrowserModDirectory(dir))
                {
                    packageCandidates.Add(dir);
                }
            }
        }

        private static string SafePackageName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "mod";
            return Regex.Replace(name, @"[^A-Za-z0-9 ._()-]+", "_").Trim();
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(destination, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private void CollectImportCandidates(string path, List<string> jsonCandidates, List<string> asiCandidates, List<string> archiveCandidates, List<string> packageCandidates)
        {
            if (File.Exists(path))
            {
                var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                if (ext == ".json") jsonCandidates.Add(path);
                else if (IsSupportedArchiveExtension(ext)) archiveCandidates.Add(path);
                else if (ext == ".asi" || ext == ".dll" || ext == ".ini") asiCandidates.Add(path);
                return;
            }

            if (!Directory.Exists(path)) return;
            if (IsRawOverlayDirectory(path) || IsBrowserModDirectory(path))
            {
                packageCandidates.Add(path);
                return;
            }

            foreach (var dir in Directory.GetDirectories(path))
            {
                CollectImportCandidates(dir, jsonCandidates, asiCandidates, archiveCandidates, packageCandidates);
            }

            jsonCandidates.AddRange(Directory.GetFiles(path, "*.json", SearchOption.TopDirectoryOnly));
            archiveCandidates.AddRange(Directory.GetFiles(path, "*.zip", SearchOption.TopDirectoryOnly));
            archiveCandidates.AddRange(Directory.GetFiles(path, "*.7z", SearchOption.TopDirectoryOnly));
            archiveCandidates.AddRange(Directory.GetFiles(path, "*.rar", SearchOption.TopDirectoryOnly));
            asiCandidates.AddRange(Directory.GetFiles(path, "*.asi", SearchOption.TopDirectoryOnly));
            asiCandidates.AddRange(Directory.GetFiles(path, "*.dll", SearchOption.TopDirectoryOnly));
            asiCandidates.AddRange(Directory.GetFiles(path, "*.ini", SearchOption.TopDirectoryOnly));
        }

        private string ExtractArchiveForImport(string archivePath)
        {
            var importsRoot = Path.Combine(Path.GetTempPath(), "UJMM", "imports");
            Directory.CreateDirectory(importsRoot);
            var safeName = Regex.Replace(Path.GetFileNameWithoutExtension(archivePath) ?? "archive", @"[^A-Za-z0-9._-]+", "_");
            if (safeName.Length > 36) safeName = safeName.Substring(0, 36).TrimEnd('.', '_', '-');
            var extractRoot = Path.Combine(importsRoot, safeName + "_" + DateTime.Now.ToString("HHmmssfff"));
            Directory.CreateDirectory(extractRoot);
            ExtractArchiveToDirectory(archivePath, extractRoot);
            return extractRoot;
        }

        private static bool IsSupportedArchiveExtension(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            return ext == ".zip" || ext == ".7z" || ext == ".rar";
        }

        private static void ExtractArchiveToDirectory(string archivePath, string extractRoot)
        {
            var ext = (Path.GetExtension(archivePath) ?? "").ToLowerInvariant();
            if (ext == ".zip")
            {
                ExtractZipToDirectorySafe(archivePath, extractRoot);
                return;
            }
            if (ext == ".7z" || ext == ".rar")
            {
                ExtractWithSevenZip(archivePath, extractRoot);
                return;
            }
            throw new InvalidDataException("Unsupported archive type: " + ext);
        }

        private static void ExtractZipToDirectorySafe(string zipPath, string extractRoot)
        {
            using (var stream = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var destination = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName));
                    if (!destination.StartsWith(Path.GetFullPath(extractRoot), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("ZIP contains an unsafe path: " + entry.FullName);

                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using (var input = entry.Open())
                    using (var output = File.Create(destination))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        private static void ExtractWithSevenZip(string archivePath, string extractRoot)
        {
            var sevenZip = LocateSevenZip();
            if (string.IsNullOrEmpty(sevenZip))
                throw new InvalidOperationException("7-Zip extractor was not found. Bundle tools\\7zip\\7z.exe with UJMM or install 7-Zip.");

            var psi = new ProcessStartInfo
            {
                FileName = sevenZip,
                Arguments = "x -y -o\"" + extractRoot + "\" -- \"" + archivePath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(sevenZip)
            };
            using (var process = Process.Start(psi))
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidDataException("7-Zip failed (" + process.ExitCode + "): " + (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));
            }
        }

        private static string LocateSevenZip()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(appDir, "tools", "7zip", "7z.exe"),
                Path.Combine(appDir, "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        private bool IsRawOverlayDirectory(string path)
        {
            if (IsRawOverlayPayloadDirectory(path)) return true;
            if (IsLooseRawOverlayRoot(path)) return true;
            if (!File.Exists(Path.Combine(path, "modinfo.json"))) return false;
            if (Directory.GetDirectories(path).Any(IsRawOverlayPayloadDirectory)) return true;

            var filesRoot = Path.Combine(path, "files");
            if (Directory.Exists(filesRoot) && Directory.GetDirectories(filesRoot).Any(IsRawOverlayPayloadDirectory)) return true;

            try
            {
                var info = json.DeserializeObject(File.ReadAllText(Path.Combine(path, "modinfo.json"), Encoding.UTF8)) as Dictionary<string, object>;
                if (info != null)
                {
                    var configuredRoot = Convert.ToString(info.ContainsKey("files_dir") ? info["files_dir"] : "");
                    if (!string.IsNullOrWhiteSpace(configuredRoot))
                    {
                        var root = Path.Combine(path, configuredRoot);
                        if (Directory.Exists(root) && Directory.GetDirectories(root).Any(IsRawOverlayPayloadDirectory)) return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsRawOverlayPayloadDirectory(string path)
        {
            return Directory.Exists(path)
                && Regex.IsMatch(Path.GetFileName(path), @"^\d{4}$")
                && (File.Exists(Path.Combine(path, "0.pamt"))
                    || File.Exists(Path.Combine(path, "0.paz"))
                    || Directory.GetFiles(path, "*", SearchOption.AllDirectories).Any());
        }

        private static bool IsLooseRawOverlayRoot(string path)
        {
            if (!Directory.Exists(path)) return false;
            if (Regex.IsMatch(Path.GetFileName(path), @"^\d{4}$")) return false;

            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            if (files.Length == 0) return false;

            return files.Any(file =>
            {
                if (IsPackageMetadataFile(path, file)) return false;
                var rel = RelativePath(path, file).Replace('\\', '/');
                var slash = rel.IndexOf('/');
                if (slash <= 0) return false;
                var rootName = rel.Substring(0, slash);
                return IsLikelyGameOverlayRoot(rootName);
            });
        }

        private static bool IsLikelyGameOverlayRoot(string rootName)
        {
            var knownRoots = new[]
            {
                "character", "ui", "gamedata", "gamecommondata", "sound", "audio", "music",
                "effect", "texture", "font", "localization", "script", "datasheet", "world",
                "level", "prefab"
            };
            return knownRoots.Any(root => string.Equals(root, rootName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPackageMetadataFile(string root, string file)
        {
            var rel = RelativePath(root, file).Replace('\\', '/');
            if (rel.IndexOf('/') >= 0) return false;
            var name = Path.GetFileName(file);
            return string.Equals(name, "mod.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "modinfo.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase);
        }

        private static string RelativePath(string root, string path)
        {
            return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private bool IsBrowserModDirectory(string path)
        {
            var manifestPath = Path.Combine(path, "manifest.json");
            if (!File.Exists(manifestPath)) return false;
            try
            {
                var root = json.DeserializeObject(File.ReadAllText(manifestPath, Encoding.UTF8)) as Dictionary<string, object>;
                return root != null && string.Equals(Convert.ToString(root.ContainsKey("format") ? root["format"] : ""), "crimson_browser_mod_v1", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void AddAsiFiles()
        {
            if (!IsGameFolder(gamePath))
            {
                MessageBox.Show("Set the Crimson Desert folder first.", "Game folder missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var dialog = new OpenFileDialog { Filter = "ASI/DLL/INI (*.asi;*.dll;*.ini)|*.asi;*.dll;*.ini|All files (*.*)|*.*", Multiselect = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var bin64 = Path.Combine(gamePath, "bin64");
                foreach (var file in dialog.FileNames)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".asi" && ext != ".dll" && ext != ".ini") continue;
                    File.Copy(file, Path.Combine(bin64, Path.GetFileName(file)), true);
                    File.Copy(file, Path.Combine(asiDir, Path.GetFileName(file)), true);
                }
                RefreshAsi();
                Log("Imported ASI/DLL/INI files into bin64 and mods/_asi.");
            }
        }

        private void RefreshAsi()
        {
            if (asiList == null) return;
            asiList.Items.Clear();
            if (!IsGameFolder(gamePath))
            {
                if (loaderLabel != null) loaderLabel.Text = "ASI loader: bin64 not found";
                return;
            }

            var bin64 = Path.Combine(gamePath, "bin64");
            var loaderNames = new[] { "dinput8.dll", "xinput1_4.dll", "xinput1_3.dll", "dsound.dll", "winmm.dll", "version.dll" };
            var loaders = loaderNames.Where(name => File.Exists(Path.Combine(bin64, name))).ToList();
            if (loaderLabel != null)
            {
                loaderLabel.Text = "ASI loader: " + (loaders.Count == 0 ? "not detected" : string.Join(", ", loaders));
            }

            // Show only ASI mods (and their per-mod .ini sidecars) - not the hundreds of game-shipped DLLs.
            var asiFiles = Directory.GetFiles(bin64)
                .Where(path => path.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".asi.disabled", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName)
                .ToList();
            // Pair each ASI with any .ini that has the same stem (e.g. MyMod.asi + MyMod.ini)
            // .NET FW 4 String.Replace doesn't take StringComparison; the inner GetFileNameWithoutExtension already strips ".disabled" off, so a plain replace works for all-lowercase stems.
            var asiStems = new HashSet<string>(asiFiles.Select(p =>
            {
                var stem = Path.GetFileNameWithoutExtension(p);
                if (stem.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - 4);
                return stem;
            }), StringComparer.OrdinalIgnoreCase);
            var iniSidecars = Directory.GetFiles(bin64, "*.ini")
                .Where(p => asiStems.Contains(Path.GetFileNameWithoutExtension(p)))
                .OrderBy(Path.GetFileName)
                .ToList();
            var files = asiFiles.Concat(iniSidecars).ToList();

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var lower = name.ToLowerInvariant();
                var status = lower.EndsWith(".asi.disabled") ? "disabled" :
                    lower.EndsWith(".asi") ? "enabled" :
                    lower.EndsWith(".ini") ? "config" : "unknown";
                var item = new ListViewItem(name) { Tag = file };
                item.SubItems.Add(status);
                item.SubItems.Add(new FileInfo(file).Length.ToString());
                asiList.Items.Add(item);
            }
            if (files.Count == 0)
            {
                var empty = new ListViewItem("(no ASI mods installed)") { ForeColor = Color.FromArgb(120, 110, 90) };
                empty.SubItems.Add("");
                empty.SubItems.Add("");
                asiList.Items.Add(empty);
            }
        }

        private void ToggleSelectedAsi()
        {
            foreach (ListViewItem item in asiList.SelectedItems)
            {
                var path = Convert.ToString(item.Tag);
                if (path.EndsWith(".asi.disabled", StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(path, path.Substring(0, path.Length - ".disabled".Length));
                }
                else if (path.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(path, path + ".disabled");
                }
            }
            RefreshAsi();
        }

        private void RunValidation()
        {
            if (!IsGameFolder(gamePath))
            {
                MessageBox.Show("Set the Crimson Desert folder first.", "Game folder missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var selected = mods.Where(mod => activeBoxes.ContainsKey(mod.Path) && activeBoxes[mod.Path].Checked).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Enable at least one mod first.", "No mods selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var validationCacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".cache", "validation_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(validationCacheDir);
            ResetFieldResolutions(selected);
            ResolveFieldFormatMods(selected, validationCacheDir);
            Log("Starting mod match verification...");
            var checkedCount = 0;
            var overlayChecked = 0;
            var alreadyPatched = 0;
            var issues = new List<string>();

            foreach (var mod in selected)
            {
                if (IsOverlayMod(mod))
                {
                    foreach (var folder in mod.OverlayFolders)
                    {
                        var files = OverlayFiles(folder).Where(File.Exists).ToList();
                        if (files.Count == 0)
                        {
                            issues.Add("EMPTY OVERLAY [" + mod.Name + "] " + Path.GetFileName(folder));
                            continue;
                        }
                        foreach (var file in files)
                        {
                            overlayChecked++;
                            checkedCount++;
                        }
                    }
                    continue;
                }
                var group = GroupFor(mod);
                foreach (var change in mod.ChangesForGroup(group))
                {
                    if (!change.IsResolvedBytes)
                    {
                        issues.Add("UNRESOLVED [" + mod.Name + "] " + change.Label + " (" + change.GameFile + "). " + (change.ResolveError ?? "Field-format schema resolution failed."));
                        continue;
                    }
                    var file = ArchiveExtractor.Extract(gamePath, change.GameFile, validationCacheDir, Log);
                    if (file == null || !File.Exists(file))
                    {
                        issues.Add("Missing extracted file: " + change.GameFile);
                        continue;
                    }

                    var data = File.ReadAllBytes(file);
                    var original = HexToBytes(change.Original);
                    var patched = HexToBytes(change.Patched);
                    if (change.Offset < 0 || change.Offset + original.Length > data.Length)
                    {
                        issues.Add("Out of range: " + change.Label);
                        continue;
                    }

                    var actual = data.Skip(change.Offset).Take(original.Length).ToArray();
                    checkedCount++;
                    if (BytesEqual(actual, original)) continue;
                    if (BytesEqual(actual, patched))
                    {
                        alreadyPatched++;
                        continue;
                    }
                    issues.Add("MISMATCH [" + mod.Name + "] " + change.Label + " @ " + change.GameFile + "+0x" + change.Offset.ToString("X") + ": expected " + change.Original.ToLowerInvariant() + ", got " + BytesToHex(actual));
                }
            }

            if (inspectorCounter != null) inspectorCounter.Text = checkedCount + " checked";
            if (checkGuards != null)
            {
                if (issues.Count == 0) checkGuards.SetState(checkedCount + " ok", BadgeKind.Ok);
                else checkGuards.SetState(issues.Count + " mismatch", BadgeKind.Bad);
            }
            if (checkOverlay != null) checkOverlay.SetState(overlayChecked == 0 ? "byte patches" : overlayChecked + " file" + (overlayChecked == 1 ? "" : "s"), BadgeKind.Ok);
            if (checkConflicts != null) checkConflicts.SetState(alreadyPatched + " already applied", alreadyPatched == 0 ? BadgeKind.Neutral : BadgeKind.Ok);

            if (issues.Count == 0)
            {
                Log("Match check passed.");
                MessageBox.Show("Mods match this game version - every patch's original bytes match what's installed.", "Match check passed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                foreach (var issue in issues.Take(80)) Log(issue);
                if (issues.Count > 80) Log("... plus " + (issues.Count - 80) + " more issue(s).");
                MessageBox.Show("Some patches don't match the installed game (likely a Crimson Desert update). Check the inspector log for details - those mods may need to be updated for this game version.", "Match failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void ResetFieldResolutions(IEnumerable<JsonMod> selected)
        {
            foreach (var change in selected.SelectMany(m => m.Changes))
            {
                if (string.IsNullOrWhiteSpace(change.FieldPath)) continue;
                change.Offset = 0;
                change.Original = "";
                change.Patched = "";
                change.IsResolvedBytes = false;
                change.ResolveError = "";
            }
        }

        // ---------------------------------------------------------- APPLY MODS (paz append + pamt redirect)
        //
        // For each modded file:
        //   1. Find the matching PAMT entry, read original compressed bytes from .paz.
        //   2. Decompress, apply patches, recompress (LZ4 all-literals).
        //   3. APPEND the recompressed bytes to the .paz file (original data untouched).
        //   4. Surgically rewrite the entry's pazOffset/compSize/origSize in the PAMT.
        //
        // Backups (one-time, per archive):
        //   _jmm_backups\0008\0.pamt.original           - full PAMT bytes pre-apply
        //   _jmm_backups\0008\<N>.paz.length.original   - original byte length of the .paz
        // Revert truncates each .paz back to its recorded length and restores the PAMT.
        // No multi-GB paz copies, just length tracking + header replay.

        private const string LooseManifestName = "loose_manifest.json";   // legacy probe manifest
        private const string ProbeBackupsDirName = "_ujmm_probe_backups"; // legacy probe backups

        private string LooseManifestPath()
        {
            return Path.Combine(backupsDir, LooseManifestName);
        }

        private string ProbeBackupsRoot()
        {
            return IsGameFolder(gamePath) ? Path.Combine(gamePath, ProbeBackupsDirName) : "";
        }

        private string PazBackupsRoot()
        {
            return IsGameFolder(gamePath) ? Path.Combine(gamePath, "_jmm_backups", "0008") : "";
        }

        private sealed class FieldEntry
        {
            public string Name;
            public int EntryOffset;
            public int BlobStart;
            public int BlobSize;
        }

        private static Tuple<Dictionary<string, FieldEntry>, List<FieldEntry>> BuildFieldEntryMap(byte[] pabgb, byte[] pabgh)
        {
            var list = new List<FieldEntry>();
            bool shortHeader = false;
            int count16 = BitConverter.ToUInt16(pabgh, 0);
            int count;
            int headerSize;
            int recordSize;
            if (count16 > 0 && 2 + count16 * 6 == pabgh.Length)
            {
                count = count16;
                headerSize = 2;
                recordSize = 6;
                shortHeader = true;
            }
            else if (count16 > 0 && 2 + count16 * 8 <= pabgh.Length && 2 + count16 * 8 >= pabgh.Length - 16)
            {
                count = count16;
                headerSize = 2;
                recordSize = 8;
            }
            else
            {
                count = BitConverter.ToInt32(pabgh, 0);
                headerSize = 4;
                recordSize = 8;
            }

            int maxCount = Math.Max(0, (pabgh.Length - headerSize) / recordSize);
            if (count > maxCount) count = maxCount;
            int nameLenPrefix = shortHeader ? 2 : 4;
            for (int i = 0; i < count; i++)
            {
                int recOff = headerSize + i * recordSize;
                if (recOff + recordSize > pabgh.Length) break;
                int entryOffset = BitConverter.ToInt32(pabgh, recOff + (shortHeader ? 2 : 4));
                if (entryOffset < 0 || entryOffset + nameLenPrefix + 4 > pabgb.Length) continue;
                int nameLen = BitConverter.ToInt32(pabgb, entryOffset + nameLenPrefix);
                if (nameLen <= 0 || nameLen > 512 || entryOffset + nameLenPrefix + 4 + nameLen > pabgb.Length) continue;
                string name;
                try { name = Encoding.UTF8.GetString(pabgb, entryOffset + nameLenPrefix + 4, nameLen); }
                catch { continue; }
                int blobStart = entryOffset + nameLenPrefix + 4 + nameLen;
                list.Add(new FieldEntry { Name = name, EntryOffset = entryOffset, BlobStart = blobStart });
            }

            list.Sort((a, b) => a.EntryOffset.CompareTo(b.EntryOffset));
            for (int i = 0; i < list.Count; i++)
            {
                int next = i + 1 < list.Count ? list[i + 1].EntryOffset : pabgb.Length;
                list[i].BlobSize = Math.Max(0, next - list[i].BlobStart);
            }

            var map = new Dictionary<string, FieldEntry>(StringComparer.Ordinal);
            foreach (var entry in list)
            {
                if (!map.ContainsKey(entry.Name)) map[entry.Name] = entry;
            }
            return Tuple.Create(map, list);
        }

        private void ResolveFieldFormatMods(IEnumerable<JsonMod> selected)
        {
            ResolveFieldFormatMods(selected, cacheDir);
        }

        private void ResolveFieldFormatMods(IEnumerable<JsonMod> selected, string extractCacheDir)
        {
            var unresolved = selected.SelectMany(m => m.Changes).Where(c => !c.IsResolvedBytes && !string.IsNullOrWhiteSpace(c.FieldPath)).ToList();
            if (unresolved.Count == 0) return;

            var maps = new Dictionary<string, Tuple<byte[], Dictionary<string, FieldEntry>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in unresolved)
            {
                try
                {
                    if (!string.Equals(change.Operation ?? "set", "set", StringComparison.OrdinalIgnoreCase))
                    {
                        change.ResolveError = "Unsupported field op '" + change.Operation + "'.";
                        continue;
                    }

                    var gameFile = change.GameFile ?? "";
                    if (!maps.ContainsKey(gameFile))
                    {
                        var dataPath = ArchiveExtractor.Extract(gamePath, gameFile, extractCacheDir, Log);
                        var schemaFile = Path.ChangeExtension(gameFile, ".pabgh");
                        var schemaPath = ArchiveExtractor.Extract(gamePath, schemaFile, extractCacheDir, Log);
                        if (dataPath == null || !File.Exists(dataPath) || schemaPath == null || !File.Exists(schemaPath))
                        {
                            change.ResolveError = "Could not extract " + gameFile + " and its .pabgh companion.";
                            continue;
                        }
                        var data = File.ReadAllBytes(dataPath);
                        var schema = File.ReadAllBytes(schemaPath);
                        var builtMap = BuildFieldEntryMap(data, schema).Item1;
                        maps[gameFile] = Tuple.Create(data, builtMap);
                    }

                    var tuple = maps[gameFile];
                    var bytes = tuple.Item1;
                    var entryMap = tuple.Item2;
                    FieldEntry entry;
                    if (!entryMap.TryGetValue(change.EntryName ?? "", out entry))
                    {
                        change.ResolveError = "Entry '" + change.EntryName + "' was not found in " + gameFile + ".";
                        continue;
                    }

                    int offset;
                    int length;
                    byte[] patched;
                    if (TryResolveKnownField(change, entry, out offset, out length, out patched))
                    {
                        if (offset < 0 || offset + length > bytes.Length)
                        {
                            change.ResolveError = "Resolved field offset is outside " + gameFile + ".";
                            continue;
                        }
                        change.Offset = offset;
                        change.Original = BytesToHex(bytes.Skip(offset).Take(length).ToArray());
                        change.Patched = BytesToHex(patched);
                        change.IsResolvedBytes = true;
                        change.ResolveError = "";
                        if (!string.IsNullOrWhiteSpace(change.TargetDisplay))
                        {
                            change.TargetDisplay = change.TargetDisplay + "  @ +0x" + offset.ToString("X");
                        }
                    }
                    else
                    {
                        change.ResolveError = "Unsupported field path '" + change.FieldPath + "' in " + Path.GetFileName(gameFile) + ".";
                    }
                }
                catch (Exception ex)
                {
                    change.ResolveError = ex.Message;
                }
            }

            int resolved = unresolved.Count(c => c.IsResolvedBytes);
            if (resolved > 0)
            {
                Log("Resolved " + resolved + " field-format patch(es) to byte offsets.");
                RefreshPatchList();
            }
        }

        private static bool TryResolveKnownField(PatchChange change, FieldEntry entry, out int offset, out int length, out byte[] patched)
        {
            offset = -1;
            length = 0;
            patched = null;

            var file = Path.GetFileName(change.GameFile ?? "").ToLowerInvariant();
            var field = (change.FieldPath ?? "").Trim();
            if (file == "iteminfo.pabgb" && string.Equals(field, "max_stack_count", StringComparison.OrdinalIgnoreCase))
            {
                offset = entry.BlobStart;
                length = 4;
                patched = BitConverter.GetBytes(Convert.ToInt32(change.NewValue, System.Globalization.CultureInfo.InvariantCulture));
                return true;
            }

            var match = Regex.Match(field, @"^faction_schedule_list\[(\d+)\]\.flag_d$", RegexOptions.IgnoreCase);
            if (file == "factionnode.pabgb" && match.Success)
            {
                int scheduleIndex = Convert.ToInt32(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int rel;
                if (TryKnownFactionScheduleFlagOffset(change.EntryName ?? "", scheduleIndex, out rel))
                {
                    offset = entry.BlobStart + rel;
                    length = 1;
                    patched = new[] { Convert.ToByte(Convert.ToInt32(change.NewValue, System.Globalization.CultureInfo.InvariantCulture)) };
                    return true;
                }
            }

            return false;
        }

        private static bool TryKnownFactionScheduleFlagOffset(string entryName, int scheduleIndex, out int rel)
        {
            rel = -1;
            switch ((entryName ?? "") + "|" + scheduleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                case "Node_Her_ArboriaCraftshop|1": rel = 875; return true;
                case "Node_Her_KarinQuarry|2": rel = 1249; return true;
                case "Node_Her_PervinFort|1": rel = 873; return true;
                case "Node_Her_TimberWagonFactory|1": rel = 879; return true;
                case "Node_Her_TimberWagonFactory|2": rel = 1293; return true;
                case "Node_Her_TimberWagonFactory|3": rel = 1708; return true;
                case "Node_Her_InksworthPrinter|2": rel = 1229; return true;
                case "Node_Her_KilndenKukuWorkshop|1": rel = 958; return true;
                case "Node_Her_ReachwoodRuins|1": rel = 931; return true;
                case "Node_Her_FlorindaleVillage|1": rel = 816; return true;
                case "Node_Her_FlorindaleVillage|2": rel = 1226; return true;
                case "Node_Her_WindHillFactory|1": rel = 936; return true;
                case "Node_Her_WindHillFactory|2": rel = 1421; return true;
                case "Node_Her_WindHillFactory|3": rel = 1906; return true;
                case "Node_Cal_CalphadeRefinery|1": rel = 873; return true;
                case "Node_Cal_CalphadeGunpowderFactory|1": rel = 888; return true;
                case "Node_Cal_CalphadeGunpowderFactory|2": rel = 1308; return true;
                case "Node_Her_SeniaVillage|1": rel = 884; return true;
                case "Node_Dem_DemenissArmory|2": rel = 1243; return true;
                case "Node_Dem_DemenissArmory|3": rel = 1656; return true;
                case "Node_Dem_KingMountainDigSite|1": rel = 966; return true;
                case "Node_Dem_SunsetDyehouse|1": rel = 822; return true;
                case "Node_Dem_BulwarkWeaponFactory|2": rel = 1260; return true;
                case "Node_Del_MarniWorkshop|1": rel = 891; return true;
                case "Node_Del_MarniWorkshop|2": rel = 1299; return true;
                case "Node_Del_MarniWorkshop|3": rel = 1707; return true;
                case "Node_Neut_GorthakIronMaker|2": rel = 1325; return true;
                case "Node_Neut_GorthakIronMaker|3": rel = 1738; return true;
                case "Node_Neut_GorthakIronMaker|4": rel = 2151; return true;
                case "Node_Neut_GorthakIronMaker|5": rel = 2564; return true;
                case "Node_Neut_ZarganTankFactory|2": rel = 1247; return true;
                case "Node_Neut_ZarganTankFactory|3": rel = 1660; return true;
                case "Node_Del_SteelspikeWeaponStorage|2": rel = 1321; return true;
                case "Node_Del_SteelspikeWeaponStorage|3": rel = 1746; return true;
                case "Node_Del_SteelspikeWeaponStorage|4": rel = 2172; return true;
                case "Node_Del_SteelspikeWeaponStorage|5": rel = 2597; return true;
                case "Node_Del_RustfieldScrapyard|2": rel = 1297; return true;
                case "Node_Del_RustfieldScrapyard|3": rel = 1710; return true;
                case "Node_Del_RustfieldScrapyard|4": rel = 2123; return true;
                case "Node_Del_RustfieldScrapyard|5": rel = 2536; return true;
                case "Node_Del_WindCliffFort|1": rel = 881; return true;
                case "Node_Del_SnakeTemple|1": rel = 935; return true;
                case "Node_Del_MarniMechFactory|1": rel = 887; return true;
                case "Node_Del_MarniMechFactory|2": rel = 1299; return true;
                case "Node_Del_MarniMechFactory|3": rel = 1711; return true;
                case "Node_Del_MarniMechFactory|4": rel = 2123; return true;
                case "Node_Del_MarniAirLab|1": rel = 880; return true;
                case "Node_Del_MarniAirLab|2": rel = 1286; return true;
                case "Node_Del_MarniAirLab|3": rel = 1692; return true;
                case "Node_Kwe_BeighenBasketMaker|1": rel = 805; return true;
                case "Node_Kwe_LongleafTreeVillage|1": rel = 872; return true;
                case "Node_Kwe_WindsongMountain|1": rel = 875; return true;
                case "Node_Kwe_VerheimRuins|1": rel = 914; return true;
                case "Node_Kwe_AshclawKeep|1": rel = 873; return true;
                case "Node_Crim_DesertOasis|1": rel = 852; return true;
                case "Node_Crim_SaltroadCamp|1": rel = 854; return true;
                case "Node_Crim_HelmaraVillage|1": rel = 889; return true;
                case "Node_Crim_HelmaraVillage|2": rel = 1292; return true;
                case "Node_Crim_StompsandFortRuins|1": rel = 930; return true;
                case "Node_Crim_FallenAbyss|1": rel = 912; return true;
                case "Node_Crim_TimewornRuins|1": rel = 918; return true;
            }
            return false;
        }

        private void ApplyOverlayStub()
        {
            ApplyByPazAppend();
        }

        private int ApplyOverlayPackages(List<JsonMod> selected)
        {
            var overlays = selected.Where(m => (m.FormatTag == "RAW" || m.FormatTag == "BROWSER") && m.OverlayFolders != null && m.OverlayFolders.Count > 0).ToList();
            if (overlays.Count == 0) return 0;
            var answer = MessageBox.Show("Install " + overlays.Count + " RAW/Browser overlay mod(s)?\r\n\r\nUJMM will copy compiled overlay folders as-is. Loose RAW/Browser folders will be packed into 0.paz + 0.pamt, then registered in meta\\0.papgt.", "Install overlays?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return 0;
            var papgtLive = Path.Combine(gamePath, "meta", "0.papgt");
            var papgtBackup = Path.GetFullPath(Path.Combine(PazBackupsRoot(), "..", "0.papgt.original"));
            Directory.CreateDirectory(Path.GetDirectoryName(papgtBackup));
            if (File.Exists(papgtLive) && !File.Exists(papgtBackup)) File.Copy(papgtLive, papgtBackup);

            int installed = 0;
            foreach (var mod in overlays)
            {
                foreach (var sourceFolder in mod.OverlayFolders)
                {
                    if (!Directory.Exists(sourceFolder)) continue;
                    var wanted = Path.GetFileName(sourceFolder);
                    var slot = NextOverlaySlot(wanted);
                    var target = Path.Combine(gamePath, slot);
                    bool packedLooseOverlay = false;
                    if (IsCompiledOverlayFolder(sourceFolder))
                    {
                        CopyDirectory(sourceFolder, target);
                    }
                    else
                    {
                        BuildLooseOverlayPackage(sourceFolder, target, gamePath, Log);
                        packedLooseOverlay = true;
                    }
                    var marker = Path.GetFullPath(Path.Combine(PazBackupsRoot(), "..", "overlay_folders.txt"));
                    File.AppendAllText(marker, slot + "\r\n", Encoding.UTF8);
                    var pamtPath = Path.Combine(target, "0.pamt");
                    if (File.Exists(pamtPath))
                    {
                        uint pamtCrc = BitConverter.ToUInt32(File.ReadAllBytes(pamtPath), 0);
                        File.WriteAllBytes(papgtLive, BuildPapgtWithMod(papgtLive, slot, pamtCrc));
                        var installKind = packedLooseOverlay ? "packed loose overlay" : "compiled overlay";
                        Log("Installed " + mod.FormatTag + " " + installKind + " " + mod.Name + " into " + slot + " (PAMT CRC 0x" + pamtCrc.ToString("X8") + ").");
                    }
                    else
                    {
                        Log("Installed " + mod.FormatTag + " loose overlay " + mod.Name + " into " + slot + ".");
                    }
                    installed++;
                }
            }
            if (installed > 0) WriteRestoreGuardManifest("post-overlay-apply");
            return installed;
        }

        private static bool IsCompiledOverlayFolder(string folder)
        {
            return File.Exists(Path.Combine(folder, "0.pamt"))
                && Directory.GetFiles(folder, "*.paz", SearchOption.TopDirectoryOnly).Length > 0;
        }

        private sealed class LooseOverlayFile
        {
            public string SourcePath;
            public string RelativePath;
            public string DirectoryPath;
            public string FileName;
            public byte[] PackedBytes;
            public uint OriginalSize;
            public uint Offset;
            public uint FileNameOffset;
        }

        private static void BuildLooseOverlayPackage(string sourceFolder, string targetFolder, string gameRoot, Action<string> log)
        {
            var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    if (string.Equals(name, "0.pamt", StringComparison.OrdinalIgnoreCase)) return false;
                    if (string.Equals(Path.GetExtension(name), ".paz", StringComparison.OrdinalIgnoreCase)) return false;
                    if (IsPackageMetadataFile(sourceFolder, path)) return false;
                    return true;
                })
                .Select(path =>
                {
                    var rel = path.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    rel = NormalizeOverlayPatchOutputPath(rel);
                    return new LooseOverlayFile
                    {
                        SourcePath = path,
                        RelativePath = rel,
                        DirectoryPath = NormalizeOverlayDirectory(Path.GetDirectoryName(rel) ?? ""),
                        FileName = Path.GetFileName(rel)
                    };
                })
                .Where(file => !string.IsNullOrWhiteSpace(file.RelativePath) && !string.IsNullOrWhiteSpace(file.FileName))
                .OrderBy(file => file.DirectoryPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0) throw new InvalidOperationException("Loose overlay folder has no files to pack: " + sourceFolder);

            Directory.CreateDirectory(targetFolder);
            var paz = new MemoryStream();
            foreach (var file in files)
            {
                var raw = BuildLooseOverlayFileBytes(sourceFolder, file.SourcePath, file.RelativePath, gameRoot, log);
                file.OriginalSize = checked((uint)raw.Length);
                file.PackedBytes = ArchiveExtractor.Lz4BlockCompress(raw);
                var aligned = AlignUp((uint)paz.Position, 16);
                while (paz.Position < aligned) paz.WriteByte(0);
                file.Offset = checked((uint)paz.Position);
                paz.Write(file.PackedBytes, 0, file.PackedBytes.Length);
            }

            var pazBytes = paz.ToArray();
            File.WriteAllBytes(Path.Combine(targetFolder, "0.paz"), pazBytes);
            var pamtBytes = BuildLooseOverlayPamt(files, pazBytes);
            File.WriteAllBytes(Path.Combine(targetFolder, "0.pamt"), pamtBytes);
        }

        private static string NormalizeOverlayPatchOutputPath(string relativePath)
        {
            if (relativePath.EndsWith(".merge", StringComparison.OrdinalIgnoreCase))
                return relativePath.Substring(0, relativePath.Length - ".merge".Length);
            if (relativePath.EndsWith(".patch", StringComparison.OrdinalIgnoreCase))
                return relativePath.Substring(0, relativePath.Length - ".patch".Length);
            return relativePath;
        }

        private static byte[] BuildLooseOverlayFileBytes(string sourceFolder, string sourcePath, string relativePath, string gameRoot, Action<string> log)
        {
            var ext = Path.GetExtension(sourcePath);
            if (!string.Equals(ext, ".merge", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, ".patch", StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadAllBytes(sourcePath);
            }

            var original = ExtractOverlayOriginal(gameRoot, Path.GetFileName(sourceFolder), relativePath);
            if (original == null)
            {
                if (log != null) log("Overlay patch source not found in game archives for " + relativePath + "; packing patch payload only.");
                return File.ReadAllBytes(sourcePath);
            }

            var text = DecodeText(original);
            var patchText = File.ReadAllText(sourcePath, Encoding.UTF8);
            if (string.Equals(ext, ".merge", StringComparison.OrdinalIgnoreCase))
            {
                if (relativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    || relativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    var mergedXml = ApplyXmlMergeDocument(original, patchText, log, relativePath);
                    if (mergedXml != null) return mergedXml;
                }
                var merged = text.TrimEnd() + "\r\n\r\n" + patchText.Trim() + "\r\n";
                return new UTF8Encoding(false).GetBytes(merged);
            }

            var patchedXml = ApplyXmlPatchDocument(original, patchText, log, relativePath);
            if (patchedXml != null) return patchedXml;

            var patched = ApplySimpleXmlPatch(text, patchText, log, relativePath);
            return new UTF8Encoding(false).GetBytes(patched);
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            return Encoding.UTF8.GetString(bytes);
        }

        private sealed class XmlDocContext
        {
            public XDocument Document;
            public Encoding Encoding;
            public bool Wrapped;
        }

        private static XmlDocContext ParseGameXml(byte[] bytes, Action<string> log, string relativePath)
        {
            try
            {
                Encoding encoding;
                string text;
                using (var ms = new MemoryStream(bytes))
                using (var reader = new StreamReader(ms, true))
                {
                    text = reader.ReadToEnd();
                    encoding = reader.CurrentEncoding ?? new UTF8Encoding(false);
                }

                try
                {
                    return new XmlDocContext
                    {
                        Document = XDocument.Parse(text, LoadOptions.PreserveWhitespace),
                        Encoding = encoding,
                        Wrapped = false
                    };
                }
                catch (XmlException)
                {
                    var wrapped = "<__cdmm_root__>" + text + "</__cdmm_root__>";
                    return new XmlDocContext
                    {
                        Document = XDocument.Parse(wrapped, LoadOptions.PreserveWhitespace),
                        Encoding = encoding,
                        Wrapped = true
                    };
                }
            }
            catch (Exception ex)
            {
                if (log != null) log("XML parse failed for " + relativePath + ": " + ex.Message);
                return null;
            }
        }

        private static byte[] SerializeGameXml(XmlDocContext ctx)
        {
            if (ctx == null || ctx.Document == null) return null;
            var encoding = ctx.Encoding ?? new UTF8Encoding(false);
            var settings = new XmlWriterSettings
            {
                Encoding = encoding,
                OmitXmlDeclaration = ctx.Document.Declaration == null,
                Indent = false,
                NewLineHandling = NewLineHandling.None
            };

            if (ctx.Wrapped && ctx.Document.Root != null)
            {
                var sb = new StringBuilder();
                foreach (var node in ctx.Document.Root.Nodes())
                {
                    sb.Append(node.ToString(SaveOptions.DisableFormatting));
                }
                return encoding.GetBytes(sb.ToString());
            }

            using (var ms = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(ms, settings))
                {
                    ctx.Document.WriteTo(writer);
                }
                return ms.ToArray();
            }
        }

        private sealed class XmlPatchOperation
        {
            public string Op;
            public string XPath;
            public string Value;
            public string Attribute;
            public Dictionary<string, string> Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static byte[] ApplyXmlPatchDocument(byte[] originalBytes, string patchText, Action<string> log, string relativePath)
        {
            var ctx = ParseGameXml(originalBytes, log, relativePath);
            if (ctx == null) return null;

            List<XmlPatchOperation> ops;
            string error;
            if (!TryLoadXmlPatchOperations(patchText, out ops, out error))
            {
                if (log != null) log("XML patch parse failed for " + relativePath + ": " + error);
                return null;
            }

            var applied = 0;
            foreach (var op in ops)
            {
                try
                {
                    var matches = ctx.Document.XPathSelectElements(op.XPath).ToList();
                    if (matches.Count == 0)
                    {
                        if (log != null) log("XML patch selector matched no elements in " + relativePath + ": " + op.XPath);
                        continue;
                    }

                    var lower = (op.Op ?? "").ToLowerInvariant();
                    foreach (var el in matches.ToList())
                    {
                        if (lower == "set-attr")
                        {
                            if (!string.IsNullOrWhiteSpace(op.Attribute))
                                el.SetAttributeValue(op.Attribute, op.Value ?? "");
                            foreach (var attr in op.Attributes)
                                el.SetAttributeValue(attr.Key, attr.Value ?? "");
                        }
                        else if (lower == "remove-attr")
                        {
                            if (!string.IsNullOrWhiteSpace(op.Attribute))
                                el.SetAttributeValue(op.Attribute, null);
                            foreach (var attr in op.Attributes.Keys.ToList())
                                el.SetAttributeValue(attr, null);
                        }
                        else if (lower == "remove")
                        {
                            el.Remove();
                        }
                        else if (lower == "replace")
                        {
                            var fragment = TryParseXmlFragment(op.Value);
                            if (fragment != null) el.ReplaceWith(fragment);
                            else el.Value = op.Value ?? "";
                        }
                        else if (lower == "add")
                        {
                            var fragment = TryParseXmlFragment(op.Value);
                            if (fragment != null) el.Add(fragment);
                            else el.Add(new XText(op.Value ?? ""));
                        }
                        else if (lower == "add-before")
                        {
                            var fragment = TryParseXmlFragment(op.Value);
                            if (fragment != null) el.AddBeforeSelf(fragment);
                            else el.AddBeforeSelf(new XText(op.Value ?? ""));
                        }
                        else if (lower == "add-after")
                        {
                            var fragment = TryParseXmlFragment(op.Value);
                            if (fragment != null) el.AddAfterSelf(fragment);
                            else el.AddAfterSelf(new XText(op.Value ?? ""));
                        }
                    }
                    applied++;
                }
                catch (Exception ex)
                {
                    if (log != null) log("XML patch failed for " + relativePath + ": " + ex.Message);
                }
            }
            if (log != null) log("XML patch materialized " + applied + " operation(s) for " + relativePath + ".");
            return SerializeGameXml(ctx);
        }

        private static bool TryLoadXmlPatchOperations(string patchText, out List<XmlPatchOperation> ops, out string error)
        {
            ops = new List<XmlPatchOperation>();
            error = "";
            try
            {
                var root = XDocument.Parse(patchText, LoadOptions.PreserveWhitespace).Root;
                if (root == null)
                {
                    error = "empty patch";
                    return false;
                }

                foreach (var node in root.Elements())
                {
                    var name = node.Name.LocalName.ToLowerInvariant();
                    var op = new XmlPatchOperation();
                    var at = AttributeValue(node, "at");
                    var target = AttributeValue(node, "target");
                    var find = AttributeValue(node, "find");
                    var key = AttributeValue(node, "key");
                    var into = AttributeValue(node, "into");

                    if (name == "set" || name == "set-attr")
                    {
                        op.Op = "set-attr";
                        op.Attribute = AttributeValue(node, "attr") ?? AttributeValue(node, "attribute");
                        op.Value = AttributeValue(node, "value");
                        foreach (var attr in node.Attributes())
                        {
                            var local = attr.Name.LocalName;
                            if (local == "at" || local == "target" || local == "find" || local == "key" || local == "into" || local == "attr" || local == "attribute" || local == "value") continue;
                            if (local.StartsWith("match-", StringComparison.OrdinalIgnoreCase)) continue;
                            op.Attributes[local] = attr.Value;
                        }
                    }
                    else if (name == "unset" || name == "remove-attr")
                    {
                        op.Op = "remove-attr";
                        op.Attribute = AttributeValue(node, "attr") ?? AttributeValue(node, "attribute");
                    }
                    else if (name == "remove" || name == "delete")
                    {
                        op.Op = "remove";
                    }
                    else if (name == "replace")
                    {
                        op.Op = "replace";
                        op.Value = InnerXml(node);
                        foreach (var attr in node.Attributes())
                        {
                            var local = attr.Name.LocalName;
                            if (local == "at" || local == "target" || local == "find" || local == "key") continue;
                            op.Attributes[local] = attr.Value;
                        }
                        if (op.Attributes.Count > 0 && string.IsNullOrWhiteSpace(op.Value))
                        {
                            op.Op = "set-attr";
                        }
                    }
                    else if (name == "add" || name == "insert")
                    {
                        op.Op = "add";
                        op.Value = InnerXml(node);
                        if (!string.IsNullOrWhiteSpace(into)) target = "//" + into;
                    }
                    else if (name == "add-before" || name == "insert-before")
                    {
                        op.Op = "add-before";
                        op.Value = InnerXml(node);
                    }
                    else if (name == "add-after" || name == "insert-after")
                    {
                        op.Op = "add-after";
                        op.Value = InnerXml(node);
                    }
                    else
                    {
                        continue;
                    }

                    op.XPath = ResolvePatchTarget(at ?? target, find, key, node);
                    if (string.IsNullOrWhiteSpace(op.XPath)) continue;
                    ops.Add(op);
                }
                if (ops.Count == 0)
                {
                    error = "no supported operations";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ResolvePatchTarget(string target, string find, string key, XElement node)
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                target = target.Trim();
                if (target.StartsWith("#", StringComparison.Ordinal)) return "//*[@id=" + XPathQuote(target.Substring(1)) + "]";
                if (target.StartsWith(".", StringComparison.Ordinal)) return "//*[contains(concat(' ', normalize-space(@class), ' '), " + XPathQuote(" " + target.Substring(1) + " ") + ")]";
                return target;
            }
            if (!string.IsNullOrWhiteSpace(find))
            {
                var filters = new List<string>();
                if (!string.IsNullOrWhiteSpace(key)) filters.Add("@key=" + XPathQuote(key));
                foreach (var attr in node.Attributes().Where(a => a.Name.LocalName.StartsWith("match-", StringComparison.OrdinalIgnoreCase)))
                {
                    filters.Add("@" + attr.Name.LocalName.Substring(6) + "=" + XPathQuote(attr.Value));
                }
                return "//" + find + (filters.Count > 0 ? "[" + string.Join(" and ", filters.ToArray()) + "]" : "");
            }
            return "";
        }

        private static string XPathQuote(string value)
        {
            if (!value.Contains("'")) return "'" + value + "'";
            if (!value.Contains("\"")) return "\"" + value + "\"";
            return "concat('" + value.Replace("'", "',\"'\",'") + "')";
        }

        private static string AttributeValue(XElement el, string name)
        {
            var attr = el.Attribute(name);
            return attr != null ? attr.Value : null;
        }

        private static string InnerXml(XElement el)
        {
            return string.Concat(el.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))).Trim();
        }

        private static XElement TryParseXmlFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.TrimStart().StartsWith("<", StringComparison.Ordinal)) return null;
            try { return XElement.Parse(value, LoadOptions.PreserveWhitespace); }
            catch { return null; }
        }

        private static byte[] ApplyXmlMergeDocument(byte[] originalBytes, string mergeText, Action<string> log, string relativePath)
        {
            var ctx = ParseGameXml(originalBytes, log, relativePath);
            if (ctx == null) return null;

            try
            {
                XDocument mergeDoc;
                var wrappedMerge = false;
                try
                {
                    mergeDoc = XDocument.Parse(mergeText, LoadOptions.PreserveWhitespace);
                }
                catch (XmlException)
                {
                    mergeDoc = XDocument.Parse("<__cdmm_root__>" + mergeText + "</__cdmm_root__>", LoadOptions.PreserveWhitespace);
                    wrappedMerge = true;
                }

                var root = mergeDoc.Root;
                if (root == null) return null;
                var mergeNodes = (root.Name.LocalName == "xml-merge" || wrappedMerge) ? root.Elements() : new[] { root };
                var merged = 0;
                var added = 0;
                var deleted = 0;
                foreach (var el in mergeNodes)
                {
                    if (el.HasElements)
                    {
                        var section = ctx.Document.Descendants(el.Name.LocalName).FirstOrDefault();
                        if (section != null)
                        {
                            foreach (var child in el.Elements())
                            {
                                MergeElementLikeJmm(new XElement(child), section, ctx.Document, ref merged, ref added, ref deleted);
                            }
                            continue;
                        }
                    }
                    MergeElementLikeJmm(new XElement(el), null, ctx.Document, ref merged, ref added, ref deleted);
                }
                if (log != null) log("XML merge materialized for " + relativePath + ": " + merged + " merged, " + added + " added, " + deleted + " deleted.");
                return SerializeGameXml(ctx);
            }
            catch (Exception ex)
            {
                if (log != null) log("XML merge failed for " + relativePath + ": " + ex.Message);
                return null;
            }
        }

        private static void MergeElementLikeJmm(XElement mergeEl, XElement parentHint, XDocument doc, ref int totalMerged, ref int totalAdded, ref int totalDeleted)
        {
            var localName = mergeEl.Name.LocalName;
            string keyAttr;
            string keyValue;
            FindMergeIdentity(mergeEl, parentHint, doc, localName, out keyAttr, out keyValue);
            var deleteAttr = mergeEl.Attribute("__delete");
            var shouldDelete = deleteAttr != null && string.Equals(deleteAttr.Value, "true", StringComparison.OrdinalIgnoreCase);

            if (keyAttr == null || keyValue == null)
            {
                if (shouldDelete) return;
                var parent = parentHint ?? doc.Root;
                if (parent != null) parent.Add(mergeEl);
                totalAdded++;
                return;
            }

            var search = parentHint != null ? parentHint.Elements(localName) : doc.Descendants(localName);
            var existing = search.FirstOrDefault(e =>
            {
                var attr = e.Attribute(keyAttr);
                return attr != null && attr.Value == keyValue;
            });

            if (shouldDelete)
            {
                if (existing != null)
                {
                    existing.Remove();
                    totalDeleted++;
                }
                return;
            }

            if (existing != null)
            {
                foreach (var attr in mergeEl.Attributes())
                {
                    var local = attr.Name.LocalName;
                    if (local == keyAttr || local.StartsWith("__", StringComparison.Ordinal)) continue;
                    existing.SetAttributeValue(attr.Name, attr.Value);
                }
                totalMerged++;
                return;
            }

            var copy = new XElement(mergeEl);
            var del = copy.Attribute("__delete");
            if (del != null) del.Remove();
            var insertParent = parentHint;
            if (insertParent == null)
            {
                var sameKind = doc.Descendants(localName).FirstOrDefault();
                insertParent = sameKind != null ? sameKind.Parent : doc.Root;
            }
            if (insertParent != null) insertParent.Add(copy);
            totalAdded++;
        }

        private static void FindMergeIdentity(XElement mergeEl, XElement parentHint, XDocument doc, string elName, out string keyAttr, out string keyValue)
        {
            keyAttr = null;
            keyValue = null;
            var priority = new[] { "Key", "Name", "Id", "key", "name", "id" };
            foreach (var candidate in priority)
            {
                var attr = mergeEl.Attribute(candidate);
                if (attr != null)
                {
                    keyAttr = candidate;
                    keyValue = attr.Value;
                    return;
                }
            }
            foreach (var attr in mergeEl.Attributes())
            {
                var local = attr.Name.LocalName;
                if (!local.StartsWith("__", StringComparison.Ordinal) && (local.EndsWith("_key", StringComparison.OrdinalIgnoreCase) || local.EndsWith("_id", StringComparison.OrdinalIgnoreCase) || local.EndsWith("_name", StringComparison.OrdinalIgnoreCase)))
                {
                    keyAttr = local;
                    keyValue = attr.Value;
                    return;
                }
            }
            var existing = (parentHint != null ? parentHint.Elements(elName) : doc.Descendants(elName)).FirstOrDefault();
            if (existing == null) return;
            foreach (var candidate in priority)
            {
                if (existing.Attribute(candidate) != null && mergeEl.Attribute(candidate) != null)
                {
                    keyAttr = candidate;
                    keyValue = mergeEl.Attribute(candidate).Value;
                    return;
                }
            }
        }

        private static string ApplySimpleXmlPatch(string original, string patchText, Action<string> log, string relativePath)
        {
            var clean = Regex.Replace(patchText, "<!--.*?-->", "", RegexOptions.Singleline);
            foreach (Match op in Regex.Matches(clean, "<(set|replace)\\b([^>]*)/>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var attrs = ParseXmlLikeAttributes(op.Groups[2].Value);
                if (!attrs.ContainsKey("at")) continue;
                var at = attrs["at"];
                if (!at.StartsWith("#", StringComparison.Ordinal)) continue;
                var id = at.Substring(1);
                attrs.Remove("at");
                if (attrs.Count == 0) continue;
                original = ApplyAttributesToElementById(original, id, attrs, log, relativePath);
            }
            return original;
        }

        private static Dictionary<string, string> ParseXmlLikeAttributes(string text)
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(text, "([A-Za-z0-9:_-]+)\\s*=\\s*\"([^\"]*)\"", RegexOptions.Singleline))
            {
                attrs[match.Groups[1].Value] = match.Groups[2].Value;
            }
            return attrs;
        }

        private static string ApplyAttributesToElementById(string original, string id, Dictionary<string, string> attrs, Action<string> log, string relativePath)
        {
            var elementPattern = "<([A-Za-z0-9:_-]+)\\b([^<>]*\\bid\\s*=\\s*[\"']" + Regex.Escape(id) + "[\"'][^<>]*)>";
            var changed = false;
            var result = Regex.Replace(original, elementPattern, match =>
            {
                changed = true;
                var tagName = match.Groups[1].Value;
                var body = match.Groups[2].Value;
                foreach (var attr in attrs)
                {
                    var attrPattern = "\\s+" + Regex.Escape(attr.Key) + "\\s*=\\s*\"[^\"]*\"";
                    if (Regex.IsMatch(body, attrPattern, RegexOptions.IgnoreCase))
                        body = Regex.Replace(body, attrPattern, " " + attr.Key + "=\"" + attr.Value + "\"", RegexOptions.IgnoreCase);
                    else
                        body += " " + attr.Key + "=\"" + attr.Value + "\"";
                }
                return "<" + tagName + body + ">";
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!changed && log != null) log("XML patch selector #" + id + " not found in " + relativePath + ".");
            return result;
        }

        private static byte[] ExtractOverlayOriginal(string gameRoot, string groupName, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(groupName)) return null;
            try
            {
                var groupDir = Path.Combine(gameRoot, groupName);
                var pamtPath = Path.Combine(groupDir, "0.pamt");
                if (!File.Exists(pamtPath)) return null;
                var pamt = ArchiveExtractor.ParsePamtFull(pamtPath, groupDir);
                var normalized = relativePath.Replace('\\', '/');
                var entry = pamt.Entries.FirstOrDefault(e => string.Equals((e.Path ?? "").Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
                if (entry == null || !File.Exists(entry.PazFile)) return null;
                using (var fs = File.OpenRead(entry.PazFile))
                {
                    fs.Seek(entry.Offset, SeekOrigin.Begin);
                    var blob = new byte[entry.CompSize];
                    var got = fs.Read(blob, 0, blob.Length);
                    if (got != blob.Length) return null;
                    if (entry.CompressionType == 2) return ArchiveExtractor.Lz4BlockDecompressPublic(blob, entry.OrigSize);
                    if (entry.CompressionType == 0 || entry.CompSize == entry.OrigSize) return blob;
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeOverlayDirectory(string path)
        {
            return (path ?? "").Replace('\\', '/').Trim('/');
        }

        private static uint AlignUp(uint value, uint alignment)
        {
            if (alignment == 0) return value;
            var remainder = value % alignment;
            return remainder == 0 ? value : value + (alignment - remainder);
        }

        private static byte[] BuildLooseOverlayPamt(List<LooseOverlayFile> files, byte[] pazBytes)
        {
            const uint OverlayPamtUnknown = 0x610E0232u;
            const ushort CompressionLz4 = 2;

            var directoryBlock = new MemoryStream();
            var dirWriter = new BinaryWriter(directoryBlock, Encoding.UTF8);
            var segmentOffsets = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var dirPath in files.Select(file => file.DirectoryPath).Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var parts = dirPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    var partial = string.Join("/", parts.Take(i + 1).ToArray());
                    if (segmentOffsets.ContainsKey(partial)) continue;
                    var offset = checked((uint)directoryBlock.Position);
                    segmentOffsets[partial] = offset;
                    uint parent = i == 0 ? 0xFFFFFFFFu : segmentOffsets[string.Join("/", parts.Take(i).ToArray())];
                    var segmentName = i == 0 ? parts[i] : "/" + parts[i];
                    WritePamtNameNode(dirWriter, parent, segmentName);
                }
            }

            var fileNameBlock = new MemoryStream();
            var fileNameWriter = new BinaryWriter(fileNameBlock, Encoding.UTF8);
            var folderRows = new List<Tuple<uint, uint, uint, uint>>();
            var orderedFiles = new List<LooseOverlayFile>();
            uint fileStart = 0;
            foreach (var group in files.GroupBy(file => file.DirectoryPath, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var groupFiles = group.OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var file in groupFiles)
                {
                    file.FileNameOffset = checked((uint)fileNameBlock.Position);
                    WritePamtNameNode(fileNameWriter, 0xFFFFFFFFu, file.FileName);
                    orderedFiles.Add(file);
                }
                uint folderHash = string.IsNullOrEmpty(group.Key) ? 0u : ArchiveExtractor.PaChecksum(Encoding.UTF8.GetBytes(group.Key), 0, Encoding.UTF8.GetByteCount(group.Key));
                uint dirOffset = string.IsNullOrEmpty(group.Key) ? 0xFFFFFFFFu : segmentOffsets[group.Key];
                folderRows.Add(Tuple.Create(folderHash, dirOffset, fileStart, checked((uint)groupFiles.Count)));
                fileStart += checked((uint)groupFiles.Count);
            }

            var body = new MemoryStream();
            var bw = new BinaryWriter(body, Encoding.UTF8);
            bw.Write((uint)1);
            bw.Write(OverlayPamtUnknown);
            bw.Write((uint)0);
            bw.Write(ArchiveExtractor.PaChecksum(pazBytes, 0, pazBytes.Length));
            bw.Write(checked((uint)pazBytes.Length));
            var dirBytes = directoryBlock.ToArray();
            bw.Write(checked((uint)dirBytes.Length));
            bw.Write(dirBytes);
            var nameBytes = fileNameBlock.ToArray();
            bw.Write(checked((uint)nameBytes.Length));
            bw.Write(nameBytes);
            bw.Write(checked((uint)folderRows.Count));
            foreach (var folder in folderRows)
            {
                bw.Write(folder.Item1);
                bw.Write(folder.Item2);
                bw.Write(folder.Item3);
                bw.Write(folder.Item4);
            }
            bw.Write(checked((uint)orderedFiles.Count));
            foreach (var file in orderedFiles)
            {
                bw.Write(file.FileNameOffset);
                bw.Write(file.Offset);
                bw.Write(checked((uint)file.PackedBytes.Length));
                bw.Write(file.OriginalSize);
                bw.Write((ushort)0);
                bw.Write(CompressionLz4);
            }

            var bodyBytes = body.ToArray();
            var output = new MemoryStream();
            var outw = new BinaryWriter(output, Encoding.UTF8);
            outw.Write(ArchiveExtractor.PaChecksum(bodyBytes, 8, bodyBytes.Length - 8));
            output.Write(bodyBytes, 0, bodyBytes.Length);
            return output.ToArray();
        }

        private static void WritePamtNameNode(BinaryWriter writer, uint parent, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            if (bytes.Length > byte.MaxValue) throw new InvalidOperationException("PAMT path segment is too long: " + name);
            writer.Write(parent);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        private string NextOverlaySlot(string preferred)
        {
            int n;
            if (!int.TryParse(preferred, out n)) n = 36;
            while (Directory.Exists(Path.Combine(gamePath, n.ToString("0000")))) n++;
            return n.ToString("0000");
        }

        private static byte[] BuildPapgtWithMod(string papgtPath, string modDirName, uint pamtCrc)
        {
            var data = File.ReadAllBytes(papgtPath);
            int count = data[8];
            int stringBase = 12 + count * 12 + 4;
            var entries = new List<Tuple<byte, ushort, byte, uint, string>>();
            for (int i = 0; i < count; i++)
            {
                int off = 12 + i * 12;
                byte optional = data[off];
                ushort lang = BitConverter.ToUInt16(data, off + 1);
                byte zero = data[off + 3];
                uint nameOffset = BitConverter.ToUInt32(data, off + 4);
                uint crc = BitConverter.ToUInt32(data, off + 8);
                int pos = stringBase + (int)nameOffset;
                int len = 0;
                while (pos + len < data.Length && data[pos + len] != 0) len++;
                entries.Add(Tuple.Create(optional, lang, zero, crc, Encoding.ASCII.GetString(data, pos, len)));
            }
            entries.RemoveAll(e => string.Equals(e.Item5, modDirName, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, Tuple.Create((byte)0, (ushort)16383, (byte)0, pamtCrc, modDirName));

            var strings = new MemoryStream();
            var nameOffsets = new List<uint>();
            foreach (var e in entries)
            {
                nameOffsets.Add((uint)strings.Position);
                var nameBytes = Encoding.ASCII.GetBytes(e.Item5);
                strings.Write(nameBytes, 0, nameBytes.Length);
                strings.WriteByte(0);
            }

            var body = new MemoryStream();
            var bw = new BinaryWriter(body);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                bw.Write(e.Item1);
                bw.Write(e.Item2);
                bw.Write(e.Item3);
                bw.Write(nameOffsets[i]);
                bw.Write(e.Item4);
            }
            var stringBytes = strings.ToArray();
            bw.Write((uint)stringBytes.Length);
            bw.Write(stringBytes);
            var bodyBytes = body.ToArray();
            uint headerCrc = ArchiveExtractor.PaChecksum(bodyBytes, 0, bodyBytes.Length);

            var output = new MemoryStream();
            var outw = new BinaryWriter(output);
            outw.Write(BitConverter.ToUInt32(data, 0));
            outw.Write(headerCrc);
            outw.Write((byte)entries.Count);
            outw.Write(BitConverter.ToUInt16(data, 9));
            outw.Write(data[11]);
            output.Write(bodyBytes, 0, bodyBytes.Length);
            return output.ToArray();
        }

        private sealed class ApplyWork
        {
            public PazEntry Entry;
            public int EntryIndex;
            public List<PatchChange> Changes;
            public uint NewPazOffset;
            public uint NewCompSize;
            public uint NewOrigSize;
            public uint NewFlags;
            public bool Succeeded;
        }

        private void ApplyByPazAppend()
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
            ResolveFieldFormatMods(selected);
            int overlayInstalled = ApplyOverlayPackages(selected);

            // Group changes by gameFile across all active mods (using each mod's selected preset).
            // Skip patches the user has unchecked in the patch list (disabledPatches).
            var byGameFile = new Dictionary<string, List<PatchChange>>(StringComparer.OrdinalIgnoreCase);
            int skippedDisabled = 0;
            foreach (var mod in selected)
            {
                if (mod.FormatTag == "RAW" || mod.FormatTag == "BROWSER") continue;
                var group = GroupFor(mod);
                int idx = 0;
                foreach (var c in mod.ChangesForGroup(group))
                {
                    var key = PatchKey(mod, group, idx);
                    idx++;
                    if (string.IsNullOrEmpty(c.GameFile)) continue;
                    if (!c.IsResolvedBytes) { skippedDisabled++; Log("Skipped unresolved field-format intent [" + mod.Name + "]: " + c.Label); continue; }
                    if (disabledPatches.Contains(key)) { skippedDisabled++; continue; }
                    if (!byGameFile.ContainsKey(c.GameFile)) byGameFile[c.GameFile] = new List<PatchChange>();
                    byGameFile[c.GameFile].Add(c);
                }
            }
            if (skippedDisabled > 0) Log("Skipped " + skippedDisabled + " patch(es) the user disabled in the Patch Board.");
            if (byGameFile.Count == 0)
            {
                if (overlayInstalled > 0)
                {
                    MessageBox.Show("Installed " + overlayInstalled + " overlay package(s).", "Mods applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else MessageBox.Show("Selected mods have no patches with a target game_file.", "Nothing to apply", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var pre = MessageBox.Show(
                "Apply " + byGameFile.Count + " modded game file(s) to your Crimson Desert install?\r\n\r\n" +
                "How it works:\r\n" +
                "  * Modded bytes are APPENDED to the existing archive(s) - original data is never overwritten.\r\n" +
                "  * The archive index (PAMT) is patched in place to point at the new bytes.\r\n" +
                "  * Pre-apply length of each archive + the original PAMT are saved to <game>\\_jmm_backups\\.\r\n" +
                "  * Click 'Restore Backup' to fully revert (truncates archives back, restores the PAMT).\r\n\r\n" +
                "Continue?",
                "Apply mods?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (pre != DialogResult.Yes) return;

            var pamtPath = Path.Combine(gamePath, "0008", "0.pamt");
            var pazDir = Path.Combine(gamePath, "0008");

            PamtParseResult pamtData;
            try
            {
                pamtData = ArchiveExtractor.ParsePamtFull(pamtPath, pazDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not parse PAMT: " + ex.Message, "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Match PAMT entries to gameFiles (substring match on basename, same as the validator).
            var workItems = new List<ApplyWork>();
            foreach (var pair in byGameFile)
            {
                var basename = Path.GetFileName(pair.Key).ToLowerInvariant();
                for (int i = 0; i < pamtData.Entries.Count; i++)
                {
                    var e = pamtData.Entries[i];
                    if (string.IsNullOrEmpty(e.Path)) continue;
                    if (!e.Path.ToLowerInvariant().Contains(basename)) continue;
                    if (e.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue; // encrypted - skip
                    workItems.Add(new ApplyWork { Entry = e, EntryIndex = i, Changes = pair.Value });
                }
            }
            if (workItems.Count == 0)
            {
                MessageBox.Show("No matching archive entries found for the selected mods.", "Apply aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Backups (idempotent - if already present, do not overwrite - they reflect pre-FIRST-apply state)
            var backupRoot = PazBackupsRoot();
            Directory.CreateDirectory(backupRoot);
            // Back up papgt too - Apply now updates it (PamtCrc + papgt HeaderCrc) so we need a revert source.
            var papgtLive = Path.Combine(gamePath, "meta", "0.papgt");
            var papgtBackup = Path.Combine(backupRoot, "..", "0.papgt.original"); // _jmm_backups/0.papgt.original
            papgtBackup = Path.GetFullPath(papgtBackup);
            if (File.Exists(papgtLive) && !File.Exists(papgtBackup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(papgtBackup));
                File.Copy(papgtLive, papgtBackup);
                Log("Backed up papgt to " + papgtBackup);
            }
            var pamtBackup = Path.Combine(backupRoot, "0.pamt.original");
            if (!File.Exists(pamtBackup))
            {
                File.Copy(pamtPath, pamtBackup);
                Log("Backed up PAMT to " + pamtBackup);
            }
            var affectedPazFiles = workItems.Select(w => w.Entry.PazFile).Distinct().ToList();
            foreach (var pazFile in affectedPazFiles)
            {
                var lengthFile = Path.Combine(backupRoot, Path.GetFileName(pazFile) + ".length.original");
                if (!File.Exists(lengthFile))
                {
                    var sz = new FileInfo(pazFile).Length;
                    File.WriteAllText(lengthFile, sz.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Log("Recorded original length " + sz + " for " + Path.GetFileName(pazFile));
                }
            }
            WriteRestoreGuardManifest("pre-apply");

            int totalApplied = 0, totalMismatch = 0, fileSkipped = 0;
            foreach (var pazGroup in workItems.GroupBy(w => w.Entry.PazFile))
            {
                var pazFile = pazGroup.Key;
                using (var paz = new FileStream(pazFile, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    foreach (var w in pazGroup)
                    {
                        try
                        {
                            // Read original bytes
                            var readSize = w.Entry.Compressed ? w.Entry.CompSize : w.Entry.OrigSize;
                            paz.Seek(w.Entry.Offset, SeekOrigin.Begin);
                            var originalBytes = new byte[readSize];
                            int got = 0;
                            while (got < originalBytes.Length)
                            {
                                int r = paz.Read(originalBytes, got, originalBytes.Length - got);
                                if (r <= 0) break;
                                got += r;
                            }

                            // Decompress (or use as-is if uncompressed)
                            byte[] decompressed;
                            if (w.Entry.Compressed)
                            {
                                if (w.Entry.CompressionType != 2)
                                {
                                    Log("Skipping " + w.Entry.Path + " - unsupported compression type " + w.Entry.CompressionType);
                                    fileSkipped++;
                                    continue;
                                }
                                decompressed = ArchiveExtractor.Lz4BlockDecompressPublic(originalBytes, w.Entry.OrigSize);
                            }
                            else
                            {
                                decompressed = originalBytes;
                            }

                            // Apply patches
                            int applied = 0, mismatch = 0;
                            foreach (var c in w.Changes)
                            {
                                var orig = HexToBytes(c.Original);
                                var patched = HexToBytes(c.Patched);
                                if (c.Offset < 0 || c.Offset + orig.Length > decompressed.Length)
                                {
                                    mismatch++;
                                    Log("Out of range: " + c.Label);
                                    continue;
                                }
                                bool matches = true;
                                for (int i = 0; i < orig.Length; i++)
                                {
                                    if (decompressed[c.Offset + i] != orig[i]) { matches = false; break; }
                                }
                                if (!matches)
                                {
                                    bool already = true;
                                    for (int i = 0; i < patched.Length; i++)
                                    {
                                        if (decompressed[c.Offset + i] != patched[i]) { already = false; break; }
                                    }
                                    if (!already)
                                    {
                                        mismatch++;
                                        Log("Byte mismatch: " + c.Label);
                                        continue;
                                    }
                                }
                                Buffer.BlockCopy(patched, 0, decompressed, c.Offset, patched.Length);
                                applied++;
                            }
                            if (applied == 0)
                            {
                                Log("Skipped " + w.Entry.Path + " - no patches succeeded.");
                                fileSkipped++;
                                totalMismatch += mismatch;
                                continue;
                            }

                            // Write the patched data UNCOMPRESSED:
                            //   compSize == origSize signals "no decompression needed"
                            //   compType bits (16..19) cleared to 0
                            // This avoids any LZ4 encoder ambiguity - the engine reads raw bytes.
                            var rawBytes = decompressed;

                            // Pad paz file to a 16-byte boundary BEFORE this entry (the engine bounds-checks 16-byte alignment).
                            // The engine validates that every entry's pazOffset is 16-aligned; misaligned offsets cause launch failure.
                            paz.Seek(0, SeekOrigin.End);
                            long endPos = paz.Position;
                            int prePad = (int)((16 - (endPos % 16)) % 16);
                            if (prePad > 0) paz.Write(new byte[prePad], 0, prePad);
                            long newOffset = paz.Position; // 16-aligned
                            paz.Write(rawBytes, 0, rawBytes.Length);

                            w.NewPazOffset = (uint)newOffset;
                            w.NewCompSize = (uint)rawBytes.Length;
                            w.NewOrigSize = (uint)rawBytes.Length;
                            // Preserve pazIndex (low 8 bits) and any flag bits outside 16..19; clear the comp-type nibble.
                            w.NewFlags = (w.Entry.Flags & 0xFFF0FFFFu);
                            w.Succeeded = true;

                            totalApplied += applied;
                            totalMismatch += mismatch;
                            Log("Applied " + applied + " to " + w.Entry.Path + " (compSize " + w.Entry.CompSize + " -> " + w.NewCompSize + ", uncompressed, +" + prePad + "B align pad)");
                        }
                        catch (Exception ex)
                        {
                            Log("Apply error for " + w.Entry.Path + ": " + ex.Message);
                            fileSkipped++;
                        }
                    }
                    // Pad paz file end to 16-byte alignment so the recorded size matches engine expectations.
                    paz.Seek(0, SeekOrigin.End);
                    long finalEnd = paz.Position;
                    int finalPad = (int)((16 - (finalEnd % 16)) % 16);
                    if (finalPad > 0)
                    {
                        paz.Write(new byte[finalPad], 0, finalPad);
                        Log("Padded " + Path.GetFileName(pazFile) + " end with " + finalPad + " bytes for 16-byte alignment.");
                    }
                    paz.Flush();
                }
            }

            // Patch PAMT in memory - surgical 16-byte edit per entry (skip nodeRef, write pazOffset/compSize/origSize/flags)
            var pamtNew = (byte[])pamtData.PamtBytes.Clone();
            int pamtChanges = 0;
            foreach (var w in workItems)
            {
                if (!w.Succeeded) continue;
                int eOff = pamtData.EntrySectionStart + w.EntryIndex * 20;
                if (eOff + 20 > pamtNew.Length)
                {
                    Log("PAMT entry offset out of range for " + w.Entry.Path + " - skipped.");
                    continue;
                }
                ArchiveExtractor.WriteU32LE(pamtNew, eOff + 4, w.NewPazOffset);
                ArchiveExtractor.WriteU32LE(pamtNew, eOff + 8, w.NewCompSize);
                ArchiveExtractor.WriteU32LE(pamtNew, eOff + 12, w.NewOrigSize);
                ArchiveExtractor.WriteU32LE(pamtNew, eOff + 16, w.NewFlags);
                pamtChanges++;
            }

            // Update each affected paz's recorded size in the PAMT header.
            // PAMT header layout, starting at byte 16:
            //   for each paz i in 0..pazCount-1:
            //     bytes [16 + i*12 + 0 .. +3]  -- 4 unknown bytes (likely a hash, leaving alone)
            //     bytes [16 + i*12 + 4 .. +7]  -- u32 LE: paz file size in bytes
            //     bytes [16 + i*12 + 8 .. +11] -- 4 inter-paz bytes (only present for non-last paz)
            // We only update the size field, since the paz grew when we appended.
            int pazSizeUpdates = 0;
            foreach (var pazFile in workItems.Where(w => w.Succeeded).Select(w => w.Entry.PazFile).Distinct())
            {
                var name = Path.GetFileName(pazFile);
                int dot = name.IndexOf('.');
                if (dot <= 0) continue;
                if (!int.TryParse(name.Substring(0, dot), out int pazIndex)) continue;
                long newLen;
                try { newLen = new FileInfo(pazFile).Length; } catch { continue; }
                if (newLen > uint.MaxValue)
                {
                    Log("WARNING: " + name + " grew past 4 GB - paz size field can't represent it.");
                    continue;
                }
                int sizeOffset = 16 + pazIndex * 12 + 4;
                if (sizeOffset + 4 > pamtNew.Length)
                {
                    Log("Paz size field offset out of range for " + name + " (idx " + pazIndex + ").");
                    continue;
                }
                uint oldSize = (uint)(pamtData.PamtBytes[sizeOffset]
                    | (pamtData.PamtBytes[sizeOffset + 1] << 8)
                    | (pamtData.PamtBytes[sizeOffset + 2] << 16)
                    | (pamtData.PamtBytes[sizeOffset + 3] << 24));
                ArchiveExtractor.WriteU32LE(pamtNew, sizeOffset, (uint)newLen);
                Log("PAMT header: " + name + " size " + oldSize + " -> " + newLen);
                pazSizeUpdates++;
            }
            Log("Updated PAMT header paz sizes: " + pazSizeUpdates);

            // ============================================================
            // Compute and write Pearl Abyss CRCs (the part we missed for hours):
            //   * per-paz Crc - PaChecksum of the .paz file content, at PAMT[12+pazIdx*12+4]
            //   * PAMT HeaderCrc - PaChecksum of pamt[12..end], at PAMT[0..3]
            //   * papgt's PamtCrc field for "0008" - copy of the new HeaderCrc
            //   * papgt's own HeaderCrc - PaChecksum of papgt[12..end], at papgt[4..7]
            // Without these, the engine refuses to launch.
            // ============================================================
            int crcUpdates = 0;
            foreach (var pazFile in workItems.Where(w => w.Succeeded).Select(w => w.Entry.PazFile).Distinct())
            {
                var name = Path.GetFileName(pazFile);
                int dot = name.IndexOf('.');
                if (dot <= 0) continue;
                if (!int.TryParse(name.Substring(0, dot), out int pazNum)) continue;
                int pamtBaseNum;
                if (!int.TryParse(Path.GetFileNameWithoutExtension(pamtPath), out pamtBaseNum)) pamtBaseNum = 0;
                int pazIdx = pazNum - pamtBaseNum;
                int crcOffset = 12 + pazIdx * 12 + 4;
                if (crcOffset + 4 > pamtNew.Length) continue;
                Log("Computing PaChecksum of " + name + " (" + new FileInfo(pazFile).Length + " bytes)...");
                Application.DoEvents();
                uint pazCrc = ArchiveExtractor.PaChecksumFile(pazFile);
                ArchiveExtractor.WriteU32LE(pamtNew, crcOffset, pazCrc);
                Log("PAMT row[" + pazIdx + "] Crc -> 0x" + pazCrc.ToString("X8"));
                crcUpdates++;
            }
            // PAMT HeaderCrc = PaChecksum(pamt[12..end])
            uint pamtHeaderCrc = ArchiveExtractor.PaChecksum(pamtNew, 12, pamtNew.Length - 12);
            ArchiveExtractor.WriteU32LE(pamtNew, 0, pamtHeaderCrc);
            Log("PAMT HeaderCrc -> 0x" + pamtHeaderCrc.ToString("X8"));

            try
            {
                File.WriteAllBytes(pamtPath, pamtNew);
                Log("Wrote modified PAMT (" + pamtChanges + " entries redirected, " + crcUpdates + " paz CRCs + 1 HeaderCrc updated).");
            }
            catch (Exception ex)
            {
                Log("PAMT write failed: " + ex.Message);
                WriteRestoreGuardManifest("apply-failed-after-paz-append");
                MessageBox.Show("PAMT write failed: " + ex.Message + "\r\n\r\nThe paz files were appended to but the index wasn't updated. The game will still work; click 'Restore Backup' to truncate the appended bytes.", "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Update meta/0.papgt: find the entry whose folder name = the PAMT's parent directory (e.g. "0008") and refresh its PamtCrc.
            try
            {
                var pamtDirName = Path.GetFileName(Path.GetDirectoryName(pamtPath));
                if (File.Exists(papgtLive))
                {
                    var papgt = File.ReadAllBytes(papgtLive);
                    int papgtEntryCount = papgt[8];
                    int strTableStart = 12 + papgtEntryCount * 12 + 4; // 4 = string-table-size prefix
                    bool foundEntry = false;
                    for (int j = 0; j < papgtEntryCount; j++)
                    {
                        int eOff = 12 + j * 12;
                        uint nameOffset = (uint)(papgt[eOff + 4] | (papgt[eOff + 5] << 8) | (papgt[eOff + 6] << 16) | (papgt[eOff + 7] << 24));
                        int strPos = strTableStart + (int)nameOffset;
                        int strLen = 0;
                        while (strPos + strLen < papgt.Length && papgt[strPos + strLen] != 0) strLen++;
                        var entryName = System.Text.Encoding.ASCII.GetString(papgt, strPos, strLen);
                        if (entryName == pamtDirName)
                        {
                            ArchiveExtractor.WriteU32LE(papgt, eOff + 8, pamtHeaderCrc);
                            foundEntry = true;
                            Log("papgt entry '" + entryName + "' PamtCrc -> 0x" + pamtHeaderCrc.ToString("X8"));
                            break;
                        }
                    }
                    if (!foundEntry)
                    {
                        Log("WARNING: archive '" + pamtDirName + "' not found in papgt - engine may reject changes.");
                    }
                    else
                    {
                        // Recompute papgt's own HeaderCrc over papgt[12..end]
                        uint papgtCrc = ArchiveExtractor.PaChecksum(papgt, 12, papgt.Length - 12);
                        ArchiveExtractor.WriteU32LE(papgt, 4, papgtCrc);
                        Log("papgt HeaderCrc -> 0x" + papgtCrc.ToString("X8"));
                        File.WriteAllBytes(papgtLive, papgt);
                        Log("Wrote updated meta/0.papgt.");
                    }
                }
                else
                {
                    Log("WARNING: meta/0.papgt not found - engine probably won't launch with modified PAMT.");
                }
            }
            catch (Exception ex)
            {
                Log("papgt update failed: " + ex.Message);
            }
            WriteRestoreGuardManifest("post-apply");

            MessageBox.Show(
                "Applied " + totalApplied + " patches across " + pamtChanges + " archive entries.\r\n" +
                (totalMismatch > 0 ? totalMismatch + " patch(es) skipped due to byte mismatch (see log).\r\n" : "") +
                (fileSkipped > 0 ? fileSkipped + " file(s) skipped entirely.\r\n" : "") +
                "\r\nLaunch Crimson Desert and test.\r\nClick 'Restore Backup' to fully revert.",
                "Mods applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }


        private void RevertPazAppend()
        {
            if (!IsGameFolder(gamePath)) return;
            var backupRoot = PazBackupsRoot();
            if (!Directory.Exists(backupRoot)) return;
            if (!ValidateRestoreGuard()) return;

            // Restore PAMT
            var pamtBackup = Path.Combine(backupRoot, "0.pamt.original");
            var pamtLive = Path.Combine(gamePath, "0008", "0.pamt");
            int restored = 0;
            if (File.Exists(pamtBackup) && File.Exists(pamtLive))
            {
                try
                {
                    File.Copy(pamtBackup, pamtLive, true);
                    Log("Restored PAMT from " + pamtBackup);
                    restored++;
                }
                catch (Exception ex) { Log("PAMT restore failed: " + ex.Message); }
            }

            // Restore papgt (Apply now updates it, so revert needs to undo that)
            var papgtBackupRevert = Path.Combine(backupRoot, "..", "0.papgt.original");
            papgtBackupRevert = Path.GetFullPath(papgtBackupRevert);
            var papgtLiveRevert = Path.Combine(gamePath, "meta", "0.papgt");
            if (File.Exists(papgtBackupRevert) && File.Exists(papgtLiveRevert))
            {
                try
                {
                    File.Copy(papgtBackupRevert, papgtLiveRevert, true);
                    Log("Restored papgt from " + papgtBackupRevert);
                    restored++;
                }
                catch (Exception ex) { Log("papgt restore failed: " + ex.Message); }
            }

            var overlayMarker = Path.GetFullPath(Path.Combine(backupRoot, "..", "overlay_folders.txt"));
            if (File.Exists(overlayMarker))
            {
                foreach (var slot in File.ReadAllLines(overlayMarker).Select(s => s.Trim()).Where(s => Regex.IsMatch(s, @"^\d{4}$")).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var dir = Path.Combine(gamePath, slot);
                        if (Directory.Exists(dir))
                        {
                            Directory.Delete(dir, true);
                            Log("Removed overlay folder " + slot + ".");
                            restored++;
                        }
                    }
                    catch (Exception ex) { Log("Overlay remove failed for " + slot + ": " + ex.Message); }
                }
            }

            // Truncate each tracked .paz back to its recorded original length
            foreach (var lengthFile in Directory.GetFiles(backupRoot, "*.length.original"))
            {
                try
                {
                    var pazName = Path.GetFileName(lengthFile);
                    pazName = pazName.Substring(0, pazName.Length - ".length.original".Length); // "0.paz"
                    var pazLive = Path.Combine(gamePath, "0008", pazName);
                    if (!File.Exists(pazLive)) continue;
                    var origLen = long.Parse(File.ReadAllText(lengthFile).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    var curLen = new FileInfo(pazLive).Length;
                    if (curLen > origLen)
                    {
                        using (var fs = new FileStream(pazLive, FileMode.Open, FileAccess.Write))
                        {
                            fs.SetLength(origLen);
                        }
                        Log("Truncated " + pazName + " from " + curLen + " back to " + origLen);
                        restored++;
                    }
                    else if (curLen < origLen)
                    {
                        Log("WARNING: " + pazName + " is shorter (" + curLen + ") than recorded original (" + origLen + ") - not truncating. Restore from your Steam files if needed.");
                    }
                }
                catch (Exception ex) { Log("Truncate failed for " + lengthFile + ": " + ex.Message); }
            }

            // Clean up the backup markers so subsequent applies create fresh ones
            try
            {
                if (File.Exists(pamtBackup)) File.Delete(pamtBackup);
                if (File.Exists(papgtBackupRevert)) File.Delete(papgtBackupRevert);
                if (File.Exists(overlayMarker)) File.Delete(overlayMarker);
                foreach (var lf in Directory.GetFiles(backupRoot, "*.length.original")) File.Delete(lf);
                var guard = RestoreGuardManifestPath();
                if (!string.IsNullOrEmpty(guard) && File.Exists(guard)) File.Delete(guard);
            }
            catch { }

            if (restored > 0) Log("Apply reverted: " + restored + " archive(s) restored to pre-apply state.");
        }

        // Legacy loose-file probe - kept for any orphaned probe writes from earlier sessions.
        private void ApplyAsLooseFiles()
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

            // Collect (game_file -> list of changes) across all active mods using each mod's preset.
            var byGameFile = new Dictionary<string, List<PatchChange>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in selected)
            {
                var group = GroupFor(mod);
                foreach (var c in mod.ChangesForGroup(group))
                {
                    if (string.IsNullOrEmpty(c.GameFile)) continue;
                    if (!c.IsResolvedBytes) continue;
                    if (!byGameFile.ContainsKey(c.GameFile)) byGameFile[c.GameFile] = new List<PatchChange>();
                    byGameFile[c.GameFile].Add(c);
                }
            }
            if (byGameFile.Count == 0)
            {
                MessageBox.Show("Selected mods have no patches with a target game_file.", "Nothing to apply", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var pre = MessageBox.Show(
                "This will write modded copies of " + byGameFile.Count + " game file(s) into your Crimson Desert folder as loose files (the engine reads loose files first).\r\n\r\n" +
                "* Original game archives are NEVER modified.\r\n" +
                "* Any pre-existing file at a target path is backed up first.\r\n" +
                "* Click 'Restore Backup' to fully revert.\r\n\r\n" +
                "Continue?",
                "Apply mods?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (pre != DialogResult.Yes) return;

            // Build the work list: for each logical game_file, find the actual archive entry path,
            // extract bytes (decompressed), apply patches in-memory.
            var work = new List<Tuple<string, byte[], int, int>>(); // (archivePath, patchedBytes, mismatched, applied)
            foreach (var pair in byGameFile)
            {
                var logicalFile = pair.Key;
                var changes = pair.Value;

                // Trigger extraction (reuses cache if already there)
                var cachedPath = ArchiveExtractor.Extract(gamePath, logicalFile, cacheDir, Log);
                if (cachedPath == null || !File.Exists(cachedPath))
                {
                    Log("Apply: could not extract " + logicalFile + ". Skipped.");
                    continue;
                }
                // Derive the archive-internal path (relative to cache root) - that's where we'll write loose
                var archiveRelPath = MakeRelative(cacheDir, cachedPath);
                if (string.IsNullOrEmpty(archiveRelPath))
                {
                    Log("Apply: could not derive archive path for " + cachedPath);
                    continue;
                }

                var bytes = File.ReadAllBytes(cachedPath);
                int applied = 0, mismatched = 0;
                foreach (var c in changes)
                {
                    var origBytes = HexToBytes(c.Original);
                    var newBytes = HexToBytes(c.Patched);
                    if (c.Offset < 0 || c.Offset + origBytes.Length > bytes.Length)
                    {
                        Log("Apply: offset out of range in " + logicalFile + ": " + c.Label);
                        mismatched++;
                        continue;
                    }
                    // Confirm originals match before overwriting (guards against stale offsets)
                    bool ok = true;
                    for (int i = 0; i < origBytes.Length; i++)
                    {
                        if (bytes[c.Offset + i] != origBytes[i]) { ok = false; break; }
                    }
                    if (!ok)
                    {
                        // Allow already-patched (idempotent re-apply)
                        bool already = true;
                        for (int i = 0; i < newBytes.Length; i++)
                        {
                            if (bytes[c.Offset + i] != newBytes[i]) { already = false; break; }
                        }
                        if (!already)
                        {
                            mismatched++;
                            Log("Apply: byte guard failed for " + c.Label);
                            continue;
                        }
                        applied++;
                        continue;
                    }
                    Buffer.BlockCopy(newBytes, 0, bytes, c.Offset, newBytes.Length);
                    applied++;
                }

                if (mismatched > 0 && applied == 0)
                {
                    Log("Apply: skipping " + logicalFile + " entirely - every patch failed its byte guard.");
                    continue;
                }
                work.Add(Tuple.Create(archiveRelPath, bytes, mismatched, applied));
                Log("Apply: prepared " + logicalFile + " - " + applied + " patches applied" + (mismatched > 0 ? ", " + mismatched + " skipped" : ""));
            }

            if (work.Count == 0)
            {
                MessageBox.Show("Nothing to apply (no files prepared cleanly). Run Check Match to diagnose.", "Apply aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Load existing manifest so we accumulate across applies.
            var manifest = LoadLooseManifest();

            int written = 0, backedUp = 0;
            var probeBackupsRoot = ProbeBackupsRoot();
            foreach (var w in work)
            {
                var archiveRelPath = w.Item1.Replace('\\', '/');
                var patched = w.Item2;
                var target = Path.Combine(gamePath, archiveRelPath.Replace('/', Path.DirectorySeparatorChar));

                try
                {
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    // If a real file exists at the target, back it up first.
                    string backupPath = "";
                    if (File.Exists(target))
                    {
                        backupPath = Path.Combine(probeBackupsRoot, archiveRelPath.Replace('/', Path.DirectorySeparatorChar) + ".bak");
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                        if (!File.Exists(backupPath)) File.Copy(target, backupPath, false);
                        backedUp++;
                    }

                    File.WriteAllBytes(target, patched);
                    written++;

                    // Manifest entry
                    var entry = new Dictionary<string, object>
                    {
                        ["target"] = target,
                        ["archive_path"] = archiveRelPath,
                        ["had_existing"] = !string.IsNullOrEmpty(backupPath),
                        ["backup"] = backupPath ?? ""
                    };
                    manifest[target] = entry;
                }
                catch (Exception ex)
                {
                    Log("Apply: write failed for " + target + ": " + ex.Message);
                }
            }

            SaveLooseManifest(manifest);
            Log("Apply: wrote " + written + " loose file(s)" + (backedUp > 0 ? " (backed up " + backedUp + " pre-existing)" : ""));

            MessageBox.Show(
                "Wrote " + written + " modded file(s) to your game folder as loose files.\r\n" +
                (backedUp > 0 ? backedUp + " pre-existing file(s) were backed up to <game>\\_ujmm_probe_backups\\.\r\n" : "") +
                "\r\nLaunch Crimson Desert and test the modded behavior.\r\n" +
                "If the changes take effect -> loose-file overlay works.\r\n" +
                "If they don't -> click 'Restore Backup' to revert and we'll try the next approach.",
                "Mods applied (probe)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RevertLooseFileApply()
        {
            var manifest = LoadLooseManifest();
            if (manifest.Count == 0) return;
            int restored = 0, deleted = 0, errors = 0;
            foreach (var pair in manifest.ToList())
            {
                var entry = pair.Value as Dictionary<string, object>;
                if (entry == null) continue;
                var target = Convert.ToString(entry.ContainsKey("target") ? entry["target"] : pair.Key);
                bool hadExisting = entry.ContainsKey("had_existing") && entry["had_existing"] is bool && (bool)entry["had_existing"];
                var backup = entry.ContainsKey("backup") ? Convert.ToString(entry["backup"]) : "";
                try
                {
                    if (hadExisting && !string.IsNullOrEmpty(backup) && File.Exists(backup))
                    {
                        File.Copy(backup, target, true);
                        try { File.Delete(backup); } catch { }
                        restored++;
                    }
                    else if (File.Exists(target))
                    {
                        File.Delete(target);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    Log("Revert: failed for " + target + ": " + ex.Message);
                }
            }
            // Clean up the probe-backups dir if it's empty
            try
            {
                var root = ProbeBackupsRoot();
                if (Directory.Exists(root))
                {
                    if (Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 0)
                    {
                        Directory.Delete(root, true);
                    }
                }
            }
            catch { }
            try { File.Delete(LooseManifestPath()); } catch { }
            Log("Revert: restored " + restored + ", deleted " + deleted + (errors > 0 ? ", " + errors + " error(s)" : "") + ".");
        }

        private Dictionary<string, object> LoadLooseManifest()
        {
            try
            {
                var p = LooseManifestPath();
                if (!File.Exists(p)) return new Dictionary<string, object>();
                var d = json.DeserializeObject(File.ReadAllText(p, Encoding.UTF8)) as Dictionary<string, object>;
                return d ?? new Dictionary<string, object>();
            }
            catch { return new Dictionary<string, object>(); }
        }

        private void SaveLooseManifest(Dictionary<string, object> manifest)
        {
            try
            {
                Directory.CreateDirectory(backupsDir);
                File.WriteAllText(LooseManifestPath(), json.Serialize(manifest), Encoding.UTF8);
            }
            catch (Exception ex) { Log("SaveLooseManifest: " + ex.Message); }
        }

        private static string MakeRelative(string root, string full)
        {
            try
            {
                var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                var fileFull = Path.GetFullPath(full);
                if (fileFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return fileFull.Substring(rootFull.Length + 1).Replace('\\', '/');
                }
            }
            catch { }
            return "";
        }

        private string LivePapgtPath()
        {
            return IsGameFolder(gamePath) ? Path.Combine(gamePath, "meta", "0.papgt") : "";
        }

        private string AppPapgtBackupPath()
        {
            return Path.Combine(backupsDir, "0.papgt.original");
        }

        private string GamePapgtBackupPath()
        {
            return IsGameFolder(gamePath) ? Path.Combine(gamePath, "_jmm_backups", "0.papgt.original") : "";
        }

        private string RestoreGuardManifestPath()
        {
            return IsGameFolder(gamePath) ? Path.Combine(gamePath, "_jmm_backups", "restore_guard.json") : "";
        }

        private string Sha256File(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
        }

        private Dictionary<string, object> CaptureRestoreGuardState(string reason)
        {
            var backupRoot = PazBackupsRoot();
            var pazDir = Path.Combine(gamePath, "0008");
            var pazLengths = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(pazDir))
            {
                foreach (var paz in Directory.GetFiles(pazDir, "*.paz"))
                {
                    var name = Path.GetFileName(paz);
                    if (!Regex.IsMatch(name, @"^\d+\.paz$", RegexOptions.IgnoreCase)) continue;
                    pazLengths[name] = new FileInfo(paz).Length;
                }
            }

            var pamtLive = Path.Combine(gamePath, "0008", "0.pamt");
            var papgtLive = LivePapgtPath();
            return new Dictionary<string, object>
            {
                ["version"] = 1,
                ["app_version"] = Program.AppVersion,
                ["reason"] = reason ?? "",
                ["created_utc"] = DateTime.UtcNow.ToString("o"),
                ["game_path"] = gamePath ?? "",
                ["live_papgt_sha256"] = Sha256File(papgtLive),
                ["live_pamt_sha256"] = Sha256File(pamtLive),
                ["backup_papgt_sha256"] = Sha256File(Path.GetFullPath(Path.Combine(backupRoot, "..", "0.papgt.original"))),
                ["backup_pamt_sha256"] = Sha256File(Path.Combine(backupRoot, "0.pamt.original")),
                ["paz_lengths"] = pazLengths
            };
        }

        private void WriteRestoreGuardManifest(string reason)
        {
            if (!IsGameFolder(gamePath)) return;
            try
            {
                var path = RestoreGuardManifestPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json.Serialize(CaptureRestoreGuardState(reason)), Encoding.UTF8);
                Log("Backup guard updated (" + reason + ").");
            }
            catch (Exception ex)
            {
                Log("Backup guard update failed: " + ex.Message);
            }
        }

        private bool ValidateRestoreGuard()
        {
            var manifestPath = RestoreGuardManifestPath();
            if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
            {
                MessageBox.Show(
                    "Restore was blocked because this backup does not have UJMM's restore-guard metadata.\r\n\r\n" +
                    "This can happen with backups made by older builds. To avoid restoring stale files over a newer game update, UJMM will not use this backup automatically.\r\n\r\n" +
                    "If the game was updated, verify/repair the game files, then create a fresh Backup before applying mods again.",
                    "Restore blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("Restore blocked: restore_guard.json is missing.");
                return false;
            }

            try
            {
                var manifest = json.DeserializeObject(File.ReadAllText(manifestPath, Encoding.UTF8)) as Dictionary<string, object>;
                if (manifest == null) throw new InvalidDataException("restore_guard.json is invalid.");

                var expectedPapgt = Convert.ToString(manifest.ContainsKey("live_papgt_sha256") ? manifest["live_papgt_sha256"] : "");
                var expectedPamt = Convert.ToString(manifest.ContainsKey("live_pamt_sha256") ? manifest["live_pamt_sha256"] : "");
                var livePapgt = LivePapgtPath();
                var livePamt = Path.Combine(gamePath, "0008", "0.pamt");

                var problems = new List<string>();
                if (!string.IsNullOrEmpty(expectedPapgt) && !string.Equals(expectedPapgt, Sha256File(livePapgt), StringComparison.OrdinalIgnoreCase))
                    problems.Add("meta\\0.papgt no longer matches the UJMM-applied state.");
                if (!string.IsNullOrEmpty(expectedPamt) && !string.Equals(expectedPamt, Sha256File(livePamt), StringComparison.OrdinalIgnoreCase))
                    problems.Add("0008\\0.pamt no longer matches the UJMM-applied state.");

                var lengths = manifest.ContainsKey("paz_lengths") ? manifest["paz_lengths"] as Dictionary<string, object> : null;
                if (lengths != null)
                {
                    foreach (var pair in lengths)
                    {
                        var paz = Path.Combine(gamePath, "0008", pair.Key);
                        if (!File.Exists(paz))
                        {
                            problems.Add(pair.Key + " is missing.");
                            continue;
                        }
                        long expectedLen;
                        if (!long.TryParse(Convert.ToString(pair.Value), out expectedLen)) continue;
                        var actualLen = new FileInfo(paz).Length;
                        if (actualLen != expectedLen)
                            problems.Add(pair.Key + " length changed (" + actualLen + " != " + expectedLen + ").");
                    }
                }

                if (problems.Count == 0) return true;

                MessageBox.Show(
                    "Restore was blocked because the current game files no longer match the state UJMM created when the mods were applied.\r\n\r\n" +
                    string.Join("\r\n", problems.Take(6).ToArray()) +
                    (problems.Count > 6 ? "\r\n..." : "") +
                    "\r\n\r\nThis usually means the game was updated or repaired after the backup was made. UJMM will not restore stale backups over updated game files.",
                    "Restore blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("Restore blocked by guard: " + string.Join("; ", problems.ToArray()));
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Restore was blocked because restore_guard.json could not be validated:\r\n\r\n" + ex.Message, "Restore blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Log("Restore blocked: guard validation failed: " + ex.Message);
                return false;
            }
        }

        private bool EnsurePapgtBackup(bool overwrite)
        {
            if (!IsGameFolder(gamePath))
            {
                return false;
            }

            var live = LivePapgtPath();
            if (!File.Exists(live))
            {
                Log("Cannot back up 0.papgt because meta/0.papgt was not found.");
                return false;
            }

            var appBackup = AppPapgtBackupPath();
            var gameBackup = GamePapgtBackupPath();
            Directory.CreateDirectory(Path.GetDirectoryName(appBackup));
            Directory.CreateDirectory(Path.GetDirectoryName(gameBackup));

            if (overwrite || !File.Exists(appBackup))
            {
                File.Copy(live, appBackup, true);
            }
            if (overwrite || !File.Exists(gameBackup))
            {
                File.Copy(live, gameBackup, true);
            }
            return true;
        }

        // Creates / refreshes the full revert state. Run after a game update so
        // Restore Backup has fresh backups to revert to.
        private void CreateFullBackup()
        {
            if (!IsGameFolder(gamePath))
            {
                MessageBox.Show("Set the Crimson Desert folder first.", "Game folder missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var ans = MessageBox.Show(
                "Save the current game state as the revert point?\r\n\r\n" +
                "What gets saved (~few MB total):\r\n" +
                "  * meta\\0.papgt (registration file)\r\n" +
                "  * 0008\\0.pamt (archive index)\r\n" +
                "  * Byte length of each 0008\\<N>.paz file\r\n\r\n" +
                "When to run this:\r\n" +
                "  * After Crimson Desert updates and BEFORE applying any mods\r\n" +
                "  * After Steam -> Verify Integrity of Game Files\r\n" +
                "  * Whenever you're confident the current state is vanilla\r\n\r\n" +
                "If mods are currently applied, click 'Restore Backup' first so the backup captures the vanilla state.\r\n\r\n" +
                "Continue?",
                "Create Backup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans != DialogResult.Yes) return;

            try
            {
                int saved = 0;

                // 1. papgt
                var papgtLive = LivePapgtPath();
                if (!string.IsNullOrEmpty(papgtLive) && File.Exists(papgtLive))
                {
                    var appBackup = AppPapgtBackupPath();
                    Directory.CreateDirectory(Path.GetDirectoryName(appBackup));
                    File.Copy(papgtLive, appBackup, true);

                    var gameBackup = GamePapgtBackupPath();
                    if (!string.IsNullOrEmpty(gameBackup))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(gameBackup));
                        File.Copy(papgtLive, gameBackup, true);
                    }
                    saved++;
                    Log("Backup: papgt refreshed.");
                }
                else
                {
                    Log("Backup: papgt not found at " + papgtLive + ", skipped.");
                }

                // 2. pamt
                var pamtLive = Path.Combine(gamePath, "0008", "0.pamt");
                var backupRoot = PazBackupsRoot();
                Directory.CreateDirectory(backupRoot);
                if (File.Exists(pamtLive))
                {
                    var pamtBackup = Path.Combine(backupRoot, "0.pamt.original");
                    File.Copy(pamtLive, pamtBackup, true);
                    saved++;
                    Log("Backup: pamt refreshed.");
                }
                else
                {
                    Log("Backup: pamt not found, skipped.");
                }

                // 3. paz lengths (only files matching <digits>.paz, not the .paz.backup leftovers)
                var pazDir = Path.Combine(gamePath, "0008");
                int pazCount = 0;
                if (Directory.Exists(pazDir))
                {
                    var pazRegex = new Regex(@"^\d+\.paz$");
                    foreach (var pazFile in Directory.GetFiles(pazDir, "*.paz"))
                    {
                        var name = Path.GetFileName(pazFile);
                        if (!pazRegex.IsMatch(name)) continue;
                        var sz = new FileInfo(pazFile).Length;
                        var lengthFile = Path.Combine(backupRoot, name + ".length.original");
                        File.WriteAllText(lengthFile, sz.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        pazCount++;
                    }
                    Log("Backup: recorded length for " + pazCount + " paz file(s).");
                }
                WriteRestoreGuardManifest("manual-backup");

                UpdateInspectorBackup();
                MessageBox.Show(
                    "Backup created.\r\n\r\n" +
                    "Captured: " + saved + " index file(s) + " + pazCount + " paz length record(s).\r\n\r\n" +
                    "Until you click Apply Mods, this is the state Restore Backup will revert to.",
                    "Backup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Backup failed: " + ex.Message, "Backup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BackupPapgt()
        {
            if (!IsGameFolder(gamePath))
            {
                MessageBox.Show("Set the Crimson Desert folder first.", "Game folder missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var answer = MessageBox.Show("Create/overwrite the 0.papgt backup from the current game file?", "Backup 0.papgt", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            try
            {
                if (EnsurePapgtBackup(true))
                {
                    WriteRestoreGuardManifest("manual-papgt-backup");
                    Log("Backed up 0.papgt to backups/ and game/_jmm_backups/.");
                    UpdateInspectorBackup();
                    MessageBox.Show("Backup created.", "Backup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Backup failed: " + ex.Message, "Backup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RestorePapgtBackup()
        {
            if (!IsGameFolder(gamePath))
            {
                MessageBox.Show("Set the Crimson Desert folder first.", "Game folder missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var backup = File.Exists(GamePapgtBackupPath()) ? GamePapgtBackupPath() : AppPapgtBackupPath();
            if (!File.Exists(backup))
            {
                MessageBox.Show("No 0.papgt backup exists yet. Use Backup 0.papgt first.", "No backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var answer = MessageBox.Show("Restore 0.papgt from backup?\r\n\r\nThis overwrites the current meta/0.papgt registration file.", "Restore backup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            if (!ValidateRestoreGuard()) return;

            try
            {
                File.Copy(backup, LivePapgtPath(), true);
                config["modsApplied"] = false;
                config["modsInstalled"] = false;
                SaveConfig(config);
                Log("Restored meta/0.papgt from backup: " + backup);
                MessageBox.Show("0.papgt restored.", "Restore complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Restore failed: " + ex.Message, "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string DetectGameFolder()
        {
            var candidates = BuildGameFolderCandidates();
            return candidates.FirstOrDefault(IsGameFolder) ?? "";
        }

        private bool IsGameFolder(string path)
        {
            path = NormalizeGameFolderInput(path);
            return !string.IsNullOrWhiteSpace(path)
                && Directory.Exists(Path.Combine(path, "bin64"))
                && File.Exists(Path.Combine(path, "0008", "0.pamt"));
        }

        private static IEnumerable<string> BuildGameFolderCandidates()
        {
            var candidates = new List<string>
            {
                @"E:\Program Files\Steam\steamapps\common\Crimson Desert",
                @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
                @"C:\Program Files\Steam\steamapps\common\Crimson Desert"
            };

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                home = Environment.GetEnvironmentVariable("HOME");
            }

            if (!string.IsNullOrWhiteSpace(home))
            {
                var steamRoots = new[]
                {
                    Path.Combine(home, ".steam", "steam"),
                    Path.Combine(home, ".steam", "root"),
                    Path.Combine(home, ".local", "share", "Steam"),
                    Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
                };

                foreach (var root in steamRoots)
                {
                    AddSteamGameCandidate(candidates, root);
                    foreach (var library in ReadSteamLibraryFolders(root))
                    {
                        AddSteamGameCandidate(candidates, library);
                    }
                }
            }

            return candidates.Select(NormalizeGameFolderInput)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddSteamGameCandidate(List<string> candidates, string steamRoot)
        {
            if (string.IsNullOrWhiteSpace(steamRoot)) return;
            candidates.Add(Path.Combine(steamRoot, "steamapps", "common", "Crimson Desert"));
        }

        private static IEnumerable<string> ReadSteamLibraryFolders(string steamRoot)
        {
            var output = new List<string>();
            if (string.IsNullOrWhiteSpace(steamRoot)) return output;
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) return output;

            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf, Encoding.UTF8), "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    var path = match.Groups[1].Value.Replace(@"\\", @"\");
                    path = NormalizeGameFolderInput(path);
                    if (!string.IsNullOrWhiteSpace(path)) output.Add(path);
                }
            }
            catch { }
            return output;
        }

        private static string NormalizeGameFolderInput(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            path = path.Trim().Trim('"', '\'');

            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try { path = new Uri(path).LocalPath; } catch { path = path.Substring("file://".Length); }
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home) && (path == "~" || path.StartsWith("~/") || path.StartsWith(@"~\", StringComparison.Ordinal)))
            {
                path = Path.Combine(home, path.Length == 1 ? "" : path.Substring(2));
            }

            path = Environment.ExpandEnvironmentVariables(path);
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\');
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
            logBox.AppendText(message + Environment.NewLine);
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

    // ====================================================== CUSTOM CONTROLS

    internal class RoundedPanel : Panel
    {
        public int CornerRadius { get; set; } = 18;
        public int BorderWidth { get; set; } = 1;
        public Color BorderColor { get; set; } = Color.FromArgb(80, 216, 166, 64);
        public Color GradientTopOverride { get; set; } = Color.Empty;
        public Color GradientBottomOverride { get; set; } = Color.Empty;
        public Color BottomBorderColor { get; set; } = Color.Empty;
        public bool Dashed { get; set; } = false;
        public bool PulseAccent { get; set; } = false;
        public Color AccentColor { get; set; } = Color.FromArgb(216, 166, 64);

        public RoundedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            var top = GradientTopOverride == Color.Empty ? Color.FromArgb(245, 32, 33, 21) : GradientTopOverride;
            var bot = GradientBottomOverride == Color.Empty ? Color.FromArgb(245, 18, 19, 14) : GradientBottomOverride;
            using (var path = RoundedRect(rect, CornerRadius))
            {
                if (rect.Width > 0 && rect.Height > 0)
                {
                    using (var brush = new LinearGradientBrush(rect, top, bot, 90f))
                        g.FillPath(brush, path);
                }
                if (BorderWidth > 0)
                {
                    using (var pen = new Pen(PulseAccent ? AccentColor : BorderColor, BorderWidth))
                    {
                        if (Dashed)
                        {
                            pen.DashStyle = DashStyle.Dash;
                            pen.DashPattern = new float[] { 6f, 4f };
                        }
                        g.DrawPath(pen, path);
                    }
                }
                if (BottomBorderColor != Color.Empty && CornerRadius == 0)
                {
                    using (var pen = new Pen(BottomBorderColor, 1))
                        g.DrawLine(pen, 0, rect.Bottom, rect.Right, rect.Bottom);
                }
            }
            base.OnPaint(e);
        }

        public static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0 || rect.Width <= 0 || rect.Height <= 0)
            {
                if (rect.Width > 0 && rect.Height > 0) path.AddRectangle(rect);
                else path.AddRectangle(new Rectangle(0, 0, 1, 1));
                return path;
            }
            var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseAllFigures();
            return path;
        }
    }

    internal sealed class Pill : Control
    {
        public Color DotColor { get; set; } = Color.Empty;
        public Color WarnColor { get; set; } = Color.FromArgb(216, 166, 64);
        public Color PillFillColor { get; set; } = Color.FromArgb(12, 255, 255, 255);
        public Color PillBorderColor { get; set; } = Color.FromArgb(30, 255, 255, 255);
        public Color PillTextColor { get; set; } = Color.FromArgb(169, 157, 124);
        public bool BorderlessTag { get; set; } = false;

        public Pill()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            Font = new Font("Consolas", 8.5f, FontStyle.Bold);
            ForeColor = PillTextColor;
            BackColor = Color.Transparent;
            Height = 28;
            Margin = new Padding(0, 0, 8, 0);
        }

        public override string Text
        {
            get { return base.Text; }
            set
            {
                base.Text = value;
                AutoFitWidth();
                Invalidate();
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            AutoFitWidth();
        }

        private void AutoFitWidth()
        {
            using (var g = CreateGraphics())
            {
                var size = TextRenderer.MeasureText(g, Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                var leftPad = (DotColor != Color.Empty) ? 26 : 12;
                var rightPad = 12;
                Width = size.Width + leftPad + rightPad;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            using (var path = RoundedPanel.RoundedRect(rect, Math.Max(0, Height / 2)))
            using (var bg = new SolidBrush(PillFillColor))
            {
                g.FillPath(bg, path);
                if (!BorderlessTag)
                {
                    using (var border = new Pen(PillBorderColor, 1))
                        g.DrawPath(border, path);
                }
            }

            var dotSpace = DotColor != Color.Empty ? 20 : 0;
            if (DotColor != Color.Empty)
            {
                using (var glow = new SolidBrush(Color.FromArgb(80, DotColor)))
                    g.FillEllipse(glow, 7, Height / 2 - 7, 14, 14);
                using (var dot = new SolidBrush(DotColor))
                    g.FillEllipse(dot, 9, Height / 2 - 5, 10, 10);
            }
            var fg = BorderlessTag ? PillTextColor : ForeColor;
            var size = TextRenderer.MeasureText(g, Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            var textX = Math.Max(dotSpace + 6, (Width - size.Width + dotSpace) / 2);
            var y = (Height - size.Height) / 2;
            TextRenderer.DrawText(g, Text ?? "", Font, new Point(textX, y), fg, TextFormatFlags.NoPadding);
        }
    }

    // Double-buffered scrolling Panel - used for the install panel body. Uses standard
    // DoubleBuffered (NOT WS_EX_COMPOSITED, which defers paint passes and breaks live thumb tracking).
    // We additionally handle WM_VSCROLL with SB_THUMBTRACK explicitly: WinForms' default ScrollableControl
    // updates AutoScrollPosition on thumb track but doesn't always paint immediately - calling Refresh
    // forces an immediate repaint so the user sees the content move while their mouse is still held.
    internal sealed class BufferedScrollPanel : Panel
    {
        public BufferedScrollPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_VSCROLL = 0x115;
            const int WM_HSCROLL = 0x114;
            const int SB_THUMBTRACK = 5;
            const int SB_THUMBPOSITION = 4;
            base.WndProc(ref m);
            if (m.Msg == WM_VSCROLL || m.Msg == WM_HSCROLL)
            {
                int code = m.WParam.ToInt32() & 0xFFFF;
                if (code == SB_THUMBTRACK || code == SB_THUMBPOSITION)
                {
                    // Force an immediate repaint so the content tracks the thumb in real time.
                    Refresh();
                }
                else
                {
                    Update();
                }
            }
        }
    }

    // Same trick for FlowLayoutPanel - used for the mod card host so the cards repaint smoothly
    // during AutoScroll. Same WS_EX_COMPOSITED reasoning: skip it.
    internal sealed class BufferedFlowPanel : FlowLayoutPanel
    {
        public BufferedFlowPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    // Fully custom checkbox built on Control, NOT CheckBox - the CheckBox base class still drives
    // some native painting through the Windows comctl32 button class even with UserPaint set, which
    // is what was leaving the checkbox looking unchanged regardless of our OnPaint colour tweaks.
    internal sealed class FlatCheck : Control
    {
        private bool _checked;
        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler CheckedChanged;

        public Color BoxFill { get; set; } = Color.FromArgb(80, 82, 66);
        public Color BoxBorder { get; set; } = Color.FromArgb(230, 210, 160);
        public Color CheckedFill { get; set; } = Color.FromArgb(216, 166, 64);
        public Color CheckedBorder { get; set; } = Color.FromArgb(244, 199, 103);
        public Color CheckMarkColor { get; set; } = Color.FromArgb(21, 15, 8);

        public FlatCheck()
        {
            SetStyle(ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Width = 20;
            Height = 20;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int side = Math.Min(Width, Height) - 2;
            var rect = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
            using (var path = RoundedPanel.RoundedRect(rect, 4))
            {
                using (var fill = new SolidBrush(Checked ? CheckedFill : BoxFill))
                    g.FillPath(fill, path);
                using (var pen = new Pen(Checked ? CheckedBorder : BoxBorder, 1.8f))
                    g.DrawPath(pen, path);
            }
            if (Checked)
            {
                using (var pen = new Pen(CheckMarkColor, 2.4f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    int x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;
                    g.DrawLines(pen, new[] {
                        new Point(x + (int)(w * 0.22), y + (int)(h * 0.52)),
                        new Point(x + (int)(w * 0.42), y + (int)(h * 0.72)),
                        new Point(x + (int)(w * 0.78), y + (int)(h * 0.30))
                    });
                }
            }
        }
    }

    // TabControl subclass that paints its full background dark before letting DrawItem render the tabs.
    // The stock TabControl with Appearance.FlatButtons paints its strip with SystemColors.Control (white),
    // which leaks through under our owner-drawn tab buttons. Taking over OnPaint kills that completely.
    internal sealed class DarkTabControl : TabControl
    {
        public Color StripColor { get; set; } = Color.FromArgb(14, 15, 11);

        public DarkTabControl()
        {
            SetStyle(ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            DrawMode = TabDrawMode.OwnerDrawFixed;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var b = new SolidBrush(StripColor)) e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Background - the whole control area, including the strip-right and the page-border line.
            using (var b = new SolidBrush(StripColor)) e.Graphics.FillRectangle(b, ClientRectangle);
            // Tabs - fire DrawItem for each so existing handlers render the buttons.
            for (int i = 0; i < TabCount; i++)
            {
                var rect = GetTabRect(i);
                var state = (i == SelectedIndex) ? DrawItemState.Selected : DrawItemState.None;
                var args = new DrawItemEventArgs(e.Graphics, Font, rect, i, state);
                OnDrawItem(args);
            }
        }
    }

    internal sealed class GradientButton : Control
    {
        public enum Style { Default, Primary, Safe, Danger, Donate }
        public Style Kind { get; set; } = Style.Default;
        private bool hovering;
        private bool pressed;

        public GradientButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Consolas", 9f, FontStyle.Bold);
            Height = 36;
            Margin = new Padding(0, 0, 8, 0);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            Color a, b, fg, border;
            switch (Kind)
            {
                case Style.Primary:
                    a = Color.FromArgb(244, 199, 103); b = Color.FromArgb(216, 166, 64);
                    fg = Color.FromArgb(23, 16, 6); border = Color.FromArgb(120, 244, 199, 103); break;
                case Style.Safe:
                    a = Color.FromArgb(155, 229, 179); b = Color.FromArgb(101, 197, 134);
                    fg = Color.FromArgb(8, 19, 13); border = Color.FromArgb(120, 101, 197, 134); break;
                case Style.Danger:
                    a = Color.FromArgb(238, 141, 127); b = Color.FromArgb(216, 92, 76);
                    fg = Color.FromArgb(24, 7, 5); border = Color.FromArgb(120, 216, 92, 76); break;
                case Style.Donate:
                    a = Color.FromArgb(255, 221, 0); b = Color.FromArgb(255, 193, 7);
                    fg = Color.FromArgb(24, 16, 4); border = Color.FromArgb(140, 255, 193, 7); break;
                default:
                    a = Color.FromArgb(34, 255, 255, 255); b = Color.FromArgb(14, 255, 255, 255);
                    fg = ForeColor; border = Color.FromArgb(36, 255, 255, 255); break;
            }
            if (hovering)
            {
                a = Lighten(a, 0.10f); b = Lighten(b, 0.10f);
            }
            if (pressed)
            {
                a = Darken(a, 0.10f); b = Darken(b, 0.10f);
            }
            using (var path = RoundedPanel.RoundedRect(rect, 12))
            using (var brush = new LinearGradientBrush(rect, a, b, 135f))
            using (var pen = new Pen(border, 1))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text ?? "", Font, rect, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static Color Lighten(Color c, float amt)
        {
            return Color.FromArgb(c.A,
                (int)Math.Min(255, c.R + 255 * amt),
                (int)Math.Min(255, c.G + 255 * amt),
                (int)Math.Min(255, c.B + 255 * amt));
        }

        private static Color Darken(Color c, float amt)
        {
            return Color.FromArgb(c.A,
                (int)Math.Max(0, c.R - 255 * amt),
                (int)Math.Max(0, c.G - 255 * amt),
                (int)Math.Max(0, c.B - 255 * amt));
        }
    }

    internal sealed class BrandMark : Control
    {
        public string Letters { get; set; } = "UJ";

        // Loaded once at startup. If logo.png sits next to the exe (or as an
        // embedded resource named "logo.png"), it is rendered into the brand
        // tile. Otherwise we fall back to the gold "UJ" gradient tile.
        private static readonly Image logoImage = LoadLogoImage();

        private static Image LoadLogoImage()
        {
            try
            {
                var sidecar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
                if (File.Exists(sidecar))
                {
                    using (var fs = File.OpenRead(sidecar))
                    {
                        return Image.FromStream(new MemoryStream(File.ReadAllBytes(sidecar)));
                    }
                }
                var asm = Assembly.GetExecutingAssembly();
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("logo.png", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var stream = asm.GetManifestResourceStream(name))
                        {
                            if (stream != null) return Image.FromStream(stream);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public BrandMark()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            if (logoImage != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var path = RoundedPanel.RoundedRect(rect, Math.Max(6, Math.Min(rect.Width, rect.Height) / 6)))
                {
                    g.SetClip(path);
                    g.DrawImage(logoImage, rect);
                    g.ResetClip();
                }
                return;
            }
            using (var path = RoundedPanel.RoundedRect(rect, 14))
            using (var brush = new LinearGradientBrush(rect, Color.FromArgb(244, 199, 103), Color.FromArgb(159, 103, 36), 135f))
            using (var border = new Pen(Color.FromArgb(120, 216, 166, 64), 1))
            {
                g.FillPath(brush, path);
                g.DrawPath(border, path);
            }
            using (var f = new Font("Consolas", 18, FontStyle.Bold))
            {
                var ts = TextRenderer.MeasureText(g, Letters ?? "", f);
                var x = (Width - ts.Width) / 2;
                var y = (Height - ts.Height) / 2;
                TextRenderer.DrawText(g, Letters ?? "", f, new Point(x, y), Color.FromArgb(21, 16, 7), TextFormatFlags.NoPadding);
            }
        }
    }

    internal sealed class ThemeSwatch : Control
    {
        public Theme SwatchTheme { get; private set; }
        public bool IsActive { get; set; }
        // When true the swatch renders as a dashed-border placeholder with a centred "+" - used for the
        // unconfigured Custom theme slot, so it reads as "click to choose" rather than as a real colour.
        public bool IsEmptyPlaceholder { get; set; }

        public ThemeSwatch(Theme theme)
        {
            SwatchTheme = theme;
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

            if (IsEmptyPlaceholder)
            {
                // Subtle translucent fill so the swatch reads as "empty" against the dark panel.
                using (var path = RoundedPanel.RoundedRect(rect, 12))
                using (var brush = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                {
                    g.FillPath(brush, path);
                }
                // Dashed border to communicate "click to set"
                using (var path2 = RoundedPanel.RoundedRect(rect, 12))
                using (var pen = new Pen(Color.FromArgb(120, 200, 200, 200), 1.4f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash, DashPattern = new float[] { 3f, 3f } })
                {
                    g.DrawPath(pen, path2);
                }
                // Centred "+" glyph
                var plusColor = Color.FromArgb(170, 220, 220, 220);
                using (var pen = new Pen(plusColor, 1.6f))
                {
                    int cx = Width / 2;
                    int cy = Height / 2;
                    int arm = Math.Min(Width, Height) / 6;
                    g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                    g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
                }
                if (IsActive)
                {
                    using (var path3 = RoundedPanel.RoundedRect(rect, 12))
                    using (var pen = new Pen(SwatchTheme.Accent2, 2))
                    {
                        g.DrawPath(pen, path3);
                    }
                }
                return;
            }

            using (var path = RoundedPanel.RoundedRect(rect, 12))
            using (var brush = new LinearGradientBrush(rect, SwatchTheme.Accent2, SwatchTheme.Panel2, 135f))
            {
                g.FillPath(brush, path);
            }
            if (IsActive)
            {
                using (var path2 = RoundedPanel.RoundedRect(rect, 12))
                using (var pen = new Pen(SwatchTheme.Accent2, 2))
                {
                    g.DrawPath(pen, path2);
                }
            }
            else
            {
                using (var path2 = RoundedPanel.RoundedRect(rect, 12))
                using (var pen = new Pen(Color.FromArgb(36, 255, 255, 255), 1))
                {
                    g.DrawPath(pen, path2);
                }
            }
        }
    }

    internal enum BadgeKind { Neutral, Ok, Warn, Bad }

    internal sealed class CheckGridRow : RoundedPanel
    {
        private readonly BadgePill badgeControl;
        private readonly Font labelFont = new Font("Consolas", 9.5f);
        private string labelText;
        private Color dotColor = Color.FromArgb(140, 140, 140);

        public CheckGridRow(string label, string badge, BadgeKind kind)
        {
            CornerRadius = 14;
            BorderWidth = 1;
            GradientTopOverride = Color.FromArgb(36, 0, 0, 0);
            GradientBottomOverride = Color.FromArgb(58, 0, 0, 0);
            BorderColor = Color.FromArgb(28, 255, 255, 255);
            Padding = new Padding(11, 0, 11, 0);
            Height = 38;
            DoubleBuffered = true;
            labelText = label;

            // Badge is the only child control - everything else is painted in OnPaint so the dot
            // and label always sit on the row's exact vertical centre, regardless of font metrics.
            badgeControl = new BadgePill
            {
                Width = 78,
                Height = 22,
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            // Position badge against the right edge, vertically centred.
            badgeControl.Location = new Point(0, 0); // updated in OnLayout
            Controls.Add(badgeControl);

            SetState(badge, kind);
            Resize += (s, e) => PositionBadge();
            PositionBadge();
        }

        private void PositionBadge()
        {
            int x = Width - Padding.Right - badgeControl.Width;
            int y = (Height - badgeControl.Height) / 2;
            badgeControl.Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); // draw the rounded background
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int dotSize = 12;
            int dotX = Padding.Left;
            int dotY = (Height - dotSize) / 2;
            // Glow + filled dot.
            using (var glow = new SolidBrush(Color.FromArgb(80, dotColor)))
                g.FillEllipse(glow, dotX - 1, dotY - 1, dotSize + 2, dotSize + 2);
            using (var b = new SolidBrush(dotColor))
                g.FillEllipse(b, dotX + 1, dotY + 1, dotSize - 2, dotSize - 2);

            // Label - painted at the row's exact vertical centre so it always lines up with the dot.
            int textLeft = dotX + dotSize + 8;
            int textRight = badgeControl.Left - 6;
            var textRect = new Rectangle(textLeft, 0, Math.Max(20, textRight - textLeft), Height);
            TextRenderer.DrawText(g, labelText ?? "", labelFont, textRect,
                Color.FromArgb(244, 234, 209),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        public void SetState(string text, BadgeKind kind)
        {
            badgeControl.SetState(text, kind);
            switch (kind)
            {
                case BadgeKind.Ok: dotColor = Color.FromArgb(101, 197, 134); break;
                case BadgeKind.Warn: dotColor = Color.FromArgb(216, 166, 64); break;
                case BadgeKind.Bad: dotColor = Color.FromArgb(216, 92, 76); break;
                default: dotColor = Color.FromArgb(140, 140, 140); break;
            }
            Invalidate();
        }
    }

    internal sealed class BadgePill : Control
    {
        private string text = "";
        private BadgeKind kind = BadgeKind.Neutral;

        public BadgePill()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Consolas", 8.5f, FontStyle.Bold);
            Height = 22;
        }

        public void SetState(string newText, BadgeKind newKind)
        {
            text = newText;
            kind = newKind;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            Color fill, fg;
            switch (kind)
            {
                case BadgeKind.Ok: fill = Color.FromArgb(101, 197, 134); fg = Color.FromArgb(8, 19, 13); break;
                case BadgeKind.Warn: fill = Color.FromArgb(216, 166, 64); fg = Color.FromArgb(24, 16, 8); break;
                case BadgeKind.Bad: fill = Color.FromArgb(216, 92, 76); fg = Color.White; break;
                default: fill = Color.FromArgb(40, 255, 255, 255); fg = Color.FromArgb(169, 157, 124); break;
            }
            using (var path = RoundedPanel.RoundedRect(rect, Math.Max(0, Height / 2)))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
            }
            var ts = TextRenderer.MeasureText(g, text ?? "", Font);
            var x = (Width - ts.Width) / 2;
            var y = (Height - ts.Height) / 2;
            TextRenderer.DrawText(g, text ?? "", Font, new Point(x, y), fg, TextFormatFlags.NoPadding);
        }
    }

    internal sealed class DotPanel : Panel
    {
        public Color Color { get; set; } = System.Drawing.Color.FromArgb(101, 197, 134);

        public DotPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = System.Drawing.Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var glow = new SolidBrush(System.Drawing.Color.FromArgb(80, Color)))
                g.FillEllipse(glow, -1, -1, Width + 2, Height + 2);
            using (var b = new SolidBrush(Color))
                g.FillEllipse(b, 1, 1, Width - 2, Height - 2);
        }
    }

    // ====================================================== DATA / EXTRACTOR

    internal sealed class JsonMod
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public string Version { get; private set; }
        public string Description { get; private set; }
        public string Author { get; private set; }
        public string FormatTag { get; private set; }
        public List<PatchChange> Changes { get; private set; }
        public List<string> Groups { get; private set; }
        public List<string> OverlayFolders { get; private set; }

        public static JsonMod Load(string path, JavaScriptSerializer json)
        {
            var root = json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("JSON root is not an object.");
            if (IsFieldFormat(root)) return LoadFieldFormat(path, root);

            var mod = new JsonMod
            {
                Path = path,
                Name = GetString(root, "name", System.IO.Path.GetFileNameWithoutExtension(path)),
                Version = GetString(root, "version", ""),
                Description = GetString(root, "description", ""),
                Author = GetString(root, "author", ""),
                FormatTag = "JSON",
                Changes = new List<PatchChange>(),
                Groups = new List<string>(),
                OverlayFolders = new List<string>()
            };

            if (root.ContainsKey("patches") && root["patches"] is object[] patches)
            {
                foreach (var patchObj in patches)
                {
                    var patch = patchObj as Dictionary<string, object>;
                    if (patch == null) continue;
                    var gameFile = GetString(patch, "game_file", GetString(patch, "file", ""));
                    if (!patch.ContainsKey("changes") || !(patch["changes"] is object[] changes)) continue;
                    foreach (var changeObj in changes)
                    {
                        var change = changeObj as Dictionary<string, object>;
                        if (change == null) continue;
                        mod.Changes.Add(new PatchChange
                        {
                            GameFile = gameFile,
                            Offset = Convert.ToInt32(change["offset"]),
                            Label = GetString(change, "label", ""),
                            Original = GetString(change, "original", "").Replace(" ", ""),
                            Patched = GetString(change, "patched", "").Replace(" ", ""),
                            IsResolvedBytes = true
                        });
                    }
                }
            }
            else if (root.ContainsKey("changes") && root["changes"] is object[] topLevelChanges)
            {
                var gameFile = GetString(root, "game_file", GetString(root, "file", ""));
                foreach (var changeObj in topLevelChanges)
                {
                    var change = changeObj as Dictionary<string, object>;
                    if (change == null) continue;
                    mod.Changes.Add(new PatchChange
                    {
                        GameFile = gameFile,
                        Offset = Convert.ToInt32(change["offset"]),
                        Label = GetString(change, "label", ""),
                        Original = GetString(change, "original", "").Replace(" ", ""),
                        Patched = GetString(change, "patched", "").Replace(" ", ""),
                        IsResolvedBytes = true
                    });
                }
            }

            mod.Groups = mod.Changes.Select(change => change.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (mod.Groups.Count == 0) mod.Groups.Add("All");
            return mod;
        }

        private static bool IsFieldFormat(Dictionary<string, object> root)
        {
            if (!root.ContainsKey("format")) return false;
            try
            {
                if (Convert.ToInt32(root["format"]) != 3) return false;
            }
            catch { return false; }
            return root.ContainsKey("targets") || (root.ContainsKey("target") && root.ContainsKey("intents"));
        }

        private static JsonMod LoadFieldFormat(string path, Dictionary<string, object> root)
        {
            var info = root.ContainsKey("modinfo") ? root["modinfo"] as Dictionary<string, object> : null;
            var mod = new JsonMod
            {
                Path = path,
                Name = info != null ? GetString(info, "title", System.IO.Path.GetFileNameWithoutExtension(path)) : System.IO.Path.GetFileNameWithoutExtension(path),
                Version = info != null ? GetString(info, "version", "") : GetString(root, "version", ""),
                Description = info != null ? GetString(info, "description", "") : GetString(root, "description", ""),
                Author = info != null ? GetString(info, "author", "") : GetString(root, "author", ""),
                FormatTag = "FIELDS",
                Changes = new List<PatchChange>(),
                Groups = new List<string>(),
                OverlayFolders = new List<string>()
            };

            if (root.ContainsKey("targets") && root["targets"] is object[] targets)
            {
                foreach (var targetObj in targets)
                {
                    var target = targetObj as Dictionary<string, object>;
                    if (target == null) continue;
                    AddFieldIntents(mod, GetString(target, "file", ""), target.ContainsKey("intents") ? target["intents"] as object[] : null);
                }
            }
            else
            {
                AddFieldIntents(mod, GetString(root, "target", ""), root.ContainsKey("intents") ? root["intents"] as object[] : null);
            }

            mod.Groups = mod.Changes.Select(change => change.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (mod.Groups.Count == 0) mod.Groups.Add("All");
            return mod;
        }

        public static JsonMod LoadOverlayDirectory(string path, JavaScriptSerializer json, string formatTag)
        {
            Dictionary<string, object> info = null;
            string metadata = formatTag == "BROWSER" ? System.IO.Path.Combine(path, "manifest.json") : System.IO.Path.Combine(path, "modinfo.json");
            if (formatTag == "RAW" && !File.Exists(metadata)) metadata = System.IO.Path.Combine(path, "mod.json");
            if (File.Exists(metadata)) info = json.DeserializeObject(File.ReadAllText(metadata, Encoding.UTF8)) as Dictionary<string, object>;
            if (info != null && info.ContainsKey("modinfo") && info["modinfo"] is Dictionary<string, object> nestedInfo) info = nestedInfo;
            var mod = new JsonMod
            {
                Path = path,
                Name = info != null ? GetString(info, formatTag == "BROWSER" ? "title" : "name", GetString(info, "title", System.IO.Path.GetFileName(path))) : System.IO.Path.GetFileName(path),
                Version = info != null ? GetString(info, "version", "") : "",
                Description = info != null ? GetString(info, "description", "") : "",
                Author = info != null ? GetString(info, "author", "") : "",
                FormatTag = formatTag,
                Changes = new List<PatchChange>(),
                Groups = new List<string> { "All" },
                OverlayFolders = new List<string>()
            };

            var root = path;
            if (formatTag == "RAW"
                && Regex.IsMatch(System.IO.Path.GetFileName(path), @"^\d{4}$")
                && (File.Exists(System.IO.Path.Combine(path, "0.pamt"))
                    || File.Exists(System.IO.Path.Combine(path, "0.paz"))
                    || Directory.GetFiles(path, "*", SearchOption.AllDirectories).Any()))
            {
                mod.OverlayFolders.Add(path);
                if (string.IsNullOrWhiteSpace(mod.Description)) mod.Description = "Raw folder mod";
                return mod;
            }
            if (formatTag == "RAW" && info != null)
            {
                var filesDir = GetString(info, "files_dir", "files");
                var candidateRoot = System.IO.Path.Combine(path, filesDir);
                if (Directory.Exists(candidateRoot)) root = candidateRoot;
            }
            if (formatTag == "BROWSER" && info != null)
            {
                root = System.IO.Path.Combine(path, GetString(info, "files_dir", "files"));
            }
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var name = System.IO.Path.GetFileName(dir);
                    if (Regex.IsMatch(name, @"^\d{4}$")) mod.OverlayFolders.Add(dir);
                }
            }
            if (formatTag == "RAW" && mod.OverlayFolders.Count == 0 && IsLooseRawOverlayRoot(path))
            {
                mod.OverlayFolders.Add(path);
                if (string.IsNullOrWhiteSpace(mod.Description)) mod.Description = "Raw folder mod";
            }
            if (mod.OverlayFolders.Count == 1)
            {
                if (string.IsNullOrWhiteSpace(mod.Description) && info != null)
                    mod.Description = GetString(info, "title", "");
                if (info == null) mod.Name = System.IO.Path.GetFileName(mod.OverlayFolders[0]);
            }
            return mod;
        }

        private static bool IsLooseRawOverlayRoot(string path)
        {
            if (!Directory.Exists(path)) return false;
            if (Regex.IsMatch(System.IO.Path.GetFileName(path), @"^\d{4}$")) return false;

            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            if (files.Length == 0) return false;

            return files.Any(file =>
            {
                if (IsPackageMetadataFile(path, file)) return false;
                var rel = RelativePath(path, file).Replace('\\', '/');
                var slash = rel.IndexOf('/');
                if (slash <= 0) return false;
                var rootName = rel.Substring(0, slash);
                return IsLikelyGameOverlayRoot(rootName);
            });
        }

        private static bool IsLikelyGameOverlayRoot(string rootName)
        {
            var knownRoots = new[]
            {
                "character", "ui", "gamedata", "gamecommondata", "sound", "audio", "music",
                "effect", "texture", "font", "localization", "script", "datasheet", "world",
                "level", "prefab"
            };
            return knownRoots.Any(root => string.Equals(root, rootName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPackageMetadataFile(string root, string file)
        {
            var rel = RelativePath(root, file).Replace('\\', '/');
            if (rel.IndexOf('/') >= 0) return false;
            var name = System.IO.Path.GetFileName(file);
            return string.Equals(name, "mod.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "modinfo.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase);
        }

        private static string RelativePath(string root, string path)
        {
            return path.Substring(root.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        private static void AddFieldIntents(JsonMod mod, string gameFile, object[] intents)
        {
            if (intents == null) return;
            foreach (var intentObj in intents)
            {
                var intent = intentObj as Dictionary<string, object>;
                if (intent == null) continue;

                var entry = GetString(intent, "entry", "");
                var key = intent.ContainsKey("key") && intent["key"] != null ? Convert.ToString(intent["key"]) : "";
                var field = GetString(intent, "field", "");
                var op = GetString(intent, "op", "set");
                var newValue = intent.ContainsKey("new") && intent["new"] != null ? Convert.ToString(intent["new"]) : "";
                var labelParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(entry)) labelParts.Add(entry);
                if (!string.IsNullOrWhiteSpace(field)) labelParts.Add(field);
                if (!string.IsNullOrWhiteSpace(op)) labelParts.Add(op + " " + newValue);
                var label = labelParts.Count > 0 ? string.Join(" - ", labelParts.ToArray()) : "Field intent";

                mod.Changes.Add(new PatchChange
                {
                    GameFile = gameFile,
                    Offset = 0,
                    Label = label,
                    Original = "",
                    Patched = "",
                    IsResolvedBytes = false,
                    FieldPath = field,
                    EntryName = entry,
                    EntryKey = key,
                    Operation = op,
                    NewValue = newValue,
                    TargetDisplay = System.IO.Path.GetFileName(gameFile) + " - " + field + (string.IsNullOrWhiteSpace(newValue) ? "" : " -> " + newValue)
                });
            }
        }

        public IEnumerable<PatchChange> ChangesForGroup(string group)
        {
            return string.IsNullOrWhiteSpace(group) ? Changes : Changes.Where(change => string.Equals(change.Group, group, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetString(Dictionary<string, object> dict, string key, string fallback)
        {
            return dict.ContainsKey(key) && dict[key] != null ? Convert.ToString(dict[key]) : fallback;
        }
    }

    internal sealed class PatchChange
    {
        private static readonly Regex GroupRegex = new Regex(@"^\s*\[([^\]]+)\]\s*(.*)$", RegexOptions.Compiled);

        public string GameFile;
        public int Offset;
        public string Label;
        public string Original;
        public string Patched;
        public bool IsResolvedBytes;
        public string FieldPath;
        public string EntryName;
        public string EntryKey;
        public string Operation;
        public string NewValue;
        public string TargetDisplay;
        public string ResolveError;

        public string Group
        {
            get
            {
                var match = GroupRegex.Match(Label ?? "");
                return match.Success ? match.Groups[1].Value.Trim() : "All";
            }
        }

        public string CleanLabel
        {
            get
            {
                var match = GroupRegex.Match(Label ?? "");
                return match.Success ? match.Groups[2].Value.Trim() : Label;
            }
        }
    }

    internal sealed class PamtParseResult
    {
        public List<PazEntry> Entries = new List<PazEntry>();
        public int EntrySectionStart;   // byte offset within the PAMT where entry records begin
        public byte[] PamtBytes;        // raw bytes (caller can mutate then write back)
    }

    internal static class ArchiveExtractor
    {
        // ============================================================
        // LZ4 block encoder (all-literals form).
        //
        // Produces a valid LZ4 block per the Block Format spec: the entire
        // input is emitted as one literal-only sequence (one token byte +
        // optional extra-length bytes + literal bytes; no match section).
        // The existing Lz4BlockDecompress accepts this - when the decoder
        // exhausts input after consuming literals it stops cleanly.
        //
        // Cost: ~3-bytes-per-65KB overhead vs the input size. We pay that
        // overhead for simplicity - implementing a real LZ4 matcher buys
        // smaller output but matches our constraints already (we append
        // to .paz, so file growth doesn't shift any other entries).
        // ============================================================
        public static byte[] Lz4BlockCompress(byte[] data)
        {
            if (data == null) data = new byte[0];
            int len = data.Length;
            var output = new List<byte>(len + 8);
            if (len == 0)
            {
                output.Add(0x00);
                return output.ToArray();
            }
            if (len < 15)
            {
                output.Add((byte)(len << 4));
            }
            else
            {
                output.Add(0xF0); // literal-length code = 15 -> read extra bytes
                int remaining = len - 15;
                while (remaining >= 255)
                {
                    output.Add(255);
                    remaining -= 255;
                }
                output.Add((byte)remaining);
            }
            output.AddRange(data);
            return output.ToArray();
        }

        public static byte[] Lz4BlockDecompressPublic(byte[] data, uint originalSize)
        {
            return Lz4BlockDecompress(data, originalSize);
        }

        // ============================================================
        // PAMT parse + entry-section byte offset (so callers can patch
        // entries in place by computing entrySectionStart + idx*20).
        // Otherwise identical to ParsePamt.
        // ============================================================
        public static PamtParseResult ParsePamtFull(string pamtPath, string pazDir)
        {
            var data = File.ReadAllBytes(pamtPath);
            var pamtStem = Path.GetFileNameWithoutExtension(pamtPath);
            var off = 4;
            var pazCount = ReadU32(data, ref off);
            off += 8;
            for (var i = 0; i < pazCount; i++)
            {
                off += 8;
                if (i < pazCount - 1) off += 4;
            }

            var folderSize = ReadU32(data, ref off);
            var folderEnd = off + (int)folderSize;
            var folderPrefix = "";
            while (off < folderEnd)
            {
                var parent = ReadU32(data, ref off);
                var slen = data[off++];
                var name = Encoding.UTF8.GetString(data, off, slen);
                off += slen;
                if (parent == 0xFFFFFFFF) folderPrefix = name;
            }

            var nodeSize = ReadU32(data, ref off);
            var nodeStart = off;
            var nodes = new Dictionary<uint, Tuple<uint, string>>();
            while (off < nodeStart + nodeSize)
            {
                var rel = (uint)(off - nodeStart);
                var parent = ReadU32(data, ref off);
                var slen = data[off++];
                var name = Encoding.UTF8.GetString(data, off, slen);
                off += slen;
                nodes[rel] = Tuple.Create(parent, name);
            }

            Func<uint, string> buildPath = nodeRef =>
            {
                var parts = new List<string>();
                var cur = nodeRef;
                var guard = 0;
                while (cur != 0xFFFFFFFF && guard++ < 64 && nodes.ContainsKey(cur))
                {
                    var node = nodes[cur];
                    parts.Add(node.Item2);
                    cur = node.Item1;
                }
                parts.Reverse();
                return string.Concat(parts);
            };

            var folderCount = ReadU32(data, ref off);
            off += 4 + (int)folderCount * 16;

            var entrySectionStart = off;
            var entries = new List<PazEntry>();
            while (off + 20 <= data.Length)
            {
                var nodeRef = ReadU32(data, ref off);
                var pazOffset = ReadU32(data, ref off);
                var compSize = ReadU32(data, ref off);
                var origSize = ReadU32(data, ref off);
                var flags = ReadU32(data, ref off);
                var pazIndex = (int)(flags & 0xFF);
                var nodePath = buildPath(nodeRef);
                var fullPath = string.IsNullOrEmpty(folderPrefix) ? nodePath : folderPrefix + "/" + nodePath;
                var pazNum = int.Parse(pamtStem) + pazIndex;
                entries.Add(new PazEntry
                {
                    Path = fullPath,
                    PazFile = Path.Combine(pazDir, pazNum + ".paz"),
                    Offset = pazOffset,
                    CompSize = compSize,
                    OrigSize = origSize,
                    Flags = flags
                });
            }

            return new PamtParseResult { Entries = entries, EntrySectionStart = entrySectionStart, PamtBytes = data };
        }

        public static void WriteU32LE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        // ============================================================
        // 32-bit hash candidates - diagnostic harness.
        // We don't yet know which algorithm Crimson Desert uses for the
        // 4-byte field at PAMT offset 16 + i*12 + 0. The diagnostic
        // computes all common 32-bit hashes over the paz file (full + a
        // few windows) and prints them so we can identify the match.
        // Once the algorithm is known these can be trimmed down.
        // ============================================================

        private static uint[] _crc32IeeeTable;
        private static uint[] _crc32CTable;

        public static uint[] PublicCrcTable(uint poly) { return BuildCrcTable(poly); }

        // ============================================================
        // Bob Jenkins' lookup3 / hashlittle - used by Pearl Abyss engine.
        // Reference: http://burtleburtle.net/bob/c/lookup3.c
        // Returns just the primary 32-bit hash 'c'.
        // ============================================================
        public static uint HashLittle(byte[] data, int offset, int length, uint initval)
        {
            uint a, b, c;
            a = b = c = 0xdeadbeefu + (uint)length + initval;

            int i = offset;
            int remaining = length;
            while (remaining > 12)
            {
                a += (uint)data[i] | ((uint)data[i + 1] << 8) | ((uint)data[i + 2] << 16) | ((uint)data[i + 3] << 24);
                b += (uint)data[i + 4] | ((uint)data[i + 5] << 8) | ((uint)data[i + 6] << 16) | ((uint)data[i + 7] << 24);
                c += (uint)data[i + 8] | ((uint)data[i + 9] << 8) | ((uint)data[i + 10] << 16) | ((uint)data[i + 11] << 24);
                // mix(a,b,c)
                a -= c; a ^= Rotl(c, 4); c += b;
                b -= a; b ^= Rotl(a, 6); a += c;
                c -= b; c ^= Rotl(b, 8); b += a;
                a -= c; a ^= Rotl(c, 16); c += b;
                b -= a; b ^= Rotl(a, 19); a += c;
                c -= b; c ^= Rotl(b, 4); b += a;
                i += 12; remaining -= 12;
            }
            // Tail (1..12 bytes)
            switch (remaining)
            {
                case 12: c += (uint)data[i + 11] << 24; goto case 11;
                case 11: c += (uint)data[i + 10] << 16; goto case 10;
                case 10: c += (uint)data[i + 9] << 8; goto case 9;
                case 9: c += (uint)data[i + 8]; goto case 8;
                case 8: b += (uint)data[i + 7] << 24; goto case 7;
                case 7: b += (uint)data[i + 6] << 16; goto case 6;
                case 6: b += (uint)data[i + 5] << 8; goto case 5;
                case 5: b += (uint)data[i + 4]; goto case 4;
                case 4: a += (uint)data[i + 3] << 24; goto case 3;
                case 3: a += (uint)data[i + 2] << 16; goto case 2;
                case 2: a += (uint)data[i + 1] << 8; goto case 1;
                case 1: a += (uint)data[i]; break;
                case 0: return c;
            }
            // final(a,b,c)
            c ^= b; c -= Rotl(b, 14);
            a ^= c; a -= Rotl(c, 11);
            b ^= a; b -= Rotl(a, 25);
            c ^= b; c -= Rotl(b, 16);
            a ^= c; a -= Rotl(c, 4);
            b ^= a; b -= Rotl(a, 14);
            c ^= b; c -= Rotl(b, 24);
            return c;
        }

        private static uint Rotl(uint x, int k) { return (x << k) | (x >> (32 - k)); }

        // For huge files, allocate the whole file into memory and call HashLittle once.
        public static uint HashLittleFile(string path, uint initval)
        {
            var buf = File.ReadAllBytes(path);
            return HashLittle(buf, 0, buf.Length, initval);
        }

        // ============================================================
        // Pearl Abyss checksum (used by Crimson Desert).
        // Pearl Abyss archive checksum routine used for PAZ/PAMT/PAPGT integrity fields.
        //
        // Variant of Bob Jenkins lookup3 with a custom init and a
        // custom finalisation that mixes Rotl + Rotr.
        //   PA_MAGIC = 558_228_019 (= 0x21456BB3)
        //   init: a = b = c = (uint)(length - PA_MAGIC)
        //   mix:  same six rotations as standard lookup3 (4,6,8,16,19,4)
        //   final: 8-step custom mix (see code below).
        //
        // Used to compute:
        //   * per-paz Crc       - PaChecksum(<entire .paz file>),
        //                         written into 0.pamt at row+4 of each paz
        //   * PAMT HeaderCrc    - PaChecksum(pamt[12..end]),
        //                         written at 0.pamt[0..3]
        //   * papgt HeaderCrc   - PaChecksum(papgt_body[entries+stringtable]),
        //                         written at meta/0.papgt[4..7]
        //   * papgt PamtCrc[i]  - copy of the corresponding archive's PAMT HeaderCrc
        // ============================================================
        public const uint PA_MAGIC = 558228019u;

        public static uint PaChecksum(byte[] data, int offset, int length)
        {
            if (length == 0) return 0u;
            uint a, b, c;
            a = b = c = (uint)(length - PA_MAGIC);
            int p = offset;
            int rem = length;
            while (rem > 12)
            {
                b += (uint)data[p] | ((uint)data[p + 1] << 8) | ((uint)data[p + 2] << 16) | ((uint)data[p + 3] << 24);
                a += (uint)data[p + 4] | ((uint)data[p + 5] << 8) | ((uint)data[p + 6] << 16) | ((uint)data[p + 7] << 24);
                c += (uint)data[p + 8] | ((uint)data[p + 9] << 8) | ((uint)data[p + 10] << 16) | ((uint)data[p + 11] << 24);
                b -= c; b ^= Rotl(c, 4); c += a;
                a -= b; a ^= Rotl(b, 6); b += c;
                c -= a; c ^= Rotl(a, 8); a += b;
                b -= c; b ^= Rotl(c, 16); c += a;
                a -= b; a ^= Rotl(b, 19); b += c;
                c -= a; c ^= Rotl(a, 4); a += b;
                p += 12; rem -= 12;
            }
            if (rem >= 12) c += (uint)data[p + 11] << 24;
            if (rem >= 11) c += (uint)data[p + 10] << 16;
            if (rem >= 10) c += (uint)data[p + 9] << 8;
            if (rem >= 9) c += (uint)data[p + 8];
            if (rem >= 8) a += (uint)data[p + 7] << 24;
            if (rem >= 7) a += (uint)data[p + 6] << 16;
            if (rem >= 6) a += (uint)data[p + 5] << 8;
            if (rem >= 5) a += (uint)data[p + 4];
            if (rem >= 4) b += (uint)data[p + 3] << 24;
            if (rem >= 3) b += (uint)data[p + 2] << 16;
            if (rem >= 2) b += (uint)data[p + 1] << 8;
            if (rem >= 1) b += (uint)data[p];
            // Custom finalisation - note the Rotr at steps 3 and 8.
            uint t1 = (a ^ c) - Rotl(a, 14);
            uint t2 = (b ^ t1) - Rotl(t1, 11);
            uint t3 = (t2 ^ a) - Rotr(t2, 7);
            uint t4 = (t3 ^ t1) - Rotl(t3, 16);
            uint t5 = Rotl(t4, 4);
            uint t6 = (t2 ^ t4) - t5;
            uint t7 = (t6 ^ t3) - Rotl(t6, 14);
            return (t7 ^ t4) - Rotr(t7, 8);
        }

        private static uint Rotr(uint x, int k) { return (x >> k) | (x << (32 - k)); }

        // Full-file PaChecksum - loads the whole file (up to ~2 GB) and hashes it in one pass.
        public static uint PaChecksumFile(string path)
        {
            var data = File.ReadAllBytes(path);
            return PaChecksum(data, 0, data.Length);
        }

        private static uint[] BuildCrcTable(uint poly)
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int j = 0; j < 8; j++)
                {
                    c = ((c & 1) != 0) ? (poly ^ (c >> 1)) : (c >> 1);
                }
                t[i] = c;
            }
            return t;
        }

        public sealed class HashAccumulator
        {
            public uint Crc32 = 0xFFFFFFFFu;
            public uint Crc32C = 0xFFFFFFFFu;
            public uint AdlerA = 1;
            public uint AdlerB = 0;
            public uint Fnv1a = 0x811C9DC5u;
            public ulong Sum32 = 0;
            public uint Xor32 = 0;
            public long ByteCount = 0;
            // For xxHash32 we accumulate via a small streaming impl below.
            public XxHash32State Xx = new XxHash32State();

            public void Update(byte[] buf, int offset, int count)
            {
                if (_crc32IeeeTable == null) _crc32IeeeTable = BuildCrcTable(0xEDB88320u);
                if (_crc32CTable == null) _crc32CTable = BuildCrcTable(0x82F63B78u);
                var crcT = _crc32IeeeTable;
                var crcCT = _crc32CTable;
                uint c = Crc32;
                uint cc = Crc32C;
                uint a = AdlerA;
                uint b = AdlerB;
                uint f = Fnv1a;
                ulong s = Sum32;
                uint x = Xor32;
                for (int i = 0; i < count; i++)
                {
                    byte v = buf[offset + i];
                    c = crcT[(c ^ v) & 0xFFu] ^ (c >> 8);
                    cc = crcCT[(cc ^ v) & 0xFFu] ^ (cc >> 8);
                    a = (a + v) % 65521u;
                    b = (b + a) % 65521u;
                    f = (f ^ v) * 16777619u;
                    s += v;
                    if (((ByteCount + i) & 3) == 0) x ^= (uint)(v << 0);
                    else if (((ByteCount + i) & 3) == 1) x ^= (uint)(v << 8);
                    else if (((ByteCount + i) & 3) == 2) x ^= (uint)(v << 16);
                    else x ^= (uint)(v << 24);
                }
                Crc32 = c;
                Crc32C = cc;
                AdlerA = a;
                AdlerB = b;
                Fnv1a = f;
                Sum32 = s;
                Xor32 = x;
                ByteCount += count;
                Xx.Update(buf, offset, count);
            }

            public uint FinalCrc32 { get { return Crc32 ^ 0xFFFFFFFFu; } }
            public uint FinalCrc32C { get { return Crc32C ^ 0xFFFFFFFFu; } }
            public uint FinalAdler32 { get { return (AdlerB << 16) | AdlerA; } }
            public uint FinalSum32 { get { return (uint)(Sum32 & 0xFFFFFFFFu); } }
            public uint FinalXxHash32 { get { return Xx.Finalize(); } }
        }

        public sealed class XxHash32State
        {
            const uint P1 = 2654435761u;
            const uint P2 = 2246822519u;
            const uint P3 = 3266489917u;
            const uint P4 = 668265263u;
            const uint P5 = 374761393u;
            uint v1, v2, v3, v4;
            byte[] buf = new byte[16];
            int bufLen = 0;
            ulong total = 0;
            uint seed = 0;
            bool large = false;
            public XxHash32State() { Reset(0); }
            public void Reset(uint s)
            {
                seed = s;
                v1 = s + P1 + P2;
                v2 = s + P2;
                v3 = s + 0;
                v4 = s - P1;
                bufLen = 0;
                total = 0;
                large = false;
            }
            static uint Rotl(uint x, int r) { return (x << r) | (x >> (32 - r)); }
            static uint Round(uint acc, uint input) { acc += input * P2; acc = Rotl(acc, 13); acc *= P1; return acc; }
            public void Update(byte[] data, int offset, int count)
            {
                total += (ulong)count;
                if (bufLen != 0)
                {
                    int need = 16 - bufLen;
                    if (count < need)
                    {
                        Buffer.BlockCopy(data, offset, buf, bufLen, count);
                        bufLen += count;
                        return;
                    }
                    Buffer.BlockCopy(data, offset, buf, bufLen, need);
                    int p = 0;
                    v1 = Round(v1, BitConverter.ToUInt32(buf, p)); p += 4;
                    v2 = Round(v2, BitConverter.ToUInt32(buf, p)); p += 4;
                    v3 = Round(v3, BitConverter.ToUInt32(buf, p)); p += 4;
                    v4 = Round(v4, BitConverter.ToUInt32(buf, p));
                    offset += need; count -= need; bufLen = 0; large = true;
                }
                while (count >= 16)
                {
                    v1 = Round(v1, BitConverter.ToUInt32(data, offset)); offset += 4;
                    v2 = Round(v2, BitConverter.ToUInt32(data, offset)); offset += 4;
                    v3 = Round(v3, BitConverter.ToUInt32(data, offset)); offset += 4;
                    v4 = Round(v4, BitConverter.ToUInt32(data, offset)); offset += 4;
                    count -= 16; large = true;
                }
                if (count > 0)
                {
                    Buffer.BlockCopy(data, offset, buf, 0, count);
                    bufLen = count;
                }
            }
            public uint Finalize()
            {
                uint h;
                if (large) h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
                else h = seed + P5;
                h += (uint)total;
                int p = 0;
                while (bufLen - p >= 4)
                {
                    h += BitConverter.ToUInt32(buf, p) * P3;
                    h = Rotl(h, 17) * P4;
                    p += 4;
                }
                while (p < bufLen)
                {
                    h += (uint)buf[p] * P5;
                    h = Rotl(h, 11) * P1;
                    p++;
                }
                h ^= h >> 15; h *= P2;
                h ^= h >> 13; h *= P3;
                h ^= h >> 16;
                return h;
            }
        }

        public static HashAccumulator HashFile(string path, long maxBytes, Action<long, long> progress)
        {
            var acc = new HashAccumulator();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
            {
                long total = fs.Length;
                long limit = (maxBytes > 0 && maxBytes < total) ? maxBytes : total;
                var buf = new byte[1 << 20]; // 1 MB chunks
                long done = 0;
                while (done < limit)
                {
                    int want = (int)Math.Min(buf.Length, limit - done);
                    int got = fs.Read(buf, 0, want);
                    if (got <= 0) break;
                    acc.Update(buf, 0, got);
                    done += got;
                    if (progress != null) progress(done, limit);
                }
            }
            return acc;
        }

        public static byte[] ReadFirstBytes(string path, int n)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long len = fs.Length;
                int take = (int)Math.Min((long)n, len);
                var buf = new byte[take];
                int got = 0;
                while (got < take)
                {
                    int r = fs.Read(buf, got, take - got);
                    if (r <= 0) break;
                    got += r;
                }
                if (got < take) Array.Resize(ref buf, got);
                return buf;
            }
        }

        public static byte[] ReadLastBytes(string path, int n)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long len = fs.Length;
                int take = (int)Math.Min((long)n, len);
                fs.Seek(len - take, SeekOrigin.Begin);
                var buf = new byte[take];
                int got = 0;
                while (got < take)
                {
                    int r = fs.Read(buf, got, take - got);
                    if (r <= 0) break;
                    got += r;
                }
                if (got < take) Array.Resize(ref buf, got);
                return buf;
            }
        }

        public static HashAccumulator HashBytes(byte[] data)
        {
            var acc = new HashAccumulator();
            acc.Update(data, 0, data.Length);
            return acc;
        }

        public static string Extract(string gamePath, string gameFile, string cacheDir, Action<string> log)
        {
            Directory.CreateDirectory(cacheDir);
            var existing = Locate(cacheDir, gameFile);
            if (existing != null) return existing;

            var pamt = Path.Combine(gamePath, "0008", "0.pamt");
            var pazDir = Path.Combine(gamePath, "0008");
            var basename = Path.GetFileName(gameFile).ToLowerInvariant();
            List<PazEntry> entries;
            try
            {
                entries = ParsePamt(pamt, pazDir);
            }
            catch (Exception ex)
            {
                log("Could not parse archive index: " + ex.Message);
                return null;
            }

            var matches = entries.Where(entry => entry.Path.ToLowerInvariant().Contains(basename)).ToList();
            if (matches.Count == 0)
            {
                log("No archive entry matched " + gameFile + ".");
                return null;
            }

            foreach (var entry in matches)
            {
                try
                {
                    var readSize = entry.Compressed ? entry.CompSize : entry.OrigSize;
                    byte[] blob;
                    using (var stream = File.OpenRead(entry.PazFile))
                    {
                        stream.Seek(entry.Offset, SeekOrigin.Begin);
                        blob = new byte[readSize];
                        stream.Read(blob, 0, blob.Length);
                    }

                    if (entry.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        log("Skipping encrypted XML extraction for " + entry.Path + ".");
                        continue;
                    }

                    if (entry.Compressed)
                    {
                        if (entry.CompressionType != 2)
                        {
                            log("Skipping unsupported compression type for " + entry.Path + ": " + entry.CompressionType);
                            continue;
                        }
                        blob = Lz4BlockDecompress(blob, entry.OrigSize);
                    }

                    var target = Path.Combine(cacheDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.WriteAllBytes(target, blob);
                    log("Extracted: " + target);
                }
                catch (Exception ex)
                {
                    log("Could not extract " + entry.Path + ": " + ex.Message);
                }
            }

            return Locate(cacheDir, gameFile);
        }

        private static string Locate(string cacheDir, string gameFile)
        {
            var basename = Path.GetFileName(gameFile);
            return Directory.Exists(cacheDir)
                ? Directory.GetFiles(cacheDir, basename, SearchOption.AllDirectories).OrderBy(path => path.Length).FirstOrDefault()
                : null;
        }

        private static List<PazEntry> ParsePamt(string pamtPath, string pazDir)
        {
            var data = File.ReadAllBytes(pamtPath);
            var pamtStem = Path.GetFileNameWithoutExtension(pamtPath);
            var off = 4;
            var pazCount = ReadU32(data, ref off);
            off += 8;
            for (var i = 0; i < pazCount; i++)
            {
                off += 8;
                if (i < pazCount - 1) off += 4;
            }

            var folderSize = ReadU32(data, ref off);
            var folderEnd = off + (int)folderSize;
            var folderPrefix = "";
            while (off < folderEnd)
            {
                var parent = ReadU32(data, ref off);
                var slen = data[off++];
                var name = Encoding.UTF8.GetString(data, off, slen);
                off += slen;
                if (parent == 0xFFFFFFFF) folderPrefix = name;
            }

            var nodeSize = ReadU32(data, ref off);
            var nodeStart = off;
            var nodes = new Dictionary<uint, Tuple<uint, string>>();
            while (off < nodeStart + nodeSize)
            {
                var rel = (uint)(off - nodeStart);
                var parent = ReadU32(data, ref off);
                var slen = data[off++];
                var name = Encoding.UTF8.GetString(data, off, slen);
                off += slen;
                nodes[rel] = Tuple.Create(parent, name);
            }

            Func<uint, string> buildPath = nodeRef =>
            {
                var parts = new List<string>();
                var cur = nodeRef;
                var guard = 0;
                while (cur != 0xFFFFFFFF && guard++ < 64 && nodes.ContainsKey(cur))
                {
                    var node = nodes[cur];
                    parts.Add(node.Item2);
                    cur = node.Item1;
                }
                parts.Reverse();
                return string.Concat(parts);
            };

            var folderCount = ReadU32(data, ref off);
            off += 4 + (int)folderCount * 16;
            var entries = new List<PazEntry>();
            while (off + 20 <= data.Length)
            {
                var nodeRef = ReadU32(data, ref off);
                var pazOffset = ReadU32(data, ref off);
                var compSize = ReadU32(data, ref off);
                var origSize = ReadU32(data, ref off);
                var flags = ReadU32(data, ref off);
                var pazIndex = (int)(flags & 0xFF);
                var nodePath = buildPath(nodeRef);
                var fullPath = string.IsNullOrEmpty(folderPrefix) ? nodePath : folderPrefix + "/" + nodePath;
                var pazNum = int.Parse(pamtStem) + pazIndex;
                entries.Add(new PazEntry
                {
                    Path = fullPath,
                    PazFile = Path.Combine(pazDir, pazNum + ".paz"),
                    Offset = pazOffset,
                    CompSize = compSize,
                    OrigSize = origSize,
                    Flags = flags
                });
            }
            return entries;
        }

        private static uint ReadU32(byte[] data, ref int off)
        {
            var value = BitConverter.ToUInt32(data, off);
            off += 4;
            return value;
        }

        private static byte[] Lz4BlockDecompress(byte[] data, uint originalSize)
        {
            var output = new List<byte>((int)originalSize);
            var i = 0;
            while (i < data.Length)
            {
                var token = data[i++];
                var literalLen = token >> 4;
                if (literalLen == 15)
                {
                    byte extra;
                    do
                    {
                        extra = data[i++];
                        literalLen += extra;
                    } while (extra == 255);
                }

                for (var j = 0; j < literalLen; j++) output.Add(data[i++]);
                if (i >= data.Length) break;

                var offset = data[i] | (data[i + 1] << 8);
                i += 2;
                if (offset <= 0 || offset > output.Count) throw new InvalidDataException("Invalid LZ4 match offset.");

                var matchLen = token & 0x0F;
                if (matchLen == 15)
                {
                    byte extra;
                    do
                    {
                        extra = data[i++];
                        matchLen += extra;
                    } while (extra == 255);
                }
                matchLen += 4;

                var start = output.Count - offset;
                for (var j = 0; j < matchLen; j++)
                {
                    output.Add(output[start++]);
                }
            }

            if (output.Count != originalSize)
            {
                throw new InvalidDataException("Decompressed size mismatch: got " + output.Count + ", expected " + originalSize + ".");
            }
            return output.ToArray();
        }
    }

    internal sealed class PazEntry
    {
        public string Path;
        public string PazFile;
        public uint Offset;
        public uint CompSize;
        public uint OrigSize;
        public uint Flags;

        public bool Compressed { get { return CompSize != OrigSize; } }
        public int CompressionType { get { return (int)((Flags >> 16) & 0x0F); } }
    }

    internal sealed class NexusLink
    {
        public int ModId;
        public int FileId;
        public string InstalledVersion;
        public string LatestVersion;
        public string LastCheckUtc;
        public bool UpdateAvailable;
        public long LatestFileTimestamp;
        public string ModName;

        public static string SidecarPathFor(string modPath)
        {
            return modPath + ".nxm.json";
        }

        public static NexusLink Load(string modPath)
        {
            var sidecar = SidecarPathFor(modPath);
            if (!File.Exists(sidecar)) return null;
            try
            {
                var json = File.ReadAllText(sidecar, Encoding.UTF8);
                var d = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                if (d == null) return null;
                var link = new NexusLink();
                if (d.ContainsKey("nexus_mod_id")) try { link.ModId = Convert.ToInt32(d["nexus_mod_id"]); } catch { }
                if (d.ContainsKey("nexus_file_id")) try { link.FileId = Convert.ToInt32(d["nexus_file_id"]); } catch { }
                link.InstalledVersion = d.ContainsKey("installed_version") ? Convert.ToString(d["installed_version"]) : "";
                link.LatestVersion = d.ContainsKey("latest_version") ? Convert.ToString(d["latest_version"]) : "";
                link.LastCheckUtc = d.ContainsKey("last_check_utc") ? Convert.ToString(d["last_check_utc"]) : "";
                link.UpdateAvailable = d.ContainsKey("update_available") && d["update_available"] is bool && (bool)d["update_available"];
                if (d.ContainsKey("latest_file_timestamp")) try { link.LatestFileTimestamp = Convert.ToInt64(d["latest_file_timestamp"]); } catch { }
                link.ModName = d.ContainsKey("mod_name") ? Convert.ToString(d["mod_name"]) : "";
                return link;
            }
            catch { return null; }
        }

        public void Save(string modPath)
        {
            var d = new Dictionary<string, object>
            {
                ["nexus_mod_id"] = ModId,
                ["nexus_file_id"] = FileId,
                ["installed_version"] = InstalledVersion ?? "",
                ["latest_version"] = LatestVersion ?? "",
                ["last_check_utc"] = LastCheckUtc ?? "",
                ["update_available"] = UpdateAvailable,
                ["latest_file_timestamp"] = LatestFileTimestamp,
                ["mod_name"] = ModName ?? ""
            };
            var json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d);
            File.WriteAllText(SidecarPathFor(modPath), json, Encoding.UTF8);
        }

        public static void Delete(string modPath)
        {
            var p = SidecarPathFor(modPath);
            if (File.Exists(p)) try { File.Delete(p); } catch { }
        }

        public static int? ParseModIdFromUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var trimmed = s.Trim();
            int direct;
            if (int.TryParse(trimmed, out direct) && direct > 0) return direct;
            try
            {
                var u = new Uri(trimmed);
                var segs = u.AbsolutePath.Trim('/').Split('/');
                for (int i = 0; i < segs.Length - 1; i++)
                {
                    if (string.Equals(segs[i], "mods", StringComparison.OrdinalIgnoreCase))
                    {
                        int n;
                        if (int.TryParse(segs[i + 1], out n)) return n;
                    }
                }
            }
            catch { }
            return null;
        }
    }

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
            if (string.IsNullOrEmpty(remoteTag) || string.IsNullOrEmpty(localVersion)) return false;
            var r = NormalizeVersion(remoteTag);
            var l = NormalizeVersion(localVersion);
            if (r == null || l == null) return !string.Equals(remoteTag.TrimStart('v', 'V'), localVersion.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Max(r.Length, l.Length); i++)
            {
                int rv = i < r.Length ? r[i] : 0;
                int lv = i < l.Length ? l[i] : 0;
                if (rv > lv) return true;
                if (rv < lv) return false;
            }
            return false;
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
                    MessageBox.Show("Could not parse a Nexus mod ID from that input.\r\nExample: https://www.nexusmods.com/crimsondesert/mods/1072", "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    internal sealed class NxmUrl
    {
        public string Game;
        public int ModId;
        public int FileId;
        public string Key;
        public string Expires;
        public string UserId;

        public static NxmUrl Parse(string url)
        {
            try
            {
                var u = new Uri(url);
                if (!string.Equals(u.Scheme, Program.NxmScheme, StringComparison.OrdinalIgnoreCase)) return null;
                var nxm = new NxmUrl();
                nxm.Game = u.Host;
                var segs = u.AbsolutePath.Trim('/').Split('/');
                for (int i = 0; i < segs.Length - 1; i++)
                {
                    if (string.Equals(segs[i], "mods", StringComparison.OrdinalIgnoreCase)) int.TryParse(segs[i + 1], out nxm.ModId);
                    if (string.Equals(segs[i], "files", StringComparison.OrdinalIgnoreCase)) int.TryParse(segs[i + 1], out nxm.FileId);
                }
                var qs = u.Query.TrimStart('?').Split('&');
                foreach (var q in qs)
                {
                    var idx = q.IndexOf('=');
                    if (idx < 0) continue;
                    var k = q.Substring(0, idx);
                    var v = Uri.UnescapeDataString(q.Substring(idx + 1));
                    if (string.Equals(k, "key", StringComparison.OrdinalIgnoreCase)) nxm.Key = v;
                    else if (string.Equals(k, "expires", StringComparison.OrdinalIgnoreCase)) nxm.Expires = v;
                    else if (string.Equals(k, "user_id", StringComparison.OrdinalIgnoreCase)) nxm.UserId = v;
                }
                if (nxm.ModId == 0 || nxm.FileId == 0 || string.IsNullOrEmpty(nxm.Game)) return null;
                return nxm;
            }
            catch { return null; }
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

    internal sealed class Theme
    {
        public string Name;
        public Color Background;
        public Color Panel;
        public Color Panel2;
        public Color Text;
        public Color Muted;
        public Color Accent;
        public Color Accent2;

        public static Theme Gilded() { return Make("Gilded", "#0c0d0a", "#171812", "#202115", "#f4ead1", "#a99d7c", "#d8a640", "#f4c767"); }
        public static Theme Ember() { return Make("Ember", "#120c09", "#1d1510", "#271b13", "#ffe6cd", "#b58d75", "#e28b3f", "#ffc06b"); }
        public static Theme Frost() { return Make("Frost", "#081018", "#101821", "#152334", "#e9f7ff", "#91a9ba", "#8cc7dd", "#bfefff"); }
        public static Theme Forest() { return Make("Forest", "#091109", "#101910", "#182718", "#eef5d8", "#98ad83", "#c0b15e", "#ece18c"); }

        // Custom theme: every palette slot is derived from the chosen accent so the whole UI shifts,
        // not just borders. Mirrors the built-ins (Frost/Forest also tint their backgrounds).
        public static Theme Custom(Color accent)
        {
            // Linear blend toward black for backgrounds, toward white for text.
            // Gives the panel a subtle hue-tinted dark instead of pure black.
            Color mix(Color a, Color b, float t) => Color.FromArgb(
                Math.Max(0, Math.Min(255, (int)(a.R * (1 - t) + b.R * t))),
                Math.Max(0, Math.Min(255, (int)(a.G * (1 - t) + b.G * t))),
                Math.Max(0, Math.Min(255, (int)(a.B * (1 - t) + b.B * t))));

            var black = Color.FromArgb(0, 0, 0);
            var white = Color.FromArgb(255, 255, 255);
            var midGray = Color.FromArgb(140, 140, 140);

            var bg = mix(accent, black, 0.94f);     // ~6% accent tint, near-black
            var panel = mix(accent, black, 0.91f);  // ~9%
            var panel2 = mix(accent, black, 0.86f); // ~14%
            var text = mix(accent, white, 0.85f);   // bright, slight accent tint
            var muted = mix(accent, midGray, 0.55f);// readable secondary text

            // Cap background luminance so a chosen pastel/yellow doesn't break readability.
            int lum(Color c) => c.R + c.G + c.B;
            if (lum(bg) > 60) bg = mix(bg, black, 0.6f);
            if (lum(panel) > 90) panel = mix(panel, black, 0.5f);
            if (lum(panel2) > 130) panel2 = mix(panel2, black, 0.4f);

            // Accent2 is the brighter highlight used for active tab text, top-bar pills, etc.
            var accent2 = mix(accent, white, 0.30f);

            return new Theme
            {
                Name = "Custom",
                Background = bg,
                Panel = panel,
                Panel2 = panel2,
                Text = text,
                Muted = muted,
                Accent = accent,
                Accent2 = accent2
            };
        }

        private static Theme Make(string name, string bg, string panel, string panel2, string text, string muted, string accent, string accent2)
        {
            return new Theme
            {
                Name = name,
                Background = ColorTranslator.FromHtml(bg),
                Panel = ColorTranslator.FromHtml(panel),
                Panel2 = ColorTranslator.FromHtml(panel2),
                Text = ColorTranslator.FromHtml(text),
                Muted = ColorTranslator.FromHtml(muted),
                Accent = ColorTranslator.FromHtml(accent),
                Accent2 = ColorTranslator.FromHtml(accent2)
            };
        }
    }
}



