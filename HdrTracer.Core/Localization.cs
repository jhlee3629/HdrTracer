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
        ["menu.lang.ko"]      = "한국어",
        ["menu.lang.en"]      = "영어",
        ["menu.searchHelp"]   = "검색 도움말",
        ["menu.export"]       = "결과 내보내기 (CSV)",
        ["ctx.export"]        = "선택 항목 내보내기",
        ["export.done"]       = "{0}개 항목을 CSV로 저장했습니다.",

        // 필터링 메뉴
        ["menu.filter"]  = "필터링",

        // 타이틀바 버튼 툴팁
        ["tip.menu"]     = "메뉴",
        ["tip.minimize"] = "최소화",
        ["tip.maximize"] = "최대화",
        ["tip.close"]    = "닫기",
        ["tip.search"]   = "검색",
        ["tip.delete"]   = "삭제",
        ["tip.pin"]      = "고정/해제",

        ["search.placeholder"] = "파일 이름 입력 후 Enter",
        ["empty.title"] = "결과가 없습니다",
        ["empty.body"]  = "검색어를 줄이거나 다르게 써보세요\n여러 단어는 모두 포함된 것만 찾습니다\n도움말: 메뉴(HdrTracer ▼) → 검색 도움말",

        // 검색 결과 우클릭 메뉴
        ["ctx.open"]       = "열기",
        ["ctx.runAsAdmin"] = "관리자 권한으로 실행",
        ["ctx.openWith"]   = "다른 프로그램으로 열기",
        ["ctx.reveal"]     = "폴더에서 보기",
        ["ctx.searchFolder"] = "이 폴더에서만 검색",
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
        ["ctx.delete.more"] = "…외 {0}개",
        ["ctx.delete.danger"] = "⚠️ 주의: 시스템에 중요한 항목이 포함되어 있습니다. 삭제하면 시스템이 불안정해질 수 있습니다.",
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
        ["filter.size"]       = "크기",
        ["filter.size.10mb"]  = "10MB 이상",
        ["filter.size.100mb"] = "100MB 이상",
        ["filter.size.1gb"]   = "1GB 이상",
        ["filter.date"]       = "기간",
        ["filter.date.today"] = "오늘",
        ["filter.date.week"]  = "최근 1주",
        ["filter.date.month"] = "최근 1달",
        ["filter.date.year"]  = "최근 1년",
        ["filter.clear"]      = "조건 지우기",

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
        ["status.selected"] = "{0}개 선택, {1}",

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
        ["settings.autostart"]      = "Windows 시작 시 자동 실행",
        ["settings.autostart.desc"] = "로그인하면 트레이에 자동으로 실행됩니다. (작업 스케줄러 등록, UAC 없음)",
        ["settings.autostart.fail"] = "자동 실행 설정 변경에 실패했습니다.",
        ["settings.excluded"]      = "검색에서 제외할 폴더 이름",
        ["settings.excluded.desc"] = "세미콜론(;)으로 구분. 이 이름의 폴더와 그 안의 모든 항목이 결과에서 숨겨집니다. 예: WinSxS; node_modules",

        // 단축키 창
        ["sc.title"]        = "단축키",
        ["sc.appMenu"]      = "앱 메뉴",
        ["sc.openSettings"] = "설정 열기",
        ["sc.refresh"]      = "인덱스 새로 고침",
        ["sc.zoomIn"]       = "확대",
        ["sc.zoomOut"]      = "축소",
        ["sc.zoomReset"]    = "배율 초기화",
        ["sc.searchBox"]    = "검색창",
        ["sc.pinnedSearch"] = "고정 검색 실행 (📌 위에서부터 1~9)",
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

        ["help.search.title"] = "검색 도움말",
        ["help.search.body"] =
            "#기본 검색\n" +
            "휴가 사진|'휴가'와 '사진'이 모두 이름에 있는 것 (공백 = 그리고)\n" +
            "*.jpg|jpg 파일만  (여러 개 가능: *.jpg *.png)\n" +
            "\n" +
            "#빼고 싶은 게 있을 때\n" +
            "보고서 -임시|이름에 '임시'가 있는 것은 빼고\n" +
            "*.txt -*.log|txt는 찾고 log는 제외\n" +
            "\n" +
            "#특정 폴더 안에서만\n" +
            "사진 D:\\백업\\|D:\\백업 폴더 아래에서만 '사진' 검색\n" +
            "사진 \\여행\\|경로에 '여행' 폴더가 있는 것만\n" +
            "사진 \"D:\\내 문서\\\"|공백이 있는 경로는 따옴표로 묶기\n" +
            "\n" +
            "#이름 모양으로 찾기 (와일드카드)\n" +
            "IMG_*_편집|IMG_로 시작하고 _편집으로 끝나는 이름\n" +
            "보고서*최종.hwp|'보고서'로 시작해 '최종.hwp'로 끝나는 형식\n" +
            "가을_?.jpg|? 는 글자 하나 (가을_1은 되고 가을_12는 안 됨)\n" +
            "* 이 있으면 이름 전체가 그 모양과 일치해야 합니다.\n" +
            "\n" +
            "#크기·날짜로 거르기\n" +
            "*.mp4 >500MB|500MB보다 큰 mp4만 (단위 필수: KB MB GB TB)\n" +
            ">1GB|1GB 넘는 파일 찾기 (단독 사용 가능)\n" +
            "사진 >2026-01|2026년 1월 이후 수정된 것만 (연-월-일 순서)\n" +
            "*.pdf <2024|2024년 이전에 수정된 pdf\n" +
            "보고서 >week|최근 7일 (today · week · month · year)\n" +
            "\n" +
            "폴더 검색은 결과를 우클릭해 '이 폴더에서만 검색'을 눌러도 됩니다.\n" +
            "경로만 입력하면 검색되지 않아요. 단어나 확장자와 함께 쓰세요.",

        // 정보 창
        ["about.title"]   = "정보",
        ["about.version"] = "버전 {0}",
        ["about.desc"]    = "NTFS 파일 시스템을 직접 읽어 빠르게 검색하는 도구입니다.",

        // 인덱스 새로 고침 다이얼로그
        ["refresh.confirm.msg"]   = "인덱스를 처음부터 새로 만듭니다.\n\n잠시 동안 검색이 멈출 수 있습니다. 계속하시겠어요?",
        ["refresh.confirm.title"] = "인덱스 새로 고침",
        ["refresh.fail"]          = "인덱스 새로 고침 실패",

        // 공통
        ["common.error"] = "오류",
        ["common.ok"]     = "확인",
        ["common.cancel"] = "취소",
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
        ["menu.lang.ko"]      = "Korean",
        ["menu.lang.en"]      = "English",
        ["menu.searchHelp"]   = "Search Help",
        ["menu.export"]       = "Export results (CSV)",
        ["ctx.export"]        = "Export selected items",
        ["export.done"]       = "Saved {0} items to CSV.",

        // Filter menu
        ["menu.filter"]  = "Filter",

        // Title bar button tooltips
        ["tip.menu"]     = "Menu",
        ["tip.minimize"] = "Minimize",
        ["tip.maximize"] = "Maximize",
        ["tip.close"]    = "Close",
        ["tip.search"]   = "Search",
        ["tip.delete"]   = "Delete",
        ["tip.pin"]      = "Pin / Unpin",

        ["search.placeholder"] = "Type a file name and press Enter",
        ["empty.title"] = "No results",
        ["empty.body"]  = "Try a shorter or different search term\nMultiple words match only items containing all of them\nHelp: menu (HdrTracer ▼) → Search help",

        // Search result context menu
        ["ctx.open"]       = "Open",
        ["ctx.runAsAdmin"] = "Run as administrator",
        ["ctx.openWith"]   = "Open with...",
        ["ctx.reveal"]     = "Show in folder",
        ["ctx.searchFolder"] = "Search in this folder",
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
        ["ctx.delete.more"] = "…and {0} more",
        ["ctx.delete.danger"] = "⚠️ Warning: system-critical items are included. Deleting them may make your system unstable.",
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
        ["filter.size"]       = "Size",
        ["filter.size.10mb"]  = "Over 10MB",
        ["filter.size.100mb"] = "Over 100MB",
        ["filter.size.1gb"]   = "Over 1GB",
        ["filter.date"]       = "Date",
        ["filter.date.today"] = "Today",
        ["filter.date.week"]  = "Past week",
        ["filter.date.month"] = "Past month",
        ["filter.date.year"]  = "Past year",
        ["filter.clear"]      = "Clear",

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
        ["status.selected"] = "{0} selected, {1}",

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
        ["settings.autostart"]      = "Start with Windows",
        ["settings.autostart.desc"] = "Runs in the tray automatically at sign-in (Task Scheduler, no UAC prompt).",
        ["settings.autostart.fail"] = "Failed to change the auto-start setting.",
        ["settings.excluded"]      = "Folder names to exclude from search",
        ["settings.excluded.desc"] = "Separate with semicolons (;). Folders with these names — and everything inside them — are hidden from results. e.g. WinSxS; node_modules",

        // Shortcuts window
        ["sc.title"]        = "Shortcuts",
        ["sc.appMenu"]      = "App Menu",
        ["sc.openSettings"] = "Open Settings",
        ["sc.refresh"]      = "Refresh Index",
        ["sc.zoomIn"]       = "Zoom In",
        ["sc.zoomOut"]      = "Zoom Out",
        ["sc.zoomReset"]    = "Reset Zoom",
        ["sc.searchBox"]    = "Search Box",
        ["sc.pinnedSearch"] = "Run pinned search (1–9 from top)",
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

        ["menu.searchHelp"] = "Search help",
        ["help.search.title"] = "Search Help",
        ["help.search.body"] =
            "#Basic search\n" +
            "vacation photo|names containing both words (space = AND)\n" +
            "*.jpg|jpg files only  (multiple allowed: *.jpg *.png)\n" +
            "\n" +
            "#Excluding things\n" +
            "report -draft|skip names containing 'draft'\n" +
            "*.txt -*.log|find txt but not log\n" +
            "\n" +
            "#Only inside a folder\n" +
            "photo D:\\Backup\\|search 'photo' only under D:\\Backup\n" +
            "photo \\Trips\\|only items whose path has a 'Trips' folder\n" +
            "photo \"D:\\My Docs\\\"|wrap paths with spaces in quotes\n" +
            "\n" +
            "#Match by name shape (wildcards)\n" +
            "IMG_*_edit|names starting IMG_ and ending _edit\n" +
            "report*final.docx|starts with 'report', ends 'final.docx'\n" +
            "photo_?.jpg|? is a single character (photo_1 yes, photo_12 no)\n" +
            "With * the whole name must match the shape.\n" +
            "\n" +
            "#Filter by size / date\n" +
            "*.mp4 >500MB|mp4 larger than 500MB (unit required: KB MB GB TB)\n" +
            ">1GB|find files over 1GB (works alone)\n" +
            "photo >2026-01|modified since Jan 2026 (year-month-day order)\n" +
            "*.pdf <2024|pdf modified before 2024\n" +
            "report >week|last 7 days (today · week · month · year)\n" +
            "\n" +
            "You can also right-click a result → 'Search in this folder'.\n" +
            "A path alone finds nothing — combine it with a word or extension.",

        // About window
        ["about.title"]   = "About",
        ["about.version"] = "Version {0}",
        ["about.desc"]    = "A tool that reads the NTFS file system directly for fast searching.",

        // Refresh index dialog
        ["refresh.confirm.msg"]   = "The index will be rebuilt from scratch.\n\nSearch may pause briefly. Continue?",
        ["refresh.confirm.title"] = "Refresh Index",
        ["refresh.fail"]          = "Index refresh failed",

        // Common
        ["common.error"] = "Error",
        ["common.ok"]     = "OK",
        ["common.cancel"] = "Cancel",
    };
}
