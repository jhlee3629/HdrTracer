namespace HdrTracer.Core;

// 언어별 문자열 사전 (Localization의 부분 클래스).
// 새 언어를 추가하려면: 이 형식으로 파일 하나를 만들고 Localization.cs의 Table()에 한 줄 추가.
public static partial class Localization
{
    internal static readonly Dictionary<string, string> _en = new()
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
        ["menu.lang.zh"]      = "Chinese (Simplified)",
        ["menu.lang.ja"]      = "Japanese",
        ["menu.lang.es"]      = "Spanish",
        ["menu.lang.de"]      = "German",
        ["menu.lang.fr"]      = "French",
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
        ["filter.kind"]        = "Kind",
        ["filter.kind.folder"] = "Folders only",
        ["filter.kind.file"]   = "Files only",

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
        ["status.indexingProgress"] = "Indexing… {0}s elapsed  ·  {1}",
        ["status.driveDone"]        = "{0} done ({1:N0})",
        ["status.driveWorking"]     = "{0} working",

        // Tray
        ["tray.open"] = "Open HdrTracer",
        ["tray.exit"] = "Exit",
        ["tray.pinned"]       = "Pinned searches",
        ["tray.pinned.empty"] = "No pinned searches",
        ["tray.settings"]     = "Settings",

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
        ["settings.hotkey"]      = "Global hotkey (Win+Alt+S)",
        ["settings.hotkey.desc"] = "Summon the window from any app with Win+Alt+S; press again to hide to tray.",
        ["hotkey.fail"]          = "Failed to register the global hotkey (Win+Alt+S) — another program may be using it.",

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
        ["sc.globalHotkey"]      = "Show / hide the window (works from any app)",

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
            "IMG_*_edit.txt|starts with IMG_, ends with _edit.txt (extension included)\n" +
            "IMG_*_edit*|add a trailing * to match any extension\n" +
            "report*final.docx|starts with 'report', ends 'final.docx'\n" +
            "photo_?.jpg|? is a single character (photo_1 yes, photo_12 no)\n" +
            "With * the whole name must match the shape.\n" +
            "project folder:|folders only (use file: for files only)\n" +
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
