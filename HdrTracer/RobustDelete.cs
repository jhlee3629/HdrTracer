using System.Runtime.InteropServices;
using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

internal static class RobustDelete
{
    internal enum Cause
    {
        None,
        NotFound, 
        Locked,   
        AccessDenied, 
        BadName,      
        NotEmpty,     
        Protected,    
        Other
    }

    internal sealed class ItemResult
    {
        public required string Path { get; init; }
        public bool Success { get; set; }
        public bool Permanent { get; set; } 
        public Cause Cause { get; set; }
        public int Win32Error { get; set; }

        public string? BlockingPath { get; set; }

        public List<string> LockingProcesses { get; } = new();
    }

    internal sealed class Report
    {
        public List<ItemResult> Items { get; } = new();
        public HashSet<string> DeletedPaths { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public int OkCount { get; set; }
        public int FailCount { get; set; }
        public int PermanentCount { get; set; }
    }

    public static Report Run(System.Windows.Window owner,
                             IReadOnlyList<string> paths,
                             Func<string, bool> isDangerous)
    {
        var report = new Report();
        var needPermanent = new List<ItemResult>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            var item = new ItemResult { Path = path };
            report.Items.Add(item);

            uint attrs = GetAttributes(path);
            if (attrs == INVALID_FILE_ATTRIBUTES)
            {
                item.Success = true; 
                report.DeletedPaths.Add(path);
                continue;
            }

            bool isDir = (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0;

            if ((attrs & FILE_ATTRIBUTE_READONLY) != 0)
                ClearReadOnly(path);

            Cause probe = Probe(path, out int probeErr);

            if (probe == Cause.NotFound)
            {
                item.Success = true;
                report.DeletedPaths.Add(path);
                continue;
            }

            if (probe == Cause.Locked)
            {
                item.Cause = Cause.Locked;
                item.Win32Error = probeErr;
                FillLockHolders(item, path);
                continue;
            }
            if (probe == Cause.AccessDenied)
            {
                item.Cause = Cause.AccessDenied;
                item.Win32Error = probeErr;
                continue;
            }

            if (!IsShellUnsafe(path))
            {
                if (TryRecycle(path, isDir, out Cause rc, out int rerr))
                {
                    item.Success = true;
                    report.DeletedPaths.Add(path);
                    continue;
                }

                item.Cause = rc;
                item.Win32Error = rerr;

                if (rc == Cause.Locked)
                {
                    FillLockHolders(item, path);
                    continue;
                }
            }
            else
            {
                item.Cause = Cause.BadName;
            }

            if (isDangerous(path))
            {
                item.Cause = Cause.Protected;
                continue;
            }

            needPermanent.Add(item);
        }

        if (needPermanent.Count > 0 && AskPermanent(owner, needPermanent.Count))
        {
            foreach (var item in needPermanent)
            {
                if (DeletePermanently(item.Path, out int err, out string? blockedAt))
                {
                    item.Success = true;
                    item.Permanent = true;
                    item.Cause = Cause.None;
                    report.DeletedPaths.Add(item.Path);
                }
                else
                {
                    item.Cause = ClassifyWin32(err);
                    item.Win32Error = err;
                    item.BlockingPath = blockedAt is null ? null : Unext(blockedAt);

                    if (item.Cause == Cause.Locked)
                        FillLockHolders(item, item.BlockingPath ?? item.Path);
                }
            }
        }

        foreach (var it in report.Items)
        {
            if (it.Success) { report.OkCount++; if (it.Permanent) report.PermanentCount++; }
            else report.FailCount++;
        }
        return report;
    }

    public static void ShowFailureReport(System.Windows.Window owner, Report report)
    {
        var failed = report.Items.Where(i => !i.Success).ToList();
        if (failed.Count == 0) return;

        var sb = new System.Text.StringBuilder();

        foreach (var group in failed.GroupBy(i => i.Cause)
                                    .OrderByDescending(g => g.Count()))
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append('[').Append(CauseText(group.Key)).Append("] ")
              .Append(group.Count()).Append('\n');

            const int previewMax = 8;
            foreach (var it in group.Take(previewMax))
            {
                sb.Append("  · ").Append(Leaf(it.Path)).Append('\n');

                if (it.BlockingPath is not null &&
                    !string.Equals(it.BlockingPath, it.Path, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("      ↳ ").Append(T("rd.blockedAt")).Append(' ')
                      .Append(Shorten(it.BlockingPath, 56)).Append('\n');
                }

                if (it.Cause == Cause.Locked)
                {
                    if (it.LockingProcesses.Count > 0)
                    {
                        foreach (var p in it.LockingProcesses.Take(5))
                            sb.Append("      ▸ ").Append(p).Append('\n');
                    }
                    else
                    {
                        sb.Append("      ▸ ").Append(T("rd.lock.unknown")).Append('\n');
                    }
                }
            }
            if (group.Count() > previewMax)
                sb.Append("  · …").Append(group.Count() - previewMax).Append('\n');

            sb.Append("  → ").Append(HintText(group.Key)).Append('\n');
        }

        InfoDialog.Show(owner, T("rd.report.title"), sb.ToString().TrimEnd('\n'));
    }

    private static string Leaf(string path)
    {
        string n;
        try { n = System.IO.Path.GetFileName(path.TrimEnd('\\')); }
        catch { n = path; }
        if (string.IsNullOrEmpty(n)) n = path;
        return Shorten(n, 50);
    }

    private static string Shorten(string s, int max)
    {
        if (s.Length <= max) return s;
        int half = max / 2 - 1;
        if (half < 1) return s;
        return s[..half] + "…" + s[^half..];
    }

    private static bool TryRecycle(string path, bool isDir, out Cause cause, out int win32Error)
    {
        cause = Cause.None;
        win32Error = 0;

        try
        {
            if (isDir)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            if (GetAttributes(path) == INVALID_FILE_ATTRIBUTES) return true;

            cause = Cause.Other;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            cause = Cause.AccessDenied;
            win32Error = ERROR_ACCESS_DENIED;
            return false;
        }
        catch (Exception ex)
        {
            if (GetAttributes(path) == INVALID_FILE_ATTRIBUTES) return true;

            if ((ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000))
            {
                win32Error = ex.HResult & 0xFFFF;
                cause = ClassifyWin32(win32Error);
            }
            else
            {
                cause = Cause.Other;
            }
            return false;
        }
    }

    private static Cause Probe(string path, out int win32Error)
    {
        win32Error = 0;

        IntPtr h = CreateFileW(
            Ext(path),
            DELETE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
            IntPtr.Zero);

        if (h == INVALID_HANDLE_VALUE)
        {
            win32Error = Marshal.GetLastWin32Error();
            return ClassifyWin32(win32Error);
        }

        CloseHandle(h);
        return Cause.None;
    }

    private static Cause ClassifyWin32(int err) => err switch
    {
        0                          => Cause.None,
        ERROR_FILE_NOT_FOUND       => Cause.NotFound,
        ERROR_PATH_NOT_FOUND       => Cause.NotFound,
        ERROR_ACCESS_DENIED        => Cause.AccessDenied,
        ERROR_SHARING_VIOLATION    => Cause.Locked,
        ERROR_LOCK_VIOLATION       => Cause.Locked,
        ERROR_INVALID_NAME         => Cause.BadName,
        ERROR_FILENAME_EXCED_RANGE => Cause.BadName,
        ERROR_BAD_PATHNAME         => Cause.BadName,
        ERROR_DIR_NOT_EMPTY        => Cause.NotEmpty,
        _                          => Cause.Other
    };

    private static bool IsShellUnsafe(string path)
    {
        if (path.Length >= MAX_PATH_SAFE) return true;

        string leaf;
        try { leaf = System.IO.Path.GetFileName(path.TrimEnd('\\')); }
        catch { return true; }
        if (leaf.Length == 0) return false;

        char last = leaf[^1];
        return last == ' ' || last == '.';
    }

    private static void FillLockHolders(ItemResult item, string path)
    {
        try
        {
            foreach (var name in FindLockHolders(path))
                if (!item.LockingProcesses.Contains(name))
                    item.LockingProcesses.Add(name);
        }
        catch
        {
            
        }
    }

    private const int LockQueryFileMax = 256;

    private static List<string> FindLockHolders(string path)
    {
        var result = new List<string>();

        var files = CollectForLockQuery(path, LockQueryFileMax);
        if (files.Count == 0) return result;

        var key = new System.Text.StringBuilder(CCH_RM_SESSION_KEY + 1);
        if (RmStartSession(out uint session, 0, key) != 0) return result;

        try
        {
            if (RmRegisterResources(session, (uint)files.Count, files.ToArray(),
                                    0, null, 0, null) != 0)
                return result;

            uint needed = 0, count = 0, reasons = 0;
            int rc = RmGetList(session, out needed, ref count, null, ref reasons);

            if (rc == ERROR_MORE_DATA && needed > 0)
            {
                var infos = new RM_PROCESS_INFO[needed];
                count = needed;
                rc = RmGetList(session, out needed, ref count, infos, ref reasons);
                if (rc == 0)
                {
                    for (int i = 0; i < count && i < infos.Length; i++)
                    {
                        string name = infos[i].strAppName;
                        if (string.IsNullOrWhiteSpace(name))
                            name = infos[i].strServiceShortName;
                        if (string.IsNullOrWhiteSpace(name)) name = "?";

                        result.Add($"{name} (PID {infos[i].Process.dwProcessId})");
                    }
                }
            }
        }
        finally
        {
            RmEndSession(session);
        }

        return result;
    }

    private static List<string> CollectForLockQuery(string path, int max)
    {
        var list = new List<string>();

        uint attrs = GetAttributes(path);
        if (attrs == INVALID_FILE_ATTRIBUTES) return list;

        if (path.Length >= MAX_PATH_SAFE) return list;

        if ((attrs & FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            list.Add(path);
            return list;
        }

        if ((attrs & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return list;

        CollectFilesRecursive(path, list, max, 0);
        return list;
    }

    private static void CollectFilesRecursive(string dir, List<string> list, int max, int depth)
    {
        if (list.Count >= max || depth > 8) return;

        IntPtr find = FindFirstFileW(Ext(dir) + @"\*", out WIN32_FIND_DATAW fd);
        if (find == INVALID_HANDLE_VALUE) return;

        try
        {
            do
            {
                if (list.Count >= max) return;

                string name = fd.cFileName;
                if (name is "." or "..") continue;

                string child = dir + @"\" + name;
                if (child.Length >= MAX_PATH_SAFE) continue;

                bool isDir = (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                bool isLink = (fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;

                if (isDir && !isLink)
                    CollectFilesRecursive(child, list, max, depth + 1);
                else if (!isDir)
                    list.Add(child);
            }
            while (FindNextFileW(find, out fd));
        }
        finally
        {
            FindClose(find);
        }
    }

    private static bool DeletePermanently(string path, out int win32Error, out string? blockedAt)
    {
        win32Error = 0;
        blockedAt = null;
        string ext = Ext(path);

        uint attrs = GetFileAttributesW(ext);
        if (attrs == INVALID_FILE_ATTRIBUTES)
        {
            win32Error = Marshal.GetLastWin32Error();
            return win32Error is ERROR_FILE_NOT_FOUND or ERROR_PATH_NOT_FOUND;
        }

        if ((attrs & FILE_ATTRIBUTE_READONLY) != 0)
            SetFileAttributesW(ext, attrs & ~FILE_ATTRIBUTE_READONLY);

        bool isDir = (attrs & FILE_ATTRIBUTE_DIRECTORY) != 0;
        bool isLink = (attrs & FILE_ATTRIBUTE_REPARSE_POINT) != 0;

        if (!isDir)
        {
            if (DeleteFileW(ext)) return true;
            win32Error = Marshal.GetLastWin32Error();
            blockedAt = ext;
            return false;
        }

        if (isLink)
        {
            if (RemoveDirectoryW(ext)) return true;
            win32Error = Marshal.GetLastWin32Error();
            blockedAt = ext;
            return false;
        }

        return DeleteTree(ext, 0, out win32Error, out blockedAt);
    }

    private const int MaxDepth = 512;

    private static bool DeleteTree(string extDir, int depth, out int win32Error, out string? blockedAt)
    {
        win32Error = 0;
        blockedAt = null;

        if (depth > MaxDepth)
        {
            win32Error = ERROR_DIR_NOT_EMPTY;
            blockedAt = extDir;
            return false;
        }

        IntPtr find = FindFirstFileW(extDir + @"\*", out WIN32_FIND_DATAW fd);
        if (find != INVALID_HANDLE_VALUE)
        {
            try
            {
                do
                {
                    string name = fd.cFileName;
                    if (name is "." or "..") continue;

                    string child = extDir + @"\" + name;

                    if ((fd.dwFileAttributes & FILE_ATTRIBUTE_READONLY) != 0)
                        SetFileAttributesW(child, fd.dwFileAttributes & ~FILE_ATTRIBUTE_READONLY);

                    bool childIsDir = (fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                    bool childIsLink = (fd.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;

                    if (childIsDir && !childIsLink)
                    {
                        if (!DeleteTree(child, depth + 1, out win32Error, out blockedAt)) return false;
                    }
                    else if (childIsDir)
                    {
                        if (!RemoveDirectoryW(child))
                        { win32Error = Marshal.GetLastWin32Error(); blockedAt = child; return false; }
                    }
                    else
                    {
                        if (!DeleteFileW(child))
                        { win32Error = Marshal.GetLastWin32Error(); blockedAt = child; return false; }
                    }
                }
                while (FindNextFileW(find, out fd));
            }
            finally
            {
                FindClose(find);
            }
        }

        if (RemoveDirectoryW(extDir)) return true;
        win32Error = Marshal.GetLastWin32Error();
        blockedAt = extDir;
        return false;
    }

    private static string Ext(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }

    private static string Unext(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }

    private static uint GetAttributes(string path) => GetFileAttributesW(Ext(path));

    private static void ClearReadOnly(string path)
    {
        string ext = Ext(path);
        uint a = GetFileAttributesW(ext);
        if (a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_READONLY) != 0)
            SetFileAttributesW(ext, a & ~FILE_ATTRIBUTE_READONLY);
    }

    private static string T(string key) => Loc.T(key);

    private static string CauseText(Cause c) => Loc.T(c switch
    {
        Cause.Locked       => "rd.cause.locked",
        Cause.AccessDenied => "rd.cause.denied",
        Cause.BadName      => "rd.cause.badname",
        Cause.NotEmpty     => "rd.cause.notempty",
        Cause.NotFound     => "rd.cause.notfound",
        Cause.Protected    => "rd.cause.protected",
        _                  => "rd.cause.other"
    });

    private static string HintText(Cause c) => Loc.T(c switch
    {
        Cause.Locked       => "rd.hint.locked",
        Cause.AccessDenied => "rd.hint.denied",
        Cause.BadName      => "rd.hint.badname",
        Cause.NotEmpty     => "rd.hint.notempty",
        Cause.Protected    => "rd.hint.protected",
        Cause.NotFound     => "rd.hint.notfound",
        _                  => "rd.hint.other"
    });

    private static bool AskPermanent(System.Windows.Window owner, int count)
        => ConfirmDialog.Show(owner, T("rd.perm.title"),
                              string.Format(T("rd.perm.msg"), count));

    private const int MAX_PATH_SAFE = 250;   

    private const uint INVALID_FILE_ATTRIBUTES      = 0xFFFFFFFF;
    private const uint FILE_ATTRIBUTE_READONLY      = 0x00000001;
    private const uint FILE_ATTRIBUTE_DIRECTORY     = 0x00000010;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400;

    private const uint DELETE                       = 0x00010000;
    private const uint FILE_SHARE_READ              = 0x00000001;
    private const uint FILE_SHARE_WRITE             = 0x00000002;
    private const uint FILE_SHARE_DELETE            = 0x00000004;
    private const uint OPEN_EXISTING                = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS   = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    private const int ERROR_FILE_NOT_FOUND       = 2;
    private const int ERROR_PATH_NOT_FOUND       = 3;
    private const int ERROR_ACCESS_DENIED        = 5;
    private const int ERROR_SHARING_VIOLATION    = 32;
    private const int ERROR_LOCK_VIOLATION       = 33;
    private const int ERROR_INVALID_NAME         = 123;
    private const int ERROR_DIR_NOT_EMPTY        = 145;
    private const int ERROR_BAD_PATHNAME         = 161;
    private const int ERROR_FILENAME_EXCED_RANGE = 206;
    private const int ERROR_MORE_DATA            = 234;

    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]  public string cAlternateFileName;
    }

    private const int CCH_RM_SESSION_KEY  = 32;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(
        out uint pSessionHandle, int dwSessionFlags, System.Text.StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles, string[]? rgsFilenames,
        uint nApplications, RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributesW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveDirectoryW(string lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATAW lpFindFileData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr hFindFile);
}
