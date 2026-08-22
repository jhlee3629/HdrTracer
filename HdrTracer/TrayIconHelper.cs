using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

public sealed class TrayIconHelper : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly Window _window;

    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler<int>? PinnedSearchRequested;

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

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                ToggleWindow();
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Opening += (_, _) => RebuildMenu(menu);
        RebuildMenu(menu); 
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void RebuildMenu(System.Windows.Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var showItem = menu.Items.Add(Loc.T("tray.open"));
        showItem.Click += (_, _) => ShowWindow();

        var pinnedRoot = new System.Windows.Forms.ToolStripMenuItem(Loc.T("tray.pinned"));
        var pinned = PinnedSearchesProvider?.Invoke();
        if (pinned is { Count: > 0 })
        {
            for (int i = 0; i < pinned.Count; i++)
            {
                int idx = i; 
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
        try
        {
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

        return SystemIcons.Application;
    }

    public void ShowWindow()
    {
        if (_window.Visibility != Visibility.Visible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
        ForceForeground();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

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
        catch { }
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

    public void ToggleWindow()
    {
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
