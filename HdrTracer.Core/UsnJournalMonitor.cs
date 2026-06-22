using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.Core;

public sealed class UsnJournalMonitor : IDisposable
{
    private readonly FileIndex _index;
    private readonly string _driveLetter;
    private readonly SafeFileHandle _handle;
    /// <summary>USN 저널을 읽는 볼륨 핸들. 안전 제거(query-remove) 알림 등록에 사용된다.</summary>
    public SafeFileHandle VolumeHandle => _handle;
    private readonly ulong _journalId;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private long _nextUsn;
    public long CurrentUsn => Interlocked.Read(ref _nextUsn);
    public ulong JournalId => _journalId;

    public long EventsProcessed;
    public long EntriesAdded;
    public long EntriesRemoved;
    public long EntriesRenamed;

    /// <summary>인덱스가 갱신되었을 때 발생. GUI가 이 이벤트를 구독해서 검색을 재실행할 수 있다.</summary>
    public event Action? IndexChanged;

    private bool _supported = true;
    public bool IsSupported => _supported;

    public UsnJournalMonitor(FileIndex index, string driveLetter)
    {
        _index = index;
        _driveLetter = driveLetter;

        _handle = Native.CreateFile(
            $@"\\.\{driveLetter}",
            Native.GENERIC_READ,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Native.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (_handle.IsInvalid)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        bool ok = Native.DeviceIoControlQuery(
            _handle,
            Native.FSCTL_QUERY_USN_JOURNAL,
            IntPtr.Zero, 0,
            out Native.USN_JOURNAL_DATA_V0 data,
            (uint)Marshal.SizeOf<Native.USN_JOURNAL_DATA_V0>(),
            out _, IntPtr.Zero);

        if (!ok)
        {
            // USN Journal이 없는 드라이브 (예: 일부 USB) — 모니터링 비활성, 정적 인덱스로만 사용
            _journalId = 0;
            _nextUsn = 0;
            _supported = false;
            return;
        }

        _journalId = data.UsnJournalID;
        _nextUsn = data.NextUsn;
    }

    public void Start()
    {
        if (!_supported) return;
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "UsnJournalMonitor" };
        _thread.Start();
    }

    private unsafe void RunLoop()
    {
        const int BufferSize = 64 * 1024;
        var buffer = new byte[BufferSize];
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            var readData = new Native.READ_USN_JOURNAL_DATA_V0
            {
                StartUsn          = _nextUsn,
                ReasonMask        = 0xFFFFFFFF,
                ReturnOnlyOnClose = 1,
                Timeout           = 1,
                BytesToWaitFor    = 1,
                UsnJournalID      = _journalId
            };

            fixed (byte* outPtr = buffer)
            {
                bool ok = Native.DeviceIoControlReadJournal(
                    _handle,
                    Native.FSCTL_READ_USN_JOURNAL,
                    ref readData,
                    (uint)Marshal.SizeOf<Native.READ_USN_JOURNAL_DATA_V0>(),
                    (IntPtr)outPtr,
                    BufferSize,
                    out uint bytesReturned,
                    IntPtr.Zero);

                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 38 || err == 258) continue;
                    if (token.IsCancellationRequested) break;
                    Thread.Sleep(100);
                    continue;
                }

                if (bytesReturned < 8) continue;

                long newNextUsn = *(long*)outPtr;

                byte* p = outPtr + 8;
                byte* end = outPtr + bytesReturned;

                bool anyChange = false;
                while (p < end)
                {
                    var rec = (Native.USN_RECORD_V2*)p;
                    if (rec->RecordLength == 0) break;

                    if (ProcessRecord(rec, p)) anyChange = true;
                    p += rec->RecordLength;
                }

                _nextUsn = newNextUsn;

                if (anyChange) IndexChanged?.Invoke();
            }
        }
    }

    private unsafe bool ProcessRecord(Native.USN_RECORD_V2* rec, byte* recStart)
    {
        uint reason = rec->Reason;
        char* namePtr = (char*)(recStart + rec->FileNameOffset);
        int nameLen = rec->FileNameLength / 2;
        bool isDir = (rec->FileAttributes & 0x10) != 0;
        bool changed = false;

        lock (_index)
        {
            if ((reason & Native.USN_REASON_FILE_DELETE) != 0)
            {
                _index.RemoveByMftRef(rec->FileReferenceNumber);
                EntriesRemoved++;
                changed = true;
            }
            else if ((reason & Native.USN_REASON_RENAME_NEW_NAME) != 0)
            {
                _index.RenameByMftRef(rec->FileReferenceNumber, namePtr, nameLen);
                EntriesRenamed++;
                changed = true;
            }
            else if ((reason & Native.USN_REASON_FILE_CREATE) != 0)
            {
                _index.AddAndLink(namePtr, nameLen,
                    rec->FileReferenceNumber, rec->ParentFileReferenceNumber,
                    isDir, 0);
                EntriesAdded++;
                changed = true;
            }
        }
        EventsProcessed++;
        return changed;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        if (!_handle.IsInvalid && !_handle.IsClosed)
            _handle.Dispose();
        _cts.Dispose();
    }
}