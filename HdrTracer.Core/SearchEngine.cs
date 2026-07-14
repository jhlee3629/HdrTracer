namespace HdrTracer.Core;

public readonly record struct SearchHit(FileIndex Index, int EntryIndex);

public sealed class SearchEngine
{
    /// <summary>휴지통 안 항목을 결과에서 제외할지 (기본 true).</summary>
    public bool ExcludeRecycleBin { get; set; } = true;

    /// <summary>
    /// 숨김+시스템 속성 항목(예: 알약 미끼 폴더 "!!QAdC")을 검색 결과에서 제외할지 여부.
    /// true면 숨김(탐색기와 동일, 기본), false면 결과에 표시.
    /// </summary>
    public bool HideHiddenSystemItems { get; set; } = true;

    /// <summary>추가 필터 묶음: 제외 단어/확장자, 경로, 크기, 수정 날짜.</summary>
    private sealed class ExtraFilters
    {
        public string[] ExcludeTokens = Array.Empty<string>();
        public HashSet<string> ExcludeExts = new(StringComparer.OrdinalIgnoreCase);
        public string[] PathFilters = Array.Empty<string>();
        public long SizeMin = -1;       // '>' 크기: 이 값 초과
        public long SizeMax = -1;       // '<' 크기: 이 값 미만
        public long DateMinTicks = 0;   // '>' 날짜: 이 시점(UTC Ticks) 이후(포함)
        public long DateMaxTicks = 0;   // '<' 날짜: 이 시점(UTC Ticks) 이전(미포함)

        public bool HasAttribute => SizeMin >= 0 || SizeMax >= 0 || DateMinTicks > 0 || DateMaxTicks > 0;
        public bool Any => ExcludeTokens.Length > 0 || ExcludeExts.Count > 0
                        || PathFilters.Length > 0 || HasAttribute;
    }

    public List<SearchHit> Search(IReadOnlyList<FileIndex> indexes, string query, int maxResults = 1_000_000)
    {
        var results = new List<SearchHit>();
        if (indexes.Count == 0) return results;

        var (textTokens, extFilter, patterns, extra) = ParseQuery(query);

        bool hasText = textTokens.Length > 0;
        bool hasExt  = extFilter.Count > 0;
        bool hasPattern = patterns.Length > 0;

        // 텍스트·확장자·크기/날짜 조건이 하나도 없으면 결과 없음.
        // (제외·경로 필터만으로는 검색하지 않음 — 전체 경로 계산 비용 방지.
        //  크기/날짜는 숫자 비교라 싸므로 단독 검색 허용: 예) ">1GB")
        if (!hasText && !hasExt && !hasPattern && !extra.HasAttribute) return results;

        bool excludeRecycle = ExcludeRecycleBin;
        bool hideHiddenSystem = HideHiddenSystemItems;

        var partials = new List<SearchHit>[indexes.Count];

        Parallel.For(0, indexes.Count, i =>
        {
            partials[i] = SearchOneIndex(indexes[i], textTokens, extFilter, patterns, extra,
                excludeRecycle, hideHiddenSystem);
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

    /// <summary>
    /// 검색어를 분해한다.
    /// "보고서 *.pdf"        → 텍스트 ["보고서"], 확장자 {pdf}
    /// "보고서 -임시"        → 제외 단어 ["임시"]
    /// "보고서 -*.tmp"       → 제외 확장자 {tmp}
    /// "사진 D:\백업\"       → 경로 필터 (그 경로 아래만) / "\여행\" → 경로에 포함
    /// "*.mp4 >500MB"        → 크기 조건 (단위 필수: B KB MB GB TB)
    /// "사진 >2026-01"       → 날짜 조건 (연-월-일 부분 표기, 연도 선행이면 . / 구분도 허용)
    /// "보고서 >week"        → 상대 날짜 (today / week / month / year)
    /// </summary>
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
                // 크기/날짜 조건: > 또는 < 로 시작
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
                    // 해석 불가(예: ">abc") → 일반 텍스트 토큰으로 취급
                    textParts.Add(token.ToLowerInvariant());
                    continue;
                }

                // 제외: -단어 / -*.ext / -.ext
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

                // 경로 필터: '\' 를 포함하면 경로로 취급 ('/'도 허용)
                if (token.Contains('\\') || token.Contains('/'))
                {
                    pathParts.Add(token.Replace('/', '\\'));
                    continue;
                }

                bool hasWild = token.IndexOf('*') >= 0 || token.IndexOf('?') >= 0;

                if (token.StartsWith("*.") && token.Length > 2
                    && token.IndexOf('*', 2) < 0 && token.IndexOf('?', 2) < 0)
                    exts.Add(token.Substring(2));                 // 확장자 문법 (*.jpg)
                else if (token.StartsWith(".") && token.Length > 1 && !hasWild)
                    exts.Add(token.Substring(1));
                else if (hasWild)
                    patternParts.Add(token);                      // 이름 와일드카드 (IMG_*_편집)
                else
                    textParts.Add(token.ToLowerInvariant());
            }
        }

        extra.ExcludeTokens = excludeParts.ToArray();
        extra.PathFilters   = pathParts.ToArray();
        return (textParts.ToArray(), exts, patternParts.ToArray(), extra);
    }

    /// <summary>공백 분리하되 "큰따옴표" 묶음은 하나의 토큰으로(따옴표 제거). 공백 포함 경로용.</summary>
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

    /// <summary>"500KB", "1.5GB" 형태를 바이트로. 단위(B/KB/MB/GB/TB) 필수. 실패 시 false.</summary>
    private static bool TryParseSize(string s, out long bytes)
    {
        bytes = 0;
        int i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (i == 0 || i == s.Length) return false;   // 숫자 없음 또는 단위 없음

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

    /// <summary>
    /// 날짜 토큰을 "그 기간의 시작 시점"(UTC Ticks)으로 해석.
    /// 허용: 2026 / 2026-01 / 2026-01-15 (연도 선행이면 구분자 . / 도 허용: 2026.1.15, 2026/01/15)
    /// 상대: today(오늘), week(최근 7일), month(최근 30일), year(최근 1년)
    /// </summary>
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

    /// <summary>파일명이 확장자 필터에 맞는지. exts가 비면 항상 통과.</summary>
    private static bool MatchesExtension(ReadOnlySpan<char> name, HashSet<string> exts)
    {
        if (exts.Count == 0) return true;
        int dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1) return false;
        return exts.Contains(name.Slice(dot + 1).ToString());
    }

    /// <summary>
    /// 추가 필터 통과 여부. 싼 검사(크기/날짜 숫자 비교) → 이름 검사 → 비싼 경로 계산 순.
    /// 다른 필터를 모두 통과한 항목에만 마지막으로 적용할 것.
    /// </summary>
    private static bool PassesExtraFilters(FileIndex index, int entryIndex, ReadOnlySpan<char> name, ExtraFilters f)
    {
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

    /// <summary>
    /// 단일 인덱스에 대해 검색.
    /// 텍스트 토큰이 있으면 N-gram 후보 우선, 없으면(확장자/크기/날짜만) 전체 선형 스캔.
    /// </summary>
    private static List<SearchHit> SearchOneIndex(
        FileIndex index, string[] tokens, HashSet<string> extFilter, string[] patterns, ExtraFilters extra,
        bool excludeRecycle, bool hideHiddenSystem)
    {
        var local = new List<SearchHit>(1024);
        int count = index.Count;
        bool hasText = tokens.Length > 0;
        bool hasExt  = extFilter.Count > 0;
        bool hasPattern = patterns.Length > 0;
        bool hasExtra = extra.Any;

        if (!hasText && !hasPattern)
        {
            // === 확장자/크기/날짜만: 전체 선형 스캔 (파일만) ===
            for (int j = 0; j < count; j++)
            {
                if (index.IsDeleted(j)) continue;
                if (index.IsDirectory(j)) continue;   // 파일 속성 필터는 파일 대상

                var name = index.GetNameSpan(j);
                if (hasExt && !MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                local.Add(new SearchHit(index, j));
            }
            return local;
        }

        // === 텍스트 검색 (+ 선택적 필터) ===
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
                    if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                    local.Add(new SearchHit(index, j));
                }
            }

            // ngram 빌드 이후 추가된 영역은 항상 선형 검사
            for (int j = ngramBuiltCount; j < count; j++)
            {
                if (index.IsDeleted(j)) continue;

                var name = index.GetNameSpan(j);
                if (!MatchesAll(name, tokens)) continue;
                if (hasPattern && !MatchesAllPatterns(name, patterns)) continue;
                if (hasExt && !MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                local.Add(new SearchHit(index, j));
            }
        }
        else
        {
            // 폴백: 전체 선형 스캔
            for (int j = 0; j < count; j++)
            {
                if (index.IsDeleted(j)) continue;

                var name = index.GetNameSpan(j);
                if (!MatchesAll(name, tokens)) continue;
                if (hasPattern && !MatchesAllPatterns(name, patterns)) continue;
                if (hasExt && !MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                if (hasExtra && !PassesExtraFilters(index, j, name, extra)) continue;
                local.Add(new SearchHit(index, j));
            }
        }

        return local;
    }

    /// <summary>
    /// N-gram 인덱스를 사용해 후보 엔트리 인덱스 배열을 만든다.
    /// 모든 토큰이 ngram 사용 가능해야 의미 있음. 하나라도 못 쓰면 null 반환 (폴백).
    /// </summary>
    private static int[]? TryGetCandidatesViaNgram(FileIndex index, string[] tokens, string[] patterns)
    {
        var ngram = index.Ngram;
        if (ngram is null) return null;

        var arrays = new List<int[]>(tokens.Length + patterns.Length);

        // 텍스트 토큰: 전부 ngram 사용 가능해야 (기존 동작 유지)
        foreach (var token in tokens)
        {
            var c = ngram.GetCandidates(token);
            if (c is null)
                return null;
            if (c.Length == 0)
                return Array.Empty<int>();
            arrays.Add(c);
        }

        // 패턴: * ? 로 쪼갠 글자 조각 중 ngram이 쓸 수 있는 것만 "선택적으로" 후보 축소에 활용.
        // (조각이 짧아 ngram이 못 쓰면 건너뜀 — 그 경우 아래 폴백/선형 검사로 걸러짐)
        foreach (var pat in patterns)
        {
            foreach (var frag in pat.Split('*', '?'))
            {
                if (frag.Length == 0) continue;
                var c = ngram.GetCandidates(frag);
                if (c is null) continue;
                if (c.Length == 0)
                    return Array.Empty<int>();   // 필수 조각이 아무 이름에도 없음 → 결과 없음
                arrays.Add(c);
            }
        }

        if (arrays.Count == 0) return null;      // 좁힐 수단 없음 → 선형 폴백
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

    /// <summary>
    /// 와일드카드 전체 일치: 이름 전체가 패턴 모양과 맞아야 한다 (대소문자 무시).
    /// '*' = 아무 글자들(0개 이상), '?' = 아무 글자 하나.
    /// 예) "IMG_*_편집" 은 IMG_0234_편집 은 맞고 복사_IMG_1_편집 은 안 맞음.
    /// </summary>
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
                starP = p++;      // 별 위치 기억
                starN = n;
            }
            else if (starP >= 0)
            {
                p = starP + 1;    // 별이 글자 하나를 더 삼키게 되돌아감
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

    private static bool IsInRecycleBin(FileIndex index, int entryIndex)
    {
        // 1) 항목 자체가 휴지통 메타파일 패턴($R로 시작하는 8글자 + 확장자)
        //    예: $RHS7QYY.txt, $IHS7QYY.txt
        //    휴지통이 비워진 후에도 NTFS MFT에 유령 엔트리로 남는 경우가 있어 제외한다.
        var selfName = index.GetNameSpan(entryIndex);
        if (IsRecycleMetaName(selfName)) return true;

        // 2) 상위 폴더 중 $Recycle.Bin이 있으면 휴지통 안의 파일
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

    /// <summary>
    /// 휴지통 메타파일 이름인지 검사: $R 또는 $I 로 시작하고 그 뒤가 6글자 영숫자,
    /// 그 다음은 끝나거나 '.' (확장자).
    /// 예: $RHS7QYY, $RHS7QYY.txt, $IHS7QYY.pdf
    /// </summary>
    private static bool IsRecycleMetaName(ReadOnlySpan<char> name)
    {
        if (name.Length < 8) return false;
        if (name[0] != '$') return false;
        if (name[1] != 'R' && name[1] != 'I') return false;

        // 인덱스 2~7: 영숫자 6개
        for (int i = 2; i < 8; i++)
        {
            char c = name[i];
            bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            if (!ok) return false;
        }

        // 인덱스 8: 없거나 '.' 이어야 함 (그 외엔 휴지통 메타가 아닐 가능성)
        if (name.Length == 8) return true;
        return name[8] == '.';
    }
}
