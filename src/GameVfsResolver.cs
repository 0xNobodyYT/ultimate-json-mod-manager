using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CdJsonModManager
{
    internal sealed class GameVfsMatch
    {
        public PazEntry Entry;
        public string GroupName;
        public bool Corrected;
        public string RequestedPath;
    }

    internal sealed class GameVfsResolver
    {
        private readonly string gameRoot;
        private readonly Dictionary<string, PamtParseResult> pamtCache = new Dictionary<string, PamtParseResult>(StringComparer.OrdinalIgnoreCase);
        private List<string> groupNames;
        private List<GameVfsMatch> allEntries;

        public GameVfsResolver(string gameRoot)
        {
            this.gameRoot = gameRoot;
        }

        public GameVfsMatch Resolve(string requestedPath, string preferredGroupName = null)
        {
            var requested = Normalize(requestedPath);
            if (string.IsNullOrWhiteSpace(requested)) return null;

            if (!string.IsNullOrWhiteSpace(preferredGroupName))
            {
                var preferred = ResolveInGroup(requested, preferredGroupName, true);
                if (preferred != null) return preferred;
            }

            var exact = AllEntries()
                .Where(m => string.Equals(Normalize(m.Entry.Path), requested, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.GroupName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (exact != null)
            {
                exact.Corrected = false;
                exact.RequestedPath = requestedPath;
                return exact;
            }

            var simplified = SimplifiedKey(requested);
            var simplifiedMatches = AllEntries()
                .Where(m => string.Equals(SimplifiedKey(m.Entry.Path), simplified, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var uniqueSimplified = UniqueByPath(simplifiedMatches);
            if (uniqueSimplified != null) return Corrected(uniqueSimplified, requestedPath);

            var suffixMatches = AllEntries()
                .Where(m => Normalize(m.Entry.Path).EndsWith("/" + requested, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var uniqueSuffix = UniqueByPath(suffixMatches);
            if (uniqueSuffix != null) return Corrected(uniqueSuffix, requestedPath);

            if (!string.IsNullOrWhiteSpace(preferredGroupName))
            {
                var preferredByName = ResolveInGroup(requested, preferredGroupName, false);
                if (preferredByName != null) return preferredByName;
            }

            var fileName = Path.GetFileName(requested);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var nameMatches = AllEntries()
                    .Where(m => string.Equals(Path.GetFileName(m.Entry.Path), fileName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var uniqueName = UniqueByPath(nameMatches);
                if (uniqueName != null) return Corrected(uniqueName, requestedPath);
            }

            return null;
        }

        private GameVfsMatch ResolveInGroup(string requested, string groupName, bool exactOnly)
        {
            var pamt = GetPamt(groupName);
            if (pamt == null) return null;
            var groupEntries = pamt.Entries
                .Select(entry => new GameVfsMatch { Entry = entry, GroupName = groupName })
                .ToList();

            var exact = groupEntries.FirstOrDefault(m => string.Equals(Normalize(m.Entry.Path), requested, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            if (exactOnly) return null;

            var fileName = Path.GetFileName(requested);
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            var nameMatches = groupEntries
                .Where(m => string.Equals(Path.GetFileName(m.Entry.Path), fileName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var uniqueName = UniqueByPath(nameMatches);
            return uniqueName != null ? Corrected(uniqueName, requested) : null;
        }

        private IEnumerable<GameVfsMatch> AllEntries()
        {
            if (allEntries != null) return allEntries;
            allEntries = new List<GameVfsMatch>();
            foreach (var groupName in GetGroupNames())
            {
                var pamt = GetPamt(groupName);
                if (pamt == null) continue;
                allEntries.AddRange(pamt.Entries.Select(entry => new GameVfsMatch { Entry = entry, GroupName = groupName }));
            }
            return allEntries;
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

        private static GameVfsMatch Corrected(GameVfsMatch match, string requestedPath)
        {
            return new GameVfsMatch
            {
                Entry = match.Entry,
                GroupName = match.GroupName,
                Corrected = true,
                RequestedPath = requestedPath
            };
        }

        private static GameVfsMatch UniqueByPath(List<GameVfsMatch> matches)
        {
            var uniquePaths = matches
                .GroupBy(m => Normalize(m.Entry.Path), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(m => m.GroupName, StringComparer.OrdinalIgnoreCase).First())
                .ToList();
            return uniquePaths.Count == 1 ? uniquePaths[0] : null;
        }

        private static string SimplifiedKey(string path)
        {
            var normalized = Normalize(path);
            var slash = normalized.IndexOf('/');
            var fileName = Path.GetFileName(normalized);
            if (slash <= 0 || string.IsNullOrWhiteSpace(fileName)) return normalized;
            return normalized.Substring(0, slash) + "/" + fileName;
        }

        public static string Normalize(string path)
        {
            var normalized = (path ?? "").Replace('\\', '/').TrimStart('/');
            if (normalized.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("files/".Length);
            var groupWrapper = Regex.Match(normalized, @"^\d{4}/(.+)$");
            if (groupWrapper.Success) normalized = groupWrapper.Groups[1].Value;
            return normalized;
        }
    }
}
