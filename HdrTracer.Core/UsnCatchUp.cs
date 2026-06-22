using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.Core;

/// <summary>
/// 캐시된 인덱스를 로드한 후, 마지막 USN부터 현재까지의
/// 파일 시스템 변경분을 적용한다.
/// </summary>
public static class UsnCatchUp
{
    /// <summary>변경분을 적용하고 (새 LastUsn, 변경 건수)를 반환.</summary>
    public static (long NewLastUsn, int Changes) Apply(FileIndex index, string driveLetter, ulong journalId, long fromUsn)
    {
        // 캐시에 저널 정보가 없으면 catch-up 불가 → 풀 빌드 강제
        if (journalId == 0)
            throw new InvalidOperationException("No journal info in cache");

        using var handle = Native.CreateFile(
            $@"\\.\{driveLetter}",
            Native.GENERIC_READ,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Native.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        // Journal ID가 같은지 검증
        var jdata = new Native.USN_JOURNAL_DATA_V0();
        bool ok = Native.DeviceIoControlQuery(
            handle, Native.FSCTL_QUERY_USN_JOURNAL,
            IntPtr.Zero, 0,
            out jdata, (uint)Marshal.SizeOf<Native.USN_JOURNAL_DATA_V0>(),
            out _, IntPtr.Zero);
        if (!ok)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        if (jdata.UsnJournalID != journalId)
        {
            // 저널이 재생성됨 → 캐시 무효
            throw new InvalidOperationException("USN Journal ID mismatch");
        }

        // 저널이 너무 많이 쌓여서 fromUsn이 폐기됐는지 확인
        if (fromUsn < jdata.LowestValidUsn)
        {
            throw new InvalidOperationException("Cached USN is too old");
        }

        long currentUsn = fromUsn;
        long endUsn = jdata.NextUsn;
        int changes = 0;

        const int BufferSize = 256 * 1024;
        var buffer = new byte[BufferSize];

        unsafe
        {
            fixed (byte* outPtr = buffer)
            {
                while (currentUsn < endUsn)
                {
                    var readData = new Native.READ_USN_JOURNAL_DATA_V0
                    {
                        StartUsn          = currentUsn,
                        ReasonMask        = 0xFFFFFFFF,
                        ReturnOnlyOnClose = 1,
                        Timeout           = 0,
                        BytesToWaitFor    = 0,
                        UsnJournalID      = journalId
                    };

                    bool okRead = Native.DeviceIoControlReadJournal(
                        handle,
                        Native.FSCTL_READ_USN_JOURNAL,
                        ref readData,
                        (uint)Marshal.SizeOf<Native.READ_USN_JOURNAL_DATA_V0>(),
                        (IntPtr)outPtr,
                        BufferSize,
                        out uint bytesReturned,
                        IntPtr.Zero);

                    if (!okRead || bytesReturned < 8) break;

                    long newCurrent = *(long*)outPtr;
                    if (newCurrent <= currentUsn) break;
                    currentUsn = newCurrent;

                    byte* p = outPtr + 8;
                    byte* end = outPtr + bytesReturned;

                    while (p < end)
                    {
                        var rec = (Native.USN_RECORD_V2*)p;
                        if (rec->RecordLength == 0) break;

                        ProcessRecord(index, rec, p);
                        changes++;
                        p += rec->RecordLength;
                    }
                }
            }
        }

        return (endUsn, changes);
    }

    private static unsafe void ProcessRecord(FileIndex index, Native.USN_RECORD_V2* rec, byte* recStart)
    {
        uint reason = rec->Reason;
        char* namePtr = (char*)(recStart + rec->FileNameOffset);
        int nameLen = rec->FileNameLength / 2;
        bool isDir = (rec->FileAttributes & 0x10) != 0;
        bool isHiddenSystem = (rec->FileAttributes & 0x2) != 0 && (rec->FileAttributes & 0x4) != 0;

        if ((reason & Native.USN_REASON_FILE_DELETE) != 0)
        {
            index.RemoveByMftRef(rec->FileReferenceNumber);
        }
        else if ((reason & Native.USN_REASON_RENAME_NEW_NAME) != 0)
        {
            index.RenameByMftRef(rec->FileReferenceNumber, namePtr, nameLen);
        }
        else if ((reason & Native.USN_REASON_FILE_CREATE) != 0)
        {
            index.AddAndLink(namePtr, nameLen,
                rec->FileReferenceNumber, rec->ParentFileReferenceNumber,
                isDir, 0, isHiddenSystem);
        }
    }
}