using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.App;

/// <summary>
/// Windows의 WM_DEVICECHANGE 메시지를 후킹해서 드라이브 마운트/언마운트를 감지하고,
/// 추가로 USN 모니터가 연 볼륨 핸들을 RegisterDeviceNotification(DBT_DEVTYP_HANDLE)로
/// 등록한다. 이렇게 해야 사용자가 "안전 제거"를 시도할 때 Windows가 우리에게
/// DBT_DEVICEQUERYREMOVE를 보내주고, 그때 핸들을 닫아 안전 제거가 성공한다.
/// (단순 볼륨 후킹만으로는 query-remove가 도착하지 않는다 — 이것이 핵심.)
/// </summary>
public sealed class DriveWatcher : IDisposable
{
    public event Action<string>? DriveArrived;   // 인자: "F:"
    public event Action<string>? DriveRemoved;   // 인자: "F:"
    // 안전 제거 시도 직전. 해당 드라이브 핸들을 풀어줄 마지막 기회.
    public event Action<string>? DriveQueryRemove;

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL         = 0x8000;
    private const int DBT_DEVICEQUERYREMOVE     = 0x8001;
    private const int DBT_DEVICEREMOVECOMPLETE  = 0x8004;
    private const int DBT_DEVTYP_VOLUME         = 0x00000002;
    private const int DBT_DEVTYP_HANDLE         = 0x00000006;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HDR
    {
        public uint dbch_size;
        public uint dbch_devicetype;
        public uint dbch_reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_VOLUME
    {
        public uint dbcv_size;
        public uint dbcv_devicetype;
        public uint dbcv_reserved;
        public uint dbcv_unitmask;
        public ushort dbcv_flags;
    }

    // DBT_DEVTYP_HANDLE 알림 등록/수신용 구조체
    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_HANDLE
    {
        public uint dbch_size;
        public uint dbch_devicetype;
        public uint dbch_reserved;
        public IntPtr dbch_handle;
        public IntPtr dbch_hdevnotify;
        public Guid dbch_eventguid;
        public int dbch_nameoffset;
        public byte dbch_data;
        public byte dbch_data1;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr RegisterDeviceNotification(
        IntPtr hRecipient, IntPtr notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    private HwndSource? _source;
    private IntPtr _hwnd = IntPtr.Zero;

    // 드라이브 문자 → 등록 핸들(hdevnotify)
    private readonly Dictionary<string, IntPtr> _notify =
        new(StringComparer.OrdinalIgnoreCase);
    // 등록 핸들(hdevnotify) → 드라이브 문자 (query-remove 수신 시 역추적)
    private readonly Dictionary<IntPtr, string> _notifyToDrive = new();
    private readonly object _lock = new();

    public void AttachTo(HwndSource source)
    {
        _source = source;
        _hwnd = source.Handle;
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// USN 모니터가 연 볼륨 핸들을 등록한다. 이후 그 드라이브의 안전 제거 시도 시
    /// Windows가 query-remove를 우리 창으로 보낸다.
    /// </summary>
    public void RegisterVolumeHandle(string driveLetter, SafeFileHandle volumeHandle)
    {
        if (_hwnd == IntPtr.Zero || volumeHandle is null || volumeHandle.IsInvalid) return;

        var filter = new DEV_BROADCAST_HANDLE
        {
            dbch_devicetype = DBT_DEVTYP_HANDLE,
            dbch_handle = volumeHandle.DangerousGetHandle(),
        };
        filter.dbch_size = (uint)Marshal.SizeOf<DEV_BROADCAST_HANDLE>();

        IntPtr buffer = Marshal.AllocHGlobal((int)filter.dbch_size);
        try
        {
            Marshal.StructureToPtr(filter, buffer, false);
            IntPtr h = RegisterDeviceNotification(_hwnd, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
            if (h != IntPtr.Zero)
            {
                lock (_lock)
                {
                    if (_notify.TryGetValue(driveLetter, out var old) && old != IntPtr.Zero)
                    {
                        UnregisterDeviceNotification(old);
                        _notifyToDrive.Remove(old);
                    }
                    _notify[driveLetter] = h;
                    _notifyToDrive[h] = driveLetter;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>드라이브 알림 등록 해제 (핸들을 닫기 전에 호출).</summary>
    public void UnregisterVolume(string driveLetter)
    {
        lock (_lock)
        {
            if (_notify.TryGetValue(driveLetter, out var h) && h != IntPtr.Zero)
            {
                UnregisterDeviceNotification(h);
                _notifyToDrive.Remove(h);
                _notify.Remove(driveLetter);
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE) return IntPtr.Zero;

        int evt = wParam.ToInt32();
        if (lParam == IntPtr.Zero) return IntPtr.Zero;

        var hdr = Marshal.PtrToStructure<DEV_BROADCAST_HDR>(lParam);

        // 1) 핸들 타입 알림 (우리가 등록한 볼륨 핸들에 대한 query-remove)
        if (hdr.dbch_devicetype == DBT_DEVTYP_HANDLE)
        {
            if (evt == DBT_DEVICEQUERYREMOVE)
            {
                var hh = Marshal.PtrToStructure<DEV_BROADCAST_HANDLE>(lParam);
                string? drive = null;
                lock (_lock)
                {
                    _notifyToDrive.TryGetValue(hh.dbch_hdevnotify, out drive);
                }
                if (drive is not null)
                    DriveQueryRemove?.Invoke(drive);  // 핸들을 닫아줄 기회
            }
            return IntPtr.Zero;
        }

        // 2) 볼륨 타입 알림 (도착/제거완료)
        if (hdr.dbch_devicetype == DBT_DEVTYP_VOLUME)
        {
            if (evt != DBT_DEVICEARRIVAL && evt != DBT_DEVICEREMOVECOMPLETE)
                return IntPtr.Zero;

            var vol = Marshal.PtrToStructure<DEV_BROADCAST_VOLUME>(lParam);
            var letters = UnitMaskToLetters(vol.dbcv_unitmask);
            foreach (var letter in letters)
            {
                if (evt == DBT_DEVICEARRIVAL)
                    DriveArrived?.Invoke(letter);
                else
                    DriveRemoved?.Invoke(letter);
            }
        }

        return IntPtr.Zero;
    }

    private static List<string> UnitMaskToLetters(uint mask)
    {
        var list = new List<string>();
        for (int i = 0; i < 26; i++)
        {
            if ((mask & (1u << i)) != 0)
            {
                char c = (char)('A' + i);
                list.Add(c + ":");
            }
        }
        return list;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var h in _notify.Values)
                if (h != IntPtr.Zero) UnregisterDeviceNotification(h);
            _notify.Clear();
            _notifyToDrive.Clear();
        }
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }
}
