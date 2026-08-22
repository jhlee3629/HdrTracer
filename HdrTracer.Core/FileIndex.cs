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

    public int Count => _count;
    public long FileCount { get; private set; }
    public long DirCount  { get; private set; }
    public long StringPoolBytes => (long)_poolPos * sizeof(char);
    public long EntryArrayBytes => (long)_count * Unsafe.SizeOf<Entry>();

    public string DriveLetter { get; set; } = "";

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

        long fileCount = br.ReadInt64();
        long dirCount = br.ReadInt64();
        index.FileCount = fileCount;
        index.DirCount = dirCount;

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