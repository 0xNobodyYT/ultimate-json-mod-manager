using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Ultimate JSON Mod Manager")]
[assembly: AssemblyDescription("Ultimate JSON Mod Manager for Crimson Desert")]
[assembly: AssemblyCompany("0xNobody")]
[assembly: AssemblyProduct("Ultimate JSON Mod Manager")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: AssemblyVersion("1.2.0.0")]

namespace CdJsonModManager
{
    internal static class Program
    {
        public const string AppDisplayName = "Ultimate JSON Mod Manager";
        public const string AppShortName = "UJMM";
        public const string DonateUrl = "https://buymeacoffee.com/0xNobody";
        public const string BugReportRepo = "0xNobodyYT/ultimate-json-mod-manager";
        public const string UpdateRepo = "0xNobodyYT/ultimate-json-mod-manager";
        public const string AppVersion = "1.2.0";
        public const string NexusGameDomain = "crimsondesert";
        public const string NexusSsoApplication = "ujmm"; // app slug used for SSO handshake
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
        private readonly Dictionary<string, CheckBox> activeBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> groupBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RoundedPanel> presetCards = new Dictionary<string, RoundedPanel>(StringComparer.OrdinalIgnoreCase);
        private string activePreset = "";
        private string focusedModPath = "";
        private readonly Dictionary<string, RoundedPanel> modCards = new Dictionary<string, RoundedPanel>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NexusLink> nexusLinks = new Dictionary<string, NexusLink>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Pill> modCardPills = new Dictionary<string, Pill>(StringComparer.OrdinalIgnoreCase);
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
        private TabControl tabs;
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
            gamePathText.Text = gamePath ?? "";
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
                ["selectedGroups"] = new Dictionary<string, object>()
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
            Padding = new Padding(18, 16, 18, 14);

            bottomBar = BuildBottomBar();
            bottomBar.Dock = DockStyle.Bottom;
            bottomBar.Height = 78;
            bottomBar.Margin = new Padding(0, 12, 0, 0);
            Controls.Add(bottomBar);

            var bottomGap = new Panel { Dock = DockStyle.Bottom, Height = 12, BackColor = Color.Transparent };
            Controls.Add(bottomGap);

            topBar = BuildTopBar();
            topBar.Dock = DockStyle.Top;
            topBar.Height = 96;
            Controls.Add(topBar);

            var topGap = new Panel { Dock = DockStyle.Top, Height = 14, BackColor = Color.Transparent };
            Controls.Add(topGap);

            var mainGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(mainGrid);
            mainGrid.BringToFront();

            installPanel = BuildInstallPanel();
            installPanel.Margin = new Padding(0, 0, 8, 0);
            installPanel.Dock = DockStyle.Fill;
            mainGrid.Controls.Add(installPanel, 0, 0);

            workspacePanel = BuildWorkspacePanel();
            workspacePanel.Margin = new Padding(8, 0, 8, 0);
            workspacePanel.Dock = DockStyle.Fill;
            mainGrid.Controls.Add(workspacePanel, 1, 0);

            inspectorPanel = BuildInspectorPanel();
            inspectorPanel.Margin = new Padding(8, 0, 0, 0);
            inspectorPanel.Dock = DockStyle.Fill;
            mainGrid.Controls.Add(inspectorPanel, 2, 0);
        }

        private RoundedPanel BuildTopBar()
        {
            var bar = new RoundedPanel { CornerRadius = 22, BorderWidth = 1, Padding = new Padding(20, 14, 20, 14) };

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

            var brandRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var mark = new BrandMark { Width = 64, Height = 64, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 12, 4) };
            brandRow.Controls.Add(mark, 0, 0);

            var titleStack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            titleStack.Controls.Add(new Label
            {
                Text = Program.AppDisplayName,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Font = new Font("Trebuchet MS", 17.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 234, 209),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, 0, 0)
            }, 0, 0);
            titleStack.Controls.Add(new Label
            {
                Text = "Crimson Desert · overlay-safe patching, byte-guard validation, Nexus integration",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
            titleStack.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            titleStack.Height = 56;
            titleStack.Margin = new Padding(0, 4, 0, 4);
            brandRow.RowStyles.Clear();
            brandRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            brandRow.Controls.Add(titleStack, 1, 0);
            grid.Controls.Add(brandRow, 0, 0);

            var pillRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right,
                WrapContents = false
            };
            statusGamePill = new Pill { Text = "Game: checking", DotColor = Color.FromArgb(216, 166, 64) };
            statusModsPill = new Pill { Text = "0 mods active", DotColor = Color.FromArgb(216, 166, 64) };
            statusBuildPill = new Pill { Text = "v" + Program.AppVersion, Cursor = Cursors.Hand };
            tipsHost.SetToolTip(statusBuildPill, "Application version. Click to check for updates.");
            statusBuildPill.Click += (s, e) => CheckForUpdatesInteractive();
            pillRow.Controls.Add(statusGamePill);
            pillRow.Controls.Add(statusModsPill);
            pillRow.Controls.Add(statusBuildPill);

            updatePill = new Pill { Text = "Up to date", DotColor = Color.FromArgb(101, 197, 134), Visible = false, Cursor = Cursors.Hand };
            tipsHost.SetToolTip(updatePill, "Update available — click to view release notes and install.");
            updatePill.Click += (s, e) => CheckForUpdatesInteractive();
            pillRow.Controls.Add(updatePill);

            var donateBtn = new GradientButton
            {
                Text = "♥ Buy me a coffee",
                Kind = GradientButton.Style.Donate,
                Width = 178,
                Height = 30,
                Margin = new Padding(8, 0, 0, 0)
            };
            donateBtn.Click += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo(Program.DonateUrl) { UseShellExecute = true }); }
                catch (Exception ex) { Log("Could not open donate link: " + ex.Message); }
            };
            pillRow.Controls.Add(donateBtn);

            var reportBtn = new GradientButton
            {
                Text = "Report a Bug",
                Kind = GradientButton.Style.Default,
                Width = 130,
                Height = 30,
                Margin = new Padding(6, 0, 0, 0)
            };
            reportBtn.Click += (s, e) => CrashReporter.OpenManualReport(this);
            pillRow.Controls.Add(reportBtn);

            grid.Controls.Add(pillRow, 1, 0);

            return bar;
        }

        private RoundedPanel BuildBottomBar()
        {
            var bar = new RoundedPanel { CornerRadius = 20, BorderWidth = 1, Padding = new Padding(18, 12, 18, 12) };

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
                Font = new Font("Consolas", 9.5f),
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
                WrapContents = false
            };

            var dryRun = NewGradientButton("Verify Bytes", GradientButton.Style.Default, 130, RunValidation);
            tipsHost.SetToolTip(dryRun, "Verify selected mods against the current game files without changing anything. Confirms each patch's 'original' bytes match what's actually in your installed game.");
            var apply = NewGradientButton("Apply Mods", GradientButton.Style.Primary, 150, ApplyOverlayStub);
            tipsHost.SetToolTip(apply, "Apply selected mods. Modded bytes are appended to the .paz archive (original data never overwritten) and the .pamt index is patched to point at them. Click 'Uninstall Mods' to fully revert.");
            var uninstall = NewGradientButton("Uninstall Mods", GradientButton.Style.Danger, 140, DisableAllMods);
            tipsHost.SetToolTip(uninstall, "Revert all mods: restores the .pamt from backup and truncates each .paz back to its pre-apply length. This is the only revert button you need.");
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
                RowCount = 2,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(layout);

            layout.Controls.Add(BuildPanelHeader("INSTALL", "Steam"), 0, 0);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 12, 14, 14),
                AutoScroll = true
            };
            layout.Controls.Add(body, 0, 1);

            var bodyStack = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            bodyStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) bodyStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(bodyStack);

            bodyStack.Controls.Add(BuildPathCard(), 0, 0);
            bodyStack.Controls.Add(BuildThemeSwatches(), 0, 1);
            bodyStack.Controls.Add(BuildDropZone(), 0, 2);

            modCardsHost = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 12, 0, 0)
            };
            bodyStack.Controls.Add(modCardsHost, 0, 3);

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
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
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
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 12)
            };

            themeSwatchHost.Controls.Add(MakeSwatch(Theme.Gilded()));
            themeSwatchHost.Controls.Add(MakeSwatch(Theme.Ember()));
            themeSwatchHost.Controls.Add(MakeSwatch(Theme.Frost()));
            themeSwatchHost.Controls.Add(MakeSwatch(Theme.Forest()));
            return themeSwatchHost;
        }

        private ThemeSwatch MakeSwatch(Theme theme)
        {
            var sw = new ThemeSwatch(theme)
            {
                Width = 66,
                Height = 48,
                Margin = new Padding(0, 0, 8, 0),
                Cursor = Cursors.Hand
            };
            var tooltip = new ToolTip();
            tooltip.SetToolTip(sw, theme.Name);
            sw.Click += (sender, args) => ApplyTheme(theme);
            return sw;
        }

        private RoundedPanel BuildDropZone()
        {
            var panel = new RoundedPanel
            {
                CornerRadius = 18,
                BorderWidth = 1,
                AutoSize = false,
                Height = 92,
                Margin = new Padding(0, 0, 0, 0),
                Padding = new Padding(16, 12, 16, 12),
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
                Text = "Drop JSON or ASI mods here",
                Dock = DockStyle.Fill,
                Font = new Font("Trebuchet MS", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 234, 209),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomCenter
            };
            stack.Controls.Add(dropTitle, 0, 0);

            dropHint = new Label
            {
                Text = "Accepts .json files/folders for mods, .asi/.dll/.ini for runtime hooks.",
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.TopCenter
            };
            stack.Controls.Add(dropHint, 0, 1);

            panel.DragEnter += DropZoneDragEnter;
            panel.DragOver += DropZoneDragEnter;
            panel.DragLeave += (sender, args) => { panel.PulseAccent = false; panel.Invalidate(); };
            panel.DragDrop += DropZoneDragDrop;

            // Click anywhere on the drop zone to browse
            panel.Click += (sender, args) => AddJsonMods();
            dropTitle.Click += (sender, args) => AddJsonMods();
            dropHint.Click += (sender, args) => AddJsonMods();
            stack.Click += (sender, args) => AddJsonMods();

            dropZone = panel;
            return panel;
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

            tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.FlatButtons,
                SizeMode = TabSizeMode.Normal,
                ItemSize = new Size(120, 30),
                DrawMode = TabDrawMode.OwnerDrawFixed,
                Padding = new Point(14, 6),
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Margin = new Padding(12, 8, 12, 12)
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
                Padding = new Padding(0, 0, 0, 0)
            };
            listHost.GradientTopOverride = Color.FromArgb(180, 0, 0, 0);
            listHost.GradientBottomOverride = Color.FromArgb(220, 0, 0, 0);
            listHost.BorderColor = Color.FromArgb(36, 255, 255, 255);

            patchList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Consolas", 9),
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(12, 13, 10),
                ForeColor = Color.FromArgb(244, 234, 209)
            };
            patchList.Columns.Add("Patch", 200);
            patchList.Columns.Add("Target", 200);
            patchList.SizeChanged += (s, e) => FitListColumns(patchList, new[] { 0.50f, 0.50f });
            listHost.Controls.Add(patchList);
            grid.Controls.Add(listHost, 1, 0);
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
                "Detects Ultimate ASI Loader / CDUMM in bin64. Install or remove the loader separately from mods.",
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); // status card
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // actions row
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

            nexusStatusPill = new Pill { Text = "Not connected", DotColor = Color.FromArgb(216, 92, 76), Anchor = AnchorStyles.Right, Margin = new Padding(0, 4, 0, 0) };
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

            // Actions row — SSO is the primary sign-in path. No user-facing key paste.
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 8)
            };

            nexusConnectButton = new GradientButton
            {
                Text = "Sign in with Nexus",
                Kind = GradientButton.Style.Primary,
                Width = 200,
                Height = 38,
                Margin = new Padding(0, 0, 8, 0)
            };
            tipsHost.SetToolTip(nexusConnectButton, "One-click sign-in via your browser. No password or key required — just approve the app on nexusmods.com.");
            nexusConnectButton.Click += (s, e) => OnNexusConnectClick();
            actions.Controls.Add(nexusConnectButton);

            var openBtn = NewGradientButton("Browse mods on Nexus", GradientButton.Style.Default, 200, () => SafeOpenUrl("https://www.nexusmods.com/" + Program.NexusGameDomain));
            tipsHost.SetToolTip(openBtn, "Open the Crimson Desert mods page on Nexus in your browser.");
            actions.Controls.Add(openBtn);

            var refreshBtn = NewGradientButton("Refresh Feed", GradientButton.Style.Default, 130, RefreshNexusFeed);
            tipsHost.SetToolTip(refreshBtn, "Reload the recently-updated mods list from Nexus.");
            actions.Controls.Add(refreshBtn);

            nexusRateLabel = new Label
            {
                Text = "",
                AutoSize = true,
                Font = new Font("Consolas", 8.5f),
                ForeColor = Color.FromArgb(112, 104, 79),
                BackColor = Color.Transparent,
                Padding = new Padding(8, 12, 0, 0)
            };
            actions.Controls.Add(nexusRateLabel);

            var devLink = new Label
            {
                Text = "developer: paste API key",
                AutoSize = true,
                Font = new Font("Consolas", 8.5f, FontStyle.Underline),
                ForeColor = Color.FromArgb(110, 100, 75),
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Padding(10, 16, 0, 0)
            };
            tipsHost.SetToolTip(devLink, "Developer/testing only — manually paste a Nexus API key. Regular users sign in with the button above.");
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
                    nexusUserLabel.Text = "Signed in as " + (string.IsNullOrEmpty(nexusUserName) ? "your Nexus account" : nexusUserName) + " · " + status + ".\r\nYou can now click 'Mod Manager Download' on any Crimson Desert mod page and the file lands here automatically.";
                }
                else
                {
                    nexusUserLabel.Text = "Sign in with your Nexus account to enable one-click downloads from any Crimson Desert mod page (the 'Mod Manager Download' button) and automatic update detection.\r\nThe sign-in flow opens your browser — no password or key required, just one click.";
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
                            "Once it's approved, this button will Just Work — no key pasting needed.\r\n\r\n" +
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
                Text = "v" + version + " · " + downloads.ToString("N0") + " ↓",
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
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string err;
                    var info = UpdateChecker.CheckLatest(out err);
                    if (info == null || string.IsNullOrEmpty(info.TagName)) return;
                    cachedRelease = info;
                    if (UpdateChecker.IsNewer(info.TagName, Program.AppVersion))
                    {
                        if (IsDisposed || Disposing) return;
                        BeginInvoke(new Action(() =>
                        {
                            if (updatePill != null)
                            {
                                updatePill.Text = "Update " + info.TagName + " available";
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
            if (info == null) info = UpdateChecker.CheckLatest(out err);
            if (info == null || string.IsNullOrEmpty(info.TagName))
            {
                MessageBox.Show("No release info available.\r\n\r\n" + (string.IsNullOrEmpty(err) ? "(repo has no published releases yet)" : err), "No update info", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

                    setStatus("Done — " + imported);
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
                if (ext == ".zip")
                {
                    using (var fs = File.OpenRead(path))
                    using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                    {
                        foreach (var entry in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            var dest = Path.Combine(stage, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                            var dir = Path.GetDirectoryName(dest);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            using (var src = entry.Open())
                            using (var dst = File.Create(dest))
                            {
                                src.CopyTo(dst);
                            }
                        }
                    }
                    return ImportFolder(stage, modsDirLocal, asiDirLocal, bin64Path);
                }
                return "skipped (need 7-zip/rar support to extract " + ext + ")";
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

            var pad = new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 2);
            using (var path = RoundedPanel.RoundedRect(pad, 10))
            {
                if (isActive)
                {
                    using (var brush = new LinearGradientBrush(pad, currentTheme.Accent2, currentTheme.Accent, 90f))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(180, currentTheme.Accent), 1f))
                        g.DrawPath(pen, path);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                        g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb(36, 255, 255, 255), 1f))
                        g.DrawPath(pen, path);
                }
            }
            var fg = isActive ? Color.FromArgb(18, 14, 7) : Color.FromArgb(169, 157, 124);
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

            layout.Controls.Add(BuildPanelHeader("INSPECTOR", "Verify ready", out inspectorCounter), 0, 0);

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

            checkGuards = new CheckGridRow("Original byte guards", "ready", BadgeKind.Neutral) { Margin = new Padding(0, 0, 0, 6), Dock = DockStyle.Fill };
            checkOverlay = new CheckGridRow("Overlay target", "0036", BadgeKind.Neutral) { Margin = new Padding(0, 0, 0, 6), Dock = DockStyle.Fill };
            checkConflicts = new CheckGridRow("Conflicts", "—", BadgeKind.Neutral) { Margin = new Padding(0, 0, 0, 6), Dock = DockStyle.Fill };
            checkBackup = new CheckGridRow("Backup", "missing", BadgeKind.Warn) { Margin = new Padding(0, 0, 0, 0), Dock = DockStyle.Fill };
            checkHost.Controls.Add(checkGuards, 0, 0);
            checkHost.Controls.Add(checkOverlay, 0, 1);
            checkHost.Controls.Add(checkConflicts, 0, 2);
            checkHost.Controls.Add(checkBackup, 0, 3);
            layout.Controls.Add(checkHost, 0, 1);

            var backupBtn = NewGradientButton("Create 0.papgt Backup", GradientButton.Style.Default, 0, BackupPapgt);
            backupBtn.Dock = DockStyle.Top;
            backupBtn.Margin = new Padding(14, 4, 14, 8);
            layout.Controls.Add(backupBtn, 0, 2);

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
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            counter = new Label
            {
                Text = counterText,
                AutoSize = true,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(112, 104, 79),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 14, 0, 0)
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

            if (themeSwatchHost != null)
            {
                foreach (Control c in themeSwatchHost.Controls)
                {
                    var sw = c as ThemeSwatch;
                    if (sw != null) sw.IsActive = string.Equals(sw.SwatchTheme.Name, theme.Name, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (statusGamePill != null) statusGamePill.WarnColor = theme.Accent;
            if (statusModsPill != null) statusModsPill.WarnColor = theme.Accent;

            if (gamePathText != null)
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
            // Preserve focus across reload only if the focused mod's file still exists
            if (!string.IsNullOrEmpty(focusedModPath) && !File.Exists(focusedModPath)) focusedModPath = "";
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
                    Height = 78,
                    Margin = new Padding(0, 0, 0, 10),
                    Padding = new Padding(14, 11, 14, 11),
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
                stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
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

                var checkVisible = new CheckBox
                {
                    Width = 20,
                    Height = 20,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 4, 6, 0),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                titleRow.Controls.Add(checkVisible, 0, 0);

                var nameLabel = new Label
                {
                    Text = mod.Name,
                    Dock = DockStyle.Fill,
                    Font = new Font("Trebuchet MS", 11f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand
                };
                titleRow.Controls.Add(nameLabel, 1, 0);

                var tag = new Pill
                {
                    Text = "JSON",
                    DotColor = Color.Empty,
                    PillFillColor = Color.FromArgb(216, 166, 64),
                    PillTextColor = Color.FromArgb(21, 15, 8),
                    BorderlessTag = true,
                    Font = new Font("Consolas", 7.5f, FontStyle.Bold),
                    Height = 18,
                    Anchor = AnchorStyles.Right,
                    Margin = new Padding(0, 4, 0, 0),
                    Cursor = Cursors.Hand
                };
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
                           (string.IsNullOrWhiteSpace(mod.Version + mod.Author) ? "JSON byte-patch mod" : (mod.Version + " by " + mod.Author).Trim()),
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 8.5f),
                    ForeColor = Color.FromArgb(169, 157, 124),
                    BackColor = Color.Transparent,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand
                };
                stack.Controls.Add(metaLabel, 0, 1);

                var check = new CheckBox
                {
                    Visible = false,
                    Checked = active.Contains(Path.GetFileName(mod.Path)) || active.Contains(mod.Name)
                };
                card.Controls.Add(check);
                checkVisible.Checked = check.Checked;
                bool syncing = false;
                checkVisible.CheckedChanged += (s, e) => { if (syncing) return; syncing = true; check.Checked = checkVisible.Checked; syncing = false; };
                check.CheckedChanged += (s, e) => { if (syncing) return; syncing = true; checkVisible.Checked = check.Checked; syncing = false; };
                tipsHost.SetToolTip(checkVisible, "Select / deselect this mod. Multiple mods can be selected — the patch list shows what would apply.");

                var menu = new ContextMenuStrip();
                menu.Items.Add("Uninstall / Disable", null, (s, e) => DisableMod(capturedMod));
                menu.Items.Add("Open Folder", null, (s, e) => OpenFolder(Path.GetDirectoryName(capturedMod.Path)));
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
                checkVisible.Click += (s, e) => FocusMod(capturedMod);
                check.CheckedChanged += (sender, args) =>
                {
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

        private void UpdateModPillForLink(JsonMod mod)
        {
            if (!modCardPills.ContainsKey(mod.Path)) return;
            var pill = modCardPills[mod.Path];
            if (!nexusLinks.ContainsKey(mod.Path))
            {
                pill.Text = "JSON";
                pill.PillFillColor = Color.FromArgb(216, 166, 64);
                pill.PillTextColor = Color.FromArgb(21, 15, 8);
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
            if (nexusUpdateTimer != null) return;
            nexusUpdateTimer = new System.Windows.Forms.Timer { Interval = 30 * 60 * 1000 };
            nexusUpdateTimer.Tick += (s, e) => { if (!string.IsNullOrEmpty(nexusApiKey)) CheckUpdatesNow(); };
            nexusUpdateTimer.Start();
            // Initial check 8 seconds after the form is up so it doesn't block startup
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

            // Header: which mod's presets we're showing
            var header = new Label
            {
                Text = focused.Name.ToUpperInvariant(),
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 26,
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(244, 199, 103),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(2, 0, 0, 4),
                Margin = new Padding(0, 0, 0, 4)
            };
            presetRailHost.Controls.Add(header);

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

            if (groups.Count == 0 || (groups.Count == 1 && string.Equals(groups[0], "All", StringComparison.OrdinalIgnoreCase)))
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
                    Text = g,
                    Dock = DockStyle.Fill,
                    Font = new Font("Trebuchet MS", 12.5f, FontStyle.Bold),
                    BackColor = Color.Transparent,
                    ForeColor = Color.FromArgb(244, 234, 209),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand
                };
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

            // Show all active (checked) mods' patches first — these will actually apply.
            // Then, if the focused mod isn't already active, show its patches as a preview
            // (dimmed + "preview" prefix) so the user can inspect what a mod does without
            // having to enable it first.
            var pending = new List<ListViewItem>(256);
            int activeCount = 0;
            var focused = FocusedMod();
            bool focusedAlreadyActive = false;
            foreach (var mod in mods)
            {
                if (!activeBoxes.ContainsKey(mod.Path) || !activeBoxes[mod.Path].Checked) continue;
                if (focused != null && string.Equals(mod.Path, focused.Path, StringComparison.OrdinalIgnoreCase)) focusedAlreadyActive = true;
                var group = GroupFor(mod);
                foreach (var change in mod.ChangesForGroup(group))
                {
                    var label = string.IsNullOrEmpty(change.CleanLabel) ? "(unnamed patch)" : change.CleanLabel;
                    var item = new ListViewItem(label);
                    var fileName = string.IsNullOrEmpty(change.GameFile) ? "" : Path.GetFileName(change.GameFile);
                    item.SubItems.Add(fileName + "  +0x" + change.Offset.ToString("X"));
                    pending.Add(item);
                    activeCount++;
                }
            }

            int previewCount = 0;
            if (focused != null && !focusedAlreadyActive)
            {
                var group = GroupFor(focused);
                foreach (var change in focused.ChangesForGroup(group))
                {
                    var label = string.IsNullOrEmpty(change.CleanLabel) ? "(unnamed patch)" : change.CleanLabel;
                    var item = new ListViewItem("preview · " + label);
                    var fileName = string.IsNullOrEmpty(change.GameFile) ? "" : Path.GetFileName(change.GameFile);
                    item.SubItems.Add(fileName + "  +0x" + change.Offset.ToString("X"));
                    item.ForeColor = Color.FromArgb(150, 138, 100);
                    pending.Add(item);
                    previewCount++;
                }
            }

            patchList.BeginUpdate();
            try
            {
                patchList.Items.Clear();
                if (pending.Count > 0) patchList.Items.AddRange(pending.ToArray());
            }
            finally
            {
                patchList.EndUpdate();
            }

            if (workspaceCounter != null)
            {
                int total = 0;
                foreach (var mod in mods) total += mod.Changes.Count;
                var lead = activeCount + " / " + total + " will apply";
                if (previewCount > 0) lead += " · " + previewCount + " preview";
                workspaceCounter.Text = lead;
            }
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
                if (File.Exists(mod.Path))
                {
                    File.Delete(mod.Path);
                }
                Log("Deleted JSON mod: " + mod.Name + ".");
                LoadMods();
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
                statusGamePill.Text = ok ? "Game detected" : "Game missing";
                statusGamePill.DotColor = ok ? Color.FromArgb(101, 197, 134) : currentTheme.Accent;
            }
            if (statusModsPill != null)
            {
                var n = activeBoxes.Values.Count(box => box.Checked);
                statusModsPill.Text = n + " mods active";
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
            var path = gamePathText.Text.Trim().Trim('"');
            if (!IsGameFolder(path))
            {
                MessageBox.Show("Choose the Crimson Desert folder that contains bin64 and 0008.", "Invalid folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            gamePath = path;
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
                Filter = "JSON / ASI / DLL / INI (*.json;*.asi;*.dll;*.ini)|*.json;*.asi;*.dll;*.ini|JSON mods (*.json)|*.json|ASI/DLL/INI (*.asi;*.dll;*.ini)|*.asi;*.dll;*.ini",
                Multiselect = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ImportPaths(dialog.FileNames);
            }
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
                return ext == ".json" || ext == ".asi" || ext == ".dll" || ext == ".ini";
            }
            if (Directory.Exists(path))
            {
                if (Directory.GetFiles(path, "*.json", SearchOption.AllDirectories).Any()) return true;
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

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    var ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                    if (ext == ".json") jsonCandidates.Add(path);
                    else if (ext == ".asi" || ext == ".dll" || ext == ".ini") asiCandidates.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    jsonCandidates.AddRange(Directory.GetFiles(path, "*.json", SearchOption.AllDirectories));
                    asiCandidates.AddRange(Directory.GetFiles(path, "*.asi", SearchOption.AllDirectories));
                    asiCandidates.AddRange(Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories));
                    asiCandidates.AddRange(Directory.GetFiles(path, "*.ini", SearchOption.AllDirectories));
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
            Log("Imported " + imported + " JSON, " + asiImported + " ASI/DLL/INI" + (skipped > 0 ? ", skipped " + skipped + "." : "."));
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

            var files = Directory.GetFiles(bin64)
                .Where(path => path.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".asi.disabled", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var lower = name.ToLowerInvariant();
                var status = lower.EndsWith(".asi.disabled") ? "disabled" :
                    lower.EndsWith(".asi") ? "enabled" :
                    loaders.Contains(name, StringComparer.OrdinalIgnoreCase) ? "loader" : "support/config";
                var item = new ListViewItem(name) { Tag = file };
                item.SubItems.Add(status);
                item.SubItems.Add(new FileInfo(file).Length.ToString());
                asiList.Items.Add(item);
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
                MessageBox.Show("Enable at least one JSON mod first.", "No mods selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Log("Starting byte-guard verification...");
            var checkedCount = 0;
            var alreadyPatched = 0;
            var issues = new List<string>();

            foreach (var mod in selected)
            {
                var group = GroupFor(mod);
                foreach (var change in mod.ChangesForGroup(group))
                {
                    var file = ArchiveExtractor.Extract(gamePath, change.GameFile, cacheDir, Log);
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
            if (checkOverlay != null) checkOverlay.SetState("0036", BadgeKind.Ok);
            if (checkConflicts != null) checkConflicts.SetState(alreadyPatched + " already-patched", alreadyPatched == 0 ? BadgeKind.Neutral : BadgeKind.Warn);

            if (issues.Count == 0)
            {
                Log("Verify passed.");
                MessageBox.Show("Selected patch guards match the extracted game data.", "Verify passed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                foreach (var issue in issues.Take(80)) Log(issue);
                if (issues.Count > 80) Log("... plus " + (issues.Count - 80) + " more issue(s).");
                MessageBox.Show("Some original bytes did not match. Check the inspector log.", "Verify mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        //   _jmm_backups\0008\0.pamt.original           — full PAMT bytes pre-apply
        //   _jmm_backups\0008\<N>.paz.length.original   — original byte length of the .paz
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

        private void ApplyOverlayStub()
        {
            ApplyByPazAppend();
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

            // Group changes by gameFile across all active mods (using each mod's selected preset).
            var byGameFile = new Dictionary<string, List<PatchChange>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in selected)
            {
                var group = GroupFor(mod);
                foreach (var c in mod.ChangesForGroup(group))
                {
                    if (string.IsNullOrEmpty(c.GameFile)) continue;
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
                "Apply " + byGameFile.Count + " modded game file(s) to your Crimson Desert install?\r\n\r\n" +
                "How it works:\r\n" +
                "  • Modded bytes are APPENDED to the existing archive(s) — original data is never overwritten.\r\n" +
                "  • The archive index (PAMT) is patched in place to point at the new bytes.\r\n" +
                "  • Pre-apply length of each archive + the original PAMT are saved to <game>\\_jmm_backups\\.\r\n" +
                "  • Click 'Uninstall Mods' to fully revert (truncates archives back, restores the PAMT).\r\n\r\n" +
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
                    if (e.Path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue; // encrypted — skip
                    workItems.Add(new ApplyWork { Entry = e, EntryIndex = i, Changes = pair.Value });
                }
            }
            if (workItems.Count == 0)
            {
                MessageBox.Show("No matching archive entries found for the selected mods.", "Apply aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Backups (idempotent — if already present, do not overwrite — they reflect pre-FIRST-apply state)
            var backupRoot = PazBackupsRoot();
            Directory.CreateDirectory(backupRoot);
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
                                    Log("Skipping " + w.Entry.Path + " — unsupported compression type " + w.Entry.CompressionType);
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
                                Log("Skipped " + w.Entry.Path + " — no patches succeeded.");
                                fileSkipped++;
                                totalMismatch += mismatch;
                                continue;
                            }

                            // Recompress with LZ4 all-literals
                            var newCompressed = ArchiveExtractor.Lz4BlockCompress(decompressed);

                            // Append to paz, capture new offset
                            paz.Seek(0, SeekOrigin.End);
                            long newOffset = paz.Position;
                            paz.Write(newCompressed, 0, newCompressed.Length);

                            w.NewPazOffset = (uint)newOffset;
                            w.NewCompSize = (uint)newCompressed.Length;
                            w.NewOrigSize = (uint)decompressed.Length;
                            // Preserve pazIndex (low 8 bits) and other flag bits, force compression type to 2 (LZ4) at bits 16..19
                            w.NewFlags = (w.Entry.Flags & 0xFFF0FFFFu) | 0x00020000u;
                            w.Succeeded = true;

                            totalApplied += applied;
                            totalMismatch += mismatch;
                            Log("Applied " + applied + " to " + w.Entry.Path + " (compSize " + w.Entry.CompSize + " -> " + w.NewCompSize + ")");
                        }
                        catch (Exception ex)
                        {
                            Log("Apply error for " + w.Entry.Path + ": " + ex.Message);
                            fileSkipped++;
                        }
                    }
                    paz.Flush();
                }
            }

            // Patch PAMT in memory — surgical 16-byte edit per entry (skip nodeRef, write pazOffset/compSize/origSize/flags)
            var pamtNew = (byte[])pamtData.PamtBytes.Clone();
            int pamtChanges = 0;
            foreach (var w in workItems)
            {
                if (!w.Succeeded) continue;
                int eOff = pamtData.EntrySectionStart + w.EntryIndex * 20;
                if (eOff + 20 > pamtNew.Length)
                {
                    Log("PAMT entry offset out of range for " + w.Entry.Path + " — skipped.");
                    continue;
                }
                ArchiveExtractor.WriteU32LE(pamtNew, eOff + 4, w.NewPazOffset);
                ArchiveExtractor.WriteU32LE(pamtNew, eOff + 8, w.NewCompSize);
                ArchiveExtractor.WriteU32LE(pamtNew, eOff + 12, w.NewOrigSize);
                ArchiveExtractor.WriteU32LE(pamtNew, eOff + 16, w.NewFlags);
                pamtChanges++;
            }
            try
            {
                File.WriteAllBytes(pamtPath, pamtNew);
                Log("Wrote modified PAMT (" + pamtChanges + " entries redirected).");
            }
            catch (Exception ex)
            {
                Log("PAMT write failed: " + ex.Message);
                MessageBox.Show("PAMT write failed: " + ex.Message + "\r\n\r\nThe paz files were appended to but the index wasn't updated. The game will still work; click 'Uninstall Mods' to truncate the appended bytes.", "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show(
                "Applied " + totalApplied + " patches across " + pamtChanges + " archive entries.\r\n" +
                (totalMismatch > 0 ? totalMismatch + " patch(es) skipped due to byte mismatch (see log).\r\n" : "") +
                (fileSkipped > 0 ? fileSkipped + " file(s) skipped entirely.\r\n" : "") +
                "\r\nLaunch Crimson Desert and test.\r\nClick 'Uninstall Mods' to fully revert.",
                "Mods applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RevertPazAppend()
        {
            if (!IsGameFolder(gamePath)) return;
            var backupRoot = PazBackupsRoot();
            if (!Directory.Exists(backupRoot)) return;

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
                        Log("WARNING: " + pazName + " is shorter (" + curLen + ") than recorded original (" + origLen + ") — not truncating. Restore from your Steam files if needed.");
                    }
                }
                catch (Exception ex) { Log("Truncate failed for " + lengthFile + ": " + ex.Message); }
            }

            // Clean up the backup markers so subsequent applies create fresh ones
            try
            {
                if (File.Exists(pamtBackup)) File.Delete(pamtBackup);
                foreach (var lf in Directory.GetFiles(backupRoot, "*.length.original")) File.Delete(lf);
            }
            catch { }

            if (restored > 0) Log("Apply reverted: " + restored + " archive(s) restored to pre-apply state.");
        }

        // Legacy loose-file probe — kept for any orphaned probe writes from earlier sessions.
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

            // Collect (game_file → list of changes) across all active mods using each mod's preset.
            var byGameFile = new Dictionary<string, List<PatchChange>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in selected)
            {
                var group = GroupFor(mod);
                foreach (var c in mod.ChangesForGroup(group))
                {
                    if (string.IsNullOrEmpty(c.GameFile)) continue;
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
                "• Original game archives are NEVER modified.\r\n" +
                "• Any pre-existing file at a target path is backed up first.\r\n" +
                "• Click 'Uninstall Mods' to fully revert.\r\n\r\n" +
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
                // Derive the archive-internal path (relative to cache root) — that's where we'll write loose
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
                    Log("Apply: skipping " + logicalFile + " entirely — every patch failed its byte guard.");
                    continue;
                }
                work.Add(Tuple.Create(archiveRelPath, bytes, mismatched, applied));
                Log("Apply: prepared " + logicalFile + " — " + applied + " patches applied" + (mismatched > 0 ? ", " + mismatched + " skipped" : ""));
            }

            if (work.Count == 0)
            {
                MessageBox.Show("Nothing to apply (no files prepared cleanly). Run Verify Bytes to diagnose.", "Apply aborted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                "If the changes take effect → loose-file overlay works.\r\n" +
                "If they don't → click 'Uninstall Mods' to revert and we'll try the next approach.",
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
            var candidates = new[]
            {
                @"E:\Program Files\Steam\steamapps\common\Crimson Desert",
                @"C:\Program Files (x86)\Steam\steamapps\common\Crimson Desert",
                @"C:\Program Files\Steam\steamapps\common\Crimson Desert"
            };
            return candidates.FirstOrDefault(IsGameFolder) ?? "";
        }

        private bool IsGameFolder(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && Directory.Exists(Path.Combine(path, "bin64"))
                && File.Exists(Path.Combine(path, "0008", "0.pamt"));
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

            var textX = 12;
            if (DotColor != Color.Empty)
            {
                using (var glow = new SolidBrush(Color.FromArgb(80, DotColor)))
                    g.FillEllipse(glow, 7, Height / 2 - 7, 14, 14);
                using (var dot = new SolidBrush(DotColor))
                    g.FillEllipse(dot, 9, Height / 2 - 5, 10, 10);
                textX = 26;
            }
            var fg = BorderlessTag ? PillTextColor : ForeColor;
            var size = TextRenderer.MeasureText(g, Text ?? "", Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            var y = (Height - size.Height) / 2;
            TextRenderer.DrawText(g, Text ?? "", Font, new Point(textX, y), fg, TextFormatFlags.NoPadding);
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
            var ts = TextRenderer.MeasureText(g, Text ?? "", Font);
            var x = (Width - ts.Width) / 2;
            var y = (Height - ts.Height) / 2;
            TextRenderer.DrawText(g, Text ?? "", Font, new Point(x, y), fg, TextFormatFlags.NoPadding);
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
        private readonly Label labelControl;
        private readonly BadgePill badgeControl;
        private readonly DotPanel dot;

        public CheckGridRow(string label, string badge, BadgeKind kind)
        {
            CornerRadius = 14;
            BorderWidth = 1;
            GradientTopOverride = Color.FromArgb(36, 0, 0, 0);
            GradientBottomOverride = Color.FromArgb(58, 0, 0, 0);
            BorderColor = Color.FromArgb(28, 255, 255, 255);
            Padding = new Padding(11, 6, 11, 6);
            Height = 38;

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(grid);

            dot = new DotPanel { Width = 10, Height = 10, BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) };
            grid.Controls.Add(dot, 0, 0);

            labelControl = new Label
            {
                Text = label,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(244, 234, 209),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };
            grid.Controls.Add(labelControl, 1, 0);

            badgeControl = new BadgePill
            {
                Width = 100,
                Height = 22,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 7, 0, 0)
            };
            grid.Controls.Add(badgeControl, 2, 0);

            SetState(badge, kind);
        }

        public void SetState(string text, BadgeKind kind)
        {
            badgeControl.SetState(text, kind);
            switch (kind)
            {
                case BadgeKind.Ok: dot.Color = Color.FromArgb(101, 197, 134); break;
                case BadgeKind.Warn: dot.Color = Color.FromArgb(216, 166, 64); break;
                case BadgeKind.Bad: dot.Color = Color.FromArgb(216, 92, 76); break;
                default: dot.Color = Color.FromArgb(140, 140, 140); break;
            }
            dot.Invalidate();
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
        public List<PatchChange> Changes { get; private set; }
        public List<string> Groups { get; private set; }

        public static JsonMod Load(string path, JavaScriptSerializer json)
        {
            var root = json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("JSON root is not an object.");

            var mod = new JsonMod
            {
                Path = path,
                Name = GetString(root, "name", System.IO.Path.GetFileNameWithoutExtension(path)),
                Version = GetString(root, "version", ""),
                Description = GetString(root, "description", ""),
                Author = GetString(root, "author", ""),
                Changes = new List<PatchChange>(),
                Groups = new List<string>()
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
                            Patched = GetString(change, "patched", "").Replace(" ", "")
                        });
                    }
                }
            }

            mod.Groups = mod.Changes.Select(change => change.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (mod.Groups.Count == 0) mod.Groups.Add("All");
            return mod;
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
        // The existing Lz4BlockDecompress accepts this — when the decoder
        // exhausts input after consuming literals it stops cleanly.
        //
        // Cost: ~3-bytes-per-65KB overhead vs the input size. We pay that
        // overhead for simplicity — implementing a real LZ4 matcher buys
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
                output.Add(0xF0); // literal-length code = 15 → read extra bytes
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

        public static bool DownloadAsset(ReleaseInfo info, string targetPath, Action<int> onProgress, out string error)
        {
            error = "";
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(info.DownloadUrl);
                req.UserAgent = Program.AppDisplayName + "/" + Program.AppVersion;
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

        public static void StageReplaceAndRestart(string newExePath)
        {
            var current = Assembly.GetExecutingAssembly().Location;
            var dir = Path.GetDirectoryName(current);
            var script = Path.Combine(dir, "_ujmm_update.cmd");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal");
            sb.AppendLine(":wait");
            sb.AppendLine("ping -n 2 127.0.0.1 >nul");
            sb.AppendLine("tasklist /FI \"IMAGENAME eq " + Path.GetFileName(current) + "\" 2>nul | find /i \"" + Path.GetFileName(current) + "\" >nul && goto wait");
            sb.AppendLine("move /y \"" + current + "\" \"" + current + ".old\" >nul");
            sb.AppendLine("move /y \"" + newExePath + "\" \"" + current + "\" >nul");
            sb.AppendLine("start \"\" \"" + current + "\"");
            sb.AppendLine("del \"" + current + ".old\" 2>nul");
            sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
            File.WriteAllText(script, sb.ToString(), Encoding.ASCII);

            var psi = new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
            Environment.Exit(0);
        }
    }

    internal sealed class UpdateDialog : Form
    {
        private readonly UpdateChecker.ReleaseInfo info;
        private readonly Label statusLabel;
        private readonly ProgressBar bar;
        private readonly GradientButton downloadBtn;

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
                Text = "You're on " + Program.AppVersion + ". Update now to get the latest features and fixes.",
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
                Text = string.IsNullOrEmpty(info.DownloadUrl) ? "No downloadable asset attached. Use the GitHub release page." : "Ready to download " + (info.AssetName ?? "asset") + (info.DownloadSize > 0 ? " (" + (info.DownloadSize / 1024) + " KB)" : ""),
                Dock = DockStyle.Bottom,
                Height = 22,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(169, 157, 124),
                BackColor = Color.Transparent
            };
            Controls.Add(statusLabel);

            bar = new ProgressBar { Dock = DockStyle.Bottom, Height = 22, Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100, Visible = false };
            Controls.Add(bar);

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

            var view = new GradientButton { Text = "View on GitHub", Kind = GradientButton.Style.Default, Width = 150, Height = 36 };
            view.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(info.HtmlUrl)) try { Process.Start(new ProcessStartInfo(info.HtmlUrl) { UseShellExecute = true }); } catch { }
            };
            buttons.Controls.Add(view);

            downloadBtn = new GradientButton { Text = "Download & Restart", Kind = GradientButton.Style.Primary, Width = 200, Height = 36 };
            downloadBtn.Enabled = !string.IsNullOrEmpty(info.DownloadUrl);
            downloadBtn.Click += (s, e) => StartDownload();
            buttons.Controls.Add(downloadBtn);
            Controls.Add(buttons);
        }

        private void StartDownload()
        {
            downloadBtn.Enabled = false;
            bar.Visible = true;
            statusLabel.Text = "Downloading...";
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".cache", "updates");
            Directory.CreateDirectory(dir);
            var assetName = info.AssetName ?? "Ultimate JSON Mod Manager.exe";
            var staging = Path.Combine(dir, assetName);

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string err;
                bool ok = UpdateChecker.DownloadAsset(info, staging,
                    pct => { try { BeginInvoke(new Action(() => { bar.Value = Math.Max(0, Math.Min(100, pct)); statusLabel.Text = "Downloading... " + pct + "%"; })); } catch { } },
                    out err);

                if (!ok)
                {
                    try { BeginInvoke(new Action(() => { statusLabel.Text = "Failed: " + err; downloadBtn.Enabled = true; })); } catch { }
                    return;
                }

                if (assetName.ToLowerInvariant().EndsWith(".zip"))
                {
                    var extractDir = staging + "_extracted";
                    try
                    {
                        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                        Directory.CreateDirectory(extractDir);
                        using (var fs = File.OpenRead(staging))
                        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                        {
                            foreach (var entry in zip.Entries)
                            {
                                if (string.IsNullOrEmpty(entry.Name)) continue;
                                var dest = Path.Combine(extractDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                                using (var src = entry.Open())
                                using (var dst = File.Create(dest)) src.CopyTo(dst);
                            }
                        }
                        var exe = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
                        if (exe == null)
                        {
                            try { BeginInvoke(new Action(() => { statusLabel.Text = "Update zip didn't contain an .exe"; downloadBtn.Enabled = true; })); } catch { }
                            return;
                        }
                        staging = exe;
                    }
                    catch (Exception ex)
                    {
                        try { BeginInvoke(new Action(() => { statusLabel.Text = "Extract failed: " + ex.Message; downloadBtn.Enabled = true; })); } catch { }
                        return;
                    }
                }

                try { BeginInvoke(new Action(() => { statusLabel.Text = "Restarting..."; })); } catch { }
                System.Threading.Thread.Sleep(400);
                try { UpdateChecker.StageReplaceAndRestart(staging); } catch (Exception ex)
                {
                    try { BeginInvoke(new Action(() => { statusLabel.Text = "Restart failed: " + ex.Message; downloadBtn.Enabled = true; })); } catch { }
                }
            });
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
                Text = "Get your key from nexusmods.com → Account Settings → API Access. You only need to do this once.",
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
            body.AppendLine(isCrash ? "_The app crashed with the error below._" : "_<describe the bug — what did you do, what did you expect, what happened instead?>_");
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
            Width = 640;
            Height = 520;
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
                ScrollBars = ScrollBars.Vertical,
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
