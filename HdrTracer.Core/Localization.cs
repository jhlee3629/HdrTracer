namespace HdrTracer.Core;

/// <summary>
/// 앱 전체 UI 문자열의 다국어 관리.
/// 언어별 사전은 Localization.{코드}.cs 부분 클래스 파일에 있다 (ko / en / zh / ja).
/// 번역이 없는 키는 영어로 대체되므로, 새 문구를 추가할 때 모든 언어를 한꺼번에
/// 채우지 않아도 앱이 깨지지 않는다.
/// </summary>
public static partial class Localization
{
    public enum Lang { Korean, English, Chinese, Japanese, Spanish, German, French }

    private static Lang _current = Lang.Korean;

    /// <summary>언어가 바뀌면 발생. UI가 이 이벤트를 구독해 다시 그린다.</summary>
    public static event Action? LanguageChanged;

    public static Lang Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>지원 언어 목록 (메뉴 구성 순서).</summary>
    public static readonly Lang[] SupportedLanguages =
    {
        Lang.Korean, Lang.English, Lang.Chinese, Lang.Japanese,
        Lang.Spanish, Lang.German, Lang.French
    };

    private static Dictionary<string, string> Table(Lang lang) => lang switch
    {
        Lang.Korean   => _ko,
        Lang.Chinese  => _zh,
        Lang.Japanese => _ja,
        Lang.Spanish  => _es,
        Lang.German   => _de,
        Lang.French   => _fr,
        _             => _en
    };

    /// <summary>
    /// 키로 현재 언어의 문자열을 가져온다.
    /// 없으면 영어로 대체하고, 영어에도 없으면 키 자체를 반환한다.
    /// </summary>
    public static string T(string key)
    {
        if (Table(_current).TryGetValue(key, out var v)) return v;
        if (_en.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

    /// <summary>설정 파일에 저장하는 코드 문자열 ("ko" / "en" / "zh" / "ja").</summary>
    public static string ToCode(Lang lang) => lang switch
    {
        Lang.Korean   => "ko",
        Lang.Chinese  => "zh",
        Lang.Japanese => "ja",
        Lang.Spanish  => "es",
        Lang.German   => "de",
        Lang.French   => "fr",
        _             => "en"
    };

    /// <summary>코드 문자열 → 언어. 모르는 값이면 영어.</summary>
    public static Lang FromCode(string? code) => code switch
    {
        "ko" => Lang.Korean,
        "zh" => Lang.Chinese,
        "ja" => Lang.Japanese,
        "es" => Lang.Spanish,
        "de" => Lang.German,
        "fr" => Lang.French,
        _    => Lang.English
    };

    /// <summary>메뉴에 표시할 언어 이름 키 (menu.lang.ko 등).</summary>
    public static string NameKey(Lang lang) => "menu.lang." + ToCode(lang);
}
