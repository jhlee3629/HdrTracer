namespace HdrTracer.Core;

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