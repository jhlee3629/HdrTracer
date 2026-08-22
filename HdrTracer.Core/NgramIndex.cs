using System.IO;

namespace HdrTracer.Core;

public sealed class NgramIndex
{
    private Dictionary<long, int[]> _gramToEntries = new();

    public int GramCount => _gramToEntries.Count;

    public void BuildFromIndex(FileIndex fileIndex)
    {
        var temp = new Dictionary<long, List<int>>(1 << 18);

        int count = fileIndex.Count;
        for (int entryIdx = 0; entryIdx < count; entryIdx++)
        {
            var name = fileIndex.GetNameSpan(entryIdx);
            AddNameToTemp(name, entryIdx, temp);
        }

        var final = new Dictionary<long, int[]>(temp.Count);
        foreach (var kv in temp)
        {
            var list = kv.Value;
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
        AddGramsForName(name, entryIdx, temp);
    }

    private static void AddGramsForName(ReadOnlySpan<char> name, int entryIdx, Dictionary<long, List<int>> temp)
    {
        int len = name.Length;

        for (int i = 0; i + 3 <= len; i++)
        {
            char c0 = name[i];
            char c1 = name[i + 1];
            char c2 = name[i + 2];
            
            if (IsHangul(c0) || IsHangul(c1) || IsHangul(c2)) continue;

            long key = EncodeTrigram(c0, c1, c2);
            AddToTemp(temp, key, entryIdx);
        }

        for (int i = 0; i + 2 <= len; i++)
        {
            char c0 = name[i];
            char c1 = name[i + 1];
            
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

    public static bool IsHangul(char c) => c >= 0xAC00 && c <= 0xD7A3;

    private static long EncodeTrigram(char c0, char c1, char c2)
    {
        long a = ToLowerAscii(c0);
        long b = ToLowerAscii(c1);
        long c = ToLowerAscii(c2);
     
        return (1L << 48) | (a << 32) | (b << 16) | c;
    }

    private static long EncodeBigramHangul(char c0, char c1)
    {
        return (2L << 48) | ((long)c0 << 32) | ((long)c1 << 16);
    }

    private static char ToLowerAscii(char c)
    {
        if (c >= 'A' && c <= 'Z') return (char)(c + 32);
        return c;
    }

    public int[]? GetCandidates(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var grams = ExtractGrams(token);
        if (grams.Count == 0) return null;

        var candidateLists = new List<int[]>(grams.Count);
        foreach (var g in grams)
        {
            if (!_gramToEntries.TryGetValue(g, out var arr))
            {
                return Array.Empty<int>();
            }
            candidateLists.Add(arr);
        }

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

    private static int[] IntersectAll(List<int[]> sortedArrays)
    {
        if (sortedArrays.Count == 0) return Array.Empty<int>();
        if (sortedArrays.Count == 1) return sortedArrays[0];

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