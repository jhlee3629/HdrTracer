using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;

namespace HdrTracer.Core;

public sealed class FileIndex
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Entry
    {
        public int    NameOffset;
        public ushort NameLength;
        public ushort Flags;
        public int    ParentIndex;
        public long   Size;
        public long   ModifiedTicks;   // DateTime.Ticks (UTC) — 0이면 미설정
        public ulong  MftRef;
    }

    private const ushort FlagDirectory = 1;
    private const ushort FlagDeleted   = 2;
    // 항목 자체가 숨김+시스템 속성인지
    private const ushort FlagHiddenSystem = 4;
    // 조상 폴더(또는 자기 자신)가 숨김+시스템이라 "감춰진" 항목인지 (LinkParents 후 전파됨)
    private const ushort FlagHiddenSystemEffective = 8;

    private const int InitialCapacity  = 1 << 18;
    private const int InitialPoolChars = 8 * 1024 * 1024;
    private const ulong MftRefMask     = 0x0000_FFFF_FFFF_FFFFUL;
    private const ulong NtfsRootMftRef = 5UL;

    private Entry[] _entries = new Entry[InitialCapacity];
    private char[]  _pool    = new char[InitialPoolChars];
    private int _count;
    private int _poolPos;

    private readonly Dictionary<ulong, int> _mftToIndex = new(InitialCapacity);
    private ulong[] _parentRefs = new ulong[InitialCapacity];

    public int Count => _count;
    public long FileCount { get; private set; }
    public long DirCount  { get; private set; }
    public long StringPoolBytes => (long)_poolPos * sizeof(char);
    public long EntryArrayBytes => (long)_count * Unsafe.SizeOf<Entry>();

    public string DriveLetter { get; set; } = "";

    // === N-gram 인덱스 ===
    private NgramIndex? _ngram;
    private int _ngramBuiltAtCount;

    public NgramIndex? Ngram => _ngram;
    public int NgramBuiltAtCount => _ngramBuiltAtCount;

    public unsafe void Add(char* namePtr, int nameLen, ulong mftRef, ulong parentRef, bool isDir, long size, bool isHiddenSystem = false)
    {
        if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
            Array.Resize(ref _parentRefs, _parentRefs.Length * 2);
        }
        EnsurePoolCapacity(nameLen);

        fixed (char* poolPtr = _pool)
        {
            Buffer.MemoryCopy(namePtr, poolPtr + _poolPos,
                (_pool.Length - _poolPos) * sizeof(char),
                nameLen * sizeof(char));
        }

        ulong selfMasked = mftRef & MftRefMask;

        ushort flags = (ushort)(isDir ? FlagDirectory : 0);
        if (isHiddenSystem) flags |= FlagHiddenSystem;

        _entries[_count] = new Entry
        {
            NameOffset  = _poolPos,
            NameLength  = (ushort)nameLen,
            Flags       = flags,
            ParentIndex = -2,
            Size        = size,
            MftRef      = selfMasked
        };
        _parentRefs[_count] = parentRef & MftRefMask;

        // 같은 mftRef가 여러 번 추가될 수 있음 (NTFS 하드링크).
        // 첫 인스턴스만 매핑에 기록하여 자식들이 부모 찾을 때 안정적인 위치를 얻도록 함.
        _mftToIndex.TryAdd(selfMasked, _count);
        _poolPos += nameLen;
        if (isDir) DirCount++; else FileCount++;
        _count++;
    }

    public int LinkParents()
    {
        int rootCount = 0;
        for (int i = 0; i < _count; i++)
        {
            ulong pRef = _parentRefs[i];
            if (_mftToIndex.TryGetValue(pRef, out int parentIdx) && parentIdx != i)
                _entries[i].ParentIndex = parentIdx;
            else
            {
                _entries[i].ParentIndex = -1;
                rootCount++;
            }
        }
        _parentRefs = Array.Empty<ulong>();

        PropagateHiddenSystem();
        return rootCount;
    }

    /// <summary>
    /// 숨김+시스템 폴더의 모든 하위 항목에 "감춰짐"(FlagHiddenSystemEffective) 비트를 전파한다.
    /// 인덱싱 직후 한 번만 수행하면, 검색 시에는 이 비트 하나만 검사하면 되므로 매우 빠르다.
    /// 각 항목의 조상을 따라 올라가되, 계산 결과를 캐싱(메모이제이션)하여 전체 O(N)에 처리.
    /// </summary>
    public void PropagateHiddenSystem()
    {
        // memo: 0 = 미계산, 1 = 감춰짐, 2 = 안 감춰짐
        var memo = new byte[_count];

        // 경로 누적용 버퍼 (재사용)
        var path = new int[256];

        for (int i = 0; i < _count; i++)
        {
            if (memo[i] != 0) continue;

            // i에서 부모를 따라 올라가며 미계산 노드를 path에 쌓는다.
            int len = 0;
            int cur = i;
            byte resolved = 0;   // 조상에서 확정된 결과

            while (cur >= 0)
            {
                if (memo[cur] != 0) { resolved = memo[cur]; break; }      // 캐시 적중

                // 드라이브 루트(부모 없음)는 NTFS 구조상 숨김+시스템 속성을 갖지만,
                // 이는 "루트 안을 숨기자"는 의미가 아니므로 숨김 조상으로 치지 않는다.
                // (루트를 self로 인정하면 드라이브 전체가 감춰져 버림)
                bool isRoot = _entries[cur].ParentIndex < 0;

                if (!isRoot && (_entries[cur].Flags & FlagHiddenSystem) != 0)  // 자기 자신이 숨김+시스템
                {
                    memo[cur] = 1;
                    resolved = 1;
                    break;
                }
                if (len < path.Length) path[len] = cur;
                len++;
                cur = _entries[cur].ParentIndex;   // 부모로
            }

            // 루트까지 갔는데 아무 데서도 숨김+시스템을 못 만났으면 "안 감춰짐"
            if (resolved == 0) resolved = 2;

            // path에 쌓인(자기 + 비숨김 조상들) 노드들에 결과 적용.
            // 단, path 버퍼를 넘어선 깊은 경로의 노드는 memo가 아직 0일 수 있는데,
            // 그 경우 resolved 값으로 안전하게 채워진다(다음 패스에서 재방문되지 않도록).
            int fill = Math.Min(len, path.Length);
            for (int k = 0; k < fill; k++)
                memo[path[k]] = resolved;
        }

        for (int i = 0; i < _count; i++)
        {
            if (memo[i] == 1)
                _entries[i].Flags |= FlagHiddenSystemEffective;
        }
    }

    public unsafe void AddAndLink(char* namePtr, int nameLen, ulong mftRef, ulong parentRef, bool isDir, long size, bool isHiddenSystem = false)
    {
        ulong key = mftRef & MftRefMask;
        if (_mftToIndex.ContainsKey(key)) return;

        if (_count == _entries.Length)
            Array.Resize(ref _entries, _entries.Length * 2);
        EnsurePoolCapacity(nameLen);

        fixed (char* poolPtr = _pool)
        {
            Buffer.MemoryCopy(namePtr, poolPtr + _poolPos,
                (_pool.Length - _poolPos) * sizeof(char),
                nameLen * sizeof(char));
        }

        ulong parentKey = parentRef & MftRefMask;
        int parentIdx = _mftToIndex.TryGetValue(parentKey, out int p) ? p : -1;

        ushort flags = (ushort)(isDir ? FlagDirectory : 0);
        if (isHiddenSystem) flags |= FlagHiddenSystem;

        // "감춰짐" 비트: 자기 자신이 숨김+시스템이거나, 부모가 이미 감춰진 상태면 물려받음
        bool effective = isHiddenSystem
            || (parentIdx >= 0 && (_entries[parentIdx].Flags & FlagHiddenSystemEffective) != 0);
        if (effective) flags |= FlagHiddenSystemEffective;

        _entries[_count] = new Entry
        {
            NameOffset  = _poolPos,
            NameLength  = (ushort)nameLen,
            Flags       = flags,
            ParentIndex = parentIdx,
            Size        = size,
            MftRef      = key
        };
        _mftToIndex[key] = _count;
        _poolPos += nameLen;
        if (isDir) DirCount++; else FileCount++;
        _count++;
    }

    public void RemoveByMftRef(ulong mftRef)
    {
        ulong key = mftRef & MftRefMask;
        if (_mftToIndex.TryGetValue(key, out int idx))
        {
            if ((_entries[idx].Flags & FlagDeleted) != 0) return;
            _entries[idx].Flags |= FlagDeleted;
            if ((_entries[idx].Flags & FlagDirectory) != 0) DirCount--; else FileCount--;
            _mftToIndex.Remove(key);
        }
    }

    public unsafe void RenameByMftRef(ulong mftRef, char* namePtr, int nameLen)
    {
        ulong key = mftRef & MftRefMask;
        if (!_mftToIndex.TryGetValue(key, out int idx)) return;

        EnsurePoolCapacity(nameLen);

        fixed (char* poolPtr = _pool)
        {
            Buffer.MemoryCopy(namePtr, poolPtr + _poolPos,
                (_pool.Length - _poolPos) * sizeof(char),
                nameLen * sizeof(char));
        }

        _entries[idx].NameOffset = _poolPos;
        _entries[idx].NameLength = (ushort)nameLen;
        _poolPos += nameLen;
    }

    private void EnsurePoolCapacity(int additionalChars)
    {
        if (_poolPos + additionalChars > _pool.Length)
        {
            int newSize = _pool.Length;
            while (_poolPos + additionalChars > newSize) newSize *= 2;
            Array.Resize(ref _pool, newSize);
        }
    }

    public bool IsDirectory(int index) => (_entries[index].Flags & FlagDirectory) != 0;
    public bool IsDeleted(int index)   => (_entries[index].Flags & FlagDeleted) != 0;

    /// <summary>이 항목이 숨김+시스템 폴더(또는 그 하위)에 속해 "감춰진" 항목인지.</summary>
    public bool IsHiddenSystemEffective(int index)
    {
        if ((uint)index >= (uint)_count) return false;
        return (_entries[index].Flags & FlagHiddenSystemEffective) != 0;
    }

    public int GetParentIndex(int index)
    {
        if ((uint)index >= (uint)_count) return -1;
        return _entries[index].ParentIndex;
    }

    public void SetMetadata(int index, long size, DateTime modifiedUtc)
    {
        if ((uint)index >= (uint)_count) return;
        _entries[index].Size = size;
        _entries[index].ModifiedTicks = modifiedUtc.Ticks;
    }

    public long GetSize(int index)
    {
        if ((uint)index >= (uint)_count) return 0;
        return _entries[index].Size;
    }

    public DateTime GetModifiedUtc(int index)
    {
        if ((uint)index >= (uint)_count) return DateTime.MinValue;
        long ticks = _entries[index].ModifiedTicks;
        if (ticks == 0) return DateTime.MinValue;
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    public bool HasMetadata(int index)
    {
        if ((uint)index >= (uint)_count) return false;
        return _entries[index].ModifiedTicks != 0;
    }

    public string GetName(int index)
    {
        ref var e = ref _entries[index];
        return new string(_pool, e.NameOffset, e.NameLength);
    }

    public ReadOnlySpan<char> GetNameSpan(int index)
    {
        ref var e = ref _entries[index];
        return _pool.AsSpan(e.NameOffset, e.NameLength);
    }

    public string? GetFullPath(int index)
    {
        if ((uint)index >= (uint)_count) return null;

        Span<int> stack = stackalloc int[64];
        int depth = 0;
        int cur = index;

        while (cur >= 0 && depth < stack.Length)
        {
            if (_entries[cur].MftRef == NtfsRootMftRef) break;
            stack[depth++] = cur;
            cur = _entries[cur].ParentIndex;
        }

        int totalLen = DriveLetter.Length + 1;
        for (int i = 0; i < depth; i++)
        {
            totalLen += _entries[stack[i]].NameLength;
            if (i > 0) totalLen += 1;
        }

        return string.Create(totalLen, (this, stack: stack.ToArray(), depth, drive: DriveLetter), static (span, state) =>
        {
            int pos = 0;
            state.drive.AsSpan().CopyTo(span);
            pos += state.drive.Length;
            span[pos++] = '\\';

            for (int i = state.depth - 1; i >= 0; i--)
            {
                ref var e = ref state.Item1._entries[state.stack[i]];
                state.Item1._pool.AsSpan(e.NameOffset, e.NameLength).CopyTo(span.Slice(pos));
                pos += e.NameLength;
                if (i > 0) span[pos++] = '\\';
            }
        });
    }

    // 직렬화/역직렬화
    public void WriteTo(BinaryWriter bw)
    {
        bw.Write(_count);
        bw.Write(_poolPos);

        // Entry 배열을 raw bytes로 (Entry는 unmanaged struct이므로 안전)
        int entryBytes = _count * Unsafe.SizeOf<Entry>();
        var span = MemoryMarshal.AsBytes(_entries.AsSpan(0, _count));
        bw.Write(span);

        // 문자열 풀
        var charBytes = MemoryMarshal.AsBytes(_pool.AsSpan(0, _poolPos));
        bw.Write(charBytes);

        // 통계 정보
        bw.Write(FileCount);
        bw.Write(DirCount);
    }

    public static FileIndex ReadFrom(BinaryReader br)
    {
        int count = br.ReadInt32();
        int poolPos = br.ReadInt32();

        var index = new FileIndex();

        // Entry 배열
        if (count > index._entries.Length)
            index._entries = new Entry[count];
        int entryBytes = count * Unsafe.SizeOf<Entry>();
        var entrySpan = MemoryMarshal.AsBytes(index._entries.AsSpan(0, count));
        int read = br.Read(entrySpan);
        if (read != entryBytes) throw new InvalidDataException("Entry data truncated");

        // 문자열 풀
        if (poolPos > index._pool.Length)
            index._pool = new char[poolPos];
        var poolSpan = MemoryMarshal.AsBytes(index._pool.AsSpan(0, poolPos));
        read = br.Read(poolSpan);
        if (read != poolPos * 2) throw new InvalidDataException("Pool data truncated");

        index._count = count;
        index._poolPos = poolPos;

        long fileCount = br.ReadInt64();
        long dirCount = br.ReadInt64();
        index.FileCount = fileCount;
        index.DirCount = dirCount;

        // _mftToIndex 사전 재구축 (직렬화 안 했음).
        // 같은 mftRef가 여러 번 있을 수 있으므로 첫 인스턴스만 기록.
        index._mftToIndex.EnsureCapacity(count);
        for (int i = 0; i < count; i++)
        {
            if ((index._entries[i].Flags & FlagDeleted) != 0) continue;
            index._mftToIndex.TryAdd(index._entries[i].MftRef, i);
        }

        // _parentRefs는 이미 LinkParents가 끝난 상태로 저장됐으니 비워둠
        index._parentRefs = Array.Empty<ulong>();

        return index;
    }

    public void BuildNgramIndex()
    {
        var ng = new NgramIndex();
        ng.BuildFromIndex(this);
        _ngram = ng;
        _ngramBuiltAtCount = _count;
    }

    public void SetNgramIndex(NgramIndex ngram, int builtAtCount)
    {
        _ngram = ngram;
        _ngramBuiltAtCount = builtAtCount;
    }

    public void ClearNgramIndex()
    {
        _ngram = null;
        _ngramBuiltAtCount = 0;
    }
}