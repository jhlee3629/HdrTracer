using System.Windows;
using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

// ============================================================================
//  최대화 버튼의 툴팁을 창 상태에 맞게 바꾼다.
//
//  왜 필요한가:
//    ApplyTexts가 MaximizeButton.ToolTip을 항상 tip.maximize("최대화")로 고정한다.
//    그런데 창이 이미 최대화된 상태에서 그 버튼을 누르면 창이 "복원"되므로,
//    툴팁이 실제 동작과 어긋난다. Windows 표준 창도 이때 "복원"으로 바뀐다.
//
//  어떻게 고치는가:
//    ① StateChanged — 최대화/복원될 때 툴팁을 즉시 갱신
//    ② ToolTipOpening — 툴팁이 뜨기 직전에 다시 계산
//       ②가 있는 이유: 언어를 바꾸면 ApplyTexts가 다시 tip.maximize로 덮어쓴다.
//       그 호출 순서에 기대지 않고, 툴팁을 열 때마다 현재 상태·현재 언어로
//       다시 계산하면 어떤 경우에도 어긋나지 않는다.
//
//  기존 파일은 건드리지 않는다. OnInitialized는 아직 재정의된 곳이 없어
//  여기서 안전하게 훅을 걸 수 있다 (OnSourceInitialized는 TaskbarIcon.cs가 쓰고 있음).
// ============================================================================

public partial class MainWindow
{
    private bool _maxTipHooked;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        HookMaximizeTooltip();
    }

    private void HookMaximizeTooltip()
    {
        if (_maxTipHooked) return;
        if (MaximizeButton is null) return;

        StateChanged += (_, _) => UpdateMaximizeTooltip();
        MaximizeButton.ToolTipOpening += (_, _) => UpdateMaximizeTooltip();

        _maxTipHooked = true;
        UpdateMaximizeTooltip();
    }

    /// <summary>창이 최대화돼 있으면 "복원", 아니면 "최대화".</summary>
    private void UpdateMaximizeTooltip()
    {
        if (MaximizeButton is null) return;

        MaximizeButton.ToolTip = WindowState == WindowState.Maximized
            ? Loc.T("tip.restore")
            : Loc.T("tip.maximize");
    }
}
