using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.Core;

internal static class Native
{
    public const uint GENERIC_READ             = 0x80000000;
    public const uint FILE_SHARE_READ          = 0x00000001;
    public const uint FILE_SHARE_WRITE         = 0x00000002;
    public const uint OPEN_EXISTING            = 3;
    public const uint FSCTL_ENUM_USN_DATA      = 0x000900b3;
    public const uint FSCTL_QUERY_USN_JOURNAL  = 0x000900f4;
    public const uint FSCTL_READ_USN_JOURNAL   = 0x000900bb;

    public const uint USN_REASON_FILE_CREATE     = 0x00000100;
    public const uint USN_REASON_FILE_DELETE     = 0x00000200;
    public const uint USN_REASON_RENAME_OLD_NAME = 0x00001000;
    public const uint USN_REASON_RENAME_NEW_NAME = 0x00002000;
    public const uint USN_REASON_CLOSE           = 0x80000000;
    public const uint FSCTL_CREATE_USN_JOURNAL = 0x000900e7;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        ref MFT_ENUM_DATA_V0 inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    public static extern bool DeviceIoControlQuery(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        out USN_JOURNAL_DATA_V0 outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    public static extern bool DeviceIoControlReadJournal(
        SafeFileHandle device,
        uint ioControlCode,
        ref READ_USN_JOURNAL_DATA_V0 inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    public struct MFT_ENUM_DATA_V0
    {
        public long StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct USN_RECORD_V2
    {
        public uint   RecordLength;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ulong  FileReferenceNumber;
        public ulong  ParentFileReferenceNumber;
        public long   Usn;
        public long   TimeStamp;
        public uint   Reason;
        public uint   SourceInfo;
        public uint   SecurityId;
        public uint   FileAttributes;
        public ushort FileNameLength;
        public ushort FileNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USN_JOURNAL_DATA_V0
    {
        public ulong UsnJournalID;
        public long  FirstUsn;
        public long  NextUsn;
        public long  LowestValidUsn;
        public long  MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct READ_USN_JOURNAL_DATA_V0
    {
        public long  StartUsn;
        public uint  ReasonMask;
        public uint  ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalID;
    }

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
    public static extern bool DeviceIoControlCreateJournal(
        SafeFileHandle device,
        uint ioControlCode,
        ref CREATE_USN_JOURNAL_DATA inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [StructLayout(LayoutKind.Sequential)]
    public struct CREATE_USN_JOURNAL_DATA
    {
        public ulong MaximumSize;
        public ulong AllocationDelta;
    }

    // === Raw MFT 읽기용 ===

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetFilePointerEx(
        SafeFileHandle hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(
        SafeFileHandle hFile,
        IntPtr lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);
}