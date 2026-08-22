namespace HdrTracer.Core;

public readonly record struct SearchHit(FileIndex Index, int EntryIndex);

public sealed class SearchEngine
{
    public bool ExcludeRecycleBin { get; set; } = true;

    public bool HideHiddenSystemItems { get; set; } = true;

    public string[] ExcludedFolderNames { get; set; } = Array.Empty<string>();

    private sealed class ExtraFilters
    {
        public string[] ExcludeTokens = Array.Empty<string>();
        public HashSet<string> ExcludeExts = new(StringComparer.OrdinalIgnoreCase);
        public string[] PathFilters = Array.Empty<string>();
        public long SizeMin = -1;       
        public long SizeMax = -1;       
        public long DateMinTicks = 0;   
        public long DateMaxTicks = 0;   
        public int Kind = 0;            

        public bool HasAttribute => SizeMin >= 0 || SizeMax >= 0 || DateMinTicks > 0 || DateMaxTicks > 0;
        public bool Any => ExcludeTokens.Length > 0 || ExcludeExts.Count > 0
                        || PathFilters.Length > 0 || HasAttribute || Kind != 0;
    }

    public List<SearchHit> Search(IReadOnlyList<FileIndex> indexes, string query, int maxResults = 1_000_000)
    {
        var results = new List<SearchHit>();
        if (indexes.Count == 0) return results;

        var (textTokens, extFilter, patterns, extra) = ParseQuery(query);

        bool hasText = textTokens.Length > 0;
        bool hasExt  = extFilter.Count > 0;
        bool hasPattern = patterns.Length > 0;

        if (!hasText && !hasExt && !hasPattern && !extra.HasAttribute) return results;

        bool excludeRecycle = ExcludeRecycleBin;
        bool hideHiddenSystem = HideHiddenSystemItems;
        var excludedNames = ExcludedFolderNames;

        var partials = new List<SearchHit>[indexes.Count];

        Parallel.For(0, indexes.Count, i =>
        {
            partials[i] = SearchOneIndex(indexes[i], textTokens, extFilter, patterns, extra,
                excludedNames, excludeRecycle, hideHiddenSystem);
        });

        int total = 0;
        foreach (var p in partials) total += p.Count;

        results.Capacity = Math.Min(total, maxResults);
        foreach (var p in partials)
        {
            if (results.Count + p.Count > maxResults)
            {
                int room = maxResults - results.Count;
                if (room > 0) results.AddRange(p.Take(room));
                break;
            }
            results.AddRange(p);
        }

        return results;
    }

    private static (string[] textTokens, HashSet<string> exts, string[] patterns, ExtraFilters extra) ParseQuery(string raw)
    {
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textParts    = new List<string>();
        var patternParts = new List<string>();
        var excludeParts = new List<string>();
        var pathParts    = new List<string>();
        var extra = new ExtraFilters();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (var token in Tokenize(raw))
            {
                if (token.Length > 1 && (token[0] == '>' || token[0] == '<'))
                {
                    bool greater = token[0] == '>';
                    string rest = token.Substring(1);

                    if (TryParseSize(rest, out long bytes))
                    {
                        if (greater) extra.SizeMin = Math.Max(extra.SizeMin, bytes);
                        else         extra.SizeMax = extra.SizeMax < 0 ? bytes : Math.Min(extra.SizeMax, bytes);
                        continue;
                    }
                    if (TryParseDateStartUtc(rest, out long ticks))
                    {
                        if (greater) extra.DateMinTicks = Math.Max(extra.DateMinTicks, ticks);
                        else         extra.DateMaxTicks = extra.DateMaxTicks == 0 ? ticks : Math.Min(extra.DateMaxTicks, ticks);
                        continue;
                    }
                    
                    textParts.Add(token.ToLowerInvariant());
                    continue;
                }

                if (token.Length > 1 && token[0] == '-')
                {
                    string rest = token.Substring(1);
                    if (rest.StartsWith("*.") && rest.Length > 2)
                        extra.ExcludeExts.Add(rest.Substring(2));
                    else if (rest.StartsWith(".") && rest.Length > 1 && !rest.Contains('\\'))
                        extra.ExcludeExts.Add(rest.Substring(1));
                    else
                        excludeParts.Add(rest.ToLowerInvariant());
                    continue;
                }

                if (token.Equals("folder:", StringComparison.OrdinalIgnoreCase)
                    || token.Equals("dir:", StringComparison.OrdinalIgnoreCase))
                {
                    extra.Kind = 1;
                    continue;
                }
                if (token.Equals("file:", StringComparison.OrdinalIgnoreCase))
                {
                    extra.Kind = 2;
                    continue;
                }

                if (token.Contains('\\') || token.Contains('/'))
                {
                    pathParts.Add(token.Replace('/', '\\'));
                    continue;
                }

                bool hasWild = token.IndexOf('*') >= 0 || token.IndexOf('?') >= 0;

                if (token.StartsWith("*.") && token.Length > 2
                    && token.IndexOf('*', 2) < 0 && token.IndexOf('?', 2) < 0)
                    exts.Add(token.Substring(2)); 
                else if (token.StartsWith(".") && token.Length > 1 && !hasWild)
                    exts.Add(token.Substring(1));
                else if (hasWild)
                    patternParts.Add(token);      
                else
                    textParts.Add(token.ToLowerInvariant());
            }
        }

        extra.ExcludeTokens = excludeParts.ToArray();
        extra.PathFilters   = pathParts.ToArray();
        return (textParts.ToArray(), exts, patternParts.ToArray(), extra);
    }

    private static List<string> Tokenize(string raw)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        foreach (char c in raw)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (c == ' ' && !inQuote)
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    private static bool TryParseSize(string s, out long bytes)
    {
        bytes = 0;
        int i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (i == 0 || i == s.Length) return false; 

        if (!double.TryParse(s.Substring(0, i), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double num) || num < 0)
            return false;

        double mul = s.Substring(i).Trim().ToUpperInvariant() switch
        {
            "B"  => 1d,
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            "TB" => 1024d * 1024 * 1024 * 1024,
            _    => -1d
        };
        if (mul < 0) return false;

        double v = num * mul;
        if (v > 9.2e18) return false;
        bytes = (long)v;
        return true;
    }

    private static bool TryParseDateStartUtc(string s, out long utcTicks)
    {
        utcTicks = 0;
        string t = s.Trim();
        if (t.Length == 0) return false;

        switch (t.ToLowerInvariant())
        {
            case "today": utcTicks = DateTime.Now.Date.ToUniversalTime().Ticks; return true;
            case "week":  utcTicks = DateTime.Now.Date.AddDays(-7).ToUniversalTime().Ticks; return true;
            case "month": utcTicks = DateTime.Now.Date.AddDays(-30).ToUniversalTime().Ticks; return true;
            case "year":  utcTicks = DateTime.Now.Date.AddYears(-1).ToUniversalTime().Ticks; return true;
        }

        var parts = t.Split('-', '.', '/');
        if (parts.Length > 3) return false;
        if (parts[0].Length != 4 || !int.TryParse(parts[0], out int y) || y < 1970 || y > 2999) return false;

        int m = 1, d = 1;
        if (parts.Length >= 2 && (!int.TryParse(parts[1], out m) || m < 1 || m > 12)) return false;
        if (parts.Length == 3 && (!int.TryParse(parts[2], out d) || d < 1 || d > 31)) return false;

        try
        {
            var local = new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Local);
            utcTicks = local.ToUniversalTime().Ticks;
            return true;
        }
        catch { return false; }
    }

    private static bool MatchesExtension(ReadOnlySpan<char> name, HashSet<string> exts)
    {
        if (exts.Count == 0) return true;
        int dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return false;
        return exts.Contains(name.Slice(dot + 1).ToString());
    }

    private static bool PassesExtraFilters(FileIndex index, int entryIndex, ReadOnlySpan<char> name, ExtraFilters f)
    {
        if (f.Kind != 0)
        {
            bool isDir = index.IsDirectory(entryIndex);
            if (f.Kind == 1 && !isDir) return false;   // folder: → 폴더만
            if (f.Kind == 2 && isDir)  return false;   // file:   → 파일만
        }

        if (f.SizeMin >= 0 || f.SizeMax >= 0)
        {
            long size = index.GetSize(entryIndex);
            if (f.SizeMin >= 0 && size <= f.SizeMin) return false;
            if (f.SizeMax >= 0 && size >= f.SizeMax) return false;
        }

        if (f.DateMinTicks > 0 || f.DateMaxTicks > 0)
        {
            long ticks = index.GetModifiedUtc(entryIndex).Ticks;
            if (f.DateMinTicks > 0 && ticks < f.DateMinTicks) return false;
            if (f.DateMaxTicks > 0 && ticks >= f.DateMaxTicks) return false;
        }

        for (int t = 0; t < f.ExcludeTokens.Length; t++)
        {
            if (name.IndexOf(f.ExcludeTokens[t], StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        if (f.ExcludeExts.Count > 0 && MatchesExtension(name, f.ExcludeExts))
            return false;

        if (f.PathFilters.Length > 0)
        {
            string? path = index.GetFullPath(entryIndex);
            if (path is null) return false;
            foreach (var pf in f.PathFilters)
            {
                bool ok = (pf.Length >= 2 && pf[1] == ':')
                    ? path.StartsWith(pf, StringComparison.OrdinalIgnoreCase)
                    : path.IndexOf(pf, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!ok) return false;
            }
        }

        return true;
    }

    private static List<SearchHit> SearchOneIndex(
        FileIndex index, string[] tokens, HashSet<string> extFilter, string[] patterns, ExtraFilters extra,
        string[] excludedNames, bool excludeRecycle, bool hideHiddenSystem)
    {
        var local = new List<SearchHit>(1024);
        bool hasExcludedDir = excludedNames.Length > 0;
        int count = index.Count;
        bool hasText = tokens.Length > 0;
        bool hasExt  = extFilter.Count > 0;
        bool hasPattern = patterns.Length > 0;
        bool hasExtra = extra.Any;

        if (!hasText && !hasPattern)
        {
            for (int j = 0; j < count; j++)
            {
                if (index.IsDeleted(j)) continue;
                if (index.IsDirectory(j)) continue; 

                var name = index.GetNameSpan(j);
                if (hasExt && !MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                if (hasExcludedDir && IsInExcludedFolder(index, j, excludedNames)) continue;
                if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                local.Add(new SearchHit(index, j));
            }
            return local;
        }

        int[]? candidates = TryGetCandidatesViaNgram(index, tokens, patterns);

        if (candidates is not null)
        {
            bool skipMatchCheck = (tokens.Length == 1 && !hasPattern);
            int ngramBuiltCount = index.NgramBuiltAtCount;

            if (skipMatchCheck)
            {
                foreach (int j in candidates)
                {
                    if (j >= count) continue;
                    if (index.IsDeleted(j)) continue;

                    var name = index.GetNameSpan(j);
                    if (hasExt && !MatchesExtension(name, extFilter)) continue;
                    if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                    if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                    if (hasExcludedDir && IsInExcludedFolder(index, j, excludedNames)) continue;
                    if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                    local.Add(new SearchHit(index, j));
                }
            }
            else
            {
                foreach (int j in candidates)
                {
                    if (j >= count) continue;
                    if (index.IsDeleted(j)) continue;

                    var name = index.GetNameSpan(j);
                    if (!MatchesAll(name, tokens)) continue;
                    if (hasPattern && !MatchesAllPatterns(name, patterns)) continue;
                    if (hasExt && !MatchesExtension(name, extFilter)) continue;
                    if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                    if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                    if (hasExcludedDir && IsInExcludedFolder(index, j, excludedNames)) continue;
                    if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                    local.Add(new SearchHit(index, j));
                }
            }

            for (int j = ngramBuiltCount; j < count; j++)
            {
                if (index.IsDeleted(j)) continue;

                var name = index.GetNameSpan(j);
                if (!MatchesAll(name, tokens)) continue;
                if (hasPattern && !MatchesAllPatterns(name, patterns)) continue;
                if (hasExt && !MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                if (hasExcludedDir && IsInExcludedFolder(index, j, excludedNames)) continue;
                if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                local.Add(new SearchHit(index, j));
            }
        }
        else
        {
            for (int j = 0; j < count; j++)
            {
                if (index.IsDeleted(j)) continue;

                var name = index.GetNameSpan(j);
                if (!MatchesAll(name, tokens)) continue;
                if (hasPattern && !MatchesAllPatterns(name, patterns)) continue;
                if (hasExt && !MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                if (hasExcludedDir && IsInExcludedFolder(index, j, excludedNames)) continue;
                if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                local.Add(new SearchHit(index, j));
            }
        }

        return local;
    }

    private static int[]? TryGetCandidatesViaNgram(FileIndex index, string[] tokens, string[] patterns)
    {
        var ngram = index.Ngram;
        if (ngram is null) return null;

        var arrays = new List<int[]>(tokens.Length + patterns.Length);

        foreach (var token in tokens)
        {
            var c = ngram.GetCandidates(token);
            if (c is null)
                return null;
            if (c.Length == 0)
                return Array.Empty<int>();
            arrays.Add(c);
        }

        foreach (var pat in patterns)
        {
            foreach (var frag in pat.Split('*', '?'))
            {
                if (frag.Length == 0) continue;
                var c = ngram.GetCandidates(frag);
                if (c is null) continue;
                if (c.Length == 0)
                    return Array.Empty<int>();   
                arrays.Add(c);
            }
        }

        if (arrays.Count == 0) return null;      
        if (arrays.Count == 1) return arrays[0];

        return IntersectAll(arrays);
    }

    private static int[] IntersectAll(List<int[]> sortedArrays)
    {
        sortedArrays.Sort((a, b) => a.Length.CompareTo(b.Length));
        int[] current = sortedArrays[0];
        for (int k = 1; k < sortedArrays.Count; k++)
        {
            current = IntersectTwo(current, sortedArrays[k]);
            if (current.Length == 0) return current;
        }
        return current;
    }

    private static int[] IntersectTwo(int[] a, int[] b)
    {
        var result = new List<int>(Math.Min(a.Length, b.Length));
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j]) { result.Add(a[i]); i++; j++; }
            else if (a[i] < b[j]) i++;
            else j++;
        }
        return result.ToArray();
    }

    private static bool MatchesAll(ReadOnlySpan<char> name, string[] tokens)
    {
        for (int t = 0; t < tokens.Length; t++)
        {
            if (name.IndexOf(tokens[t], StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }
        return true;
    }

    private static bool MatchesAllPatterns(ReadOnlySpan<char> name, string[] patterns)
    {
        for (int p = 0; p < patterns.Length; p++)
        {
            if (!MatchesPattern(name, patterns[p]))
                return false;
        }
        return true;
    }

    private static bool MatchesPattern(ReadOnlySpan<char> name, string pattern)
    {
        int n = 0, p = 0, starP = -1, starN = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' ||
                char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(name[n])))
            {
                p++; n++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p++; 
                starN = n;
            }
            else if (starP >= 0)
            {
                p = starP + 1; 
                n = ++starN;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static bool IsInExcludedFolder(FileIndex index, int entryIndex, string[] excludedNames)
    {
        int cur = entryIndex;
        for (int depth = 0; depth < 64 && cur >= 0; depth++)
        {
            var name = index.GetNameSpan(cur);
            for (int k = 0; k < excludedNames.Length; k++)
            {
                if (name.Equals(excludedNames[k], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            cur = index.GetParentIndex(cur);
        }
        return false;
    }

    private static bool IsInRecycleBin(FileIndex index, int entryIndex)
    {
        var selfName = index.GetNameSpan(entryIndex);
        if (IsRecycleMetaName(selfName)) return true;

        int cur = entryIndex;
        for (int depth = 0; depth < 64 && cur >= 0; depth++)
        {
            var name = index.GetNameSpan(cur);
            if (IsRecycleBinName(name)) return true;
            cur = index.GetParentIndex(cur);
        }
        return false;
    }

    private static bool IsRecycleBinName(ReadOnlySpan<char> name)
    {
        if (name.Length != 12) return false;
        return name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecycleMetaName(ReadOnlySpan<char> name)
    {
        if (name.Length < 8) return false;
        if (name[0] != '$') return false;
        if (name[1] != 'R' && name[1] != 'I') return false;

        for (int i = 2; i < 8; i++)
        {
            char c = name[i];
            bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            if (!ok) return false;
        }

        if (name.Length == 8) return true;
        return name[8] == '.';
    }
}
