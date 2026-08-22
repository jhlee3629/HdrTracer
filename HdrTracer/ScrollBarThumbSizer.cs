using System.Windows;

namespace HdrTracer.App;

internal static class ScrollBarThumbSizer
{
    private const double MinThumbHeight = 26;

    public static void Attach(System.Windows.Controls.Primitives.ScrollBar bar,
                              System.Windows.Controls.ScrollViewer sv)
    {
        if (bar.Orientation != System.Windows.Controls.Orientation.Vertical) return;

        bar.ApplyTemplate();
        if (bar.Template.FindName("PART_TrackCanvas", bar) is not System.Windows.Controls.Canvas canvas) return;
        if (bar.Template.FindName("PART_ThumbCustom", bar) is not System.Windows.Controls.Primitives.Thumb thumb) return;

        double dragStartY = 0;
        double dragStartOffset = 0;

        void Layout()
        {
            double track = canvas.ActualHeight;
            double extent = sv.ExtentHeight;
            double viewport = sv.ViewportHeight;
            double scrollable = sv.ScrollableHeight;

            if (track <= 0 || extent <= 0 || scrollable <= 0)
            {
                thumb.Height = 0; 
                return;
            }

            double ratio01 = Math.Clamp(viewport / extent, 0, 1);
            double h = Math.Max(MinThumbHeight, track * Math.Sqrt(ratio01));
            h = Math.Min(h, track);
            thumb.Height = h;

            double ratio = sv.VerticalOffset / scrollable;   
            System.Windows.Controls.Canvas.SetTop(thumb, (track - h) * Math.Clamp(ratio, 0, 1));
        }

        sv.ScrollChanged   += (_, _) => Layout();
        bar.SizeChanged    += (_, _) => Layout();
        canvas.SizeChanged += (_, _) => Layout();
        bar.Loaded         += (_, _) => Layout();

        thumb.DragStarted += (_, _) =>
        {
            dragStartY = System.Windows.Input.Mouse.GetPosition(canvas).Y;
            dragStartOffset = sv.VerticalOffset;
        };
        thumb.DragDelta += (_, _) =>
        {
            double track = canvas.ActualHeight;
            double movable = track - thumb.Height;
            double scrollable = sv.ScrollableHeight;
            if (movable <= 0 || scrollable <= 0) return;

            double dy = System.Windows.Input.Mouse.GetPosition(canvas).Y - dragStartY;
            double target = dragStartOffset + (dy / movable) * scrollable;
            sv.ScrollToVerticalOffset(Math.Clamp(target, 0, scrollable));
        };

        if (bar.Template.FindName("PART_LineUpCustom", bar)
                is System.Windows.Controls.Primitives.RepeatButton up)
            up.Click += (_, _) => sv.LineUp();

        if (bar.Template.FindName("PART_LineDownCustom", bar)
                is System.Windows.Controls.Primitives.RepeatButton down)
            down.Click += (_, _) => sv.LineDown();

        System.Windows.Threading.DispatcherTimer? pageTimer = null;
        double pressY = 0;

        void PageStep()
        {
            double top = System.Windows.Controls.Canvas.GetTop(thumb);
            double bottom = top + thumb.Height;

            if (pressY < top) sv.PageUp();
            else if (pressY > bottom) sv.PageDown();
            else pageTimer?.Stop();   // 손잡이가 누른 지점에 닿음 → 멈춤
        }

        static bool IsInThumb(object? src, System.Windows.Controls.Primitives.Thumb t)
        {
            var d = src as System.Windows.DependencyObject;
            while (d is not null)
            {
                if (ReferenceEquals(d, t)) return true;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        canvas.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInThumb(e.OriginalSource, thumb)) return;   

            pressY = e.GetPosition(canvas).Y;
            PageStep();                       

            canvas.CaptureMouse();
            pageTimer ??= new System.Windows.Threading.DispatcherTimer();
            pageTimer.Interval = TimeSpan.FromMilliseconds(300);
            pageTimer.Tick -= OnPageTick;
            pageTimer.Tick += OnPageTick;
            pageTimer.Start();
        };

        void OnPageTick(object? sender, EventArgs e)
        {
            if (pageTimer is null) return;
            pageTimer.Interval = TimeSpan.FromMilliseconds(60); 
            PageStep();
        }

        canvas.MouseMove += (_, e) =>
        {
            if (canvas.IsMouseCaptured)
                pressY = e.GetPosition(canvas).Y;               
        };

        canvas.MouseLeftButtonUp += (_, _) =>
        {
            pageTimer?.Stop();
            if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture();
        };

        canvas.LostMouseCapture += (_, _) => pageTimer?.Stop();

        Layout();
    }
}
