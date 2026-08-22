using System.IO;

namespace HdrTracer.Core;

public static class DriveDetector
{
    public static List<string> GetIndexableDrives(bool includeRemovable = false)
    {
        var result = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                bool typeOk = drive.DriveType == DriveType.Fixed
                           || (includeRemovable && drive.DriveType == DriveType.Removable);
                if (!typeOk) continue;

                if (!drive.IsReady) continue;
                if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(drive.Name.TrimEnd('\\', '/'));
            }
            catch { }
        }
        return result;
    }

    public static bool IsRemovable(string driveLetter)
    {
        try
        {
            var info = new DriveInfo(driveLetter + "\\");
            return info.DriveType == DriveType.Removable;
        }
        catch { return false; }
    }
}