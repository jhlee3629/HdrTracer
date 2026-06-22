namespace HdrTracer.Core;

/// <summary>
/// 여러 드라이브의 FileIndex를 묶어서 관리하는 Facade.
/// 슬롯의 동적 추가/제거를 지원한다.
/// </summary>
public sealed class MultiDriveIndex
{
    public sealed class DriveSlot
    {
        public required string DriveLetter { get; init; }
        public FileIndex? Index { get; set; }
        public UsnJournalMonitor? Monitor { get; set; }
        public string? Error { get; set; }
        public long BuildMs { get; set; }
    }

    private readonly List<DriveSlot> _slots = new();
    private readonly object _lock = new();

    /// <summary>드라이브 슬롯이 추가/제거될 때 발생.</summary>
    public event Action? SlotsChanged;

    public IReadOnlyList<DriveSlot> Slots
    {
        get { lock (_lock) return _slots.ToList(); }
    }

    public int TotalEntryCount
    {
        get
        {
            int sum = 0;
            lock (_lock)
            {
                foreach (var s in _slots)
                    if (s.Index is not null) sum += s.Index.Count;
            }
            return sum;
        }
    }

    public DriveSlot? FindSlot(string driveLetter)
    {
        lock (_lock)
        {
            foreach (var s in _slots)
                if (string.Equals(s.DriveLetter, driveLetter, StringComparison.OrdinalIgnoreCase))
                    return s;
        }
        return null;
    }

    public bool ContainsDrive(string driveLetter)
    {
        return FindSlot(driveLetter) is not null;
    }

    public void AddSlot(DriveSlot slot)
    {
        lock (_lock) _slots.Add(slot);
        SlotsChanged?.Invoke();
    }

    /// <summary>지정한 드라이브를 제거하고 그 슬롯의 모니터/인덱스를 정리한다.</summary>
    public void RemoveDrive(string driveLetter)
    {
        DriveSlot? removed = null;
        lock (_lock)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (string.Equals(_slots[i].DriveLetter, driveLetter, StringComparison.OrdinalIgnoreCase))
                {
                    removed = _slots[i];
                    _slots.RemoveAt(i);
                    break;
                }
            }
        }

        if (removed is not null)
        {
            try { removed.Monitor?.Dispose(); } catch { }
            // FileIndex 자체는 GC에 맡김 (배열 두 개라 곧 해제됨)
            SlotsChanged?.Invoke();
        }
    }

    public List<FileIndex> GetActiveIndexes()
    {
        var list = new List<FileIndex>(_slots.Count);
        lock (_lock)
        {
            foreach (var s in _slots)
                if (s.Index is not null) list.Add(s.Index);
        }
        return list;
    }

    public void DisposeAll()
    {
        lock (_lock)
        {
            foreach (var s in _slots)
            {
                try { s.Monitor?.Dispose(); } catch { }
            }
            _slots.Clear();
        }
    }
}