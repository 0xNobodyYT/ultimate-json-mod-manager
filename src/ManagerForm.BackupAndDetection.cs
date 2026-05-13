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

    }
}
