using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.App;

public static class IconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _byExt = new();

    private static ImageSource? _folderIcon;

    private static ImageSource? _genericFileIcon;

    public static ImageSource? GetIcon(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return _folderIcon ??= LoadFolderIcon();
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (string.IsNullOrEmpty(ext))
        {
            return _genericFileIcon ??= LoadIconForPath(path, useFileAttribute: true);
        }

        return _byExt.GetOrAdd(ext, e => LoadIconForExt(e));
    }

    private static ImageSource? LoadFolderIcon()
    {
        return LoadIconForPath("dummy", useFileAttribute: true, isDirectory: true);
    }

    private static ImageSource? LoadIconForExt(string ext)
    {
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