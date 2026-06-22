using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.App;

/// <summary>
/// 파일 종류별 시스템 아이콘을 가져와 캐싱한다.
/// 같은 확장자는 한 번만 시스템에 물어보고, 그 결과를 모든 동일 확장자 파일이 공유한다.
/// </summary>
public static class IconCache
{
    // 확장자(소문자) → 아이콘
    private static readonly ConcurrentDictionary<string, ImageSource?> _byExt = new();

    // 폴더 아이콘 (단일)
    private static ImageSource? _folderIcon;

    // 일반 파일 아이콘 (확장자 모를 때 fallback)
    private static ImageSource? _genericFileIcon;

    /// <summary>경로/종류를 보고 적절한 아이콘을 반환. 캐시에 있으면 즉시.</summary>
    public static ImageSource? GetIcon(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return _folderIcon ??= LoadFolderIcon();
        }

        // 확장자만으로 캐싱 — 모든 .png는 같은 아이콘
        var ext = Path.GetExtension(path).ToLowerInvariant();

        // .exe, .lnk, .ico 등은 파일별로 아이콘이 다를 수 있지만,
        // 일관성과 성능을 위해 확장자 단위로 통일.
        if (string.IsNullOrEmpty(ext))
        {
            return _genericFileIcon ??= LoadIconForPath(path, useFileAttribute: true);
        }

        return _byExt.GetOrAdd(ext, e => LoadIconForExt(e));
    }

    private static ImageSource? LoadFolderIcon()
    {
        // 임의의 디렉토리 경로로 SHGetFileInfo 호출 (실제 존재 안 해도 됨)
        return LoadIconForPath("dummy", useFileAttribute: true, isDirectory: true);
    }

    private static ImageSource? LoadIconForExt(string ext)
    {
        // "fake.ext" 같은 가상 파일명으로 SHGFI_USEFILEATTRIBUTES 사용 — 디스크 접근 없음
        return LoadIconForPath("fake" + ext, useFileAttribute: true);
    }

    private static ImageSource? LoadIconForPath(string path, bool useFileAttribute, bool isDirectory = false)
    {
        try
        {
            uint flags = SHGFI_ICON | SHGFI_SMALLICON;
            uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            if (useFileAttribute) flags |= SHGFI_USEFILEATTRIBUTES;

            var info = new SHFILEINFO();
            IntPtr res = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze(); // 다른 스레드에서도 안전하게 쓸 수 있게
                return src;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    // ── P/Invoke ──
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}