using System.Diagnostics;

namespace HdrTracer.App;

internal static class AutoStartManager
{
    private const string TaskName = "HdrTracer AutoStart";

    public static bool IsRegistered()
        => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Register()
    {
        string exe = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(exe)) return false;
        return RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F") == 0;
    }

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
