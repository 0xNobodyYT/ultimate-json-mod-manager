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
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace CdJsonModManager
{
    internal sealed partial class ManagerForm
    {
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
            ResolveEntryAnchoredPatches(selected, validationCacheDir);
            Log("Starting mod match verification...");
            var checkedCount = 0;
            var overlayChecked = 0;
            var alreadyPatched = 0;
            var issues = new List<string>();

            foreach (var mod in selected)
            {
                if (IsOverlayMod(mod))
                {
                    foreach (var folder in OverlayFoldersForGroup(mod, GroupFor(mod)))
                    {
                        var files = OverlayFiles(folder).Where(File.Exists).Where(file => !IsPackageMetadataFile(folder, file)).ToList();
                        if (files.Count == 0)
                        {
                            issues.Add("EMPTY OVERLAY [" + mod.Name + "] " + Path.GetFileName(folder));
                            continue;
                        }
                        var looseOverlay = !IsCompiledOverlayFolder(folder);
                        var resolver = looseOverlay ? new GameVfsResolver(gamePath) : null;
                        var preferredGroup = PreferredOverlaySourceGroup(folder);
                        foreach (var file in files)
                        {
                            if (looseOverlay)
                            {
                                var rel = file.Substring(folder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                                rel = NormalizeOverlayPatchOutputPath(rel);
                                var match = resolver.Resolve(rel, preferredGroup);
                                if (match == null && (rel.EndsWith(".patch", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".merge", StringComparison.OrdinalIgnoreCase)))
                                    issues.Add("Missing overlay merge source [" + mod.Name + "] " + rel);
                                else if (match != null && match.Corrected)
                                    Log("Overlay path would be corrected: " + rel + " -> " + match.Entry.Path + " (" + match.GroupName + ").");
                            }
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
                    var checkLength = change.IsInsert && original.Length == 0 ? 0 : original.Length;
                    if (change.Offset < 0 || change.Offset + checkLength > data.Length)
                    {
                        issues.Add("Out of range: " + change.Label);
                        continue;
                    }

                    if (change.IsInsert && original.Length == 0)
                    {
                        checkedCount++;
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
                if (string.IsNullOrWhiteSpace(change.FieldPath) && !change.IsEntryAnchored) continue;
                change.Offset = change.HasOffsetFallback ? change.OffsetFallback : 0;
                if (!string.IsNullOrWhiteSpace(change.FieldPath))
                {
                    change.Original = "";
                    change.Patched = "";
                }
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
                    int offset;
                    int length;
                    byte[] patched;
                    change.ResolveSourceBytes = bytes;
                    FieldEntry entry;
                    if (!entryMap.TryGetValue(change.EntryName ?? "", out entry))
                    {
                        if (!TryResolveRawFieldSlice(change, null, out offset, out length, out patched))
                        {
                            change.ResolveError = "Entry '" + change.EntryName + "' was not found in " + gameFile + ".";
                            continue;
                        }
                    }
                    else if (TryResolveKnownField(change, entry, out offset, out length, out patched))
                    {
                        // handled below
                    }
                    else
                    {
                        change.ResolveError = "Unsupported field path '" + change.FieldPath + "' in " + Path.GetFileName(gameFile) + ".";
                        continue;
                    }

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
                catch (Exception ex)
                {
                    change.ResolveError = ex.Message;
                }
                finally
                {
                    change.ResolveSourceBytes = null;
                }
            }

            int resolved = unresolved.Count(c => c.IsResolvedBytes);
            if (resolved > 0)
            {
                Log("Resolved " + resolved + " field-format patch(es) to byte offsets.");
                RefreshPatchList();
            }
        }

        private void ResolveEntryAnchoredPatches(IEnumerable<JsonMod> selected, string extractCacheDir)
        {
            var unresolved = selected.SelectMany(m => m.Changes)
                .Where(c => !c.IsResolvedBytes && c.IsEntryAnchored && string.IsNullOrWhiteSpace(c.FieldPath))
                .ToList();
            if (unresolved.Count == 0) return;

            var maps = new Dictionary<string, Tuple<byte[], Dictionary<string, FieldEntry>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in unresolved)
            {
                try
                {
                    var gameFile = change.GameFile ?? "";
                    if (!maps.ContainsKey(gameFile))
                    {
                        var dataPath = ArchiveExtractor.Extract(gamePath, gameFile, extractCacheDir, Log);
                        var schemaFile = Path.ChangeExtension(gameFile, ".pabgh");
                        var schemaPath = ArchiveExtractor.Extract(gamePath, schemaFile, extractCacheDir, Log);
                        if (dataPath == null || !File.Exists(dataPath) || schemaPath == null || !File.Exists(schemaPath))
                        {
                            change.ResolveError = "Could not extract " + gameFile + " and its .pabgh companion.";
                            TryUseOffsetFallback(change, null);
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
                        TryUseOffsetFallback(change, bytes);
                        continue;
                    }

                    var offset = entry.BlobStart + change.RelOffset;
                    var patchLen = change.IsInsert ? HexToBytes(change.Patched).Length : HexToBytes(change.Original).Length;
                    var relLimit = change.IsInsert ? entry.BlobSize : entry.BlobSize - patchLen;
                    if (change.RelOffset < 0 || change.RelOffset > relLimit || offset < 0 || offset + patchLen > bytes.Length)
                    {
                        change.ResolveError = "Entry-relative offset is outside '" + change.EntryName + "'.";
                        TryUseOffsetFallback(change, bytes);
                        continue;
                    }

                    change.Offset = offset;
                    change.IsResolvedBytes = true;
                    change.ResolveError = "";
                    if (!string.IsNullOrWhiteSpace(change.TargetDisplay))
                        change.TargetDisplay = Path.GetFileName(gameFile) + " - " + change.EntryName + " @ +0x" + change.RelOffset.ToString("X");
                }
                catch (Exception ex)
                {
                    change.ResolveError = ex.Message;
                    TryUseOffsetFallback(change, null);
                }
            }

            int resolved = unresolved.Count(c => c.IsResolvedBytes);
            if (resolved > 0)
            {
                Log("Resolved " + resolved + " entry-relative JSON patch(es) to byte offsets.");
                RefreshPatchList();
            }
        }

        private static void TryUseOffsetFallback(PatchChange change, byte[] data)
        {
            if (!change.HasOffsetFallback) return;
            var len = change.IsInsert ? HexToBytes(change.Patched).Length : HexToBytes(change.Original).Length;
            if (data != null && (change.OffsetFallback < 0 || change.OffsetFallback + len > data.Length)) return;
            change.Offset = change.OffsetFallback;
            change.IsResolvedBytes = true;
            change.ResolveError = "Used absolute offset fallback.";
        }

        private static bool TryResolveKnownField(PatchChange change, FieldEntry entry, out int offset, out int length, out byte[] patched)
        {
            offset = -1;
            length = 0;
            patched = null;

            if (TryResolveRawFieldSlice(change, entry, out offset, out length, out patched))
            {
                return true;
            }

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

        private static bool TryResolveRawFieldSlice(PatchChange change, FieldEntry entry, out int offset, out int length, out byte[] patched)
        {
            offset = -1;
            length = 0;
            patched = null;

            if (string.IsNullOrWhiteSpace(change.OldValue) || string.IsNullOrWhiteSpace(change.NewValue)) return false;
            if (!LooksLikeHex(change.OldValue) || !LooksLikeHex(change.NewValue)) return false;

            var oldBytes = HexToBytes(change.OldValue);
            var newBytes = HexToBytes(change.NewValue);
            if (oldBytes.Length == 0 || oldBytes.Length != newBytes.Length) return false;

            var source = change.ResolveSourceBytes;
            if (source == null || source.Length == 0) return false;

            var absoluteHints = change.AbsoluteOffsetHints;
            if (absoluteHints != null && absoluteHints.Count > 0)
            {
                foreach (var hint in absoluteHints)
                {
                    if (hint < 0 || hint + oldBytes.Length > source.Length) continue;
                    bool hintMatches = true;
                    for (int i = 0; i < oldBytes.Length; i++)
                    {
                        if (source[hint + i] != oldBytes[i]) { hintMatches = false; break; }
                    }
                    if (!hintMatches) continue;
                    offset = hint;
                    length = oldBytes.Length;
                    patched = newBytes;
                    return true;
                }
                change.ResolveError = "Raw field bytes did not match any explicit offset hint for '" + change.EntryName + "'.";
                return false;
            }

            if (entry == null) return false;

            var matches = new List<int>();
            var start = entry.BlobStart;
            var end = entry.BlobStart + entry.BlobSize - oldBytes.Length;
            if (start < 0 || end < start) return false;

            for (int pos = start; pos <= end; pos++)
            {
                bool same = true;
                for (int i = 0; i < oldBytes.Length; i++)
                {
                    if (source[pos + i] != oldBytes[i]) { same = false; break; }
                }
                if (same) matches.Add(pos);
            }

            if (matches.Count != 1)
            {
                change.ResolveError = matches.Count == 0
                    ? "Raw field bytes were not found inside entry '" + change.EntryName + "'."
                    : "Raw field bytes matched " + matches.Count + " places inside entry '" + change.EntryName + "'.";
                return false;
            }

            offset = matches[0];
            length = oldBytes.Length;
            patched = newBytes;
            return true;
        }

        private static bool LooksLikeHex(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = Regex.Replace(value, @"\s+", "");
            return value.Length > 0 && value.Length % 2 == 0 && Regex.IsMatch(value, @"\A[0-9a-fA-F]+\z");
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

    }
}
