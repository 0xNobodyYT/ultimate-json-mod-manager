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
                    if (IsBrowserModDirectory(dir)) mods.Add(JsonMod.LoadOverlayDirectory(dir, json, "BROWSER"));
                    else if (IsRawOverlayDirectory(dir)) mods.Add(JsonMod.LoadOverlayDirectory(dir, json, "RAW"));
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
                    CornerRadius = 12,
                    BorderWidth = 1,
                    Width = Math.Max(260, modCardsHost.ClientSize.Width - 4),
                    Height = 84,
                    Margin = new Padding(0, 0, 0, 6),
                    Padding = new Padding(10, 8, 10, 8),
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
                stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
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
                    Width = 21,
                    Height = 21,
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
                    Font = new Font("Trebuchet MS", 9.0f, FontStyle.Bold),
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
                    Font = new Font("Consolas", 7.0f, FontStyle.Bold),
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
                        SafeOpenUrl(nl.UpdateAvailable ? NexusModFilesUrl(nl.ModId) : NexusModPageUrl(nl.ModId));
                    }
                };

                var metaLabel = new Label
                {
                    Text = ModCardMetaText(mod),
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 7.0f),
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
                            SafeOpenUrl(NexusModPageUrl(nexusLinks[capturedMod.Path].ModId));
                    });
                    menu.Items.Add("Open Nexus Files", null, (s, e) =>
                    {
                        if (nexusLinks.ContainsKey(capturedMod.Path))
                            SafeOpenUrl(NexusModFilesUrl(nexusLinks[capturedMod.Path].ModId));
                    });
                    menu.Items.Add("Unlink from Nexus", null, (s, e) =>
                    {
                        NexusLink.Delete(capturedMod.Path);
                        nexusLinks.Remove(capturedMod.Path);
                        UpdateModPillForLink(capturedMod);
                    });
                }
                menu.Items.Add("-");
                menu.Items.Add("Delete from manager", null, (s, e) => DeleteSelectedMods(capturedMod));
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

        private static string ModCardMetaText(JsonMod mod)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(mod.Version)) parts.Add("v" + mod.Version.Trim().TrimStart('v', 'V'));
            if (!string.IsNullOrWhiteSpace(mod.Author)) parts.Add("by " + mod.Author.Trim());
            var prefix = string.Join(" - ", parts.ToArray());
            var body = !string.IsNullOrWhiteSpace(mod.Description) ? mod.Description.Trim() : mod.FormatTag + " mod";
            if (string.IsNullOrWhiteSpace(prefix)) return body;
            return prefix + " - " + body;
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
                pill.Cursor = Cursors.Hand;
                tipsHost.SetToolTip(pill, "Update available. Click to open this mod's Nexus files page.");
            }
            else
            {
                pill.Text = "v" + (string.IsNullOrEmpty(link.InstalledVersion) ? "?" : link.InstalledVersion);
                pill.PillFillColor = Color.FromArgb(101, 197, 134);
                pill.PillTextColor = Color.FromArgb(8, 19, 13);
                pill.Cursor = Cursors.Hand;
                tipsHost.SetToolTip(pill, "Linked to Nexus. Click to open the mod page.");
            }
            pill.Width = 0;
            pill.Invalidate();
        }

        private static string NexusModPageUrl(int modId)
        {
            return "https://www.nexusmods.com/" + Program.NexusGameDomain + "/mods/" + modId;
        }

        private static string NexusModFilesUrl(int modId)
        {
            return NexusModPageUrl(modId) + "?tab=files";
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
                        var latestVer = "";
                        var files = NexusClient.GetModFiles(key, Program.NexusGameDomain, link.ModId, out err);
                        var latestFile = files != null
                            ? files
                                .Where(file => !IsNexusOldFile(file))
                                .OrderByDescending(file => IsNexusMainFile(file) ? 1 : 0)
                                .ThenByDescending(NexusFileTimestamp)
                                .FirstOrDefault(file => !string.IsNullOrWhiteSpace(NexusString(file, "version")))
                            : null;
                        if (latestFile != null)
                        {
                            latestVer = NexusString(latestFile, "version");
                            newest = Math.Max(newest, NexusFileTimestamp(latestFile));
                        }
                        if (string.IsNullOrWhiteSpace(latestVer))
                        {
                            var modInfo = NexusClient.GetMod(key, Program.NexusGameDomain, link.ModId, out err);
                            if (modInfo == null) continue;
                            latestVer = modInfo.ContainsKey("version") ? Convert.ToString(modInfo["version"]) : "";
                        }
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

            var overlayHasVariants = IsOverlayMod(focused)
                && focused.OverlayGroupByFolder != null
                && focused.OverlayGroupByFolder.Count > 0;
            if (IsOverlayMod(focused) && !overlayHasVariants)
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
                int patchCount = IsOverlayMod(focused)
                    ? OverlayFoldersForGroup(focused, g).Count()
                    : focused.Changes.Count(c => string.Equals(c.Group, g, StringComparison.OrdinalIgnoreCase));
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
                    Text = IsOverlayMod(focused)
                        ? patchCount + " folder" + (patchCount == 1 ? "" : "s")
                        : patchCount + " patch" + (patchCount == 1 ? "" : "es"),
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
                    foreach (var folder in OverlayFoldersForGroup(mod, GroupFor(mod)))
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
                    foreach (var folder in OverlayFoldersForGroup(focused, GroupFor(focused)))
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

        private static IEnumerable<string> OverlayFoldersForGroup(JsonMod mod, string group)
        {
            if (mod == null || mod.OverlayFolders == null) return Enumerable.Empty<string>();
            if (mod.OverlayGroupByFolder == null || mod.OverlayGroupByFolder.Count == 0) return mod.OverlayFolders;
            if (string.IsNullOrWhiteSpace(group) || string.Equals(group, "All", StringComparison.OrdinalIgnoreCase)) return mod.OverlayFolders;
            return mod.OverlayFolders.Where(folder =>
            {
                string g;
                return mod.OverlayGroupByFolder.TryGetValue(folder, out g)
                    && string.Equals(g, group, StringComparison.OrdinalIgnoreCase);
            });
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
            DeleteMods(new[] { mod });
        }

        private void DeleteSelectedMods(JsonMod fallback)
        {
            var selected = mods
                .Where(m => activeBoxes.ContainsKey(m.Path) && activeBoxes[m.Path].Checked)
                .ToList();
            if (selected.Count <= 1 || !selected.Any(m => string.Equals(m.Path, fallback.Path, StringComparison.OrdinalIgnoreCase)))
                selected = new List<JsonMod> { fallback };
            DeleteMods(selected);
        }

        private void DeleteMods(IEnumerable<JsonMod> modsToDelete)
        {
            var targets = modsToDelete
                .Where(m => m != null)
                .GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (targets.Count == 0) return;

            var names = string.Join("\r\n", targets.Take(8).Select(m => "  * " + m.Name).ToArray());
            if (targets.Count > 8) names += "\r\n  ...";
            var answer = MessageBox.Show("Delete " + targets.Count + " mod" + (targets.Count == 1 ? "" : "s") + " from the manager?\r\n\r\n" + names + "\r\n\r\nThis removes them from mods/ but does not edit game files.", "Delete mod", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            try
            {
                foreach (var mod in targets)
                {
                    if (File.Exists(mod.Path)) File.Delete(mod.Path);
                    else if (Directory.Exists(mod.Path)) Directory.Delete(mod.Path, true);
                    Log("Deleted mod: " + mod.Name + ".");

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

                    var deadPrefix = Path.GetFileName(mod.Path) + "|";
                    disabledPatches.RemoveWhere(k => k.StartsWith(deadPrefix, StringComparison.OrdinalIgnoreCase));
                }
                config["disabledPatches"] = disabledPatches.ToArray();
                var deletedNames = new HashSet<string>(targets.Select(m => Path.GetFileName(m.Path)), StringComparer.OrdinalIgnoreCase);
                var activeNames = (config.ContainsKey("activeMods") && config["activeMods"] is object[] aArr ? aArr : new object[0])
                    .Select(o => Convert.ToString(o))
                    .Where(s => !deletedNames.Contains(s))
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
            var activeCount = activeBoxes.Values.Count(box => box.Checked);
            foreach (var box in activeBoxes.Values)
            {
                box.Checked = false;
            }
            Log(activeCount > 0 ? "Uninstalled/deactivated " + activeCount + " active JSON mod(s)." : "No active JSON mods to deactivate.");
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

    }
}
