namespace HdrTracer.Core;

public static partial class Localization
{
    public enum Lang { Korean, English, Chinese, Japanese, Spanish, German, French }

    private static Lang _current = Lang.Korean;

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

    public static string T(string key)
    {
        if (Table(_current).TryGetValue(key, out var v)) return v;
        if (_en.TryGetValue(key, out var fallback)) return fallback;
        return key;
    }

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

    public static string NameKey(Lang lang) => "menu.lang." + ToCode(lang);
}
