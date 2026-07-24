using System.IO;
using System.Text.Json;

namespace HdrTracer.Core;

public sealed class AppSettings
{
    /// <summary>이동식 드라이브(USB)를 인덱싱할지 여부. 기본 false.</summary>
    public bool IndexRemovableDrives { get; set; } = false;

    /// <summary>창 닫기 버튼(X)이 트레이로 숨김으로 동작할지 여부. 기본 true.</summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;

    /// <summary>숨김+시스템 속성 항목(예: 알약 랜섬웨어 미끼 폴더 "!!QAdC")을
    /// 검색 결과에 표시할지 여부. 기본 false (탐색기처럼 숨김).</summary>
    public bool ShowHiddenSystemItems { get; set; } = false;
    public double UiZoom { get; set; } = 1.0;
    public string Language { get; set; } = "ko";   // ← 추가 ("ko" 또는 "en")

    /// <summary>최근 검색 히스토리 (최신이 앞, 최대 10개). 영구 저장됨.</summary>
    public List<string> SearchHistory { get; set; } = new();
    public List<string> PinnedSearches { get; set; } = new();

    /// <summary>전역 단축키(Win+Alt+S) 사용 여부. 기본 켜짐(기존 동작 유지).</summary>
    public bool GlobalHotkeyEnabled { get; set; } = true;

    /// <summary>검색 결과에서 숨길 폴더 이름 목록 (예: WinSxS, node_modules)</summary>
    public List<string> ExcludedFolders { get; set; } = new();

    /// <summary>결과 정렬 상태 (컬럼 이름 문자열 + 오름차순 여부)</summary>
    public string SortColumn { get; set; } = "Name";
    public bool SortAscending { get; set; } = true;

    /// <summary>컬럼 너비(픽셀). 경로는 * 채움이라 저장 안 함. 0 이하면 기본값 사용.
    /// 기본값은 XAML 디자인값과 동일.</summary>
    public double ColWidthDrive { get; set; } = 50;
    public double ColWidthName  { get; set; } = 280;
    public double ColWidthSize  { get; set; } = 80;
    public double ColWidthDate  { get; set; } = 120;
    public double ColWidthPath  { get; set; } = 0;   // 0 = 저장 안 됨(기본 300 사용)
    
    /// <summary>창 위치·크기·최대화 상태. Width/Height가 100 미만이면 "저장 안 됨"으로 간주.</summary>
    public double WinLeft   { get; set; } = 0;
    public double WinTop    { get; set; } = 0;
    public double WinWidth  { get; set; } = 0;
    public double WinHeight { get; set; } = 0;
    public bool   WinMaximized { get; set; } = false;

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

    /// <summary>
    /// 최초 실행(설정 파일 없음/손상) 기본값: 언어를 Windows 표시 언어에 맞춘다.
    /// 한국어 Windows면 ko, 그 외는 en. 사용자가 메뉴에서 바꾸면 그 선택이 저장·유지된다.
    /// </summary>
    private static AppSettings CreateDefault()
    {
        var s = new AppSettings();
        try
        {
            s.Language = System.Globalization.CultureInfo.CurrentUICulture
                .TwoLetterISOLanguageName.Equals("ko", StringComparison.OrdinalIgnoreCase)
                ? "ko" : "en";
        }
        catch { /* 감지 실패 시 기본값(ko) 유지 */ }
        return s;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetSettingsPath(), json);
        }
        catch { /* 저장 실패 무시 */ }
    }
}
