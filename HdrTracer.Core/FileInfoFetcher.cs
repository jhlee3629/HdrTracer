using System.Runtime.InteropServices;

namespace HdrTracer.Core;

/// <summary>
/// 파일 시스템에서 크기와 수정 날짜를 빠르게 가져온다.
/// GetFileAttributesEx는 디렉토리 핸들을 열지 않고 메타데이터만 읽어 매우 빠르다.
/// </summary>
public static class FileInfoFetcher
{
    [StructLayout(LayoutKind.Sequential)]
    private struct WIN32_FILE_ATTRIBUTE_DATA
    {
        public uint dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    private enum GET_FILEEX_INFO_LEVELS
    {
        GetFileExInfoStandard = 0
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileAttributesEx(
        string lpFileName,
        GET_FILEEX_INFO_LEVELS fInfoLevelId,
        out WIN32_FILE_ATTRIBUTE_DATA lpFileInformation);

    public readonly record struct FileInfoResult(long Size, DateTime ModifiedUtc, bool Found);

    public static FileInfoResult Get(string path)
    {
        if (!GetFileAttributesEx(path, GET_FILEEX_INFO_LEVELS.GetFileExInfoStandard, out var data))
            return new FileInfoResult(0, DateTime.MinValue, false);

        long size = ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;

        long fileTime = ((long)data.ftLastWriteTime.dwHighDateTime << 32)
                      | data.ftLastWriteTime.dwLowDateTime;
        DateTime modifiedUtc = DateTime.FromFileTimeUtc(fileTime);

        return new FileInfoResult(size, modifiedUtc, true);
    }

    private const uint FILE_ATTRIBUTE_HIDDEN = 0x2;
    private const uint FILE_ATTRIBUTE_SYSTEM  = 0x4;

    /// <summary>
    /// 경로가 숨김(Hidden) + 시스템(System) 속성을 동시에 가지는지 검사.
    /// 탐색기의 "보호된 운영체제 파일 숨기기"와 같은 기준.
    /// 존재하지 않거나 조회 실패 시 false.
    /// </summary>
    public static bool IsHiddenSystem(string path)
    {
        if (!GetFileAttributesEx(path, GET_FILEEX_INFO_LEVELS.GetFileExInfoStandard, out var data))
            return false;
        return (data.dwFileAttributes & FILE_ATTRIBUTE_HIDDEN) != 0
            && (data.dwFileAttributes & FILE_ATTRIBUTE_SYSTEM) != 0;
    }

    /// <summary>"3 KB", "1.2 MB" 같은 사람 친화적 표시.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "";
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.0} MB";
        double gb = mb / 1024.0;
        return $"{gb:0.00} GB";
    }

    public static string FormatDate(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "";
        var local = utc.ToLocalTime();
        return local.ToString("yy-MM-dd HH:mm");
    }
}