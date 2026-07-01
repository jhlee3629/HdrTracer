using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

/// <summary>
/// 다이얼로그 공통 색/버튼/창. 메인 윈도우처럼 WindowStyle=None + WindowChrome로
/// 제목 표시줄을 직접 그려, 타이틀 바 색(#252526)까지 메인 윈도우와 동일하게 맞춘다.
/// (WPF+WinForms 동시 사용 환경의 타입 모호성을 피하려고 타입을 모두 정규화했다.)
/// </summary>
internal static class DialogTheme
{
    internal static readonly System.Windows.Media.Brush WindowBg  = Hex("#252526"); // 타이틀 바와 동일
    internal static readonly System.Windows.Media.Brush ButtonBg  = Hex("#2D2D30");
    internal static readonly System.Windows.Media.Brush BorderBg  = Hex("#3E3E42");
    internal static readonly System.Windows.Media.Brush HoverBg   = Hex("#3F3F46");
    internal static readonly System.Windows.Media.Brush PressedBg = Hex("#007ACC");
    internal static readonly System.Windows.Media.Brush TextFg    = Hex("#F1F1F1");
    internal static readonly System.Windows.Media.Brush InputBg   = Hex("#1E1E1E");
    internal static readonly System.Windows.Media.Brush CloseHover = Hex("#C42B1C");

    private static System.Windows.Media.Brush Hex(string h)
    {
        var b = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h));
        b.Freeze();
        return b;
    }

    private static System.Windows.Controls.ControlTemplate ButtonTemplate(
        System.Windows.Media.Brush normal, System.Windows.Media.Brush hover,
        System.Windows.Media.Brush pressed, System.Windows.Media.Brush? border, double corner)
    {
        var bd = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        bd.Name = "Bd";
        bd.SetValue(System.Windows.Controls.Border.BackgroundProperty, normal);
        if (border is not null)
        {
            bd.SetValue(System.Windows.Controls.Border.BorderBrushProperty, border);
            bd.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.Thickness(1));
        }
        bd.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(corner));

        var cp = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
        cp.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        cp.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        bd.AppendChild(cp);

        var tpl = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button)) { VisualTree = bd };

        var h = new System.Windows.Trigger { Property = System.Windows.UIElement.IsMouseOverProperty, Value = true };
        h.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Border.BackgroundProperty, hover, "Bd"));
        tpl.Triggers.Add(h);

        var p = new System.Windows.Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        p.Setters.Add(new System.Windows.Setter(System.Windows.Controls.Border.BackgroundProperty, pressed, "Bd"));
        tpl.Triggers.Add(p);

        return tpl;
    }

    /// <summary>다크 톤 버튼(테두리 + 호버/누름 효과).</summary>
    internal static System.Windows.Controls.Button MakeButton(string content)
    {
        var btn = new System.Windows.Controls.Button
        {
            Content = content,
            MinWidth = 88,
            Height = 30,
            Foreground = TextFg,
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Arrow,
            SnapsToDevicePixels = true,
            FocusVisualStyle = null,
            Template = ButtonTemplate(ButtonBg, HoverBg, PressedBg, BorderBg, 3)
        };
        return btn;
    }

    /// <summary>제목 표시줄의 닫기(✕) 버튼.</summary>
    private static System.Windows.Controls.Button MakeCloseButton()
    {
        return new System.Windows.Controls.Button
        {
            Content = "\u2715",
            Width = 44,
            Height = 32,
            Foreground = TextFg,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Arrow,
            FocusVisualStyle = null,
            Template = ButtonTemplate(System.Windows.Media.Brushes.Transparent, CloseHover, CloseHover, null, 0)
        };
    }

    /// <summary>WindowStyle=None + WindowChrome로 OS 제목 표시줄 없는 빈 창을 만든다.</summary>
    internal static System.Windows.Window NewWindow(System.Windows.Window? owner)
    {
        var win = new System.Windows.Window
        {
            Owner = owner,
            WindowStartupLocation = owner is null
                ? System.Windows.WindowStartupLocation.CenterScreen
                : System.Windows.WindowStartupLocation.CenterOwner,
            WindowStyle = System.Windows.WindowStyle.None,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
            ShowInTaskbar = false,
            Background = WindowBg,
            MinWidth = 380,
            MaxWidth = 560
        };

        System.Windows.Shell.WindowChrome.SetWindowChrome(win, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 30,
            ResizeBorderThickness = new System.Windows.Thickness(0),
            GlassFrameThickness = new System.Windows.Thickness(0),
            CornerRadius = new System.Windows.CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        return win;
    }

    /// <summary>커스텀 제목 표시줄(#252526) + 본문을 조립해 창에 채운다.</summary>
    internal static void Compose(System.Windows.Window win, string title, System.Windows.FrameworkElement body)
    {
        var rootGrid = new System.Windows.Controls.Grid();
        rootGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(34) });
        rootGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        // 제목 표시줄 (메인 윈도우와 동일한 #252526)
        var bar = new System.Windows.Controls.Grid { Background = WindowBg };
        bar.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto });
        System.Windows.Controls.Grid.SetRow(bar, 0);

        var titleText = new System.Windows.Controls.TextBlock
        {
            Text = title,
            Foreground = TextFg,
            FontSize = 12,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(12, 0, 0, 0)
        };
        System.Windows.Controls.Grid.SetColumn(titleText, 0);
        bar.Children.Add(titleText);

        var closeBtn = MakeCloseButton();
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
        closeBtn.Click += (_, _) =>
        {
            try { win.DialogResult = false; }
            catch { win.Close(); }
        };
        System.Windows.Controls.Grid.SetColumn(closeBtn, 1);
        bar.Children.Add(closeBtn);

        rootGrid.Children.Add(bar);

        System.Windows.Controls.Grid.SetRow(body, 1);
        rootGrid.Children.Add(body);

        // 부모(다크)와 구분되도록 가는 외곽선
        var outer = new System.Windows.Controls.Border
        {
            BorderBrush = BorderBg,
            BorderThickness = new System.Windows.Thickness(1),
            Child = rootGrid
        };

        win.Content = outer;
    }
}

/// <summary>버튼 글자까지 앱 언어(한/영)를 따르는 확인 대화상자.</summary>
internal static class ConfirmDialog
{
    public static bool Show(System.Windows.Window? owner, string title, string message)
    {
        var win = DialogTheme.NewWindow(owner);

        var body = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(20) };
        body.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        body.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(18) });
        body.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var text = new System.Windows.Controls.TextBlock
        {
            Text = message,
            Foreground = DialogTheme.TextFg,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            FontSize = 13,
            MaxWidth = 500
        };
        System.Windows.Controls.Grid.SetRow(text, 0);
        body.Children.Add(text);

        var btnRow = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        System.Windows.Controls.Grid.SetRow(btnRow, 2);

        var okBtn = DialogTheme.MakeButton(Loc.T("common.ok"));
        okBtn.IsDefault = true;
        okBtn.Margin = new System.Windows.Thickness(0, 0, 8, 0);
        okBtn.Click += (_, _) => win.DialogResult = true;

        var cancelBtn = DialogTheme.MakeButton(Loc.T("common.cancel"));
        cancelBtn.IsCancel = true;
        cancelBtn.Click += (_, _) => win.DialogResult = false;

        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        body.Children.Add(btnRow);

        DialogTheme.Compose(win, title, body);
        return win.ShowDialog() == true;
    }
}

/// <summary>한 줄 텍스트 입력 대화상자(이름 바꾸기 등). 동일한 다크 스타일.</summary>
internal static class InputDialog
{
    public static string? Show(System.Windows.Window? owner, string title, string prompt, string defaultValue, bool selectAll)
    {
        var win = DialogTheme.NewWindow(owner);
        win.MinWidth = 420;

        var body = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(20) };
        body.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        body.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(8) });
        body.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        body.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(18) });
        body.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

        var label = new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Foreground = DialogTheme.TextFg,
            FontSize = 13
        };
        System.Windows.Controls.Grid.SetRow(label, 0);
        body.Children.Add(label);

        var tb = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            Background = DialogTheme.InputBg,
            Foreground = DialogTheme.TextFg,
            BorderBrush = DialogTheme.BorderBg,
            BorderThickness = new System.Windows.Thickness(1),
            CaretBrush = DialogTheme.TextFg,
            Padding = new System.Windows.Thickness(6, 4, 6, 4),
            FontSize = 13,
            MinWidth = 380
        };
        System.Windows.Controls.Grid.SetRow(tb, 2);
        body.Children.Add(tb);

        var btnRow = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        System.Windows.Controls.Grid.SetRow(btnRow, 4);

        var okBtn = DialogTheme.MakeButton(Loc.T("common.ok"));
        okBtn.IsDefault = true;
        okBtn.Margin = new System.Windows.Thickness(0, 0, 8, 0);
        okBtn.Click += (_, _) => win.DialogResult = true;

        var cancelBtn = DialogTheme.MakeButton(Loc.T("common.cancel"));
        cancelBtn.IsCancel = true;
        cancelBtn.Click += (_, _) => win.DialogResult = false;

        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        body.Children.Add(btnRow);

        DialogTheme.Compose(win, title, body);

        win.Loaded += (_, _) =>
        {
            tb.Focus();
            if (selectAll)
            {
                tb.SelectAll();
            }
            else
            {
                int dot = defaultValue.LastIndexOf('.');
                if (dot > 0) tb.Select(0, dot);
                else tb.SelectAll();
            }
        };

        return win.ShowDialog() == true ? tb.Text.Trim() : null;
    }
}
