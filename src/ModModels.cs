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
        public Dictionary<string, string> OverlayGroupByFolder { get; private set; }

        public static JsonMod Load(string path, JavaScriptSerializer json)
        {
            var root = json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("JSON root is not an object.");
            if (IsFieldFormat(root)) return LoadFieldFormat(path, root);

            var info = root.ContainsKey("modinfo") ? root["modinfo"] as Dictionary<string, object> : null;
            var mod = new JsonMod
            {
                Path = path,
                Name = info != null ? GetString(info, "title", GetString(info, "name", System.IO.Path.GetFileNameWithoutExtension(path))) : GetString(root, "name", System.IO.Path.GetFileNameWithoutExtension(path)),
                Version = info != null ? GetString(info, "version", GetString(root, "version", "")) : GetString(root, "version", ""),
                Description = info != null ? GetString(info, "description", GetString(root, "description", "")) : GetString(root, "description", ""),
                Author = info != null ? GetString(info, "author", GetString(root, "author", "")) : GetString(root, "author", ""),
                FormatTag = "JSON",
                Changes = new List<PatchChange>(),
                Groups = new List<string>(),
                OverlayFolders = new List<string>(),
                OverlayGroupByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
                        AddClassicChange(mod, gameFile, patch, change);
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
                    AddClassicChange(mod, gameFile, root, change);
                }
            }

            mod.Groups = mod.Changes.Select(change => change.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (mod.Groups.Count == 0) mod.Groups.Add("All");
            return mod;
        }

        private static void AddClassicChange(JsonMod mod, string gameFile, Dictionary<string, object> patch, Dictionary<string, object> change)
        {
            int offset;
            int relOffset;
            var hasOffset = TryReadIntLike(change, "offset", out offset);
            var hasRelOffset = TryReadIntLike(change, "rel_offset", out relOffset);
            if (string.IsNullOrWhiteSpace(gameFile)) gameFile = GetString(change, "game_file", GetString(change, "file", ""));

            var entry = GetString(change, "entry", GetString(patch, "entry", ""));
            var type = GetString(change, "type", GetString(change, "op", "replace")).Trim();
            var label = GetString(change, "label", "");
            if (string.IsNullOrWhiteSpace(label))
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(entry)) parts.Add(entry);
                if (hasRelOffset) parts.Add("+0x" + relOffset.ToString("X"));
                else if (hasOffset) parts.Add("0x" + offset.ToString("X"));
                label = parts.Count > 0 ? string.Join(" ", parts.ToArray()) : "Patch";
            }

            var original = NormalizeHex(GetString(change, "original", GetString(change, "old", "")));
            var patched = NormalizeHex(GetString(change, "patched", GetString(change, "new", GetString(change, "bytes", ""))));
            var c = new PatchChange
            {
                GameFile = gameFile,
                Offset = hasOffset ? offset : 0,
                Label = label,
                Original = original,
                Patched = patched,
                IsResolvedBytes = hasOffset && (!hasRelOffset || string.IsNullOrWhiteSpace(entry)),
                EntryName = entry,
                PatchType = string.IsNullOrWhiteSpace(type) ? "replace" : type,
                SourceGroup = GetString(patch, "source_group", GetString(change, "source_group", "")),
                TargetDisplay = System.IO.Path.GetFileName(gameFile) + (string.IsNullOrWhiteSpace(entry) ? "" : " - " + entry)
            };
            if (hasOffset)
            {
                c.HasOffsetFallback = true;
                c.OffsetFallback = offset;
            }
            if (hasRelOffset)
            {
                c.RelOffset = relOffset;
                c.HasRelOffset = true;
                c.IsResolvedBytes = false;
            }
            if (c.IsInsert && string.IsNullOrWhiteSpace(c.Patched))
            {
                c.Patched = NormalizeHex(GetString(change, "insert", ""));
            }
            if (!hasOffset && !hasRelOffset)
            {
                c.IsResolvedBytes = false;
                c.ResolveError = "No offset or entry-relative offset was provided.";
            }
            mod.Changes.Add(c);
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
                OverlayFolders = new List<string>(),
                OverlayGroupByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
            if (formatTag == "RAW" && !File.Exists(metadata)) metadata = System.IO.Path.Combine(path, "manifest.json");
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
                OverlayFolders = new List<string>(),
                OverlayGroupByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
            if (formatTag == "RAW" && info == null)
            {
                var candidateRoot = System.IO.Path.Combine(path, "files");
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
            if (formatTag == "RAW" && mod.OverlayFolders.Count == 0)
            {
                foreach (var variantDir in Directory.GetDirectories(path).OrderBy(System.IO.Path.GetFileName))
                {
                    var group = System.IO.Path.GetFileName(variantDir);
                    var addedForVariant = 0;
                    foreach (var archiveDir in Directory.GetDirectories(variantDir).OrderBy(System.IO.Path.GetFileName))
                    {
                        var name = System.IO.Path.GetFileName(archiveDir);
                        if (!Regex.IsMatch(name, @"^\d{4}$")) continue;
                        if (!File.Exists(System.IO.Path.Combine(archiveDir, "0.pamt"))
                            && !File.Exists(System.IO.Path.Combine(archiveDir, "0.paz"))
                            && !Directory.GetFiles(archiveDir, "*", SearchOption.AllDirectories).Any()) continue;
                        mod.OverlayFolders.Add(archiveDir);
                        mod.OverlayGroupByFolder[archiveDir] = group;
                        addedForVariant++;
                    }
                    if (addedForVariant > 0 && !mod.Groups.Any(g => string.Equals(g, group, StringComparison.OrdinalIgnoreCase)))
                        mod.Groups.Add(group);
                }
                if (mod.OverlayGroupByFolder.Count > 0)
                    mod.Groups.RemoveAll(g => string.Equals(g, "All", StringComparison.OrdinalIgnoreCase));
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
                if (info == null && string.Equals(System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar), System.IO.Path.GetFullPath(mod.OverlayFolders[0]).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    mod.Name = System.IO.Path.GetFileName(mod.OverlayFolders[0]);
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
                "level", "prefab", "sequencer"
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
                var oldValue = intent.ContainsKey("old") && intent["old"] != null ? Convert.ToString(intent["old"]) : "";
                var newValue = intent.ContainsKey("new") && intent["new"] != null ? Convert.ToString(intent["new"]) : "";
                var offsets = ReadOffsetHints(intent);
                var labelParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(entry)) labelParts.Add(entry);
                if (!string.IsNullOrWhiteSpace(field)) labelParts.Add(field);
                if (!string.IsNullOrWhiteSpace(op)) labelParts.Add(op + " " + newValue);
                var label = GetString(intent, "label", labelParts.Count > 0 ? string.Join(" - ", labelParts.ToArray()) : "Field intent");

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
                    OldValue = oldValue,
                    NewValue = newValue,
                    AbsoluteOffsetHints = offsets,
                    TargetDisplay = System.IO.Path.GetFileName(gameFile) + " - " + field + (string.IsNullOrWhiteSpace(newValue) ? "" : " -> " + newValue)
                });
            }
        }

        private static List<int> ReadOffsetHints(Dictionary<string, object> intent)
        {
            var hints = new List<int>();
            if (intent == null) return hints;

            Action<object> add = value =>
            {
                if (value == null) return;
                try
                {
                    var text = Convert.ToString(value).Trim();
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        hints.Add(Convert.ToInt32(text.Substring(2), 16));
                    else
                        hints.Add(Convert.ToInt32(text, System.Globalization.CultureInfo.InvariantCulture));
                }
                catch { }
            };

            if (intent.ContainsKey("offset")) add(intent["offset"]);
            if (intent.ContainsKey("absolute_offset")) add(intent["absolute_offset"]);
            if (intent.ContainsKey("offsets") && intent["offsets"] is object[] arr)
            {
                foreach (var item in arr) add(item);
            }

            return hints;
        }

        private static bool TryReadIntLike(Dictionary<string, object> dict, string key, out int value)
        {
            value = 0;
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return false;
            try
            {
                var raw = dict[key];
                if (raw is int)
                {
                    value = (int)raw;
                    return true;
                }
                if (raw is long)
                {
                    value = checked((int)(long)raw);
                    return true;
                }
                var text = Convert.ToString(raw).Trim();
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    value = Convert.ToInt32(text.Substring(2), 16);
                    return true;
                }
                if (Regex.IsMatch(text, @"\A[0-9a-fA-F]+\z") && Regex.IsMatch(text, @"[a-fA-F]"))
                {
                    value = Convert.ToInt32(text, 16);
                    return true;
                }
                value = Convert.ToInt32(text, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static string NormalizeHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
            return Regex.Replace(value, @"[^0-9a-fA-F]", "");
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
        public string OldValue;
        public string NewValue;
        public List<int> AbsoluteOffsetHints;
        public byte[] ResolveSourceBytes;
        public string TargetDisplay;
        public string ResolveError;
        public int RelOffset;
        public bool HasRelOffset;
        public int OffsetFallback;
        public bool HasOffsetFallback;
        public string PatchType;
        public string SourceGroup;

        public bool IsEntryAnchored
        {
            get { return HasRelOffset && !string.IsNullOrWhiteSpace(EntryName); }
        }

        public bool IsInsert
        {
            get { return string.Equals(PatchType ?? "", "insert", StringComparison.OrdinalIgnoreCase); }
        }

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
        public int EncryptionType { get { return (int)((Flags >> 20) & 0x0F); } }
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
}
