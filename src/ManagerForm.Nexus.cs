using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace CdJsonModManager
{
    internal sealed partial class ManagerForm
    {
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
                var ans = UiSafe.Msg("Disconnect from Nexus? You can reconnect any time.", "Disconnect", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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
                        UiSafe.Msg(
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
                        UiSafe.Msg("Sign-in did not complete: " + err, "Sign-in failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                UiSafe.Msg("That key didn't validate against Nexus.\r\n\r\n" + error, "Invalid key", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                            if (statusBuildPill != null)
                            {
                                statusBuildPill.Text = "Update " + info.TagName.TrimStart('v', 'V');
                                statusBuildPill.DotColor = Color.FromArgb(244, 199, 103);
                                statusBuildPill.PillFillColor = Color.FromArgb(80, 244, 199, 103);
                                statusBuildPill.PillTextColor = Color.FromArgb(255, 236, 184);
                                statusBuildPill.Invalidate();
                                tipsHost.SetToolTip(statusBuildPill, "Update available - click to open the UJMM Nexus files page.");
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
                UiSafe.Msg("No Nexus update info available.\r\n\r\n" + (string.IsNullOrEmpty(err) ? "Sign in with Nexus, or open the UJMM Nexus page manually." : err), "No update info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var compare = UpdateChecker.CompareVersions(info.TagName, Program.AppVersion);
            if (compare < 0)
            {
                UiSafe.Msg("Nexus currently lists version " + info.TagName.TrimStart('v', 'V') + ".\r\n\r\nYou're running " + Program.AppVersion + ", which is newer than the version visible on Nexus.", "No Nexus update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!UpdateChecker.IsNewer(info.TagName, Program.AppVersion))
            {
                UiSafe.Msg("You're on the latest Nexus version (" + Program.AppVersion + ").", "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            var files = NexusClient.GetModFiles(nexusApiKey, Program.NexusGameDomain, Program.NexusAppModId, out error);
            var latestFile = files != null
                ? files
                    .Where(file => !IsNexusOldFile(file))
                    .OrderByDescending(file => IsNexusMainFile(file) ? 1 : 0)
                    .ThenByDescending(NexusFileTimestamp)
                    .FirstOrDefault(file => !string.IsNullOrWhiteSpace(NexusString(file, "version")))
                : null;

            var modInfo = NexusClient.GetMod(nexusApiKey, Program.NexusGameDomain, Program.NexusAppModId, out error);
            if (modInfo == null && latestFile == null) return null;

            var version = latestFile != null ? NexusString(latestFile, "version") : "";
            if (string.IsNullOrWhiteSpace(version) && modInfo != null && modInfo.ContainsKey("version")) version = Convert.ToString(modInfo["version"]);
            var name = modInfo != null && modInfo.ContainsKey("name") ? Convert.ToString(modInfo["name"]) : Program.AppDisplayName;
            var summary = modInfo != null && modInfo.ContainsKey("summary") ? Convert.ToString(modInfo["summary"]) : "";
            return new UpdateChecker.ReleaseInfo
            {
                TagName = version,
                Title = string.IsNullOrEmpty(name) ? Program.AppDisplayName : name,
                Body = string.IsNullOrEmpty(summary) ? "Open the Nexus files tab to download the latest UJMM build." : summary,
                HtmlUrl = Program.NexusAppFilesUrl
            };
        }

        private static string NexusString(Dictionary<string, object> data, string key)
        {
            return data != null && data.ContainsKey(key) && data[key] != null ? Convert.ToString(data[key]) : "";
        }

        private static long NexusFileTimestamp(Dictionary<string, object> file)
        {
            foreach (var key in new[] { "uploaded_timestamp", "uploaded_time", "date", "timestamp" })
            {
                long value;
                if (file != null && file.ContainsKey(key) && file[key] != null && long.TryParse(Convert.ToString(file[key]), out value)) return value;
            }
            return 0;
        }

        private static bool IsNexusOldFile(Dictionary<string, object> file)
        {
            if (file == null) return true;
            var category = NexusString(file, "category_name");
            if (!string.IsNullOrWhiteSpace(category) && category.IndexOf("old", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var categoryId = NexusString(file, "category_id");
            if (categoryId == "3") return true;
            return false;
        }

        private static bool IsNexusMainFile(Dictionary<string, object> file)
        {
            var category = NexusString(file, "category_name");
            if (!string.IsNullOrWhiteSpace(category) && category.IndexOf("main", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return NexusString(file, "category_id") == "1";
        }

        private void HandleNxmUrl(string url)
        {
            try
            {
                Activate();
                BringToFront();
                if (string.IsNullOrEmpty(nexusApiKey))
                {
                    var ans = UiSafe.Msg("A Nexus download was triggered, but you're not signed in yet.\r\n\r\nSign in now?", "Sign in required", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
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
                    var ans = UiSafe.Msg("This download is for game '" + parsed.Game + "'. This manager only supports '" + Program.NexusGameDomain + "'.\r\n\r\nIgnore?", "Wrong game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    }
}
