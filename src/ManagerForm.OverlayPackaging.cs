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
using System.Xml.XPath;

namespace CdJsonModManager
{
    internal sealed partial class ManagerForm
    {
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
                foreach (var sourceFolder in OverlayFoldersForGroup(mod, GroupFor(mod)))
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
            public ushort EntryFlags;
            public string SourceArchiveGroup;
        }

        private sealed class OverlayOriginal
        {
            public PazEntry Entry;
            public byte[] Data;
            public string GroupName;
        }

        private sealed class OverlayOriginalIndex
        {
            private readonly string gameRoot;
            private readonly string preferredGroupName;
            private readonly Dictionary<string, PamtParseResult> pamtCache = new Dictionary<string, PamtParseResult>(StringComparer.OrdinalIgnoreCase);
            private List<string> groupNames;

            public OverlayOriginalIndex(string gameRoot, string preferredGroupName)
            {
                this.gameRoot = gameRoot;
                this.preferredGroupName = preferredGroupName ?? "";
            }

            public OverlayOriginal Find(string relativePath)
            {
                if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(relativePath)) return null;
                if (!string.IsNullOrWhiteSpace(preferredGroupName))
                {
                    var found = TryExtractFromGroup(preferredGroupName, relativePath);
                    if (found != null) return found;
                }

                foreach (var groupName in GetGroupNames())
                {
                    if (string.Equals(groupName, preferredGroupName, StringComparison.OrdinalIgnoreCase)) continue;
                    var found = TryExtractFromGroup(groupName, relativePath);
                    if (found != null) return found;
                }
                return null;
            }

            private IEnumerable<string> GetGroupNames()
            {
                if (groupNames != null) return groupNames;
                try
                {
                    groupNames = Directory.GetDirectories(gameRoot)
                        .Select(Path.GetFileName)
                        .Where(name => Regex.IsMatch(name ?? "", @"^\d{4}$"))
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch
                {
                    groupNames = new List<string>();
                }
                return groupNames;
            }

            private PamtParseResult GetPamt(string groupName)
            {
                PamtParseResult pamt;
                if (pamtCache.TryGetValue(groupName, out pamt)) return pamt;
                try
                {
                    var groupDir = Path.Combine(gameRoot, groupName);
                    var pamtPath = Path.Combine(groupDir, "0.pamt");
                    if (!File.Exists(pamtPath)) return null;
                    pamt = ArchiveExtractor.ParsePamtFull(pamtPath, groupDir);
                    pamtCache[groupName] = pamt;
                    return pamt;
                }
                catch
                {
                    pamtCache[groupName] = null;
                    return null;
                }
            }

            private OverlayOriginal TryExtractFromGroup(string groupName, string relativePath)
            {
                try
                {
                    var pamt = GetPamt(groupName);
                    if (pamt == null) return null;
                    var normalized = relativePath.Replace('\\', '/');
                    var entry = pamt.Entries.FirstOrDefault(e => string.Equals((e.Path ?? "").Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
                    if (entry == null || !File.Exists(entry.PazFile)) return null;
                    using (var fs = File.OpenRead(entry.PazFile))
                    {
                        fs.Seek(entry.Offset, SeekOrigin.Begin);
                        var blob = new byte[entry.CompSize];
                        var got = fs.Read(blob, 0, blob.Length);
                        if (got != blob.Length) return null;
                        if (entry.EncryptionType == 3) blob = ArchiveExtractor.CryptChaCha20ByFileName(blob, Path.GetFileName(entry.Path));
                        if (entry.CompressionType == 2) return new OverlayOriginal { Entry = entry, Data = ArchiveExtractor.Lz4BlockDecompressPublic(blob, entry.OrigSize), GroupName = groupName };
                        if (entry.CompressionType == 0 || entry.CompSize == entry.OrigSize) return new OverlayOriginal { Entry = entry, Data = blob, GroupName = groupName };
                        return null;
                    }
                }
                catch
                {
                    return null;
                }
            }
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
            var originalIndex = new OverlayOriginalIndex(gameRoot, PreferredOverlaySourceGroup(sourceFolder));
            foreach (var file in files)
            {
                var original = originalIndex.Find(file.RelativePath);
                var raw = BuildLooseOverlayFileBytes(sourceFolder, file.SourcePath, file.RelativePath, gameRoot, original, log);
                file.OriginalSize = checked((uint)raw.Length);
                file.PackedBytes = ArchiveExtractor.Lz4BlockCompress(raw);
                file.EntryFlags = original != null ? (ushort)(original.Entry.Flags >> 16) : (ushort)0x0002;
                file.SourceArchiveGroup = original != null ? original.GroupName : PreferredOverlaySourceGroup(sourceFolder);
                if (((file.EntryFlags >> 4) & 0x0F) == 3)
                {
                    file.PackedBytes = ArchiveExtractor.CryptChaCha20ByFileName(file.PackedBytes, file.FileName);
                    if (log != null) log("Overlay " + file.RelativePath + ": encrypted ChaCha20 using matching game flags 0x" + file.EntryFlags.ToString("X4") + ".");
                }
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
            var rel = (relativePath ?? "").Replace('\\', '/').TrimStart('/');
            if (rel.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring("files/".Length);
            var filesGroup = Regex.Match(rel, @"^\d{4}/(.+)$");
            if (filesGroup.Success) rel = filesGroup.Groups[1].Value;
            if (rel.EndsWith(".merge", StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring(0, rel.Length - ".merge".Length);
            if (rel.EndsWith(".patch", StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring(0, rel.Length - ".patch".Length);
            return rel;
        }

        private static byte[] BuildLooseOverlayFileBytes(string sourceFolder, string sourcePath, string relativePath, string gameRoot, OverlayOriginal original, Action<string> log)
        {
            var ext = Path.GetExtension(sourcePath);
            if (!string.Equals(ext, ".merge", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ext, ".patch", StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadAllBytes(sourcePath);
            }

            if (original == null)
            {
                if (log != null) log("Overlay patch source not found in game archives for " + relativePath + "; packing patch payload only.");
                return File.ReadAllBytes(sourcePath);
            }

            var text = DecodeText(original.Data);
            var patchText = File.ReadAllText(sourcePath, Encoding.UTF8);
            if (relativePath.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                var css = ApplyCssMerge(text, patchText, log, relativePath);
                return new UTF8Encoding(false).GetBytes(css);
            }
            if (string.Equals(ext, ".merge", StringComparison.OrdinalIgnoreCase))
            {
                if (relativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                    || relativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    var mergedXml = ApplyXmlMergeDocument(original.Data, patchText, log, relativePath);
                    if (mergedXml != null) return mergedXml;
                }
                var merged = text.TrimEnd() + "\r\n\r\n" + patchText.Trim() + "\r\n";
                return new UTF8Encoding(false).GetBytes(merged);
            }

            var patchedXml = ApplyXmlPatchDocument(original.Data, patchText, log, relativePath);
            if (patchedXml != null) return patchedXml;

            var patched = ApplySimpleXmlPatch(text, patchText, log, relativePath);
            return new UTF8Encoding(false).GetBytes(patched);
        }

        private sealed class CssRule
        {
            public string Selector;
            public string Body;
            public int Start;
            public int End;
        }

        private static string ApplyCssMerge(string originalText, string mergeText, Action<string> log, string relativePath)
        {
            var originalRules = ParseCssRules(originalText);
            var mergeRules = ParseCssRules(mergeText);
            if (mergeRules.Count == 0)
            {
                if (log != null) log("CSS merge had no rule blocks for " + relativePath + "; appending payload.");
                return originalText.TrimEnd() + "\r\n\r\n" + mergeText.Trim() + "\r\n";
            }

            var result = originalText;
            var changed = 0;
            var added = 0;
            foreach (var rule in mergeRules)
            {
                var existing = originalRules.FirstOrDefault(r => string.Equals(NormalizeCssSelector(r.Selector), NormalizeCssSelector(rule.Selector), StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    result = result.TrimEnd() + "\r\n\r\n" + rule.Selector.Trim() + " {\r\n" + rule.Body.Trim() + "\r\n}\r\n";
                    added++;
                    continue;
                }

                var replacement = existing.Selector.Trim() + " {\r\n" + MergeCssBodies(existing.Body, rule.Body) + "\r\n}";
                result = result.Substring(0, existing.Start) + replacement + result.Substring(existing.End);
                var delta = replacement.Length - (existing.End - existing.Start);
                foreach (var later in originalRules.Where(r => r.Start > existing.Start))
                {
                    later.Start += delta;
                    later.End += delta;
                }
                changed++;
            }
            if (log != null) log("CSS merge materialized for " + relativePath + ": " + changed + " merged, " + added + " added.");
            return result;
        }

        private static List<CssRule> ParseCssRules(string css)
        {
            var rules = new List<CssRule>();
            if (string.IsNullOrEmpty(css)) return rules;
            foreach (Match match in Regex.Matches(css, @"(?s)([^{}]+)\{([^{}]*)\}"))
            {
                var selector = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(selector) || selector.StartsWith("@", StringComparison.Ordinal)) continue;
                rules.Add(new CssRule { Selector = selector, Body = match.Groups[2].Value, Start = match.Index, End = match.Index + match.Length });
            }
            return rules;
        }

        private static string MergeCssBodies(string existingBody, string mergeBody)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            Action<string> read = body =>
            {
                foreach (var raw in (body ?? "").Split(';'))
                {
                    var part = raw.Trim();
                    if (part.Length == 0) continue;
                    var colon = part.IndexOf(':');
                    if (colon <= 0) continue;
                    var name = part.Substring(0, colon).Trim();
                    var value = part.Substring(colon + 1).Trim();
                    if (!props.ContainsKey(name)) order.Add(name);
                    props[name] = value;
                }
            };
            read(existingBody);
            read(mergeBody);
            var sb = new StringBuilder();
            foreach (var name in order)
                sb.Append("  ").Append(name).Append(": ").Append(props[name]).Append(";\r\n");
            return sb.ToString().TrimEnd();
        }

        private static string NormalizeCssSelector(string selector)
        {
            return Regex.Replace(selector ?? "", @"\s+", " ").Trim();
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

        private static string PreferredOverlaySourceGroup(string sourceFolder)
        {
            var name = Path.GetFileName(sourceFolder);
            return Regex.IsMatch(name ?? "", @"^\d{4}$") ? name : "";
        }

        private static OverlayOriginal FindOverlayOriginal(string gameRoot, string preferredGroupName, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || string.IsNullOrWhiteSpace(relativePath)) return null;
            if (!string.IsNullOrWhiteSpace(preferredGroupName))
            {
                var found = TryExtractOverlayOriginalFromGroup(gameRoot, preferredGroupName, relativePath);
                if (found != null) return found;
            }

            try
            {
                foreach (var groupDir in Directory.GetDirectories(gameRoot)
                    .Where(dir => Regex.IsMatch(Path.GetFileName(dir), @"^\d{4}$"))
                    .OrderBy(Path.GetFileName))
                {
                    var found = TryExtractOverlayOriginalFromGroup(gameRoot, Path.GetFileName(groupDir), relativePath);
                    if (found != null) return found;
                }
            }
            catch { }

            return null;
        }

        private static OverlayOriginal TryExtractOverlayOriginalFromGroup(string gameRoot, string groupName, string relativePath)
        {
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
                    if (entry.EncryptionType == 3) blob = ArchiveExtractor.CryptChaCha20ByFileName(blob, Path.GetFileName(entry.Path));
                    if (entry.CompressionType == 2) return new OverlayOriginal { Entry = entry, Data = ArchiveExtractor.Lz4BlockDecompressPublic(blob, entry.OrigSize), GroupName = groupName };
                    if (entry.CompressionType == 0 || entry.CompSize == entry.OrigSize) return new OverlayOriginal { Entry = entry, Data = blob, GroupName = groupName };
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
                bw.Write(file.EntryFlags == 0 ? CompressionLz4 : file.EntryFlags);
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

    }
}
