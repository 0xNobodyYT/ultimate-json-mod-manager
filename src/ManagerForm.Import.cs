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

            packageCandidates = OutermostPackageCandidates(packageCandidates).ToList();
            jsonCandidates = jsonCandidates
                .Where(file => !IsInsideAnyDirectory(file, packageCandidates))
                .ToList();

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
                    if (Directory.Exists(package) && IsBrowserModDirectory(package))
                    {
                        var target = Path.Combine(modsDir, SafePackageName(Path.GetFileName(package)));
                        CopyDirectory(package, target);
                        RemoveLegacyPackageArtifacts(target);
                        imported++;
                        Log("Imported Browser/UI mod: " + Path.GetFileName(package));
                    }
                    else if (Directory.Exists(package) && IsRawOverlayDirectory(package))
                    {
                        var target = Path.Combine(modsDir, SafePackageName(Path.GetFileName(package)));
                        CopyDirectory(package, target);
                        RemoveLegacyPackageArtifacts(target);
                        imported++;
                        Log("Imported RAW overlay mod: " + Path.GetFileName(package));
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

        private IEnumerable<string> OutermostPackageCandidates(IEnumerable<string> candidates)
        {
            var dirs = candidates
                .Where(Directory.Exists)
                .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path.Length)
                .ToList();

            var kept = new List<string>();
            foreach (var dir in dirs)
            {
                if (kept.Any(parent => IsPathInsideDirectory(dir, parent))) continue;
                kept.Add(dir);
            }
            return kept;
        }

        private static bool IsInsideAnyDirectory(string path, IEnumerable<string> dirs)
        {
            var full = Path.GetFullPath(path);
            return dirs.Any(dir => IsPathInsideDirectory(full, dir));
        }

        private static bool IsPathInsideDirectory(string path, string dir)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDir = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.Length > fullDir.Length
                && fullPath.StartsWith(fullDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private void RemoveLegacyPackageArtifacts(string importedPackageRoot)
        {
            try
            {
                var importedName = Path.GetFileName(importedPackageRoot);
                var artifactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var folder in JsonMod.LoadOverlayDirectory(importedPackageRoot, json, IsBrowserModDirectory(importedPackageRoot) ? "BROWSER" : "RAW").OverlayFolders)
                {
                    artifactNames.Add(Path.GetFileName(folder));
                    var parent = Directory.GetParent(folder);
                    if (parent != null && !string.Equals(parent.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), importedPackageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        artifactNames.Add(parent.Name);
                    foreach (var child in Directory.GetDirectories(folder))
                    {
                        var childName = Path.GetFileName(child);
                        if (IsLikelyGameOverlayRoot(childName)) artifactNames.Add(childName);
                    }
                }
                artifactNames.Add("mod");
                artifactNames.Add("manifest");

                foreach (var name in artifactNames)
                {
                    if (string.Equals(name, importedName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Regex.IsMatch(name ?? "", @"^\d{4}$", RegexOptions.IgnoreCase)
                        && !IsLikelyGameOverlayRoot(name)
                        && !string.Equals(name, "mod", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(name, "manifest", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var dir = Path.Combine(modsDir, name);
                    if (Directory.Exists(dir) && !File.Exists(Path.Combine(dir, "modinfo.json")) && !File.Exists(Path.Combine(dir, "manifest.json")) && !File.Exists(Path.Combine(dir, "mod.json")))
                    {
                        Directory.Delete(dir, true);
                        Log("Removed old split import artifact: " + name);
                    }

                    var jsonFile = Path.Combine(modsDir, name + ".json");
                    if (File.Exists(jsonFile))
                    {
                        try
                        {
                            var root = json.DeserializeObject(File.ReadAllText(jsonFile, Encoding.UTF8)) as Dictionary<string, object>;
                            if (root != null && (root.ContainsKey("modinfo") || root.ContainsKey("files_dir") || root.ContainsKey("patches_dir")))
                            {
                                File.Delete(jsonFile);
                                Log("Removed old metadata JSON artifact: " + Path.GetFileName(jsonFile));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Legacy artifact cleanup skipped: " + ex.Message);
            }
        }

        private void AddExtractedPackageCandidates(string extractRoot, List<string> packageCandidates)
        {
            if (!Directory.Exists(extractRoot)) return;

            AddPackageCandidateTree(extractRoot, packageCandidates);
        }

        private bool AddPackageCandidateTree(string dir, List<string> packageCandidates)
        {
            if (!Directory.Exists(dir)) return false;
            if (IsRawOverlayDirectory(dir) || IsBrowserModDirectory(dir))
            {
                if (!packageCandidates.Any(existing => string.Equals(existing, dir, StringComparison.OrdinalIgnoreCase)))
                    packageCandidates.Add(dir);
                return true;
            }

            foreach (var child in Directory.GetDirectories(dir).OrderBy(Path.GetFileName))
            {
                AddPackageCandidateTree(child, packageCandidates);
            }
            return false;
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
                jsonCandidates.AddRange(Directory.GetFiles(path, "*.json", SearchOption.TopDirectoryOnly)
                    .Where(file => !IsPackageMetadataFile(path, file)));
                asiCandidates.AddRange(Directory.GetFiles(path, "*.asi", SearchOption.TopDirectoryOnly));
                asiCandidates.AddRange(Directory.GetFiles(path, "*.dll", SearchOption.TopDirectoryOnly));
                asiCandidates.AddRange(Directory.GetFiles(path, "*.ini", SearchOption.TopDirectoryOnly));
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
            if (Directory.GetDirectories(path).Any(IsRawOverlayPayloadDirectory)) return true;
            if (IsFilesOverlayDirectory(path)) return true;
            if (IsVariantOverlayPackageDirectory(path)) return true;
            if (IsManifestOverlayDirectory(path)) return true;
            if (!File.Exists(Path.Combine(path, "modinfo.json")) && !File.Exists(Path.Combine(path, "mod.json"))) return false;
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

        private static bool IsFilesOverlayDirectory(string path)
        {
            var filesRoot = Path.Combine(path, "files");
            return Directory.Exists(filesRoot) && Directory.GetDirectories(filesRoot).Any(IsRawOverlayPayloadDirectory);
        }

        private static bool IsVariantOverlayPackageDirectory(string path)
        {
            if (!File.Exists(Path.Combine(path, "mod.json"))
                && !File.Exists(Path.Combine(path, "modinfo.json"))
                && !File.Exists(Path.Combine(path, "manifest.json")))
            {
                return false;
            }
            return Directory.GetDirectories(path).Any(child =>
                Directory.GetDirectories(child).Any(IsRawOverlayPayloadDirectory));
        }

        private bool IsManifestOverlayDirectory(string path)
        {
            var manifestPath = Path.Combine(path, "manifest.json");
            if (!File.Exists(manifestPath)) return false;
            try
            {
                var root = json.DeserializeObject(File.ReadAllText(manifestPath, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return false;
                var filesDir = Convert.ToString(root.ContainsKey("files_dir") ? root["files_dir"] : "files");
                if (string.IsNullOrWhiteSpace(filesDir)) filesDir = "files";
                var filesRoot = Path.Combine(path, filesDir);
                if (Directory.Exists(filesRoot) && Directory.GetDirectories(filesRoot).Any(IsRawOverlayPayloadDirectory)) return true;
                return Directory.GetDirectories(path).Any(IsRawOverlayPayloadDirectory);
            }
            catch { return false; }
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
                "level", "prefab", "sequencer"
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
                if (string.IsNullOrEmpty(path)) continue;
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

        private void RemoveSelectedAsi()
        {
            var selected = asiList.SelectedItems.Cast<ListViewItem>()
                .Select(item => Convert.ToString(item.Tag))
                .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (selected.Count == 0) return;

            var names = string.Join("\r\n", selected.Take(8).Select(path => "  * " + Path.GetFileName(path)).ToArray());
            if (selected.Count > 8) names += "\r\n  ...";
            var answer = MessageBox.Show("Remove " + selected.Count + " ASI/DLL/INI file" + (selected.Count == 1 ? "" : "s") + "?\r\n\r\n" + names + "\r\n\r\nThis deletes the selected runtime mod file(s) from bin64 and UJMM's _asi copy when present.", "Remove ASI mod", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            foreach (var path in selected)
            {
                try
                {
                    var name = Path.GetFileName(path);
                    File.Delete(path);
                    var shadow = Path.Combine(asiDir, name);
                    if (File.Exists(shadow)) File.Delete(shadow);
                    if (name.EndsWith(".asi.disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var enabledShadow = Path.Combine(asiDir, name.Substring(0, name.Length - ".disabled".Length));
                        if (File.Exists(enabledShadow)) File.Delete(enabledShadow);
                    }
                    Log("Removed ASI mod file: " + name);
                }
                catch (Exception ex)
                {
                    Log("Could not remove ASI file " + Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            RefreshAsi();
        }

    }
}
