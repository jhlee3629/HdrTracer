using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

// ============================================================================
//  삭제 후 상태바에 띄울 문구를 만든다.
//
//  세 가지를 구분한다:
//    · 휴지통으로 감      → 기존 문구 그대로 (7개 언어 번역 재사용)
//    · 영구 삭제됨        → 그 사실을 분명히 말한다 (휴지통에 없으므로)
//    · 사용자가 취소함    → 실패가 아니라 "하지 않기로 한 것"이므로 그렇게 말한다
//
//  취소를 따로 다루는 이유:
//    영구 삭제 확인창에서 취소를 누르면 그 항목은 지워지지 않는다. 그런데 이걸
//    실패로 세면 "0개 성공, 1개 실패"처럼 사용자가 방금 거절한 일을 못 했다고
//    보고하는 꼴이 된다. RobustDelete가 Cause.Cancelled로 따로 표시하고,
//    실패 보고 창에도 올리지 않는다.
// ============================================================================

internal static class DeleteNotice
{
    /// <summary>삭제 결과에 맞는 상태바 문구를 만든다.</summary>
    /// <param name="report">RobustDelete가 돌려준 결과.</param>
    /// <param name="singleName">항목이 하나뿐일 때 그 표시 이름. 여러 개면 null.</param>
    public static string Build(RobustDelete.Report report, string? singleName)
    {
        int cancelled = report.CancelledCount;

        // 아무것도 지우지 않고 전부 취소된 경우 — 가장 흔한 취소 상황
        if (report.OkCount == 0 && report.FailCount == 0 && cancelled > 0)
            return Loc.T("dn.cancelled");

        string s;

        if (report.FailCount > 0)
        {
            s = string.Format(Loc.T("ctx.delete.partial"), report.OkCount, report.FailCount);
            if (report.PermanentCount > 0)
                s += "  ·  " + string.Format(Loc.T("dn.permSuffix"), report.PermanentCount);
        }
        else if (report.PermanentCount == 0)
        {
            // 전부 휴지통으로 감 — 기존 문구 그대로
            s = report.OkCount == 1 && singleName is not null
                ? $"{Loc.T("ctx.delete.title")}: {singleName}"
                : string.Format(Loc.T("ctx.delete.done.multi"), report.OkCount);
        }
        else if (report.OkCount == 1 && singleName is not null)
        {
            s = $"{Loc.T("dn.permOne")}: {singleName}";
        }
        else if (report.PermanentCount == report.OkCount)
        {
            s = string.Format(Loc.T("dn.permMulti"), report.OkCount);
        }
        else
        {
            s = string.Format(Loc.T("dn.mixed"), report.OkCount, report.PermanentCount);
        }

        // 일부만 취소된 경우 그 사실을 덧붙인다
        if (cancelled > 0)
            s += "  ·  " + string.Format(Loc.T("dn.cancelSuffix"), cancelled);

        return s;
    }
}
