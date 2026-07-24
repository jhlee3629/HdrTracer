using System.Drawing;
using System.IO;
using System.Windows;
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
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    public void HideWindow()
    {
        _window.Hide();
    }

    public void ToggleWindow()
    {
        if (_window.Visibility == Visibility.Visible && _window.IsActive)
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
