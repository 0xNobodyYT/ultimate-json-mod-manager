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
            var applyCacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".cache", "apply_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(applyCacheDir);
            ResetFieldResolutions(selected);
            ResolveFieldFormatMods(selected, applyCacheDir);
            ResolveEntryAnchoredPatches(selected, applyCacheDir);
            int overlayInstalled = ApplyOverlayPackages(selected);

            // Group changes by gameFile across all active mods (using each mod's selected preset).
            // Skip patches the user has unchecked in the patch list (disabledPatches).
            var byGameFile = new Dictionary<string, List<PatchChange>>(StringComparer.OrdinalIgnoreCase);
            int skippedDisabled = 0;
            int skippedUnresolved = 0;
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
                    if (!c.IsResolvedBytes)
                    {
                        skippedUnresolved++;
                        var reason = string.IsNullOrWhiteSpace(c.ResolveError) ? "" : " (" + c.ResolveError + ")";
                        Log("Skipped unresolved patch [" + mod.Name + "]: " + c.Label + reason);
                        continue;
                    }
                    if (disabledPatches.Contains(key)) { skippedDisabled++; continue; }
                    if (!byGameFile.ContainsKey(c.GameFile)) byGameFile[c.GameFile] = new List<PatchChange>();
                    byGameFile[c.GameFile].Add(c);
                }
            }
            if (skippedUnresolved > 0) Log("Skipped " + skippedUnresolved + " unresolved patch(es).");
            if (skippedDisabled > 0) Log("Skipped " + skippedDisabled + " patch(es) disabled in the Patch Board.");
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

                            // Apply patches. Replacements are guard-checked in-place; inserts are
                            // queued and materialized from high offset to low offset so earlier
                            // offsets remain stable.
                            int applied = 0, mismatch = 0;
                            var inserts = new List<Tuple<int, byte[], string>>();
                            foreach (var c in w.Changes)
                            {
                                var orig = HexToBytes(c.Original);
                                var patched = HexToBytes(c.Patched);
                                if (c.IsInsert)
                                {
                                    if ((w.Entry.Path ?? "").EndsWith(".pabgb", StringComparison.OrdinalIgnoreCase))
                                    {
                                        mismatch++;
                                        Log("Skipped insert in " + w.Entry.Path + " because entry-table inserts require companion .pabgh offset rewrites.");
                                        continue;
                                    }
                                    if (patched.Length == 0)
                                    {
                                        mismatch++;
                                        Log("Empty insert: " + c.Label);
                                        continue;
                                    }
                                    if (c.Offset < 0 || c.Offset > decompressed.Length || (orig.Length > 0 && c.Offset + orig.Length > decompressed.Length))
                                    {
                                        mismatch++;
                                        Log("Out of range: " + c.Label);
                                        continue;
                                    }
                                    if (orig.Length > 0)
                                    {
                                        bool insertGuardMatches = true;
                                        for (int i = 0; i < orig.Length; i++)
                                        {
                                            if (decompressed[c.Offset + i] != orig[i]) { insertGuardMatches = false; break; }
                                        }
                                        if (!insertGuardMatches)
                                        {
                                            mismatch++;
                                            Log("Byte mismatch: " + c.Label);
                                            continue;
                                        }
                                    }
                                    inserts.Add(Tuple.Create(c.Offset, patched, c.Label));
                                    applied++;
                                    continue;
                                }

                                if (c.Offset < 0 || c.Offset + orig.Length > decompressed.Length || c.Offset + patched.Length > decompressed.Length)
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
                            foreach (var insert in inserts.OrderByDescending(i => i.Item1))
                            {
                                var next = new byte[decompressed.Length + insert.Item2.Length];
                                Buffer.BlockCopy(decompressed, 0, next, 0, insert.Item1);
                                Buffer.BlockCopy(insert.Item2, 0, next, insert.Item1, insert.Item2.Length);
                                Buffer.BlockCopy(decompressed, insert.Item1, next, insert.Item1 + insert.Item2.Length, decompressed.Length - insert.Item1);
                                decompressed = next;
                                Log("Inserted " + insert.Item2.Length + " byte(s): " + insert.Item3);
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
                    if (pazGroup.Any(w => w.Succeeded))
                    {
                        // Pad paz file end to 16-byte alignment so the recorded size matches engine expectations.
                        paz.Seek(0, SeekOrigin.End);
                        long finalEnd = paz.Position;
                        int finalPad = (int)((16 - (finalEnd % 16)) % 16);
                        if (finalPad > 0)
                        {
                            paz.Write(new byte[finalPad], 0, finalPad);
                            Log("Padded " + Path.GetFileName(pazFile) + " end with " + finalPad + " bytes for 16-byte alignment.");
                        }
                    }
                    paz.Flush();
                }
            }

            if (totalApplied == 0)
            {
                WriteRestoreGuardManifest("apply-no-byte-patches");
                MessageBox.Show(
                    "No byte patches were applied.\r\n\r\n" +
                    (totalMismatch > 0 ? totalMismatch + " patch(es) failed byte checks. See the log for details.\r\n" : "") +
                    (fileSkipped > 0 ? fileSkipped + " file(s) were skipped.\r\n" : "") +
                    "UJMM did not rewrite the PAMT/PAPGT metadata for byte patches.",
                    "Nothing applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
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

    }
}
