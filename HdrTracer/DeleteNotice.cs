using Loc = HdrTracer.Core.Localization;

namespace HdrTracer.App;

internal static class DeleteNotice
{
    public static string Build(RobustDelete.Report report, string? singleName)
    {
        if (report.FailCount > 0)
        {
            string s = string.Format(Loc.T("ctx.delete.partial"),
                                     report.OkCount, report.FailCount);
            if (report.PermanentCount > 0)
                s += "  ·  " + string.Format(Loc.T("dn.permSuffix"), report.PermanentCount);
            return s;
        }

        if (report.PermanentCount == 0)
        {
            return report.OkCount == 1 && singleName is not null
                ? $"{Loc.T("ctx.delete.title")}: {singleName}"
                : string.Format(Loc.T("ctx.delete.done.multi"), report.OkCount);
        }

        if (report.OkCount == 1 && singleName is not null)
            return $"{Loc.T("dn.permOne")}: {singleName}";

        if (report.PermanentCount == report.OkCount)
            return string.Format(Loc.T("dn.permMulti"), report.OkCount);

        return string.Format(Loc.T("dn.mixed"), report.OkCount, report.PermanentCount);
    }
}
