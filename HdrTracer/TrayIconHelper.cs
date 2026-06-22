using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;

namespace HdrTracer.App;

/// <summary>
/// 시스템 트레이 아이콘 관리.
/// WinForms의 NotifyIcon을 WPF에서 사용.
/// </summary>
public sealed class TrayIconHelper : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly Window _window;

    public event EventHandler? ExitRequested;

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

        // 우클릭 메뉴
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var showItem = menu.Items.Add(HdrTracer.Core.Localization.T("tray.open"));
        showItem.Click += (_, _) => ShowWindow();
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exitItem = menu.Items.Add(HdrTracer.Core.Localization.T("tray.exit"));
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _notifyIcon.ContextMenuStrip = menu;
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