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

    public List<SearchHit> Search(IReadOnlyList<FileIndex> indexes, string query, int maxResults = 1_000_000)
    {
        var results = new List<SearchHit>();
        if (indexes.Count == 0) return results;

        // 쿼리에서 확장자 필터(*.ext / .ext)와 텍스트 토큰을 분리
        var (textTokens, extFilter) = ParseQuery(query);

        bool hasText = textTokens.Length > 0;
        bool hasExt  = extFilter.Count > 0;

        // 둘 다 없으면 결과 없음
        if (!hasText && !hasExt) return results;

        bool excludeRecycle = ExcludeRecycleBin;
        bool hideHiddenSystem = HideHiddenSystemItems;

        var partials = new List<SearchHit>[indexes.Count];

        Parallel.For(0, indexes.Count, i =>
        {
            partials[i] = SearchOneIndex(indexes[i], textTokens, extFilter, excludeRecycle, hideHiddenSystem);
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
    /// 검색어를 텍스트 토큰과 확장자 필터로 분리한다.
    /// "보고서 *.pdf *.docx" → (["보고서"], {pdf, docx})
    /// "*.png"               → ([], {png})
    /// ".txt"                → ([], {txt})
    /// </summary>
    private static (string[] textTokens, HashSet<string> exts) ParseQuery(string raw)
    {
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (var token in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.StartsWith("*.") && token.Length > 2)
                    exts.Add(token.Substring(2));
                else if (token.StartsWith(".") && token.Length > 1 && !token.Contains('\\'))
                    exts.Add(token.Substring(1));
                else
                    textParts.Add(token.ToLowerInvariant());
            }
        }

        return (textParts.ToArray(), exts);
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
    /// 단일 인덱스에 대해 검색.
    /// 텍스트 토큰이 있으면 N-gram 후보 우선, 없으면(확장자만) 전체 선형 스캔.
    /// 확장자 필터는 항상 마지막에 적용.
    /// </summary>
    private static List<SearchHit> SearchOneIndex(
        FileIndex index, string[] tokens, HashSet<string> extFilter, bool excludeRecycle, bool hideHiddenSystem)
    {
        var local = new List<SearchHit>(1024);
        int count = index.Count;
        bool hasText = tokens.Length > 0;
        bool hasExt  = extFilter.Count > 0;

        if (!hasText)
        {
            // === 확장자만: 전체 선형 스캔 (파일만) ===
            for (int j = 0; j < count; j++)
            {
                if (index.IsDeleted(j)) continue;
                if (index.IsDirectory(j)) continue;   // 확장자 필터는 파일 대상

                var name = index.GetNameSpan(j);
                if (!MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                local.Add(new SearchHit(index, j));
            }
            return local;
        }

        // === 텍스트 검색 (+ 선택적 확장자 필터) ===
        int[]? candidates = TryGetCandidatesViaNgram(index, tokens);

        if (candidates is not null)
        {
            bool skipMatchCheck = (tokens.Length == 1);
            int ngramBuiltCount = index.NgramBuiltAtCount;

            if (skipMatchCheck)
            {
                foreach (int j in candidates)
                {
                    if (j >= count) continue;
                    if (index.IsDeleted(j)) continue;
                    if (hasExt && !MatchesExtension(index.GetNameSpan(j), extFilter)) continue;
                    if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                    if (excludeRecycle && IsInRecycleBin(index, j)) continue;
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
                    if (hasExt && !MatchesExtension(name, extFilter)) continue;
                    if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                    if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                    local.Add(new SearchHit(index, j));
                }
            }

            // ngram 빌드 이후 추가된 영역은 항상 선형 검사
            for (int j = ngramBuiltCount; j < count; j++)
            {
                if (index.IsDeleted(j)) continue;

                var name = index.GetNameSpan(j);
                if (!MatchesAll(name, tokens)) continue;
                if (hasExt && !MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
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
                if (hasExt && !MatchesExtension(name, extFilter)) continue;
                if (hideHiddenSystem && index.IsHiddenSystemEffective(j)) continue;
                if (excludeRecycle && IsInRecycleBin(index, j)) continue;
                local.Add(new SearchHit(index, j));
            }
        }

        return local;
    }

    /// <summary>
    /// N-gram 인덱스를 사용해 후보 엔트리 인덱스 배열을 만든다.
    /// 모든 토큰이 ngram 사용 가능해야 의미 있음. 하나라도 못 쓰면 null 반환 (폴백).
    /// </summary>
    private static int[]? TryGetCandidatesViaNgram(FileIndex index, string[] tokens)
    {
        var ngram = index.Ngram;
        if (ngram is null) return null;

        List<int[]>? perToken = null;

        foreach (var token in tokens)
        {
            var c = ngram.GetCandidates(token);
            if (c is null)
                return null;
            if (c.Length == 0)
                return Array.Empty<int>();
            perToken ??= new List<int[]>(tokens.Length);
            perToken.Add(c);
        }

        if (perToken is null || perToken.Count == 0) return null;
        if (perToken.Count == 1) return perToken[0];

        return IntersectAll(perToken);
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
