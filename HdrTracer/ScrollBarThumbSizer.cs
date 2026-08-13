using System.Windows;

namespace HdrTracer.App;

/// <summary>
/// 세로 스크롤바의 손잡이 크기·위치를 직접 계산해 배치하고, 드래그를 실제 스크롤로 연결한다.
/// WPF의 Track은 "보이는 양 ÷ 전체 양"에 정비례해 손잡이를 만들기 때문에,
/// 결과가 수만 건이면 손잡이가 몇 px까지 작아지고 MinHeight로도 커지지 않는다.
/// 여기서는 최소 높이를 보장하고, 스크롤은 ScrollViewer를 직접 움직여 처리한다.
/// </summary>
internal static class ScrollBarThumbSizer
{
    // 손잡이 최소 높이. 너무 크게 잡으면 결과가 수백 건일 때와 수십만 건일 때
    // 모두 이 값에 걸려 크기가 같아 보인다(비례 계산 결과가 최소값보다 작아지므로).
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
                thumb.Height = 0;   // 스크롤할 것이 없음
                return;
            }

            // 손잡이 길이: 표준 비례(viewport/extent)를 그대로 쓰면 결과가 수천 건만 넘어도
            // 거의 항상 최소 크기가 되어 "얼마나 많은지"를 전혀 알려주지 못한다.
            // 제곱근으로 곡선을 완만하게 해서, 결과가 적으면 확실히 길고
            // 많아질수록 부드럽게 짧아지도록 한다.
            double ratio01 = Math.Clamp(viewport / extent, 0, 1);
            double h = Math.Max(MinThumbHeight, track * Math.Sqrt(ratio01));
            h = Math.Min(h, track);
            thumb.Height = h;

            double ratio = sv.VerticalOffset / scrollable;    // 0 ~ 1
            System.Windows.Controls.Canvas.SetTop(thumb, (track - h) * Math.Clamp(ratio, 0, 1));
        }

        sv.ScrollChanged   += (_, _) => Layout();
        bar.SizeChanged    += (_, _) => Layout();
        canvas.SizeChanged += (_, _) => Layout();
        bar.Loaded         += (_, _) => Layout();

        // 손잡이 드래그 → ScrollViewer를 직접 스크롤
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

        // 위/아래 화살표 → 한 줄씩 이동 (누르고 있으면 반복)
        if (bar.Template.FindName("PART_LineUpCustom", bar)
                is System.Windows.Controls.Primitives.RepeatButton up)
            up.Click += (_, _) => sv.LineUp();

        if (bar.Template.FindName("PART_LineDownCustom", bar)
                is System.Windows.Controls.Primitives.RepeatButton down)
            down.Click += (_, _) => sv.LineDown();

        // 트랙 빈 곳 클릭 → 한 페이지 이동.
        // 누르고 있으면 손잡이가 누른 지점에 닿을 때까지 계속 이동한다(탐색기와 동일).
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

        // 클릭 지점이 손잡이(또는 그 안의 막대)인지 판정.
        // Thumb 템플릿 안의 Border가 OriginalSource로 오기 때문에 시각 트리를 거슬러 확인한다.
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
            if (IsInThumb(e.OriginalSource, thumb)) return;   // 손잡이 클릭 → 드래그에 맡김

            pressY = e.GetPosition(canvas).Y;
            PageStep();                       // 첫 클릭은 즉시 한 페이지

            canvas.CaptureMouse();
            pageTimer ??= new System.Windows.Threading.DispatcherTimer();
            pageTimer.Interval = TimeSpan.FromMilliseconds(300);   // 처음엔 잠깐 기다렸다가
            pageTimer.Tick -= OnPageTick;
            pageTimer.Tick += OnPageTick;
            pageTimer.Start();
        };

        void OnPageTick(object? sender, EventArgs e)
        {
            if (pageTimer is null) return;
            pageTimer.Interval = TimeSpan.FromMilliseconds(60);    // 이후엔 빠르게 반복
            PageStep();
        }

        canvas.MouseMove += (_, e) =>
        {
            if (canvas.IsMouseCaptured)
                pressY = e.GetPosition(canvas).Y;                  // 누른 채 움직이면 목표 지점도 따라감
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
