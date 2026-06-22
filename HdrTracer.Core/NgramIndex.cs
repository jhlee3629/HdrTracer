using System.IO;

namespace HdrTracer.Core;

/// <summary>
/// 영문/숫자/기호는 trigram (3-gram), 한글은 bigram (2-gram)으로 인덱싱.
/// 토큰을 받아 후보 엔트리 인덱스 집합을 반환한다.
/// </summary>
public sealed class NgramIndex
{
    // 키 = 인코딩된 n-gram, 값 = 정렬된 엔트리 인덱스 배열
    // (List<int> 대신 int[]를 사용해 메모리 절감)
    private Dictionary<long, int[]> _gramToEntries = new();

    /// <summary>인덱스에 들어간 항목 수.</summary>
    public int GramCount => _gramToEntries.Count;

    /// <summary>모든 파일 이름을 한 번에 인덱싱.</summary>
    public void BuildFromIndex(FileIndex fileIndex)
    {
        // 빌드는 임시 List<int>로 모은 다음 마지막에 int[]로 변환
        var temp = new Dictionary<long, List<int>>(1 << 18);

        int count = fileIndex.Count;
        for (int entryIdx = 0; entryIdx < count; entryIdx++)
        {
            var name = fileIndex.GetNameSpan(entryIdx);
            AddNameToTemp(name, entryIdx, temp);
        }

        // List<int> → int[] (정렬 + 중복 제거)
        var final = new Dictionary<long, int[]>(temp.Count);
        foreach (var kv in temp)
        {
            var list = kv.Value;
            // 같은 entryIdx가 여러 번 들어갔을 수 있음 (예: "aaaa"의 "aa" 두 번)
            // 중복 제거 + 정렬
            list.Sort();
            var arr = DeduplicateSorted(list);
            final[kv.Key] = arr;
        }
        _gramToEntries = final;
    }

    private static int[] DeduplicateSorted(List<int> sorted)
    {
        if (sorted.Count == 0) return Array.Empty<int>();
        int writePos = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            if (i == 0 || sorted[i] != sorted[i - 1])
                sorted[writePos++] = sorted[i];
        }
        var result = new int[writePos];
        for (int i = 0; i < writePos; i++) result[i] = sorted[i];
        return result;
    }

    private static void AddNameToTemp(ReadOnlySpan<char> name, int entryIdx, Dictionary<long, List<int>> temp)
    {
        // 영문/숫자/기호는 소문자로 정규화한 trigram
        // 한글은 그대로 bigram
        // 한 이름 안에서 두 종류를 다 만든다
        AddGramsForName(name, entryIdx, temp);
    }

    private static void AddGramsForName(ReadOnlySpan<char> name, int entryIdx, Dictionary<long, List<int>> temp)
    {
        int len = name.Length;

        // === Trigram for 영문/숫자/기호 ===
        // 한글 문자는 trigram에 안 넣고, 한글이 끼어 있으면 trigram 끊어서 따로 처리
        for (int i = 0; i + 3 <= len; i++)
        {
            char c0 = name[i];
            char c1 = name[i + 1];
            char c2 = name[i + 2];
            // 셋 다 비-한글일 때만 trigram
            if (IsHangul(c0) || IsHangul(c1) || IsHangul(c2)) continue;

            long key = EncodeTrigram(c0, c1, c2);
            AddToTemp(temp, key, entryIdx);
        }

        // === Bigram for 한글 ===
        for (int i = 0; i + 2 <= len; i++)
        {
            char c0 = name[i];
            char c1 = name[i + 1];
            // 둘 다 한글일 때만 bigram
            if (!IsHangul(c0) || !IsHangul(c1)) continue;

            long key = EncodeBigramHangul(c0, c1);
            AddToTemp(temp, key, entryIdx);
        }
    }

    private static void AddToTemp(Dictionary<long, List<int>> temp, long key, int entryIdx)
    {
        if (!temp.TryGetValue(key, out var list))
        {
            list = new List<int>(4);
            temp[key] = list;
        }
        list.Add(entryIdx);
    }

    /// <summary>한글 음절 범위 (가-힣). 자모 영역은 따로 안 봄.</summary>
    public static bool IsHangul(char c) => c >= 0xAC00 && c <= 0xD7A3;

    /// <summary>3자 영문/숫자/기호를 64비트 키로 인코딩 (소문자 정규화).</summary>
    private static long EncodeTrigram(char c0, char c1, char c2)
    {
        // 영문은 소문자, 외에는 그대로 (16비트씩 packing)
        long a = ToLowerAscii(c0);
        long b = ToLowerAscii(c1);
        long c = ToLowerAscii(c2);
        // 상위 비트 0x01 = trigram 마커 (bigram과 충돌 방지)
        return (1L << 48) | (a << 32) | (b << 16) | c;
    }

    /// <summary>2자 한글을 64비트 키로 인코딩.</summary>
    private static long EncodeBigramHangul(char c0, char c1)
    {
        // 상위 비트 0x02 = hangul bigram 마커
        return (2L << 48) | ((long)c0 << 32) | ((long)c1 << 16);
    }

    private static char ToLowerAscii(char c)
    {
        if (c >= 'A' && c <= 'Z') return (char)(c + 32);
        return c;
    }

    /// <summary>
    /// 검색 토큰에 대한 후보 엔트리 인덱스 배열 반환.
    /// null 반환 = "인덱스로 후보를 좁힐 수 없음" → 호출자는 선형 스캔 폴백.
    /// </summary>
    public int[]? GetCandidates(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        // 토큰의 ngram들을 모두 추출
        var grams = ExtractGrams(token);
        if (grams.Count == 0) return null;

        // 각 ngram의 후보 리스트 가져오기
        var candidateLists = new List<int[]>(grams.Count);
        foreach (var g in grams)
        {
            if (!_gramToEntries.TryGetValue(g, out var arr))
            {
                // 이 ngram을 가진 파일이 하나도 없음 → 결과 없음
                return Array.Empty<int>();
            }
            candidateLists.Add(arr);
        }

        // 모든 ngram의 교집합 (각 후보가 정렬되어 있으니 빠르게 가능)
        return IntersectAll(candidateLists);
    }

    private static List<long> ExtractGrams(string token)
    {
        var result = new List<long>();
        int len = token.Length;

        for (int i = 0; i + 3 <= len; i++)
        {
            char c0 = token[i], c1 = token[i + 1], c2 = token[i + 2];
            if (IsHangul(c0) || IsHangul(c1) || IsHangul(c2)) continue;
            result.Add(EncodeTrigram(c0, c1, c2));
        }

        for (int i = 0; i + 2 <= len; i++)
        {
            char c0 = token[i], c1 = token[i + 1];
            if (!IsHangul(c0) || !IsHangul(c1)) continue;
            result.Add(EncodeBigramHangul(c0, c1));
        }

        return result;
    }

    /// <summary>두 정렬된 배열의 교집합. 일반화하면 N개 합칠 수 있음.</summary>
    private static int[] IntersectAll(List<int[]> sortedArrays)
    {
        if (sortedArrays.Count == 0) return Array.Empty<int>();
        if (sortedArrays.Count == 1) return sortedArrays[0];

        // 가장 작은 배열을 베이스로 선택 (교집합은 그것보다 작을 수밖에 없음)
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

    // === 직렬화 (영속화 캐시용) ===
    public void WriteTo(BinaryWriter bw)
    {
        bw.Write(_gramToEntries.Count);
        foreach (var kv in _gramToEntries)
        {
            bw.Write(kv.Key);
            bw.Write(kv.Value.Length);
            foreach (var idx in kv.Value) bw.Write(idx);
        }
    }

    public static NgramIndex ReadFrom(BinaryReader br)
    {
        var idx = new NgramIndex();
        int gramCount = br.ReadInt32();
        idx._gramToEntries = new Dictionary<long, int[]>(gramCount);
        for (int i = 0; i < gramCount; i++)
        {
            long key = br.ReadInt64();
            int arrLen = br.ReadInt32();
            var arr = new int[arrLen];
            for (int j = 0; j < arrLen; j++) arr[j] = br.ReadInt32();
            idx._gramToEntries[key] = arr;
        }
        return idx;
    }
}