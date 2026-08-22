using System.Runtime.InteropServices;

namespace HdrTracer.Core;

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

    public static bool IsHiddenSystem(string path)
    {
        if (!GetFileAttributesEx(path, GET_FILEEX_INFO_LEVELS.GetFileExInfoStandard, out var data))
            return false;
        return (data.dwFileAttributes & FILE_ATTRIBUTE_HIDDEN) != 0
            && (data.dwFileAttributes & FILE_ATTRIBUTE_SYSTEM) != 0;
    }

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