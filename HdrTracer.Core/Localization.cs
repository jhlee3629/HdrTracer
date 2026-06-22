namespace HdrTracer.Core;

/// <summary>앱 전체 UI 문자열의 다국어 관리.</summary>
public static class Localization
{
    public enum Lang { Korean, English }

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

    /// <summary>키로 현재 언어의 문자열을 가져온다. 없으면 키 자체 반환.</summary>
    public static string T(string key)
    {
        var table = _current == Lang.Korean ? _ko : _en;
        return table.TryGetValue(key, out var v) ? v : key;
    }

    private static readonly Dictionary<string, string> _ko = new()
    {
        // 메뉴
        ["menu.settings"]     = "설정",
        ["menu.refresh"]      = "인덱스 새로 고침",
        ["menu.resetCols"]    = "컬럼 너비 초기화",
        ["menu.zoom"]         = "배율",
        ["menu.zoom.in"]      = "확대",
        ["menu.zoom.out"]     = "축소",
        ["menu.zoom.reset"]   = "배율 초기화",
        ["menu.shortcuts"]    = "단축키",
        ["menu.about"]        = "정보",
        ["menu.language"]     = "언어",

        // 필터링 메뉴
        ["menu.filter"]  = "필터링",

        // 타이틀바 버튼 툴팁
        ["tip.menu"]     = "메뉴",
        ["tip.minimize"] = "최소화",
        ["tip.maximize"] = "최대화",
        ["tip.close"]    = "닫기",
        ["tip.search"]   = "검색",
        ["tip.delete"]   = "삭제",

        // 검색 결과 우클릭 메뉴
        ["ctx.open"]       = "열기",
        ["ctx.runAsAdmin"] = "관리자 권한으로 실행",
        ["ctx.openWith"]   = "다른 프로그램으로 열기",
        ["ctx.reveal"]     = "폴더에서 보기",
        ["ctx.copyPath"]   = "경로 복사",
        ["ctx.copyName"]   = "이름 복사",
        ["ctx.rename"]     = "이름 바꾸기",
        ["ctx.copyFile"]   = "파일 복사",
        ["ctx.delete"]     = "휴지통으로 삭제",
        ["ctx.properties"] = "속성",
        ["ctx.rename.title"]   = "이름 바꾸기",
        ["ctx.rename.prompt"]  = "새 이름:",
        ["ctx.rename.extWarn"] = "확장자를 바꾸면 파일이 제대로 열리지 않을 수 있습니다.\n그래도 변경하시겠습니까?",
        ["ctx.delete.confirm"] = "이 파일을 휴지통으로 보낼까요?",
        ["ctx.delete.confirm.multi"] = "선택한 {0}개 항목을 휴지통으로 보낼까요?",
        ["ctx.delete.done.multi"] = "{0}개 항목을 휴지통으로 보냈습니다.",
        ["ctx.delete.partial"] = "{0}개 성공, {1}개 실패",
        ["ctx.copyFile.multi"] = "{0}개 파일이 클립보드에 복사됨",
        ["ctx.copyFile.none"] = "복사할 수 있는 파일이 없습니다",
        ["ctx.copyPath.multi"] = "{0}개 경로 복사됨",
        ["ctx.copyName.multi"] = "{0}개 이름 복사됨",
        ["ctx.open.multi"] = "{0}개 항목 열기",
        ["ctx.open.partial"] = "{0}개 열림, {1}개 실패",
        ["ctx.delete.title"]   = "휴지통으로 삭제",
        ["ctx.error"]          = "오류",
        ["ctx.notExecutable"]  = "실행 가능한 파일이 아닙니다",

        // 빠른 필터 버튼
        ["filter.doc"]   = "문서",
        ["filter.img"]   = "이미지",
        ["filter.media"] = "미디어",
        ["filter.exe"]   = "실행",
        ["filter.zip"]   = "압축",

        // 컬럼 헤더
        ["col.drive"] = "DRV",
        ["col.name"]  = "이름",
        ["col.path"]  = "경로",
        ["col.size"]  = "크기",
        ["col.date"]  = "수정 날짜",

        // 상태바 / 배너
        ["status.indexing"] = "드라이브 인덱싱 중...",
        ["status.total"]    = "총",
        ["status.items"]    = "개",
        ["status.results"]  = "개 결과",
        ["status.refreshDone"] = "인덱스 새로 고침 완료",

        // 트레이
        ["tray.open"] = "HdrTracer 열기",
        ["tray.exit"] = "종료",

        // 설정 창
        ["settings.title"]      = "설정",
        ["settings.indexing"]   = "인덱싱",
        ["settings.usb"]        = "이동식 드라이브 인덱싱",
        ["settings.usb.desc"]   = "USB 드라이브의 파일도 검색에 포함합니다.",
        ["settings.tray"]       = "닫기 버튼을 누르면 트레이로 숨김",
        ["settings.tray.desc"]  = "X 버튼이 종료 대신 트레이로 보냅니다. 진짜 종료는 트레이 우클릭.",
        ["settings.hidden"]      = "숨김·시스템 항목 표시",
        ["settings.hidden.desc"] = "백신 보호 폴더 등 숨김+시스템 속성 항목도 검색 결과에 표시합니다.",
        ["settings.ok"]         = "확인",
        ["settings.usbOn"]      = "확인 누르면 USB 드라이브를 인덱싱합니다.",
        ["settings.usbOff"]     = "확인 누르면 인덱싱된 USB 데이터를 메모리에서 제거합니다.",

        // 단축키 창
        ["sc.title"]        = "단축키",
        ["sc.appMenu"]      = "앱 메뉴",
        ["sc.openSettings"] = "설정 열기",
        ["sc.refresh"]      = "인덱스 새로 고침",
        ["sc.zoomIn"]       = "확대",
        ["sc.zoomOut"]      = "축소",
        ["sc.zoomReset"]    = "배율 초기화",
        ["sc.searchBox"]    = "검색창",
        ["sc.focusSearch"]  = "검색창에 포커스",
        ["sc.clearSearch"]  = "검색어 지우기",
        ["sc.gotoResults"]  = "결과 리스트로 이동",
        ["sc.resultList"]   = "결과 리스트",
        ["sc.openItem"]     = "선택한 항목 열기",
        ["sc.viewProps"]    = "속성 보기",
        ["sc.copyPath"]     = "전체 경로 복사",
        ["sc.copyName"]     = "파일 이름만 복사",
        ["sc.copyFile"]     = "파일 복사",
        ["sc.upFirst"]      = "↑ (첫 행에서)",
        ["sc.backToSearch"] = "검색창으로 돌아가기",
        ["sc.dblClick"]     = "더블클릭",
        ["sc.globalSc"]     = "전역 단축키",
        ["sc.toggleApp"]    = "어디서든 앱 보이기/숨기기",
        ["sc.searchTips"]   = "검색 팁",
        ["sc.tip1"]         = "여러 단어를 공백으로 구분하면 모두 포함하는 결과를 찾습니다 (AND 검색).",
        ["sc.tip2"]         = "한글은 2글자, 영문은 3글자 이상부터 빠른 N-gram 검색이 동작합니다.",

        // 정보 창
        ["about.title"]   = "정보",
        ["about.version"] = "버전 1.0",
        ["about.desc"]    = "NTFS 파일 시스템을 직접 읽어 빠르게 검색하는 도구입니다.",

        // 인덱스 새로 고침 다이얼로그
        ["refresh.confirm.msg"]   = "인덱스를 처음부터 새로 만듭니다.\n\n잠시 동안 검색이 멈출 수 있습니다. 계속하시겠어요?",
        ["refresh.confirm.title"] = "인덱스 새로 고침",
        ["refresh.fail"]          = "인덱스 새로 고침 실패",

        // 공통
        ["common.error"] = "오류",
    };

    private static readonly Dictionary<string, string> _en = new()
    {
        // Menu
        ["menu.settings"]     = "Settings",
        ["menu.refresh"]      = "Refresh Index",
        ["menu.resetCols"]    = "Reset column widths",
        ["menu.zoom"]         = "Zoom",
        ["menu.zoom.in"]      = "Zoom In",
        ["menu.zoom.out"]     = "Zoom Out",
        ["menu.zoom.reset"]   = "Reset Zoom",
        ["menu.shortcuts"]    = "Shortcuts",
        ["menu.about"]        = "About",
        ["menu.language"]     = "Language",

        // Filter menu
        ["menu.filter"]  = "Filter",

        // Title bar button tooltips
        ["tip.menu"]     = "Menu",
        ["tip.minimize"] = "Minimize",
        ["tip.maximize"] = "Maximize",
        ["tip.close"]    = "Close",
        ["tip.search"]   = "Search",
        ["tip.delete"]   = "Delete",

        // Search result context menu
        ["ctx.open"]       = "Open",
        ["ctx.runAsAdmin"] = "Run as administrator",
        ["ctx.openWith"]   = "Open with...",
        ["ctx.reveal"]     = "Show in folder",
        ["ctx.copyPath"]   = "Copy path",
        ["ctx.copyName"]   = "Copy name",
        ["ctx.rename"]     = "Rename",
        ["ctx.copyFile"]   = "Copy file",
        ["ctx.delete"]     = "Move to Recycle Bin",
        ["ctx.properties"] = "Properties",
        ["ctx.rename.title"]   = "Rename",
        ["ctx.rename.prompt"]  = "New name:",
        ["ctx.rename.extWarn"] = "Changing the file extension might make the file unusable.\nProceed anyway?",
        ["ctx.delete.confirm"] = "Move this file to the Recycle Bin?",
        ["ctx.delete.confirm.multi"] = "Move the selected {0} items to the Recycle Bin?",
        ["ctx.delete.done.multi"] = "Moved {0} items to the Recycle Bin.",
        ["ctx.delete.partial"] = "{0} succeeded, {1} failed",
        ["ctx.copyFile.multi"] = "{0} files copied to clipboard",
        ["ctx.copyFile.none"] = "No files available to copy",
        ["ctx.copyPath.multi"] = "{0} paths copied",
        ["ctx.copyName.multi"] = "{0} names copied",
        ["ctx.open.multi"] = "Opened {0} items",
        ["ctx.open.partial"] = "Opened {0}, {1} failed",
        ["ctx.delete.title"]   = "Move to Recycle Bin",
        ["ctx.error"]          = "Error",
        ["ctx.notExecutable"]  = "Not an executable file",

        // Quick filter buttons
        ["filter.doc"]   = "Docs",
        ["filter.img"]   = "Images",
        ["filter.media"] = "Media",
        ["filter.exe"]   = "Apps",
        ["filter.zip"]   = "Archives",

        // Column headers
        ["col.drive"] = "DRV",
        ["col.name"]  = "Name",
        ["col.path"]  = "Path",
        ["col.size"]  = "Size",
        ["col.date"]  = "Date Modified",

        // Status bar / banner
        ["status.indexing"] = "Indexing drives...",
        ["status.total"]    = "Total",
        ["status.items"]    = " items",
        ["status.results"]  = "results",
        ["status.refreshDone"] = "Index refresh complete",

        // Tray
        ["tray.open"] = "Open HdrTracer",
        ["tray.exit"] = "Exit",

        // Settings window
        ["settings.title"]      = "Settings",
        ["settings.indexing"]   = "Indexing",
        ["settings.usb"]        = "Index removable drives",
        ["settings.usb.desc"]   = "Include files on USB drives in search.",
        ["settings.tray"]       = "Minimize to tray on close",
        ["settings.tray.desc"]  = "The X button hides to tray instead of exiting. Right-click the tray icon to exit.",
        ["settings.hidden"]      = "Show hidden & system items",
        ["settings.hidden.desc"] = "Include hidden+system items (e.g. antivirus protection folders) in search results.",
        ["settings.ok"]         = "OK",
        ["settings.usbOn"]      = "USB drives will be indexed when you click OK.",
        ["settings.usbOff"]     = "Indexed USB data will be removed from memory when you click OK.",

        // Shortcuts window
        ["sc.title"]        = "Shortcuts",
        ["sc.appMenu"]      = "App Menu",
        ["sc.openSettings"] = "Open Settings",
        ["sc.refresh"]      = "Refresh Index",
        ["sc.zoomIn"]       = "Zoom In",
        ["sc.zoomOut"]      = "Zoom Out",
        ["sc.zoomReset"]    = "Reset Zoom",
        ["sc.searchBox"]    = "Search Box",
        ["sc.focusSearch"]  = "Focus search box",
        ["sc.clearSearch"]  = "Clear search",
        ["sc.gotoResults"]  = "Move to result list",
        ["sc.resultList"]   = "Result List",
        ["sc.openItem"]     = "Open selected item",
        ["sc.viewProps"]    = "View properties",
        ["sc.copyPath"]     = "Copy full path",
        ["sc.copyName"]     = "Copy file name only",
        ["sc.copyFile"]     = "Copy file",
        ["sc.upFirst"]      = "↑ (on first row)",
        ["sc.backToSearch"] = "Back to search box",
        ["sc.dblClick"]     = "Double-click",
        ["sc.globalSc"]     = "Global Shortcut",
        ["sc.toggleApp"]    = "Show/hide app from anywhere",
        ["sc.searchTips"]   = "Search Tips",
        ["sc.tip1"]         = "Separate multiple words with spaces to find results containing all of them (AND search).",
        ["sc.tip2"]         = "Fast N-gram search works for 2+ Korean characters or 3+ English characters.",

        // About window
        ["about.title"]   = "About",
        ["about.version"] = "Version 1.0",
        ["about.desc"]    = "A tool that reads the NTFS file system directly for fast searching.",

        // Refresh index dialog
        ["refresh.confirm.msg"]   = "The index will be rebuilt from scratch.\n\nSearch may pause briefly. Continue?",
        ["refresh.confirm.title"] = "Refresh Index",
        ["refresh.fail"]          = "Index refresh failed",

        // Common
        ["common.error"] = "Error",
    };
}
