namespace HdrTracer.Core;

// 일본어 문자열. 번역이 빠진 키는 자동으로 영어로 대체된다.
public static partial class Localization
{
    internal static readonly Dictionary<string, string> _ja = new()
    {
        // 메뉴
        ["menu.settings"]     = "設定",
        ["menu.refresh"]      = "インデックスの再作成",
        ["menu.resetCols"]    = "列幅をリセット",
        ["menu.zoom"]         = "表示倍率",
        ["menu.zoom.in"]      = "拡大",
        ["menu.zoom.out"]     = "縮小",
        ["menu.zoom.reset"]   = "倍率をリセット",
        ["menu.shortcuts"]    = "ショートカット",
        ["menu.about"]        = "バージョン情報",
        ["menu.language"]     = "言語",
        ["menu.lang.ko"]      = "韓国語",
        ["menu.lang.en"]      = "英語",
        ["menu.lang.zh"]      = "中国語(簡体字)",
        ["menu.lang.ja"]      = "日本語",
        ["menu.lang.es"]      = "スペイン語",
        ["menu.lang.de"]      = "ドイツ語",
        ["menu.lang.fr"]      = "フランス語",
        ["menu.searchHelp"]   = "検索ヘルプ",
        ["menu.export"]       = "結果をエクスポート (CSV)",
        ["ctx.export"]        = "選択した項目をエクスポート",
        ["export.done"]       = "{0} 件を CSV に保存しました。",
        ["menu.filter"]       = "フィルター",

        // 툴팁
        ["tip.menu"]     = "メニュー",
        ["tip.minimize"] = "最小化",
        ["tip.maximize"] = "最大化",
        ["tip.close"]    = "閉じる",
        ["tip.search"]   = "検索",
        ["tip.delete"]   = "削除",
        ["tip.pin"]      = "ピン留め / 解除",

        // 검색창·빈 결과
        ["search.placeholder"] = "ファイル名を入力して Enter",
        ["empty.title"] = "結果がありません",
        ["empty.body"]  = "検索語を短くするか、別の語で試してください\n複数の語はすべて含む項目だけが一致します\nヘルプ: メニュー (HdrTracer ▼) → 検索ヘルプ",

        // 우클릭 메뉴
        ["ctx.open"]           = "開く",
        ["ctx.runAsAdmin"]     = "管理者として実行",
        ["ctx.openWith"]       = "プログラムから開く...",
        ["ctx.reveal"]         = "フォルダーで表示",
        ["ctx.searchFolder"]   = "このフォルダー内だけを検索",
        ["ctx.copyPath"]       = "パスをコピー",
        ["ctx.copyName"]       = "名前をコピー",
        ["ctx.rename"]         = "名前の変更",
        ["ctx.copyFile"]       = "ファイルをコピー",
        ["ctx.delete"]         = "ごみ箱に移動",
        ["ctx.properties"]     = "プロパティ",
        ["ctx.rename.title"]   = "名前の変更",
        ["ctx.rename.prompt"]  = "新しい名前:",
        ["ctx.rename.extWarn"] = "拡張子を変更するとファイルが使えなくなる場合があります。\nこのまま続行しますか？",
        ["ctx.delete.confirm"] = "このファイルをごみ箱に移動しますか？",
        ["ctx.delete.more"]    = "…ほか {0} 件",
        ["ctx.delete.danger"]  = "⚠️ 警告: システムに重要な項目が含まれています。削除するとシステムが不安定になる可能性があります。",
        ["ctx.delete.confirm.multi"] = "選択した {0} 件をごみ箱に移動しますか？",
        ["ctx.delete.done.multi"]    = "{0} 件をごみ箱に移動しました。",
        ["ctx.delete.partial"] = "成功 {0} 件、失敗 {1} 件",
        ["ctx.copyFile.multi"] = "{0} 件のファイルをクリップボードにコピーしました",
        ["ctx.copyFile.none"]  = "コピーできるファイルがありません",
        ["ctx.copyPath.multi"] = "{0} 件のパスをコピーしました",
        ["ctx.copyName.multi"] = "{0} 件の名前をコピーしました",
        ["ctx.open.multi"]     = "{0} 件を開きました",
        ["ctx.open.partial"]   = "{0} 件を開き、{1} 件が失敗しました",
        ["ctx.delete.title"]   = "ごみ箱に移動",
        ["ctx.error"]          = "エラー",
        ["ctx.notExecutable"]  = "実行ファイルではありません",

        // 빠른 필터
        ["filter.doc"]         = "文書",
        ["filter.img"]         = "画像",
        ["filter.media"]       = "メディア",
        ["filter.exe"]         = "アプリ",
        ["filter.zip"]         = "圧縮ファイル",
        ["filter.size"]        = "サイズ",
        ["filter.size.10mb"]   = "10MB 以上",
        ["filter.size.100mb"]  = "100MB 以上",
        ["filter.size.1gb"]    = "1GB 以上",
        ["filter.date"]        = "期間",
        ["filter.date.today"]  = "今日",
        ["filter.date.week"]   = "過去 1 週間",
        ["filter.date.month"]  = "過去 1 か月",
        ["filter.date.year"]   = "過去 1 年",
        ["filter.clear"]       = "条件をクリア",
        ["filter.kind"]        = "種類",
        ["filter.kind.folder"] = "フォルダーのみ",
        ["filter.kind.file"]   = "ファイルのみ",

        // 컬럼·상태
        ["col.drive"]  = "DRV",
        ["col.name"]   = "名前",
        ["col.path"]   = "パス",
        ["col.size"]   = "サイズ",
        ["col.date"]   = "更新日時",
        ["status.indexing"]         = "ドライブをインデックス中...",
        ["status.total"]            = "合計",
        ["status.items"]            = " 件",
        ["status.results"]          = "件の結果",
        ["status.refreshDone"]      = "インデックスの再作成が完了しました",
        ["status.selected"]         = "{0} 件選択、{1}",
        ["status.selectedMany"]     = "{0:N0} 件選択",
        ["status.indexingProgress"] = "インデックス中… {0} 秒経過  ·  {1}",
        ["status.driveDone"]        = "{0} 完了({1:N0})",
        ["status.driveWorking"]     = "{0} 処理中",

        // 트레이
        ["tray.open"]         = "HdrTracer を開く",
        ["tray.exit"]         = "終了",
        ["tray.pinned"]       = "ピン留めした検索",
        ["tray.pinned.empty"] = "ピン留めした検索はありません",
        ["tray.settings"]     = "設定",

        // 설정
        ["settings.title"]     = "設定",
        ["settings.indexing"]  = "インデックス",
        ["settings.usb"]       = "リムーバブルドライブをインデックス",
        ["settings.usb.desc"]  = "USB ドライブ内のファイルも検索対象にします。",
        ["settings.tray"]      = "閉じるボタンでトレイに最小化",
        ["settings.tray.desc"] = "X ボタンで終了せずトレイに隠します。終了はトレイアイコンの右クリックから。",
        ["settings.hidden"]      = "隠し・システム項目を表示",
        ["settings.hidden.desc"] = "隠し+システム属性の項目（ウイルス対策ソフトの保護フォルダーなど）も検索結果に含めます。",
        ["settings.ok"]        = "OK",
        ["settings.usbOn"]     = "OK を押すと USB ドライブのインデックスを開始します。",
        ["settings.usbOff"]    = "OK を押すとインデックス済みの USB データをメモリから解放します。",
        ["settings.autostart"]      = "Windows 起動時に自動実行",
        ["settings.autostart.desc"] = "サインイン時にトレイで自動的に起動します（タスク スケジューラ、UAC なし）。",
        ["settings.autostart.fail"] = "自動実行の設定変更に失敗しました。",
        ["settings.excluded"]      = "検索から除外するフォルダー名",
        ["settings.excluded.desc"] = "セミコロン (;) で区切ります。この名前のフォルダーとその中の項目が結果から隠れます。例: WinSxS; node_modules",
        ["settings.hotkey"]        = "グローバル ホットキー (Win+Alt+S)",
        ["settings.hotkey.desc"]   = "どのアプリからでも Win+Alt+S でウィンドウを呼び出します。隠すには Esc を使います。",
        ["settings.restoreSearch"]      = "起動時に前回の検索を復元",
        ["settings.restoreSearch.desc"] = "アプリを開き直すと、前回の検索語で検索した状態で始まります。",
        ["hotkey.fail"]            = "グローバル ホットキー (Win+Alt+S) の登録に失敗しました — 他のプログラムが使用している可能性があります。",

        // 단축키 창
        ["sc.title"]        = "ショートカット",
        ["sc.appMenu"]      = "アプリ メニュー",
        ["sc.openSettings"] = "設定を開く",
        ["sc.refresh"]      = "インデックスの再作成",
        ["sc.zoomIn"]       = "拡大",
        ["sc.zoomOut"]      = "縮小",
        ["sc.zoomReset"]    = "倍率をリセット",
        ["sc.searchBox"]    = "検索ボックス",
        ["sc.pinnedSearch"] = "ピン留めした検索を実行（上から 1〜9）",
        ["sc.focusSearch"]  = "検索ボックスにフォーカス",
        ["sc.clearSearch"]  = "検索をクリア（もう一度押すとトレイへ）",
        ["sc.gotoResults"]  = "結果リストへ移動",
        ["sc.resultList"]   = "結果リスト",
        ["sc.openItem"]     = "選択した項目を開く",
        ["sc.viewProps"]    = "プロパティを表示",
        ["sc.copyPath"]     = "フル パスをコピー",
        ["sc.copyName"]     = "ファイル名だけをコピー",
        ["sc.copyFile"]     = "ファイルをコピー",
        ["sc.upFirst"]      = "↑（先頭行で）",
        ["sc.backToSearch"] = "検索ボックスに戻る",
        ["sc.dblClick"]     = "ダブルクリック",
        ["sc.globalSc"]     = "グローバル ショートカット",
        ["sc.toggleApp"]    = "どこからでもウィンドウを呼び出す（隠すには Esc）",
        ["sc.globalHotkey"] = "ウィンドウを前面に出す（どのアプリからでも）",
        ["sc.searchTips"]   = "検索のヒント",
        ["sc.tip1"]         = "複数の語をスペースで区切ると、すべてを含む結果を検索します（AND 検索）。",
        ["sc.tip2"]         = "韓国語は 2 文字以上、英字は 3 文字以上で N-gram 高速検索が使えます。",

        // 검색 도움말
        ["help.search.title"] = "検索ヘルプ",
        ["help.search.body"] =
            "#基本の検索\n" +
            "休暇 写真|両方の語を名前に含む項目（スペース = かつ）\n" +
            "*.jpg|jpg ファイルのみ（複数可: *.jpg *.png）\n" +
            "\n" +
            "#除外したいとき\n" +
            "報告書 -一時|名前に「一時」を含むものを除く\n" +
            "*.txt -*.log|txt を探し log を除く\n" +
            "\n" +
            "#名前の形で探す（ワイルドカード）\n" +
            "IMG_*_編集.txt|IMG_ で始まり _編集.txt で終わる名前（拡張子まで含む）\n" +
            "IMG_*_編集*|拡張子を問わないときは末尾に * を付ける\n" +
            "写真_?.jpg|? は 1 文字（写真_1 は可、写真_12 は不可）\n" +
            "\n" +
            "#サイズ・日付で絞る\n" +
            "*.mp4 >500MB|500MB より大きい mp4（単位必須: KB MB GB TB）\n" +
            ">1GB|1GB を超えるファイルを探す（単独で使用可）\n" +
            "写真 >2026-01|2026 年 1 月以降に更新（年-月-日 の順）\n" +
            "*.pdf <2024|2024 年より前に更新された pdf\n" +
            "報告書 >week|直近 7 日間（today · week · month · year）\n" +
            "プロジェクト folder:|フォルダーだけを探す（ファイルだけなら file:）\n" +
            "\n" +
            "#特定のフォルダー内だけ\n" +
            "写真 D:\\バックアップ\\|そのフォルダー配下だけを検索\n" +
            "写真 \\旅行\\|パスに「旅行」フォルダーがあるものだけ\n" +
            "写真 \"D:\\マイ ドキュメント\\\"|スペースを含むパスは引用符で囲む\n" +
            "\n" +
            "結果を右クリック →「このフォルダー内だけを検索」でも指定できます。\n" +
            "パスだけでは検索されません。語句や拡張子と一緒に使ってください。",

        // 정보
        ["about.title"]   = "バージョン情報",
        ["about.version"] = "バージョン {0}",
        ["about.desc"]    = "NTFS ファイル システムを直接読み取って高速に検索するツールです。",

        // 인덱스 새로 고침
        ["refresh.confirm.msg"]   = "インデックスを最初から作り直します。\n\n検索が一時的に止まる場合があります。続行しますか？",
        ["refresh.confirm.title"] = "インデックスの再作成",
        ["refresh.fail"]          = "インデックスの再作成に失敗しました",

        // 공통
        ["common.error"]  = "エラー",
        ["common.ok"]     = "OK",
        ["common.cancel"] = "キャンセル",
    };
}
