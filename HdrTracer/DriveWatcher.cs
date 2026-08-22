using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.App;

public sealed class DriveWatcher : IDisposable
{
    public event Action<string>? DriveArrived;   
    public event Action<string>? DriveRemoved;   
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

    private readonly Dictionary<string, IntPtr> _notify =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IntPtr, string> _notifyToDrive = new();
    private readonly object _lock = new();

    public void AttachTo(HwndSource source)
    {
        _source = source;
        _hwnd = source.Handle;
        _source.AddHook(WndProc);
    }

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
                    DriveQueryRemove?.Invoke(drive); 
            }
            return IntPtr.Zero;
        }

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
