namespace HdrTracer.Core;

// 중국어(간체) 문자열. 번역이 빠진 키는 자동으로 영어로 대체된다.
public static partial class Localization
{
    internal static readonly Dictionary<string, string> _zh = new()
    {
        // 메뉴
        ["menu.settings"]     = "设置",
        ["menu.refresh"]      = "刷新索引",
        ["menu.resetCols"]    = "重置列宽",
        ["menu.zoom"]         = "缩放",
        ["menu.zoom.in"]      = "放大",
        ["menu.zoom.out"]     = "缩小",
        ["menu.zoom.reset"]   = "重置缩放",
        ["menu.shortcuts"]    = "快捷键",
        ["menu.about"]        = "关于",
        ["menu.language"]     = "语言",
        ["menu.lang.ko"]      = "韩语",
        ["menu.lang.en"]      = "英语",
        ["menu.lang.zh"]      = "中文(简体)",
        ["menu.lang.ja"]      = "日语",
        ["menu.lang.es"]      = "西班牙语",
        ["menu.lang.de"]      = "德语",
        ["menu.lang.fr"]      = "法语",
        ["menu.searchHelp"]   = "搜索帮助",
        ["menu.export"]       = "导出结果 (CSV)",
        ["ctx.export"]        = "导出所选项目",
        ["export.done"]       = "已将 {0} 个项目保存为 CSV。",
        ["menu.filter"]       = "筛选",

        // 툴팁
        ["tip.menu"]     = "菜单",
        ["tip.minimize"] = "最小化",
        ["tip.maximize"] = "最大化",
        ["tip.close"]    = "关闭",
        ["tip.search"]   = "搜索",
        ["tip.delete"]   = "删除",
        ["tip.pin"]      = "固定 / 取消固定",

        // 검색창·빈 결과
        ["search.placeholder"] = "输入文件名后按 Enter",
        ["empty.title"] = "没有结果",
        ["empty.body"]  = "试试更短或不同的搜索词\n多个词只会匹配同时包含它们的项目\n帮助：菜单 (HdrTracer ▼) → 搜索帮助",

        // 우클릭 메뉴
        ["ctx.open"]           = "打开",
        ["ctx.runAsAdmin"]     = "以管理员身份运行",
        ["ctx.openWith"]       = "打开方式...",
        ["ctx.reveal"]         = "在文件夹中显示",
        ["ctx.searchFolder"]   = "仅在此文件夹中搜索",
        ["ctx.copyPath"]       = "复制路径",
        ["ctx.copyName"]       = "复制名称",
        ["ctx.rename"]         = "重命名",
        ["ctx.copyFile"]       = "复制文件",
        ["ctx.delete"]         = "移到回收站",
        ["ctx.properties"]     = "属性",
        ["ctx.rename.title"]   = "重命名",
        ["ctx.rename.prompt"]  = "新名称：",
        ["ctx.rename.extWarn"] = "更改文件扩展名可能导致文件无法使用。\n仍要继续吗？",
        ["ctx.delete.confirm"] = "要将此文件移到回收站吗？",
        ["ctx.delete.more"]    = "…以及其他 {0} 个",
        ["ctx.delete.danger"]  = "⚠️ 警告：其中包含系统关键项目。删除后可能导致系统不稳定。",
        ["ctx.delete.confirm.multi"] = "要将所选的 {0} 个项目移到回收站吗？",
        ["ctx.delete.done.multi"]    = "已将 {0} 个项目移到回收站。",
        ["ctx.delete.partial"] = "成功 {0} 个，失败 {1} 个",
        ["ctx.copyFile.multi"] = "已将 {0} 个文件复制到剪贴板",
        ["ctx.copyFile.none"]  = "没有可复制的文件",
        ["ctx.copyPath.multi"] = "已复制 {0} 个路径",
        ["ctx.copyName.multi"] = "已复制 {0} 个名称",
        ["ctx.open.multi"]     = "已打开 {0} 个项目",
        ["ctx.open.partial"]   = "已打开 {0} 个，失败 {1} 个",
        ["ctx.delete.title"]   = "移到回收站",
        ["ctx.error"]          = "错误",
        ["ctx.notExecutable"]  = "不是可执行文件",

        // 빠른 필터
        ["filter.doc"]         = "文档",
        ["filter.img"]         = "图片",
        ["filter.media"]       = "媒体",
        ["filter.exe"]         = "应用",
        ["filter.zip"]         = "压缩包",
        ["filter.size"]        = "大小",
        ["filter.size.10mb"]   = "大于 10MB",
        ["filter.size.100mb"]  = "大于 100MB",
        ["filter.size.1gb"]    = "大于 1GB",
        ["filter.date"]        = "日期",
        ["filter.date.today"]  = "今天",
        ["filter.date.week"]   = "最近一周",
        ["filter.date.month"]  = "最近一个月",
        ["filter.date.year"]   = "最近一年",
        ["filter.clear"]       = "清除条件",
        ["filter.kind"]        = "类型",
        ["filter.kind.folder"] = "仅文件夹",
        ["filter.kind.file"]   = "仅文件",

        // 컬럼·상태
        ["col.drive"]  = "盘符",
        ["col.name"]   = "名称",
        ["col.path"]   = "路径",
        ["col.size"]   = "大小",
        ["col.date"]   = "修改日期",
        ["status.indexing"]         = "正在索引驱动器...",
        ["status.total"]            = "合计",
        ["status.items"]            = " 个",
        ["status.results"]          = "个结果",
        ["status.refreshDone"]      = "索引刷新完成",
        ["status.selected"]         = "已选择 {0} 个，{1}",
        ["status.selectedMany"]     = "已选择 {0:N0} 个",
        ["status.indexingProgress"] = "正在索引… 已用 {0} 秒  ·  {1}",
        ["status.driveDone"]        = "{0} 完成({1:N0})",
        ["status.driveWorking"]     = "{0} 进行中",

        // 트레이
        ["tray.open"]         = "打开 HdrTracer",
        ["tray.exit"]         = "退出",
        ["tray.pinned"]       = "固定的搜索",
        ["tray.pinned.empty"] = "没有固定的搜索",
        ["tray.settings"]     = "设置",

        // 설정
        ["settings.title"]     = "设置",
        ["settings.indexing"]  = "索引",
        ["settings.usb"]       = "索引可移动驱动器",
        ["settings.usb.desc"]  = "将 USB 驱动器中的文件纳入搜索。",
        ["settings.tray"]      = "关闭时最小化到托盘",
        ["settings.tray.desc"] = "X 按钮不退出程序，而是隐藏到托盘。右键点击托盘图标可退出。",
        ["settings.hidden"]      = "显示隐藏和系统项目",
        ["settings.hidden.desc"] = "在搜索结果中包含隐藏+系统属性的项目（例如杀毒软件的保护文件夹）。",
        ["settings.ok"]        = "确定",
        ["settings.usbOn"]     = "点击确定后将开始索引 USB 驱动器。",
        ["settings.usbOff"]    = "点击确定后将从内存中移除已索引的 USB 数据。",
        ["settings.autostart"]      = "开机时自动启动",
        ["settings.autostart.desc"] = "登录后自动在托盘中运行（使用任务计划程序，不会弹出 UAC）。",
        ["settings.autostart.fail"] = "更改自动启动设置失败。",
        ["settings.excluded"]      = "搜索时排除的文件夹名称",
        ["settings.excluded.desc"] = "用分号 (;) 分隔。这些名称的文件夹及其中的所有内容都会从结果中隐藏。例如：WinSxS; node_modules",
        ["settings.hotkey"]        = "全局快捷键 (Win+Alt+S)",
        ["settings.hotkey.desc"]   = "在任何应用中按 Win+Alt+S 调出窗口。按 Esc 可隐藏。",
        ["settings.restoreSearch"]      = "启动时恢复上次的搜索",
        ["settings.restoreSearch.desc"] = "重新打开应用时，会以上次使用的搜索词直接显示结果。",
        ["hotkey.fail"]            = "注册全局快捷键 (Win+Alt+S) 失败 — 可能已被其他程序占用。",

        // 단축키 창
        ["sc.title"]        = "快捷键",
        ["sc.appMenu"]      = "应用菜单",
        ["sc.openSettings"] = "打开设置",
        ["sc.refresh"]      = "刷新索引",
        ["sc.zoomIn"]       = "放大",
        ["sc.zoomOut"]      = "缩小",
        ["sc.zoomReset"]    = "重置缩放",
        ["sc.searchBox"]    = "搜索框",
        ["sc.pinnedSearch"] = "运行固定的搜索（从上往下 1–9）",
        ["sc.focusSearch"]  = "聚焦搜索框",
        ["sc.clearSearch"]  = "清空搜索（再按一次隐藏到托盘）",
        ["sc.gotoResults"]  = "移动到结果列表",
        ["sc.resultList"]   = "结果列表",
        ["sc.openItem"]     = "打开所选项目",
        ["sc.viewProps"]    = "查看属性",
        ["sc.copyPath"]     = "复制完整路径",
        ["sc.copyName"]     = "仅复制文件名",
        ["sc.copyFile"]     = "复制文件",
        ["sc.upFirst"]      = "↑（在第一行）",
        ["sc.backToSearch"] = "返回搜索框",
        ["sc.dblClick"]     = "双击",
        ["sc.globalSc"]     = "全局快捷键",
        ["sc.toggleApp"]    = "从任何位置调出窗口（按 Esc 隐藏）",
        ["sc.globalHotkey"] = "将窗口调到前面（在任何应用中都有效）",
        ["sc.searchTips"]   = "搜索提示",
        ["sc.tip1"]         = "用空格分隔多个词，可查找同时包含所有词的结果（AND 搜索）。",
        ["sc.tip2"]         = "韩文 2 个字符以上、英文 3 个字符以上可使用 N-gram 快速搜索。",

        // 검색 도움말
        ["help.search.title"] = "搜索帮助",
        ["help.search.body"] =
            "#基本搜索\n" +
            "假期 照片|名称同时包含两个词（空格 = 并且）\n" +
            "*.jpg|仅 jpg 文件（可多个：*.jpg *.png）\n" +
            "\n" +
            "#排除不需要的项目\n" +
            "报告 -临时|名称中包含“临时”的排除\n" +
            "*.txt -*.log|查找 txt 但排除 log\n" +
            "\n" +
            "#按名称形状匹配（通配符）\n" +
            "IMG_*_编辑.txt|以 IMG_ 开头、以 _编辑.txt 结尾（含扩展名）\n" +
            "IMG_*_编辑*|不限扩展名时在末尾加 *\n" +
            "照片_?.jpg|? 表示一个字符（照片_1 可以，照片_12 不行）\n" +
            "\n" +
            "#按大小 / 日期筛选\n" +
            "*.mp4 >500MB|大于 500MB 的 mp4（单位必填：KB MB GB TB）\n" +
            ">1GB|查找超过 1GB 的文件（可单独使用）\n" +
            "照片 >2026-01|2026 年 1 月以后修改（年-月-日 顺序）\n" +
            "*.pdf <2024|2024 年之前修改的 pdf\n" +
            "报告 >week|最近 7 天（today · week · month · year）\n" +
            "项目 folder:|仅查找文件夹（仅文件用 file:）\n" +
            "\n" +
            "#仅在特定文件夹中\n" +
            "照片 D:\\备份\\|仅在 D:\\备份 下搜索\n" +
            "照片 \\旅行\\|路径中包含“旅行”文件夹的项目\n" +
            "照片 \"D:\\我的 文档\\\"|含空格的路径用引号括起来\n" +
            "\n" +
            "也可以右键点击结果 → “仅在此文件夹中搜索”。\n" +
            "只输入路径不会搜索，请与关键词或扩展名一起使用。",

        // 정보
        ["about.title"]   = "关于",
        ["about.version"] = "版本 {0}",
        ["about.desc"]    = "直接读取 NTFS 文件系统实现快速搜索的工具。",

        // 인덱스 새로 고침
        ["refresh.confirm.msg"]   = "将从头重建索引。\n\n搜索可能会短暂暂停。要继续吗？",
        ["refresh.confirm.title"] = "刷新索引",
        ["refresh.fail"]          = "索引刷新失败",

        // 공통
        ["common.error"]  = "错误",
        ["common.ok"]     = "确定",
        ["common.cancel"] = "取消",
    };
}
