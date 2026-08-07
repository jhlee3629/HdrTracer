using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

/// <summary>
/// 시스템 트레이 아이콘 관리.
/// WinForms의 NotifyIcon을 WPF에서 사용.
/// 우클릭 메뉴는 열 때마다 다시 만든다 — 고정 검색 목록과 언어 전환이 즉시 반영되도록.
/// </summary>
public sealed class TrayIconHelper : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly Window _window;

    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler<int>? PinnedSearchRequested;

    /// <summary>고정 검색 목록 공급자 (MainWindow가 설정의 PinnedSearches를 연결)</summary>
    public Func<IReadOnlyList<string>>? PinnedSearchesProvider { get; set; }

    public TrayIconHelper(Window window)
    {
        _window = window;

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "HdrTracer",
            Visible = true
        };

        // 좌클릭 → 창 토글
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                ToggleWindow();
        };

        // 우클릭 메뉴: 열릴 때마다 최신 상태로 재구성
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Opening += (_, _) => RebuildMenu(menu);
        RebuildMenu(menu);   // 초기 1회 (빈 메뉴로 열리는 것 방지)
        _notifyIcon.ContextMenuStrip = menu;
    }

    /// <summary>열기 / 고정 검색 ▸ / ─ / 설정 / ─ / 종료</summary>
    private void RebuildMenu(System.Windows.Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var showItem = menu.Items.Add(Loc.T("tray.open"));
        showItem.Click += (_, _) => ShowWindow();

        // 고정 검색 서브메뉴 (열 때마다 현재 고정 목록으로 채움)
        var pinnedRoot = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.pinned"));
        var pinned = PinnedSearchesProvider?.Invoke();
        if (pinned is { Count: > 0 })
        {
            for (int i = 0; i < pinned.Count; i++)
            {
                int idx = i;   // 클로저 캡처
                var mi = new System.Windows.Forms.ToolStripMenuItem("\uD83D\uDCCC " + pinned[i]);
                mi.Click += (_, _) => PinnedSearchRequested?.Invoke(this, idx);
                pinnedRoot.DropDownItems.Add(mi);
            }
        }
        else
        {
            pinnedRoot.DropDownItems.Add(
                new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.pinned.empty")) { Enabled = false });
        }
        menu.Items.Add(pinnedRoot);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var settingsItem = menu.Items.Add(Loc.T("tray.settings"));
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = menu.Items.Add(Loc.T("tray.exit"));
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private static Icon LoadIcon()
    {
        // 실행 파일에 포함된 sun.ico 사용
        try
        {
            // pack URI로 리소스에서 읽기
            var uri = new Uri("pack://application:,,,/Assets/sun.ico", UriKind.Absolute);
            var resourceStream = System.Windows.Application.GetResourceStream(uri);
            if (resourceStream is not null)
            {
                using var ms = new MemoryStream();
                resourceStream.Stream.CopyTo(ms);
                ms.Position = 0;
                return new Icon(ms);
            }
        }
        catch { }

        // 실패 시 시스템 기본 아이콘
        return SystemIcons.Application;
    }

    public void ShowWindow()
    {
        if (_window.Visibility != Visibility.Visible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
        ForceForeground();          // 다른 앱이 최상단일 때 Activate()가 무시되는 것 보완
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    /// <summary>
    /// Windows의 포그라운드 잠금 때문에 백그라운드 앱의 Activate()가 거부될 수 있다.
    /// 현재 최상단 창의 입력 스레드에 잠시 붙어서 확실히 앞으로 가져온다.
    /// </summary>
    /// <summary>메인 창이 전역 단축키로 소환될 때도 같은 보완을 쓸 수 있게 공개.</summary>
    public void ForceForegroundPublic() => ForceForeground();

    private void ForceForeground()
    {
        try
        {
            IntPtr hWnd = new WindowInteropHelper(_window).Handle;
            if (hWnd == IntPtr.Zero) return;

            IntPtr fore = Native.GetForegroundWindow();
            if (fore == hWnd) return;

            uint foreThread = Native.GetWindowThreadProcessId(fore, IntPtr.Zero);
            uint myThread = Native.GetCurrentThreadId();

            bool attached = foreThread != 0 && foreThread != myThread
                            && Native.AttachThreadInput(foreThread, myThread, true);
            Native.SetForegroundWindow(hWnd);
            if (attached)
                Native.AttachThreadInput(foreThread, myThread, false);
        }
        catch { /* 실패해도 Activate() 결과에 맡김 */ }
    }

    private static class Native
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }

    public void HideWindow()
    {
        _window.Hide();
    }

    /// <summary>
    /// 창 토글. "숨김 여부"는 WPF의 IsActive가 아니라 실제 최상단 창(포그라운드)으로 판정한다.
    /// (IsActive가 실제 화면 상태와 어긋나 첫 입력이 무시되던 문제 방지)
    /// </summary>
    public void ToggleWindow()
    {
        // 트레이 아이콘 클릭용 토글: 화면에 보이면 숨기고, 아니면 띄운다.
        // "맨 앞인가(GetForegroundWindow)"는 여기서 쓰지 않는다 —
        // 트레이를 클릭하는 순간 포그라운드가 작업 표시줄로 넘어가서
        // 보이는 창도 "숨은 것"으로 판정돼 숨겨지지 않는다.
        bool visibleAndUp = _window.Visibility == Visibility.Visible
                            && _window.WindowState != WindowState.Minimized;

        if (visibleAndUp)
            HideWindow();
        else
            ShowWindow();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
