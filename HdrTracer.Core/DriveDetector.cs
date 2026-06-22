using System.IO;

namespace HdrTracer.Core;

public static class DriveDetector
{
    /// <summary>
    /// 인덱싱 가능한 NTFS 드라이브 목록을 반환.
    /// </summary>
    /// <param name="includeRemovable">true면 USB 같은 이동식 드라이브도 포함.</param>
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

    /// <summary>주어진 드라이브가 이동식(USB)인지 확인.</summary>
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