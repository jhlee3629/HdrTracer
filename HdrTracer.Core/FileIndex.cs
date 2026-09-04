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
        public long   ModifiedTicks; 
        public ulong  MftRef;
    }

    private const ushort FlagDirectory = 1;
    private const ushort FlagDeleted   = 2;
    private const ushort FlagHiddenSystem = 4;
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

    /// <summary>배열에 자리를 차지한 항목 수. 삭제 표시된 것도 포함한다(내부용).</summary>
    public int Count => _count;

    /// <summary>
    /// 실제로 살아 있는 항목 수. 삭제된 것은 빠진다.
    /// 사용자에게 보여줄 총계는 Count가 아니라 이 값을 써야 한다
    /// (Count는 삭제해도 줄지 않아 유령까지 세게 된다).
    /// </summary>
    public long LiveCount => FileCount + DirCount;

    public long FileCount { get; private set; }
    public long DirCount  { get; private set; }
    public long StringPoolBytes => (long)_poolPos * sizeof(char);
    public long EntryArrayBytes => (long)_count * Unsafe.SizeOf<Entry>();

    public string DriveLetter { get; set; } = "";

    /// <summary>폴더가 삭제된 적이 있어 하위 항목 정리가 필요한지.</summary>
    private bool _needOrphanPurge;

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

    public void PropagateHiddenSystem()
    {
        var memo = new byte[_count];

        var path = new int[256];

        for (int i = 0; i < _count; i++)
        {
            if (memo[i] != 0) continue;

            int len = 0;
            int cur = i;
            byte resolved = 0; 

            while (cur >= 0)
            {
                if (memo[cur] != 0) { resolved = memo[cur]; break; }    

                bool isRoot = _entries[cur].ParentIndex < 0;

                if (!isRoot && (_entries[cur].Flags & FlagHiddenSystem) != 0)  
                {
                    memo[cur] = 1;
                    resolved = 1;
                    break;
                }
                if (len < path.Length) path[len] = cur;
                len++;
                cur = _entries[cur].ParentIndex;   
            }

            if (resolved == 0) resolved = 2;

            int fill = Math.Min(len, path.Length);
            for (int k = 0; k < fill; k++)
                memo[path[k]] = resolved;
        }

        for (int i = 0; i < _count; i++)
        {
            // 상속이 풀린 경우(2)에는 플래그를 지운다. 지우지 않으면 숨김 폴더 밖으로
            // 옮겨진 항목이 계속 감춰진 채로 남는다.
            if (memo[i] == 1)      _entries[i].Flags |= FlagHiddenSystemEffective;
            else if (memo[i] == 2) _entries[i].Flags &= unchecked((ushort)~FlagHiddenSystemEffective);
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
        if (_mftToIndex.TryGetValue(mftRef & MftRefMask, out int idx))
            MarkDeleted(idx);
    }

    /// <summary>
    /// 항목을 삭제 표시하고 개수·조회표를 정리한다.
    /// 폴더였다면 하위 항목까지 정리해야 하므로 예약해 둔다
    /// (여기서 자식을 찾으면 부모→자식 색인이 없어 전수 조사가 된다).
    /// </summary>
    private void MarkDeleted(int idx)
    {
        if ((_entries[idx].Flags & FlagDeleted) != 0) return;

        _entries[idx].Flags |= FlagDeleted;

        bool isDir = (_entries[idx].Flags & FlagDirectory) != 0;
        DecrementCount(isDir);
        if (isDir) _needOrphanPurge = true;

        _mftToIndex.Remove(_entries[idx].MftRef);
    }

    /// <summary>
    /// 지워진 폴더의 하위 항목까지 삭제 표시한다.
    ///
    /// 왜 필요한가: 폴더를 지울 때 그 안의 항목마다 USN 삭제 기록이 오지 않는 경우가 있다.
    /// 그러면 부모만 사라지고 자식이 인덱스에 남아, GetFullPath가 이미 없는 부모 이름을
    /// 붙여 엉뚱한 경로(예: D:\$RKU7ETH\안쪽1.txt)를 만들어 낸다. 열리지도 않는다.
    ///
    /// 왜 검색 직전에 하는가: 부모→자식 색인이 없어 자식을 찾으려면 전수 조사가 필요하다.
    /// 삭제할 때마다 하면 속도가 무너지므로, 폴더 삭제가 있었을 때만 한 번, 전체를 한 번만 훑는다.
    /// PropagateHiddenSystem과 같은 메모 방식이라 한 번의 순회로 끝난다.
    /// </summary>
    /// <returns>실제로 정리한 항목이 있으면 true.</returns>
    public bool PurgeOrphansIfNeeded()
    {
        if (!_needOrphanPurge) return false;
        _needOrphanPurge = false;
        if (_count == 0) return false;

        // 0 = 미확인, 1 = 삭제됨(자신 또는 조상), 2 = 살아 있음
        var memo = new byte[_count];
        for (int i = 0; i < _count; i++)
            if ((_entries[i].Flags & FlagDeleted) != 0) memo[i] = 1;

        var path = new int[256];
        int purged = 0;

        for (int i = 0; i < _count; i++)
        {
            if (memo[i] != 0) continue;

            int len = 0;
            int cur = i;
            byte resolved = 0;

            while (cur >= 0)
            {
                if (memo[cur] != 0) { resolved = memo[cur]; break; }
                if (len < path.Length) path[len] = cur;
                len++;

                int next = _entries[cur].ParentIndex;
                if (next == cur) break;      // 자기 자신을 부모로 가리키는 손상 방지
                cur = next;
            }

            if (resolved == 0) resolved = 2;

            int fill = Math.Min(len, path.Length);
            for (int k = 0; k < fill; k++)
            {
                int idx = path[k];
                memo[idx] = resolved;

                if (resolved != 1) continue;
                if ((_entries[idx].Flags & FlagDeleted) != 0) continue;

                _entries[idx].Flags |= FlagDeleted;
                DecrementCount((_entries[idx].Flags & FlagDirectory) != 0);
                _mftToIndex.Remove(_entries[idx].MftRef);
                purged++;
            }
        }

        // 폴더가 지워지거나 옮겨졌으므로 숨김+시스템 상속도 다시 계산한다.
        // (휴지통으로 옮겨진 항목이 그 안에서 감춰지는 것이 이 계산의 효과다)
        PropagateHiddenSystem();

        return purged > 0;
    }

    /// <summary>
    /// 이름 바꾸기·이동을 반영한다.
    ///
    /// 부모까지 갱신해야 하는 이유: Windows에서 "휴지통으로 삭제"는 삭제가 아니라
    /// $RECYCLE.BIN 으로의 이동이며, USN에는 삭제가 아닌 RENAME_NEW_NAME으로 기록된다.
    /// 이름만 바꾸고 부모를 그대로 두면 항목이 옛 위치에 살아 있는 것처럼 남아,
    /// D:\$RKKYQWI\... 같은 실재하지 않는 경로가 만들어지고 열리지도 않는다.
    /// 일반적인 파일 이동에서도 같은 문제가 생긴다.
    /// </summary>
    /// <param name="parentRef">새 부모의 MFT 참조. 0이면 부모를 건드리지 않는다.</param>
    public unsafe void RenameByMftRef(ulong mftRef, char* namePtr, int nameLen, ulong parentRef)
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

        // ── 부모(위치) 갱신 ──
        ulong parentKey = parentRef & MftRefMask;
        if (parentKey == 0) return;                          // 부모 정보 없음 → 이름만
        if (_entries[idx].MftRef == NtfsRootMftRef) return;  // 루트는 부모를 두지 않는다

        if (!_mftToIndex.TryGetValue(parentKey, out int parentIdx))
        {
            // 새 부모가 인덱스에 없다. 어디로 갔는지 알 수 없으므로
            // 틀린 경로를 보여주느니 없는 것으로 다룬다.
            MarkDeleted(idx);
            return;
        }
        if (parentIdx == idx) return;                        // 자기 자신 방지

        bool wasDir = (_entries[idx].Flags & FlagDirectory) != 0;
        bool moved  = _entries[idx].ParentIndex != parentIdx;

        _entries[idx].ParentIndex = parentIdx;

        // 부모가 바뀌었으니 숨김+시스템 상속을 다시 본다
        bool effective = (_entries[idx].Flags & FlagHiddenSystem) != 0
                      || (_entries[parentIdx].Flags & FlagHiddenSystemEffective) != 0;
        if (effective) _entries[idx].Flags |= FlagHiddenSystemEffective;
        else           _entries[idx].Flags &= unchecked((ushort)~FlagHiddenSystemEffective);

        // 폴더가 옮겨졌으면 그 안의 항목도 상속을 다시 계산해야 한다
        if (wasDir && moved) _needOrphanPurge = true;
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

    /// <summary>
    /// 삭제되지 않은 항목을 실제로 세어 FileCount/DirCount를 다시 맞춘다.
    /// 항목 수에 비례하는 한 번의 순회라 50만 건에서도 수 밀리초다.
    /// </summary>
    public void RecountLive()
    {
        long files = 0, dirs = 0;
        for (int i = 0; i < _count; i++)
        {
            if ((_entries[i].Flags & FlagDeleted) != 0) continue;
            if ((_entries[i].Flags & FlagDirectory) != 0) dirs++; else files++;
        }
        FileCount = files;
        DirCount  = dirs;
    }

    /// <summary>
    /// 개수가 상식적인 범위인지. 음수이거나 전체 항목 수를 넘으면 어딘가에서 어긋난 것이다.
    /// (살아 있는 항목은 배열에 든 항목보다 많을 수 없다)
    /// </summary>
    private bool CountsLookSane()
        => FileCount >= 0 && DirCount >= 0 && FileCount + DirCount <= _count;

    /// <summary>
    /// 개수를 하나 줄인다. 0 아래로는 내려가지 않는다.
    /// 어딘가에서 중복 차감이 나더라도 총계가 음수가 되어 화면에 터무니없는 수가
    /// 표시되는 일은 막는다(한 번 음수가 되면 캐시에 저장되어 계속 이어진다).
    /// </summary>
    private void DecrementCount(bool isDir)
    {
        if (isDir) { if (DirCount  > 0) DirCount--; }
        else       { if (FileCount > 0) FileCount--; }
    }

    public bool IsDirectory(int index) => (_entries[index].Flags & FlagDirectory) != 0;
    public bool IsDeleted(int index)   => (_entries[index].Flags & FlagDeleted) != 0;

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

    public void WriteTo(BinaryWriter bw)
    {
        // 어긋난 값을 캐시에 남기지 않는다. 한 번 잘못 저장되면
        // 다음 실행마다 그 값을 다시 읽어 계속 이어진다.
        if (!CountsLookSane()) RecountLive();

        bw.Write(_count);
        bw.Write(_poolPos);

        int entryBytes = _count * Unsafe.SizeOf<Entry>();
        var span = MemoryMarshal.AsBytes(_entries.AsSpan(0, _count));
        bw.Write(span);

        var charBytes = MemoryMarshal.AsBytes(_pool.AsSpan(0, _poolPos));
        bw.Write(charBytes);

        bw.Write(FileCount);
        bw.Write(DirCount);
    }

    public static FileIndex ReadFrom(BinaryReader br)
    {
        int count = br.ReadInt32();
        int poolPos = br.ReadInt32();

        var index = new FileIndex();

        if (count > index._entries.Length)
            index._entries = new Entry[count];
        int entryBytes = count * Unsafe.SizeOf<Entry>();
        var entrySpan = MemoryMarshal.AsBytes(index._entries.AsSpan(0, count));
        int read = br.Read(entrySpan);
        if (read != entryBytes) throw new InvalidDataException("Entry data truncated");

        if (poolPos > index._pool.Length)
            index._pool = new char[poolPos];
        var poolSpan = MemoryMarshal.AsBytes(index._pool.AsSpan(0, poolPos));
        read = br.Read(poolSpan);
        if (read != poolPos * 2) throw new InvalidDataException("Pool data truncated");

        index._count = count;
        index._poolPos = poolPos;

        index.FileCount = br.ReadInt64();
        index.DirCount  = br.ReadInt64();

        // 저장된 값을 그대로 믿지 않고 실제 항목으로 다시 센다.
        // 이렇게 해야 이미 잘못 저장된 캐시를 가진 사용자도 다음 실행에서 저절로 정상이 된다
        // (캐시를 직접 지우라고 안내할 방법이 없다). 50만 건에서 수 밀리초.
        index.RecountLive();

        index._mftToIndex.EnsureCapacity(count);
        for (int i = 0; i < count; i++)
        {
            if ((index._entries[i].Flags & FlagDeleted) != 0) continue;
            index._mftToIndex.TryAdd(index._entries[i].MftRef, i);
        }

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