using System.IO;
using System.Text.Json;

namespace HdrTracer.Core;

public sealed class AppSettings
{
    public bool IndexRemovableDrives { get; set; } = false;

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool ShowHiddenSystemItems { get; set; } = false;
    public double UiZoom { get; set; } = 1.0;
    public string Language { get; set; } = "ko"; 

    public List<string> SearchHistory { get; set; } = new();
    public List<string> PinnedSearches { get; set; } = new();

    public bool GlobalHotkeyEnabled { get; set; } = true;

    public List<string> ExcludedFolders { get; set; } = new();

    public string SortColumn { get; set; } = "Name";
    public bool SortAscending { get; set; } = true;

    public double ColWidthDrive { get; set; } = 50;
    public double ColWidthName  { get; set; } = 280;
    public double ColWidthSize  { get; set; } = 80;
    public double ColWidthDate  { get; set; } = 120;
    public double ColWidthPath  { get; set; } = 0; 
    
    public double WinLeft   { get; set; } = 0;
    public double WinTop    { get; set; } = 0;
    public double WinWidth  { get; set; } = 0;
    public double WinHeight { get; set; } = 0;
    public bool   WinMaximized { get; set; } = false;

    public bool RestoreLastSearch { get; set; } = false;

    public string LastSearchQuery { get; set; } = "";

    private static string GetSettingsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HdrTracer");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return CreateDefault();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    private static AppSettings CreateDefault()
    {
        var s = new AppSettings();
        try
        {
            s.Language = System.Globalization.CultureInfo.CurrentUICulture
                .TwoLetterISOLanguageName.ToLowerInvariant() switch
            {
                "ko" => "ko",
                "zh" => "zh",
                "ja" => "ja",
                "es" => "es",
                "de" => "de",
                "fr" => "fr",
                _    => "en"
            };
        }
        catch { }
        return s;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetSettingsPath(), json);
        }
        catch { }
    }
}
