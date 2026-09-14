using System.Windows;

namespace HdrTracer.App;

public partial class MainWindow
{
    private const string AppIconPackUri = "pack://application:,,,/Assets/sun.ico";

    private bool _taskbarIconHookAttached;

    private readonly bool _startedAtLogon = Environment.TickCount64 < 3 * 60 * 1000;

    private System.Windows.Threading.DispatcherTimer? _taskbarIconRetryTimer;
    private int _taskbarIconRetryLeft;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        ApplyWindowIcon();
        AttachTaskbarIconHook();

        if (_startedAtLogon) ScheduleTaskbarIconRetry();
    }

    private void ApplyWindowIcon()
    {
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(AppIconPackUri, UriKind.Absolute));
        }
        catch
        {
            
        }
    }

    private void AttachTaskbarIconHook()
    {
        if (_taskbarIconHookAttached) return;
        if (PresentationSource.FromVisual(this) is not System.Windows.Interop.HwndSource src) return;

        src.AddHook(TaskbarIconRestoreHook);
        _taskbarIconHookAttached = true;
    }

    private IntPtr TaskbarIconRestoreHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMsg != 0 && (uint)msg == _taskbarCreatedMsg)
        {
            _ = Dispatcher.BeginInvoke(new Action(ApplyWindowIcon),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
        return IntPtr.Zero;
    }

    private const int TaskbarIconRetryCount = 4;      

    private void ScheduleTaskbarIconRetry()
    {
        _taskbarIconRetryLeft = TaskbarIconRetryCount;

        _taskbarIconRetryTimer ??= new System.Windows.Threading.DispatcherTimer();
        _taskbarIconRetryTimer.Interval = TimeSpan.FromSeconds(2);
        _taskbarIconRetryTimer.Tick -= OnTaskbarIconRetry;
        _taskbarIconRetryTimer.Tick += OnTaskbarIconRetry;
        _taskbarIconRetryTimer.Start();
    }

    private void OnTaskbarIconRetry(object? sender, EventArgs e)
    {
        ApplyWindowIcon();

        int attempt = TaskbarIconRetryCount - _taskbarIconRetryLeft + 1;
        if ((attempt == 2 || attempt == TaskbarIconRetryCount)
            && Visibility == Visibility.Visible
            && ShowInTaskbar)
        {
            RefreshTaskbarButton();
        }

        if (--_taskbarIconRetryLeft <= 0)
        {
            _taskbarIconRetryTimer?.Stop();
            _taskbarIconRetryTimer!.Tick -= OnTaskbarIconRetry;
        }
    }
}
