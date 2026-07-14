using System.Diagnostics;

namespace HdrTracer.App;

/// <summary>
/// "Windows 시작 시 자동 실행"을 작업 스케줄러로 등록/해제한다.
/// 레지스트리 Run 키 대신 스케줄러를 쓰는 이유: 이 앱은 관리자 권한이라
/// Run 키로는 로그인마다 UAC가 뜨지만, 스케줄러 작업은
/// "가장 높은 권한으로 실행"으로 등록해 두면 로그인 시 UAC 없이 실행된다.
/// </summary>
internal static class AutoStartManager
{
    private const string TaskName = "HdrTracer AutoStart";

    /// <summary>현재 자동 실행 작업이 등록돼 있는지.</summary>
    public static bool IsRegistered()
        => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    /// <summary>현재 실행 파일 경로로 자동 실행 작업을 등록. 성공 시 true.</summary>
    public static bool Register()
    {
        string exe = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(exe)) return false;
        // /SC ONLOGON: 로그인 시 실행, /RL HIGHEST: 관리자 권한(UAC 없음), /F: 있으면 덮어씀
        return RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F") == 0;
    }

    /// <summary>자동 실행 작업을 해제. 성공(또는 원래 없음) 시 true.</summary>
    public static bool Unregister()
    {
        if (!IsRegistered()) return true;
        return RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;
    }

    private static int RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return -1;
            if (!p.WaitForExit(10_000)) return -1;
            return p.ExitCode;
        }
        catch { return -1; }
    }
}
