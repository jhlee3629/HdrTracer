using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace HdrTracer.App;

public partial class MainWindow
{
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE  = 0x0232;

    private bool _popupHookInstalled;
    private bool _inMoveLoop;
    private Rect _rectAtMoveStart;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        InstallHistoryPopupAutoClose();
    }

    private void InstallHistoryPopupAutoClose()
    {
        if (_popupHookInstalled) return;
        if (PresentationSource.FromVisual(this) is not HwndSource src) return;

        PreviewMouseDown += ClosePopupOnOutsideClick;

        src.AddHook(MoveLoopHook);

        _popupHookInstalled = true;
    }

    private void ClosePopupOnOutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryPopup is null || !HistoryPopup.IsOpen) return;

        var src = e.OriginalSource as DependencyObject;

        if (IsWithin(src, HistoryPopup)) return;

        if (IsWithin(src, SearchBox)) return;

        HistoryPopup.IsOpen = false;
    }

    private static bool IsWithin(DependencyObject? src, DependencyObject? target)
    {
        if (src is null || target is null) return false;

        for (int guard = 0; src is not null && guard < 200; guard++)
        {
            if (ReferenceEquals(src, target)) return true;

            DependencyObject? next = null;
            if (src is Visual or System.Windows.Media.Media3D.Visual3D)
                next = VisualTreeHelper.GetParent(src);

            next ??= LogicalTreeHelper.GetParent(src);
            src = next;
        }
        return false;
    }

    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private bool _captionPressed;

    private IntPtr MoveLoopHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCLBUTTONDOWN)
        {
            _captionPressed = true;

            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_captionPressed) return;
                _captionPressed = false;

                if (HistoryPopup is not null && HistoryPopup.IsOpen)
                    HistoryPopup.IsOpen = false;
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        else if (msg == WM_ENTERSIZEMOVE)
        {
            _captionPressed = false;         
            _inMoveLoop = true;
            _rectAtMoveStart = CurrentWindowRect();
        }
        else if (msg == WM_EXITSIZEMOVE && _inMoveLoop)
        {
            _inMoveLoop = false;

            var now = CurrentWindowRect();
            bool moved =
                Math.Abs(now.X      - _rectAtMoveStart.X)      > 0.5 ||
                Math.Abs(now.Y      - _rectAtMoveStart.Y)      > 0.5 ||
                Math.Abs(now.Width  - _rectAtMoveStart.Width)  > 0.5 ||
                Math.Abs(now.Height - _rectAtMoveStart.Height) > 0.5;

            if (!moved && HistoryPopup is not null && HistoryPopup.IsOpen)
                HistoryPopup.IsOpen = false;
        }

        return IntPtr.Zero;
    }

    private Rect CurrentWindowRect()
    {
        try
        {
            if (WindowState == WindowState.Maximized)
                return new Rect(0, 0, ActualWidth, ActualHeight);

            return new Rect(Left, Top, ActualWidth, ActualHeight);
        }
        catch
        {
            return Rect.Empty;
        }
    }
}
