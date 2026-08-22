using System.Windows;

namespace HdrTracer.App;

public partial class MainWindow
{
    private const string AppIconPackUri = "pack://application:,,,/Assets/sun.ico";

    private bool _taskbarIconHookAttached;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        ApplyWindowIcon();

        AttachTaskbarIconHook();
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
}
