using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HdrTracer.App;

/// <summary>
/// Windows 시스템 전역 단축키.
/// RegisterHotKey API를 사용하여 어디서든 키 입력을 받는다.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    [Flags]
    public enum Modifiers : uint
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8
    }

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x9001;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private HwndSource? _source;
    private IntPtr _hwnd;
    private bool _registered;

    public event EventHandler? Pressed;

    public GlobalHotkey(Window window)
    {
        _window = window;
    }

    /// <summary>단축키 등록. virtualKey는 System.Windows.Forms.Keys 또는 win32 vk 코드.</summary>
    public bool Register(Modifiers modifiers, uint virtualKey)
    {
        // 윈도우 핸들이 만들어진 후에야 메시지 후킹 가능
        var helper = new WindowInteropHelper(_window);
        _hwnd = helper.Handle;
        if (_hwnd == IntPtr.Zero)
        {
            // 아직 윈도우 핸들 없음 — SourceInitialized 이후 다시 시도
            return false;
        }

        _source = HwndSource.FromHwnd(_hwnd);
        if (_source is null) return false;

        _source.AddHook(WndProc);

        _registered = RegisterHotKey(_hwnd, HOTKEY_ID, (uint)modifiers, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _registered = false;
        }
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }
}