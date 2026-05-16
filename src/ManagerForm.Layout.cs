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
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CdJsonModManager
{
    internal sealed partial class ManagerForm
    {
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

            mainGrid = new TableLayoutPanel
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
            Resize += (s, e) => AdjustMainColumns();
            AdjustMainColumns();
        }

        private void AdjustMainColumns()
        {
            if (mainGrid == null || mainGrid.ColumnStyles.Count < 3) return;
            var wide = ClientSize.Width >= 1800;
            mainGrid.ColumnStyles[0].Width = wide ? 430 : 348;
            mainGrid.ColumnStyles[2].Width = wide ? 620 : 330;
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
                UiSafe.Msg("Could not find Crimson Desert in any standard Steam path. Click Browse and pick the folder manually.", "Not detected", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var asiMenu = new ContextMenuStrip();
            asiMenu.Items.Add("Toggle selected", null, (s, e) => ToggleSelectedAsi());
            asiMenu.Items.Add("Remove selected from manager", null, (s, e) => RemoveSelectedAsi());
            asiList.ContextMenuStrip = asiMenu;
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
                UiSafe.Msg("Set the Crimson Desert folder first.", "Game folder missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    }
}
