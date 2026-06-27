using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using HdrTracer.Core;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Threading;

using Loc = HdrTracer.Core.Localization;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using ListViewItem = System.Windows.Controls.ListViewItem;
using MessageBox = System.Windows.MessageBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace HdrTracer.App;

public partial class MainWindow : Window
{
    // 헤더와 모든 결과 행이 공유하는 컬럼 너비 소스 (SharedSizeGroup 대체).
    // XAML이 RootWindow.Cols 로 바인딩한다.
    public ColumnWidths Cols { get; } = new();

    private readonly MultiDriveIndex _multi = new();
    private readonly SearchEngine _engine = new();
    private readonly DriveWatcher _watcher = new();

    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private long _searchSequence;
    private const int MaxDisplayResults = 1_000_000;

    // 결과 리스트의 ContextMenu가 열려있는 동안에는 자동 재검색을 보류한다.
    // (재검색 = ItemsSource 교체 = 선택 해제 → 메뉴 클릭 시 row=null이 되는 문제 방지)
    private bool _contextMenuOpen;

    // 방금 휴지통으로 보낸 경로들. 삭제 직후 도는 자동 재검색이 (USN 인덱스 갱신 지연 때문에)
    // 이 항목들을 잠깐 되살리는 것을 막아, 우클릭 삭제도 단축키처럼 즉시·깔끔하게 사라지게 한다.
    private readonly HashSet<string> _recentlyDeletedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    private enum SortColumn { Drive, Name, Path, Size, Date, Kind }
    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAscending = true; 
    private readonly List<MetadataPreloader> _preloaders = new();
    // 각 슬롯의 캐시 메타정보 (저장할 때 필요)
    private readonly Dictionary<string, (ulong JournalId, uint VolumeSerial)> _cacheMeta = new();

    private AppSettings _settings = AppSettings.Load();

    private TrayIconHelper? _trayIcon;
    private bool _reallyClose;

    private GlobalHotkey? _globalHotkey;

    private const double ZoomMin = 0.5;
    private const double ZoomMax = 2.5;
    private const double ZoomStep = 0.1;

    private DateTime _ignoreIndexChangesUntil = DateTime.MinValue;
    private readonly DispatcherTimer _indexChangedDebounce;
    private readonly DispatcherTimer _preloadResumeTimer;
    private long _lastSearchMs;  // 사용자가 직접 한 검색의 소요 시간(자동 재검색 시 유지)

    private string _lastSearchQuery = "";

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += MainWindow_StateChanged;

        // 저장된 설정을 검색 엔진에 반영 (기본 false → 숨김+시스템 항목 숨김)
        _engine.HideHiddenSystemItems = !_settings.ShowHiddenSystemItems;

        // 저장된 컬럼 너비 복원
        ApplySavedColumnWidths();

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounceTimer.Tick += DebounceTimer_Tick;

        _indexChangedDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _indexChangedDebounce.Tick += (_, _) =>
        {
            _indexChangedDebounce.Stop();
            // 사용자가 아직 한 번도 검색을 실행하지 않았으면 자동 갱신 안 함.
            // (검색은 Enter/검색 버튼으로만 시작되어야 일관성 유지)
            if (string.IsNullOrEmpty(_lastSearchQuery)) return;
            // 검색창이 비었으면 갱신할 대상 없음
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) return;
            // 현재 검색창 내용이 마지막으로 실행한 검색과 같을 때만 결과 갱신.
            // (사용자가 새 검색어를 타이핑 중인데 자동 검색되는 것을 방지)
            if (SearchBox.Text != _lastSearchQuery) return;
            // 컨텍스트 메뉴가 열려 있으면 자동 재검색 보류 (선택 유지를 위해).
            // 메뉴가 닫힐 때 다시 트리거된다.
            if (_contextMenuOpen)
            {
                _indexChangedDebounce.Start();
                return;
            }
            RunSearch(isAuto: true);
        };

        _preloadResumeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
        _preloadResumeTimer.Tick += (_, _) =>
        {
            _preloadResumeTimer.Stop();
            foreach (var p in _preloaders) p.Resume();
        };

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        SourceInitialized += MainWindow_SourceInitialized;

        // 앱 첫 활성화 시 검색창에 키보드 포커스 강제 (UAC 거친 시작에서 포커스가 가지 않는 문제 백업).
        // 1회만 발동, 그 이후 활성화에는 영향 없음.
        bool firstActivated = false;
        Activated += (_, _) =>
        {
            if (firstActivated) return;
            firstActivated = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SearchBox.IsEnabled)
                {
                    SearchBox.Focus();
                    Keyboard.Focus(SearchBox);
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        };

        _watcher.DriveArrived += OnDriveArrived;
        _watcher.DriveRemoved += OnDriveRemoved;
        _watcher.DriveQueryRemove += OnDriveQueryRemove;

        _multi.SlotsChanged += () =>
            Dispatcher.BeginInvoke(UpdateFooterSummary);

        // 메뉴 항목 단축키 (Ctrl+, → 설정, F5 → 인덱스 다시 빌드)
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => SettingsButton_Click(this, new RoutedEventArgs())),
            Key.OemComma, ModifierKeys.Control));

        InputBindings.Add(new KeyBinding(
            new RelayCommand(async _ => await RebuildIndex()),
            Key.F5, ModifierKeys.None));

         // 줌 단축키
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomIn()),
            Key.OemPlus, ModifierKeys.Control));   // Ctrl + +
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomIn()),
            Key.Add, ModifierKeys.Control));        // Ctrl + 숫자패드 +
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomOut()),
            Key.OemMinus, ModifierKeys.Control));  // Ctrl + -
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomOut()),
            Key.Subtract, ModifierKeys.Control));   // Ctrl + 숫자패드 -
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomReset()),
            Key.D0, ModifierKeys.Control));         // Ctrl + 0
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomReset()),
            Key.NumPad0, ModifierKeys.Control));    // Ctrl + 숫자패드 0

        // 저장된 배율 즉시 적용
        ApplyZoom(_settings.UiZoom);

        // 저장된 언어 적용
        HdrTracer.Core.Localization.Current =
            _settings.Language == "en"
                ? HdrTracer.Core.Localization.Lang.English
                : HdrTracer.Core.Localization.Lang.Korean;

        // 저장된 언어로 컬럼 헤더 등 초기 텍스트 반영
        ApplyLocalizedTexts();
    }

    // ==================================================
    //  WM_DEVICECHANGE 후크 등록 (창이 핸들 만들어진 직후)
    // ==================================================
    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        _watcher.AttachTo(src);

        // 글로벌 단축키 등록 (Win + Alt + S)
        try
        {
            _globalHotkey = new GlobalHotkey(this);
            _globalHotkey.Pressed += (_, _) => ToggleVisibility();

            // S 키 = virtual key 0x53
            bool ok = _globalHotkey.Register(
                GlobalHotkey.Modifiers.Win | GlobalHotkey.Modifiers.Alt,
                0x53);

            if (!ok)
            {
                // 다른 앱이 같은 단축키를 이미 등록했을 수 있음
                System.Diagnostics.Debug.WriteLine("글로벌 단축키 등록 실패: 다른 앱과 충돌");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"글로벌 단축키 초기화 실패: {ex.Message}");
        }
    }

    private void ToggleVisibility()
    {
        if (Visibility != Visibility.Visible || WindowState == WindowState.Minimized)
        {
            // 안 보임 → 보이기 + 검색창 포커스
            if (Visibility != Visibility.Visible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        else
        {
            // 보임 + 활성화 → 숨김 (트레이로)
            if (_settings.MinimizeToTrayOnClose && _trayIcon is not null)
            {
                _trayIcon.HideWindow();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }
    }

    // ==================================================
    //  앱 로드 → 시작 시점 드라이브 인덱싱
    // ==================================================
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {   
        var drives = DriveDetector.GetIndexableDrives(_settings.IndexRemovableDrives);

        if (drives.Count == 0)
        {
            StatusText.Text = "⚠ 인덱싱 가능한 NTFS 드라이브가 없습니다.";
            StatusText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            FooterText.Text = "드라이브 없음";
            SearchBox.IsEnabled = true;
            SearchBox.Focus();
            return;
        }

        StatusText.Text = $"드라이브 {drives.Count}개 인덱싱 중: {string.Join(", ", drives)} ...";
        FooterText.Text = "인덱스 빌드 중";

        foreach (var d in drives)
            _multi.AddSlot(new MultiDriveIndex.DriveSlot { DriveLetter = d });

        var totalSw = Stopwatch.StartNew();

        var tasks = _multi.Slots.Select(slot => Task.Run(() => BuildOneDrive(slot))).ToArray();
        await Task.WhenAll(tasks);
        totalSw.Stop();

        // 모니터 시작
        foreach (var slot in _multi.Slots)
        {
            StartMonitorIfReady(slot);
            StartMetadataPreloader(slot);
        }

        totalSw.Stop();

        // 모니터 시작
        foreach (var slot in _multi.Slots)
        {
            StartMonitorIfReady(slot);
            StartMetadataPreloader(slot);
        }

        // 트레이 아이콘 (인덱싱 다 끝나고 나서 초기화)
        try
        {
            _trayIcon = new TrayIconHelper(this);
            _trayIcon.ExitRequested += (_, _) =>
            {
                _reallyClose = true;
                Close();
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"트레이 아이콘 초기화 실패: {ex.Message}");
        }

        StatusBanner.Visibility = Visibility.Collapsed;
        UpdateFooterSummary();

        SearchBox.IsEnabled = true;
        // 앱 시작 직후엔 윈도우가 키보드 포커스를 못 받은 상태일 수 있다 (특히 UAC 거쳐서 시작된 경우).
        // 단순 Focus()로는 부족하므로 Dispatcher로 미뤄서 Activate + Keyboard.Focus 강제.
        // (반환된 DispatcherOperation은 await 불필요. fire-and-forget.)
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            Activate();
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void BuildOneDrive(MultiDriveIndex.DriveSlot slot)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            uint volSerial = IndexStore.GetVolumeSerial(slot.DriveLetter);

            // 1) 캐시 시도
            var cached = IndexStore.TryLoad(slot.DriveLetter);
            if (cached is not null
                && cached.VolumeSerial == volSerial
                && volSerial != 0)
            {
                try
                {
                    var (newUsn, changes) = UsnCatchUp.Apply(
                        cached.Index, slot.DriveLetter, cached.JournalId, cached.LastUsn);

                    slot.Index = cached.Index;
                    slot.BuildMs = sw.ElapsedMilliseconds;
                    _cacheMeta[slot.DriveLetter] = (cached.JournalId, volSerial);

                    // ngram이 캐시에 없으면 (구버전 캐시) 빌드
                    if (slot.Index.Ngram is null)
                    {
                        slot.Index.BuildNgramIndex();
                    }

                    Debug.WriteLine($"[{slot.DriveLetter}] Loaded from cache, applied {changes} changes in {sw.ElapsedMilliseconds}ms");
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[{slot.DriveLetter}] Catch-up failed: {ex.Message} — rebuilding");
                    IndexStore.Delete(slot.DriveLetter);
                }
            }

            // 2) 풀 빌드 (RawMftReader가 내부에서 LinkParents까지 완료)
            var (idx, journalId, startUsn) = RawMftReader.BuildIndexWithJournalInfo(slot.DriveLetter);
            idx.BuildNgramIndex();   // ngram 빌드
            slot.Index = idx;
            slot.BuildMs = sw.ElapsedMilliseconds;
            _cacheMeta[slot.DriveLetter] = (journalId, volSerial);
        }
        catch (UnauthorizedAccessException)
        {
            slot.Error = "관리자 권한 필요";
        }
        catch (Exception ex)
        {
            slot.Error = ex.Message;
        }
    }

    private void StartMonitorIfReady(MultiDriveIndex.DriveSlot slot)
    {
        if (slot.Index is null || slot.Monitor is not null) return;
        try
        {
            var monitor = new UsnJournalMonitor(slot.Index, slot.DriveLetter);
            monitor.IndexChanged += OnIndexChanged;
            monitor.Start();
            slot.Monitor = monitor;

            // 이 볼륨 핸들을 안전 제거 알림에 등록 → 사용자가 "꺼내기"를 누르면
            // Windows가 DriveQueryRemove를 보내고, 그때 핸들을 닫아 안전 제거가 성공한다.
            _watcher.RegisterVolumeHandle(slot.DriveLetter, monitor.VolumeHandle);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[{slot.DriveLetter}] USN monitor failed: {ex.Message}");
        }
    }

    private void StartMetadataPreloader(MultiDriveIndex.DriveSlot slot)
    {
        if (slot.Index is null) return;

        var preloader = new MetadataPreloader(slot.Index, slot.DriveLetter);
        _preloaders.Add(preloader);
        preloader.Start();
    }

    // ==================================================
    //  드라이브 동적 감지
    // ==================================================
    private async void OnDriveArrived(string driveLetter)
    {
        // UI 스레드에서 호출됨 (HwndSource hook). 하지만 인덱싱은 백그라운드.
        // USB는 꽂힌 직후 OS 마운트가 끝나기 전이라, 그 순간엔 NTFS·볼륨 정보가
        // 아직 안 잡혀 인덱싱 가능 목록에서 빠질 수 있다. 바로 포기하지 말고
        // 짧은 간격으로 몇 번 재확인하여 마운트가 끝나길 기다린다.
        bool isIndexable = false;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var list = DriveDetector.GetIndexableDrives(_settings.IndexRemovableDrives);
            if (list.Contains(driveLetter, StringComparer.OrdinalIgnoreCase))
            {
                isIndexable = true;
                break;
            }
            await Task.Delay(500); // 0.5초 간격, 최대 8회(약 4초)까지 대기
        }
        if (!isIndexable)
        {
            Debug.WriteLine($"[Watcher] {driveLetter} arrived but not indexable (non-NTFS or never became ready)");
            return;
        }

        if (_multi.ContainsDrive(driveLetter))
        {
            Debug.WriteLine($"[Watcher] {driveLetter} already in index, skipping");
            return;
        }

        var slot = new MultiDriveIndex.DriveSlot { DriveLetter = driveLetter };
        _multi.AddSlot(slot);
        UpdateFooterSummary();   // "인덱싱 중..." 비슷한 표시 갱신

        // 비동기 빌드
        await Task.Run(() => BuildOneDrive(slot));

        StartMonitorIfReady(slot);
        StartMetadataPreloader(slot);

        UpdateFooterSummary();

        // USB 인덱스 빌드가 끝났으니, 활성 검색이 있으면 결과를 자동 갱신한다.
        // 파일 변경 라이브 갱신과 동일한 디바운스 경로(OnIndexChanged)를 재사용해
        // 인덱스가 안정된 직후 안정적으로 재검색되게 한다.
        if (!string.IsNullOrEmpty(_lastSearchQuery))
        {
            if (SearchBox.Text != _lastSearchQuery)
                SearchBox.Text = _lastSearchQuery;
            OnIndexChanged();
        }
    }

    private void OnDriveRemoved(string driveLetter)
    {
        if (!_multi.ContainsDrive(driveLetter)) return;

        _watcher.UnregisterVolume(driveLetter);  // 알림 등록 해제 (핸들 닫기 전)
        _multi.RemoveDrive(driveLetter);
        UpdateFooterSummary();

        // USB가 분리되면, 마지막으로 실행한 검색 기준으로 결과를 자동 갱신
        // (사라진 드라이브의 항목이 결과에서 빠지도록)
        if (!string.IsNullOrEmpty(_lastSearchQuery))
        {
            if (SearchBox.Text != _lastSearchQuery)
                SearchBox.Text = _lastSearchQuery;
            RunSearch();
        }
    }

    // 안전 제거(꺼내기) 시도 직전. 이 드라이브의 USN 모니터 핸들을 즉시 풀어줘서
    // Windows가 "사용 중" 거부 없이 분리할 수 있게 한다. RemoveDrive 내부에서
    // 해당 슬롯의 Monitor를 Dispose하므로 볼륨 핸들이 닫힌다. (그냥 뽑는 경우엔
    // DBT_DEVICEREMOVECOMPLETE → OnDriveRemoved가 처리하므로 중복돼도 안전)
    // 안전 제거(꺼내기) 시도 직전 호출. 이 드라이브가 잡고 있는 모든 것을 풀어줘서
    // Windows가 "사용 중" 거부 없이 분리하게 한다:
    //  1) 알림 등록 해제  2) 메타 사전로더 중지  3) USN 모니터 핸들 Dispose
    private void OnDriveQueryRemove(string driveLetter)
    {
        if (!_multi.ContainsDrive(driveLetter)) return;

        // 1) 디바이스 알림 등록 해제 (핸들 닫기 전에)
        _watcher.UnregisterVolume(driveLetter);

        // 2) 이 드라이브의 메타데이터 사전로더 중지 (USB 파일 접근 중단)
        for (int i = _preloaders.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_preloaders[i].DriveLetter, driveLetter, StringComparison.OrdinalIgnoreCase))
            {
                _preloaders[i].Stop();
                _preloaders.RemoveAt(i);
            }
        }

        // 3) USN 모니터 핸들 해제 (RemoveDrive 내부에서 Monitor.Dispose → 볼륨 핸들 닫힘)
        _multi.RemoveDrive(driveLetter);
        UpdateFooterSummary();

        if (!string.IsNullOrEmpty(_lastSearchQuery))
        {
            if (SearchBox.Text != _lastSearchQuery)
                SearchBox.Text = _lastSearchQuery;
            RunSearch();
        }
    }

    // ==================================================
    //  하단 상태바 요약
    // ==================================================
    private void UpdateFooterSummary()
    {
        var slots = _multi.Slots;
        if (slots.Count == 0)
        {
            FooterText.Text = Loc.T("status.indexing");
            return;
        }

        var parts = slots.Select(s =>
        {
            if (s.Error is not null) return $"{s.DriveLetter} ✕";
            if (s.Index is null) return $"{s.DriveLetter} …";
            return $"{s.DriveLetter} {s.Index.Count:N0}";
        });

        var summary = string.Join(" + ", parts);
        var total = _multi.TotalEntryCount;
        FooterText.Text = $"{summary} = {Loc.T("status.total")} {total:N0}{Loc.T("status.items")}";
    }

    // ==================================================
    //  검색 (디바운스)
    // ==================================================
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 입력이 시작되면 히스토리 팝업은 닫는다
        if (!string.IsNullOrEmpty(SearchBox.Text) && HistoryPopup.IsOpen)
            HistoryPopup.IsOpen = false;

        // 자동 검색 안 함. 검색은 검색 버튼 또는 Enter로만 실행.
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        RunSearch();
    }

    // 필터 그룹 → 검색창에 확장자 문법 자동 입력
    private static readonly Dictionary<string, string> _filterGroups = new()
    {
        ["doc"]   = "*.pdf *.doc *.docx *.xls *.xlsx *.ppt *.pptx *.txt *.hwp *.hwpx",
        ["img"]   = "*.jpg *.jpeg *.png *.gif *.bmp *.webp *.svg *.ico *.tiff",
        ["media"] = "*.mp4 *.avi *.mkv *.mov *.wmv *.mp3 *.wav *.flac *.m4a",
        ["exe"]   = "*.exe *.msi *.bat *.cmd",
        ["zip"]   = "*.zip *.rar *.7z *.tar *.gz",
    };

    private void ApplyFilter(string key)
    {
        if (!_filterGroups.TryGetValue(key, out var pattern)) return;

        // 검색창에서 기존 확장자 토큰은 제거하고 텍스트만 유지
        var keep = string.Join(" ",
            SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !t.StartsWith("*.") &&
                            !(t.StartsWith(".") && t.Length > 1 && !t.Contains('\\'))));

        // 새 확장자 패턴을 앞에 붙이고 텍스트 유지
        SearchBox.Text = string.IsNullOrWhiteSpace(keep)
            ? pattern
            : $"{pattern} {keep}";

        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();

        // 자동 검색이 없으므로 필터 선택 시 즉시 검색 실행
        HistoryPopup.IsOpen = false;
        RunSearch();
    }

    // ===== 검색 히스토리 =====
    private const int MaxHistory = 10;

    // 키보드 ↓/↑ 로 히스토리 항목을 "강조"만 할 때 true.
    // HistoryList_SelectionChanged 의 "선택 즉시 검색 실행"을 건너뛰기 위한 가드.
    private bool _historyKeyboardNav;

    private void AddToHistory(string query)
    {
        query = query.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        var h = _settings.SearchHistory;
        // 중복 제거 (대소문자 무시) 후 맨 앞에 추가
        h.RemoveAll(x => string.Equals(x, query, StringComparison.OrdinalIgnoreCase));
        h.Insert(0, query);

        // 최근 10개만 유지
        while (h.Count > MaxHistory)
            h.RemoveAt(h.Count - 1);

        _settings.Save();
    }

    private void ShowHistoryPopup()
    {
        var h = _settings.SearchHistory;
        if (h is null || h.Count == 0)
        {
            HistoryPopup.IsOpen = false;
            return;
        }
        // 검색창에 입력 중이면 표시 안 함 (검색 결과가 우선)
        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            HistoryPopup.IsOpen = false;
            return;
        }

        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = h.ToList();
        HistoryPopup.IsOpen = true;
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        // 검색 버튼: 즉시 검색 (히스토리 기록은 RunSearch가 담당)
        HistoryPopup.IsOpen = false;
        _debounceTimer.Stop();
        RunSearch();
        SearchBox.Focus();
    }

    private void SearchBox_Click(object sender, RoutedEventArgs e)
    {
        ShowHistoryPopup();
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 키보드 ↓/↑ 로 강조만 이동한 경우엔 즉시 검색하지 않고 Enter 입력을 기다린다.
        // (마우스 클릭 선택은 _historyKeyboardNav 가 false 이므로 기존대로 즉시 실행된다.)
        if (_historyKeyboardNav)
        {
            _historyKeyboardNav = false;
            return;
        }

        if (HistoryList.SelectedItem is not string picked) return;

        HistoryPopup.IsOpen = false;
        HistoryList.SelectedItem = null;

        SearchBox.Text = picked;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();

        // 검색 실행 (히스토리 기록/순서 갱신은 RunSearch가 담당)
        RunSearch();
    }

    private void HistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string item) return;

        _settings.SearchHistory.RemoveAll(
            x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
        _settings.Save();

        // 목록 갱신
        if (_settings.SearchHistory.Count == 0)
            HistoryPopup.IsOpen = false;
        else
        {
            HistoryList.ItemsSource = null;
            HistoryList.ItemsSource = _settings.SearchHistory.ToList();
        }

        e.Handled = true;  // ListBox 선택으로 전파 방지
    }

    private async void RunSearch(bool isAuto = false)
    {
        var query = SearchBox.Text;

        // 검색 시작 → 사전로딩 일시 중단, 검색 끝난 후 1.5초 뒤 재개
        foreach (var p in _preloaders) p.Pause();
        _preloadResumeTimer.Stop();   

        var indexes = _multi.GetActiveIndexes();

        if (string.IsNullOrWhiteSpace(query) || indexes.Count == 0)
        {
            ResultsList.ItemsSource = null;
            UpdateFooterSummary();
            return;
        }

        // 이전 검색 취소
        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        long mySeq = ++_searchSequence;

        try
        {
            var sw = Stopwatch.StartNew();

            // 1) 검색 자체는 백그라운드에서 (확장자 필터는 엔진이 처리)
            var hits = await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                lock (_multi)
                {
                    return _engine.Search(indexes, query, maxResults: MaxDisplayResults);
                }
            }, cts.Token);

            if (mySeq != _searchSequence) return;

            // 2) 행 객체는 즉시 생성 (Lazy라 빠름)
            var rows = new List<SearchResultRow>(hits.Count);
            foreach (var hit in hits)
            {
                cts.Token.ThrowIfCancellationRequested();
                rows.Add(new SearchResultRow
                {
                    SourceIndex = hit.Index,
                    EntryIndex = hit.EntryIndex,
                    Drive = hit.Index.DriveLetter,
                    Kind = hit.Index.IsDirectory(hit.EntryIndex) ? "폴더" : "파일"
                });
            }

            if (mySeq != _searchSequence) return;

            // 3) 정렬 (백그라운드)
            var sortedRows = await Task.Run(() => SortRows(rows), cts.Token);

            if (mySeq != _searchSequence) return;

            // 방금 삭제한 항목이 인덱스 갱신 지연으로 잠깐 되살아나는 것을 막는다.
            // 파일이 디스크에 다시 존재하면(복원 등) 추적을 풀어 정상 표시한다.
            if (_recentlyDeletedPaths.Count > 0)
            {
                sortedRows = sortedRows.Where(r =>
                {
                    if (!_recentlyDeletedPaths.Contains(r.Path)) return true;
                    if (System.IO.File.Exists(r.Path) || System.IO.Directory.Exists(r.Path))
                    {
                        _recentlyDeletedPaths.Remove(r.Path);
                        return true;
                    }
                    return false;
                }).ToList();
            }

            // 갱신 전 선택을 기억 (경로 기준)
            var prevSelectedPaths = new HashSet<string>(
                ResultsList.SelectedItems.OfType<SearchResultRow>().Select(r => r.Path),
                StringComparer.OrdinalIgnoreCase);

            // 4) UI 즉시 표시
            sw.Stop();
            ResultsList.ItemsSource = sortedRows;
            _lastSearchQuery = query;

            // 이전 선택 복원: 새 결과 중에서 같은 경로를 가진 행을 다시 선택
            if (prevSelectedPaths.Count > 0)
            {
                ResultsList.SelectedItems.Clear();
                foreach (var r in sortedRows)
                {
                    if (prevSelectedPaths.Contains(r.Path))
                        ResultsList.SelectedItems.Add(r);
                }
            }

            // 검색은 명시적 동작(버튼/Enter/필터/히스토리)으로만 실행되므로
            // 결과가 있으면 히스토리에 기록한다.
            if (sortedRows.Count > 0 && !string.IsNullOrWhiteSpace(query))
                AddToHistory(query);

            // 검색 끝남 → 3초 후 사전로딩 재개
            _preloadResumeTimer.Stop();
            _preloadResumeTimer.Start();

            // 사용자가 직접 한 검색일 때만 소요 시간을 갱신한다.
            // 자동 재검색(인덱스 변경)일 때는 직전 시간을 그대로 유지해서,
            // "가만히 있는데 시간이 바뀌는" 현상을 막는다.
            if (!isAuto)
                _lastSearchMs = sw.ElapsedMilliseconds;

            FooterText.Text = sortedRows.Count >= MaxDisplayResults
                ? $"{sortedRows.Count:N0}+ {Loc.T("status.results")} ({_lastSearchMs}ms)"
                : $"{sortedRows.Count:N0} {Loc.T("status.results")} ({_lastSearchMs}ms)";
        }
        catch (OperationCanceledException)
        { }
    }

    private void OnIndexChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            _indexChangedDebounce.Stop();
            _indexChangedDebounce.Start();
        });
    }

    // ==================================================
    //  컬럼 정렬
    // ==================================================
    private void HeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        SortColumn newCol = tag switch
        {
            "Drive" => SortColumn.Drive,
            "Name"  => SortColumn.Name,
            "Path"  => SortColumn.Path,
            "Size"  => SortColumn.Size,
            "Date"  => SortColumn.Date,
            "Kind"  => SortColumn.Kind,
            _ => SortColumn.Name
        };

        if (newCol == _sortColumn)
        {
            // 같은 컬럼 다시 클릭 → 정렬 방향 토글
            _sortAscending = !_sortAscending;
        }
        else
        {
            // 다른 컬럼 클릭 → 새 컬럼 오름차순
            _sortColumn = newCol;
            _sortAscending = true;
        }

        // 현재 검색 결과를 다시 정렬해서 표시
        if (ResultsList.ItemsSource is List<SearchResultRow> currentRows)
        {
            var sorted = SortRows(currentRows);
            ResultsList.ItemsSource = sorted;
        }
    }

    // 컬럼 경계 드래그 → 경계 양쪽 컬럼을 함께 조절 (고전적 분할선 동작).
    // 경계를 끌면 왼쪽 칸은 커지고 오른쪽 칸은 그만큼 줄어, 경계선만 움직이고
    // 나머지 컬럼은 그대로 유지된다. (경로는 * 채움이라 자동으로 흡수)
    // SharedSizeGroup 덕분에 헤더 너비만 바꾸면 모든 행이 자동으로 따라온다.
    private const double MinColumnWidth = 30;

    // 직전 DragDelta 시점의 마우스 X (HeaderGrid 기준). 드래그 시작 시 -1.
    private double _lastDragMouseX = -1;

    // 각 컬럼의 최소 너비 (그룹별)
    private static double MinWidthOf(string group) => group switch
    {
        "ColDrive" => 40,
        "ColName"  => 80,
        "ColSize"  => 60,
        "ColDate"  => 80,
        _          => 30,
    };
    private const double PathMinWidth = 60;

    private void ColumnSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.Thumb thumb) return;
        if (thumb.Tag is not string tag) return;

        // 고정된 HeaderGrid 기준 마우스 절대 X의 프레임 간 변화량을 delta로 쓴다.
        double mouseX = System.Windows.Input.Mouse.GetPosition(HeaderGrid).X;
        if (_lastDragMouseX < 0)
        {
            _lastDragMouseX = mouseX;
            return;
        }
        double d = mouseX - _lastDragMouseX;
        _lastDragMouseX = mouseX;
        if (d == 0) return;

        // 표준 분할선 규칙: 경계를 오른쪽으로 끌면 왼쪽 칸이 커지고 오른쪽 칸이 작아진다.
        //   DRV|이름     : DRV ↑, 이름 ↓   (둘 다 픽셀 → 합 보존)
        //   이름|경로    : 이름 ↑, 경로 ↓  (경로는 * 자동 흡수)
        //   경로|크기    : 경로 ↑, 크기 ↓  (경로는 * 자동 흡수)
        //   크기|수정날짜 : 크기 ↑, 수정날짜 ↓ (둘 다 픽셀 → 합 보존)
        switch (tag)
        {
            case "ColDrive":   AdjustTwoPixel("ColDrive", "ColName", d); break; // DRV | 이름
            case "ColName":    AdjustColumnAbsorbedByPath("ColName", +d); break; // 이름 | 경로: 이름 +d, 경로 흡수
            case "ColSizeLeft":AdjustColumnAbsorbedByPath("ColSize", -d); break; // 경로 | 크기: 크기 -d, 경로 흡수
            case "ColSize":    AdjustTwoPixel("ColSize", "ColDate", d); break; // 크기 | 수정날짜
        }
    }

    private void ColumnSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _lastDragMouseX = -1;
        SaveColumnWidths();
    }

    private void SaveColumnWidths()
    {
        _settings.ColWidthDrive = HeaderActualWidth(0);
        _settings.ColWidthName  = HeaderActualWidth(1);
        _settings.ColWidthSize  = HeaderActualWidth(3);
        _settings.ColWidthDate  = HeaderActualWidth(4);
        _settings.Save();
    }

    // 시작 시: 저장된 컬럼 너비를 Cols에 적용 (각 컬럼 최소 미만이면 기본값 유지 → 비정상값 방어)
    private void ApplySavedColumnWidths()
    {
        if (_settings.ColWidthDrive >= MinWidthOf("ColDrive")) Cols.SetDrive(_settings.ColWidthDrive);
        if (_settings.ColWidthName  >= MinWidthOf("ColName"))  Cols.SetName(_settings.ColWidthName);
        if (_settings.ColWidthSize  >= MinWidthOf("ColSize"))  Cols.SetSize(_settings.ColWidthSize);
        if (_settings.ColWidthDate  >= MinWidthOf("ColDate"))  Cols.SetDate(_settings.ColWidthDate);
    }

    // 메뉴: 컬럼 너비 초기화 → 디자인 기본값으로 되돌리고 저장
    private void ResetColumnWidths()
    {
        Cols.SetDrive(50);
        Cols.SetName(280);
        Cols.SetSize(80);
        Cols.SetDate(120);

        _settings.ColWidthDrive = 50;
        _settings.ColWidthName  = 280;
        _settings.ColWidthSize  = 80;
        _settings.ColWidthDate  = 120;
        _settings.Save();
    }

    // 헤더 그리드의 인덱스별 실제 렌더 폭 (0=DRV 1=이름 2=경로* 3=크기 4=수정날짜 5=여유)
    private double HeaderActualWidth(int index)
    {
        if (index < HeaderGrid.ColumnDefinitions.Count)
            return HeaderGrid.ColumnDefinitions[index].ActualWidth;
        return 0;
    }
    private double PathActualWidth() => HeaderActualWidth(2); // 경로(*)의 현재 렌더 폭

    // group → Cols의 현재 픽셀값 읽기 / 쓰기
    private double ColPx(string group) => group switch
    {
        "ColDrive" => Cols.DrivePx,
        "ColName"  => Cols.NamePx,
        "ColSize"  => Cols.SizePx,
        "ColDate"  => Cols.DatePx,
        _ => 0,
    };
    private void SetCol(string group, double px)
    {
        switch (group)
        {
            case "ColDrive": Cols.SetDrive(px); break;
            case "ColName":  Cols.SetName(px);  break;
            case "ColSize":  Cols.SetSize(px);  break;
            case "ColDate":  Cols.SetDate(px);  break;
        }
    }
    // group → 헤더 인덱스 (실제 렌더 폭 읽기용)
    private static int HeaderIndexOf(string group) => group switch
    {
        "ColDrive" => 0,
        "ColName"  => 1,
        "ColSize"  => 3,
        "ColDate"  => 4,
        _ => -1,
    };
    private double ColActual(string group)
    {
        int i = HeaderIndexOf(group);
        return i >= 0 ? HeaderActualWidth(i) : ColPx(group);
    }

    // 두 픽셀 컬럼이 경계를 나눠 갖는다: 왼쪽 +delta, 오른쪽 -delta (합 보존).
    // Cols 한 객체를 두 값 모두 갱신하므로 헤더·모든 행이 동시에 같은 너비가 된다.
    // 합이 보존되어 경로(*)가 흡수할 변화가 없으므로 수정날짜(마지막 칸)도 잘 줄어든다.
    private void AdjustTwoPixel(string leftGroup, string rightGroup, double delta)
    {
        double lw = ColActual(leftGroup), rw = ColActual(rightGroup);
        double lMin = MinWidthOf(leftGroup), rMin = MinWidthOf(rightGroup);
        double want = delta;

        if (lw + delta < lMin) delta = lMin - lw;
        if (rw - delta < rMin) delta = rw - rMin;

        const double eps = 0.5;
        if (want > 0 && delta <= eps) return;
        if (want < 0 && delta >= -eps) return;

        SetCol(leftGroup,  lw + delta);
        SetCol(rightGroup, rw - delta);
    }

    // 한쪽이 경로(*)인 경계: 픽셀 컬럼 하나(group)만 change만큼 바꾸고 경로가 흡수.
    //  change>0: 컬럼 커짐 → 경로 줄어듦(경로 최소 60에서 멈춤)
    //  change<0: 컬럼 작아짐 → 경로 늘어남(컬럼 최소에서 멈춤)
    // 경로는 * 그대로 두므로(Cols에 없음) 헤더·행 모두 자동으로 같은 폭이 된다.
    private void AdjustColumnAbsorbedByPath(string group, double change)
    {
        double cMin = MinWidthOf(group);
        double cw = ColActual(group);
        double pw = PathActualWidth();
        double want = change;

        if (cw + change < cMin) change = cMin - cw;
        if (change > 0 && pw - change < PathMinWidth) change = pw - PathMinWidth;

        const double eps = 0.5;
        if (want > 0 && change <= eps) return;
        if (want < 0 && change >= -eps) return;

        SetCol(group, cw + change);
        // 경로(*)는 자동으로 (pw - change)가 됨.
    }

    /// <summary>
    /// 두 번째 인스턴스가 실행됐을 때 외부(App)에서 호출.
    /// 숨겨져 있거나 최소화된 창을 다시 보이게 하고 활성화한다.
    /// </summary>
    public void BringToFront()
    {
        // UI 스레드에서 실행 보장
        Dispatcher.Invoke(() =>
        {
            if (_trayIcon is not null)
            {
                _trayIcon.ShowWindow();   // 트레이 헬퍼의 복원+활성화 로직 재사용
            }
            else
            {
                if (Visibility != Visibility.Visible) Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();
            }
        });
    }

    private List<SearchResultRow> SortRows(List<SearchResultRow> rows)
    {
        if (rows.Count == 0) return rows;

        // 숫자/날짜 컬럼은 별도 처리 (문자열 비교 X)
        if (_sortColumn == SortColumn.Size)
        {
            var sorted = _sortAscending
                ? rows.AsParallel().OrderBy(r => r.SizeBytes)
                : rows.AsParallel().OrderByDescending(r => r.SizeBytes);
            return sorted.ToList();
        }
        if (_sortColumn == SortColumn.Date)
        {
            var sorted = _sortAscending
                ? rows.AsParallel().OrderBy(r => r.ModifiedUtc)
                : rows.AsParallel().OrderByDescending(r => r.ModifiedUtc);
            return sorted.ToList();
        }

        // 문자열 컬럼
        Func<SearchResultRow, string> keySelector = _sortColumn switch
        {
            SortColumn.Drive => r => r.Drive,
            SortColumn.Name  => r => r.Name,
            SortColumn.Path  => r => r.Path,
            SortColumn.Kind  => r => r.Kind,
            _ => r => r.Name
        };

        var comparer = _sortColumn == SortColumn.Path
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

        if (rows.Count >= 50_000)
        {
            var ordered = _sortAscending
                ? rows.AsParallel().OrderBy(keySelector, comparer)
                : rows.AsParallel().OrderByDescending(keySelector, comparer);
            return ordered.ToList();
        }
        else
        {
            var copy = new List<SearchResultRow>(rows);
            Comparison<SearchResultRow> cmp = (a, b) =>
                comparer.Compare(keySelector(a), keySelector(b));

            if (_sortAscending) copy.Sort(cmp);
            else copy.Sort((a, b) => cmp(b, a));
            return copy;
        }
    }

    // ==================================================
    //  키보드 단축키 핸들러
    // ==================================================

    // Ctrl+L — 검색창에 포커스
    private void FocusSearchCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // Esc — 검색창 비우기 + 포커스
    private void ClearAndFocusCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // 1단계: 결과 리스트에 선택이 있으면 선택만 해제 (검색어는 유지)
        if (ResultsList.SelectedItems.Count > 0)
        {
            ResultsList.UnselectAll();
            return;
        }

        // 2단계: 선택이 없으면 검색어 지우고 검색창으로 포커스
        SearchBox.Clear();
        SearchBox.Focus();
    }

    // Enter (결과 리스트에서) — 열기
    private void OpenCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        OpenSelected();
    }

    // Alt+Enter — 속성
    private void PropertiesCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;
        try { ShowFileProperties(row.Path); }
        catch (Exception ex) { FooterText.Text = $"속성 보기 실패: {ex.Message}"; }
    }

    // Ctrl+C — 파일 복사 (파일 자체를 클립보드에)
    private void CopyFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        MenuCopyFile_Click(sender, e);
    }

    // Ctrl+Shift+C — 전체 경로 복사
    private void CopyPathCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;
        try
        {
            Clipboard.SetText(row.Path);
            FooterText.Text = $"경로 복사됨: {row.Path}";
        }
        catch { }
    }

    // 검색창에서 ↓ 누르면 결과 리스트로 포커스 이동
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Esc: 히스토리가 떠 있으면 닫기 (그 외엔 기존 동작에 맡김)
        if (e.Key == Key.Escape && HistoryPopup.IsOpen)
        {
            HistoryPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        // 히스토리 팝업이 열려 있을 때 ↓/↑ 로 항목 "강조"만 이동한다. (실행은 Enter에서)
        if (HistoryPopup.IsOpen && HistoryList.Items.Count > 0 &&
            (e.Key == Key.Down || e.Key == Key.Up))
        {
            if (e.Key == Key.Down)
            {
                // 처음 ↓ → 맨 위 항목(0번) 강조, 이후 ↓ → 한 칸씩 아래로
                int next = HistoryList.SelectedIndex < 0
                    ? 0
                    : Math.Min(HistoryList.SelectedIndex + 1, HistoryList.Items.Count - 1);
                _historyKeyboardNav = true;
                HistoryList.SelectedIndex = next;
                if (HistoryList.SelectedItem is not null)
                    HistoryList.ScrollIntoView(HistoryList.SelectedItem);
            }
            else // Key.Up
            {
                if (HistoryList.SelectedIndex <= 0)
                {
                    // 맨 위에서 ↑ → 강조 해제 (다시 검색창 입력 상태)
                    _historyKeyboardNav = true;
                    HistoryList.SelectedIndex = -1;
                }
                else
                {
                    _historyKeyboardNav = true;
                    HistoryList.SelectedIndex--;
                    if (HistoryList.SelectedItem is not null)
                        HistoryList.ScrollIntoView(HistoryList.SelectedItem);
                }
            }
            e.Handled = true;
            return;
        }

        // Enter: 검색 실행 (히스토리 기록은 RunSearch가 담당)
        if (e.Key == Key.Enter)
        {
            // 히스토리에서 강조된 항목이 있으면 그 검색어를 검색창에 넣고 실행한다.
            if (HistoryPopup.IsOpen && HistoryList.SelectedItem is string highlighted)
            {
                SearchBox.Text = highlighted;
                SearchBox.CaretIndex = SearchBox.Text.Length;
            }
            HistoryPopup.IsOpen = false;
            RunSearch();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Down) return;
        if (ResultsList.Items.Count == 0) return;

        e.Handled = true;

        if (ResultsList.SelectedIndex < 0)
            ResultsList.SelectedIndex = 0;

        ResultsList.ScrollIntoView(ResultsList.SelectedItem);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            ResultsList.UpdateLayout();
            if (ResultsList.ItemContainerGenerator.ContainerFromIndex(ResultsList.SelectedIndex)
                is ListViewItem item)
            {
                item.Focus();
                Keyboard.Focus(item);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    // 결과 리스트에서 KeyDown — Ctrl+Shift+N(이름 복사), 첫 행에서 ↑(검색창 복귀)
    // 빈 공간(항목 없는 영역) 클릭 시 선택 해제 (탐색기와 동일한 동작)
    private void ResultsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsClickOnEmptySpace(e))
        {
            ResultsList.UnselectAll();
        }
    }

    // 빈 공간에서 컨텍스트 메뉴 열리는 것을 막는다 (탐색기와 동일한 동작)
    private void ResultsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 마우스 위치에서 ListViewItem 찾기 (좌표 기반)
        var pos = Mouse.GetPosition(ResultsList);
        var hit = ResultsList.InputHitTest(pos) as DependencyObject;
        while (hit != null && hit is not ListViewItem)
        {
            if (hit == ResultsList) break;
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        }
        if (hit is not ListViewItem)
        {
            // 빈 공간 → 메뉴 열기 차단
            e.Handled = true;
        }
    }

    // 마우스 이벤트의 클릭 위치가 항목(ListViewItem)이 아닌 빈 공간인지 판정
    private bool IsClickOnEmptySpace(MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListViewItem)
        {
            if (dep == ResultsList) return true; // ListView 자체까지 도달 = 빈 공간
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
        return dep is not ListViewItem;
    }

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        // Del — 휴지통으로 삭제
        if (e.Key == Key.Delete
            && (Keyboard.Modifiers & ModifierKeys.Control) == 0
            && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            if (ResultsList.SelectedItems.Count > 0)
            {
                MenuDelete_Click(sender, e);
                e.Handled = true;
            }
            return;
        }

        // F2 — 이름 바꾸기 (첫 항목)
        if (e.Key == Key.F2)
        {
            if (ResultsList.SelectedItems.Count > 0)
            {
                MenuRename_Click(sender, e);
                e.Handled = true;
            }
            return;
        }

        // Ctrl+Shift+N — 파일 이름 복사
        if (e.Key == Key.N
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            MenuCopyName_Click(sender, e);
            e.Handled = true;
            return;
        }

        // ↑/↓ — 선택 항목을 직접 한 칸씩 이동 (가상화 재활용으로 인한 '맨 위로 점프' 방지)
        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            // 다중 선택 보조키(Shift/Ctrl)가 눌린 경우는 기본 동작에 맡긴다
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0)
                return;

            int count = ResultsList.Items.Count;
            if (count == 0) return;

            int cur = ResultsList.SelectedIndex;

            // 첫 행에서 ↑ → 검색창으로 복귀
            if (e.Key == Key.Up && cur == 0)
            {
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
                e.Handled = true;
                return;
            }

            int next;
            if (cur < 0)
            {
                // 선택이 없으면 첫 행(↓) 또는 마지막 행(↑)부터
                next = (e.Key == Key.Down) ? 0 : count - 1;
            }
            else
            {
                next = (e.Key == Key.Down) ? cur + 1 : cur - 1;
                if (next < 0) next = 0;
                if (next >= count) next = count - 1;
            }

            if (next != cur)
            {
                ResultsList.SelectedIndex = next;
                var item = ResultsList.SelectedItem;
                if (item != null)
                {
                    ResultsList.ScrollIntoView(item);
                    // 컨테이너에 키보드 포커스를 줘서 다음 키 입력도 정확히 이어지게
                    if (ResultsList.ItemContainerGenerator.ContainerFromIndex(next)
                        is System.Windows.Controls.ListViewItem lvi)
                    {
                        lvi.Focus();
                    }
                }
            }
            e.Handled = true;
            return;
        }
    }

    // ==================================================
    //  파일/폴더 동작
    // ==================================================
    private SearchResultRow? GetSelectedRow()
    {
        return ResultsList.SelectedItem as SearchResultRow;
    }

    /// <summary>현재 선택된 모든 행을 표시 순서대로 반환.</summary>
    private List<SearchResultRow> GetSelectedRows()
    {
        var rows = new List<SearchResultRow>(ResultsList.SelectedItems.Count);
        foreach (var item in ResultsList.SelectedItems)
        {
            if (item is SearchResultRow r) rows.Add(r);
        }
        return rows;
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelected();
    }

    private void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;

        // 단일 선택은 OpenSelected (히스토리 기록 포함)로 위임
        if (rows.Count == 1)
        {
            OpenSelected();
            return;
        }

        // 다중 선택: 전부 열기
        int ok = 0, fail = 0;
        foreach (var row in rows)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = row.Path,
                    UseShellExecute = true
                });
                ok++;
            }
            catch
            {
                fail++;
            }
        }

        // 검색 히스토리에 기록 (하나라도 성공했을 때)
        if (ok > 0 && !string.IsNullOrWhiteSpace(SearchBox.Text))
            AddToHistory(SearchBox.Text);

        FooterText.Text = fail == 0
            ? string.Format(Loc.T("ctx.open.multi"), ok)
            : string.Format(Loc.T("ctx.open.partial"), ok, fail);
    }

    // 실행 가능한 확장자 (관리자 권한 실행 가능 여부 판단용)
    private static readonly HashSet<string> _executableExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".com", ".ps1"
    };

    private static bool IsExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (System.IO.Directory.Exists(path)) return false;
        var ext = System.IO.Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && _executableExts.Contains(ext);
    }

    // 우클릭 메뉴가 열릴 때마다 "관리자 권한으로 실행"의 활성/비활성 토글
    private void ResultsContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = true;
        var row = GetSelectedRow();
        CtxRunAsAdmin.IsEnabled = row is not null && IsExecutablePath(row.Path);
    }

    private void ResultsContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = false;
        // 메뉴 열린 동안 인덱스 변경 알림이 보류되었을 수 있으므로 디바운스 재가동
        if (!string.IsNullOrEmpty(_lastSearchQuery))
            _indexChangedDebounce.Start();
    }

    // 관리자 권한으로 실행
    private void MenuRunAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        if (!IsExecutablePath(row.Path))
        {
            FooterText.Text = $"{Loc.T("ctx.error")}: {Loc.T("ctx.notExecutable")}";
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = row.Path,
                UseShellExecute = true,
                Verb = "runas"   // UAC 권한 상승 요청 (이미 관리자면 그대로 시작)
            };
            Process.Start(psi);
            FooterText.Text = $"{Loc.T("ctx.runAsAdmin")}: {row.Name}";

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                AddToHistory(SearchBox.Text);
        }
        catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            // 1223 = ERROR_CANCELLED — 사용자가 UAC 대화상자에서 취소
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc.T("ctx.error")}: {ex.Message}",
                Loc.T("ctx.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 다른 프로그램으로 열기 (Shell API로 직접 호출)
    private void MenuOpenWith_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        if (!System.IO.File.Exists(row.Path))
        {
            FooterText.Text = $"{Loc.T("ctx.error")}: {row.Path}";
            return;
        }

        try
        {
            // 우리 윈도우 핸들을 부모로 넘겨서 모달 대화상자로 표시
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var info = new OPENASINFO
            {
                pcszFile  = row.Path,
                pcszClass = null,
                oaifInFlags = OAIF.OAIF_EXEC | OAIF.OAIF_ALLOW_REGISTRATION
            };
            int hr = SHOpenWithDialog(helper.Handle, ref info);
            // hr가 0이면 성공, 사용자가 취소했어도 보통 성공으로 돌아옴
            if (hr != 0)
            {
                FooterText.Text = $"{Loc.T("ctx.error")}: HRESULT=0x{hr:X8}";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                    AddToHistory(SearchBox.Text);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc.T("ctx.error")}: {ex.Message}",
                Loc.T("ctx.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // SHOpenWithDialog P/Invoke
    [Flags]
    private enum OAIF : uint
    {
        OAIF_ALLOW_REGISTRATION = 0x00000001,
        OAIF_REGISTER_EXT       = 0x00000002,
        OAIF_EXEC               = 0x00000004,
        OAIF_FORCE_REGISTRATION = 0x00000008,
        OAIF_HIDE_REGISTRATION  = 0x00000020,
        OAIF_URL_PROTOCOL       = 0x00000040,
        OAIF_FILE_IS_URI        = 0x00000080
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENASINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string pcszFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pcszClass;
        public OAIF oaifInFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO oainfo);

    private void OpenSelected()
    {
        var row = GetSelectedRow();
        if (row is null) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = row.Path,
                UseShellExecute = true   // 연결된 프로그램으로 (폴더면 탐색기로)
            };
            Process.Start(psi);

            // 결과를 실제로 열었다 → 그 검색은 쓸모 있었으므로 히스토리에 기록
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                AddToHistory(SearchBox.Text);
        }
        catch (Exception ex)
        {
            FooterText.Text = $"열기 실패: {ex.Message}";
        }
    }

    private void MenuRevealInExplorer_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        try
        {
            RevealInExplorer(row.Path);
        }
        catch (Exception ex)
        {
            FooterText.Text = $"폴더에서 보기 실패: {ex.Message}";
        }
    }

    // SHOpenFolderAndSelectItems API
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHOpenFolderAndSelectItems(
        IntPtr pidlFolder,
        uint cidl,
        [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[]? apidl,
        uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr ILCreateFromPath([MarshalAs(UnmanagedType.LPWStr)] string pszPath);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    // 셸 API 호출 스레드에서 COM을 명시적으로 초기화/해제하기 위한 API
    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private static void RevealInExplorer(string path)
    {
        // 드라이브 루트 등 부모가 없으면 선택 대상이 없으므로 그냥 연다.
        bool isDirectory = Directory.Exists(path);
        if (isDirectory && string.IsNullOrEmpty(Path.GetDirectoryName(path)))
        {
            OpenInExplorer(path);
            return;
        }

        // explorer.exe /select 로 연다.
        //  - 셸(explorer)이 정상 무결성 수준의 일반 창을 띄우므로, 관리자 권한으로
        //    실행되는 이 앱에서 호출해도 제목표시줄 버튼(최소화/최대화/닫기)이 없는
        //    비정상 창이 생기지 않는다. (SHOpenFolderAndSelectItems는 앱의 관리자 COM
        //    컨텍스트에서 창을 만들어 그 비정상 창이 가끔 생겼다.)
        //  - /select 인자가 해당 파일/폴더를 폴더 안에서 선택·강조하며, 셸이 창을
        //    다 띄운 뒤 선택을 적용하므로 강조가 안정적으로 유지된다.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false
            });
        }
        catch
        {
            // 실패 시 부모 폴더만이라도 연다 (선택 강조는 없어도 위치는 보여줌)
            var dir = isDirectory ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                OpenInExplorer(dir);
        }
    }

    private static void OpenInExplorer(string folderPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
        catch { /* 실패해도 앱은 계속 */ }
    }

    private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;
        try
        {
            string text = string.Join(Environment.NewLine, rows.Select(r => r.Path));
            Clipboard.SetText(text);
            FooterText.Text = rows.Count == 1
                ? $"경로 복사됨: {rows[0].Path}"
                : string.Format(Loc.T("ctx.copyPath.multi"), rows.Count);
        }
        catch { /* 클립보드는 가끔 다른 앱이 잠가서 실패할 수 있음 */ }
    }

    private void MenuCopyName_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;
        try
        {
            string text = string.Join(Environment.NewLine, rows.Select(r => r.Name));
            Clipboard.SetText(text);
            FooterText.Text = rows.Count == 1
                ? $"이름 복사됨: {rows[0].Name}"
                : string.Format(Loc.T("ctx.copyName.multi"), rows.Count);
        }
        catch { }
    }

    private void MenuProperties_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;
        try
        {
            ShowFileProperties(row.Path);
        }
        catch (Exception ex)
        {
            FooterText.Text = $"속성 보기 실패: {ex.Message}";
        }
    }

    // ===== 파일 관리: 이름 바꾸기 / 파일 복사 / 휴지통 삭제 =====

    private void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;

        try
        {
            if (!System.IO.File.Exists(row.Path) && !System.IO.Directory.Exists(row.Path))
            {
                FooterText.Text = $"{Loc.T("ctx.error")}: {row.Path}";
                return;
            }

            string oldName = System.IO.Path.GetFileName(row.Path);
            string? dir = System.IO.Path.GetDirectoryName(row.Path);
            if (dir is null) return;

            // 파일은 확장자 보호 (탐색기처럼 이름만 선택), 폴더는 전체 선택
            bool isDir = System.IO.Directory.Exists(row.Path);
            string? newName = PromptForText(
                Loc.T("ctx.rename.title"),
                Loc.T("ctx.rename.prompt"),
                oldName,
                selectExtension: isDir);
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

            // 파일인 경우 확장자 변경 여부 확인
            if (!isDir)
            {
                string oldExt = System.IO.Path.GetExtension(oldName);
                string newExt = System.IO.Path.GetExtension(newName);
                if (!string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
                {
                    var confirm = MessageBox.Show(
                        Loc.T("ctx.rename.extWarn"),
                        Loc.T("ctx.rename.title"),
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.OK) return;
                }
            }

            string newPath = System.IO.Path.Combine(dir, newName);

            if (isDir)
                System.IO.Directory.Move(row.Path, newPath);
            else
                System.IO.File.Move(row.Path, newPath);

            FooterText.Text = $"{oldName} → {newName}";
            // 인덱스는 USN 모니터가 자동 반영. 현재 검색은 그대로 두되 결과 갱신.
            if (!string.IsNullOrEmpty(_lastSearchQuery) && SearchBox.Text == _lastSearchQuery)
                RunSearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc.T("ctx.error")}: {ex.Message}",
                Loc.T("ctx.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MenuCopyFile_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;

        try
        {
            var paths = new System.Collections.Specialized.StringCollection();
            int skipped = 0;
            foreach (var row in rows)
            {
                if (System.IO.File.Exists(row.Path) || System.IO.Directory.Exists(row.Path))
                    paths.Add(row.Path);
                else
                    skipped++;
            }

            if (paths.Count == 0)
            {
                FooterText.Text = $"{Loc.T("ctx.error")}: {Loc.T("ctx.copyFile.none")}";
                return;
            }

            // 파일 자체를 클립보드에 (탐색기에 붙여넣기 가능)
            Clipboard.SetFileDropList(paths);

            FooterText.Text = paths.Count == 1
                ? $"{Loc.T("ctx.copyFile")}: {rows[0].Name}"
                : string.Format(Loc.T("ctx.copyFile.multi"), paths.Count);
        }
        catch (Exception ex)
        {
            FooterText.Text = $"{Loc.T("ctx.error")}: {ex.Message}";
        }
    }

    private void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;

        string confirmMsg;
        if (rows.Count == 1)
        {
            confirmMsg = $"{Loc.T("ctx.delete.confirm")}\n\n{rows[0].Path}";
        }
        else
        {
            // 무엇을 지우는지 분명히: 대상 파일명을 최대 10개까지 나열하고, 더 많으면 "…외 N개"
            const int previewMax = 10;
            var names = rows.Select(r => System.IO.Path.GetFileName(r.Path.TrimEnd('\\')))
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Take(previewMax)
                            .ToList();
            string list = string.Join("\n", names);
            if (rows.Count > previewMax)
                list += "\n" + string.Format(Loc.T("ctx.delete.more"), rows.Count - previewMax);

            confirmMsg = string.Format(Loc.T("ctx.delete.confirm.multi"), rows.Count)
                         + "\n\n" + list;
        }

        // 위험(시스템 최상위) 경로가 섞여 있으면 강한 경고를 앞에 덧붙인다. (막지는 않음)
        var dangerous = rows.Select(r => r.Path)
                            .Where(IsDangerousPath)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
        var msgIcon = MessageBoxImage.Warning;
        if (dangerous.Count > 0)
        {
            const int dangerMax = 5;
            string dangerList = string.Join("\n", dangerous.Take(dangerMax));
            if (dangerous.Count > dangerMax)
                dangerList += "\n" + string.Format(Loc.T("ctx.delete.more"), dangerous.Count - dangerMax);

            confirmMsg = Loc.T("ctx.delete.danger") + "\n\n" + dangerList + "\n\n" + confirmMsg;
        }

        var confirm = MessageBox.Show(
            confirmMsg,
            Loc.T("ctx.delete.title"),
            MessageBoxButton.OKCancel,
            msgIcon);

        if (confirm != MessageBoxResult.OK) return;

        int ok = 0, fail = 0;
        string? lastError = null;

        foreach (var row in rows)
        {
            try
            {
                if (!System.IO.File.Exists(row.Path) && !System.IO.Directory.Exists(row.Path))
                {
                    fail++;
                    lastError = $"{Loc.T("ctx.error")}: {row.Path}";
                    continue;
                }

                if (System.IO.Directory.Exists(row.Path))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        row.Path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        row.Path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                lastError = ex.Message;
            }
        }

        // 결과 푸터 표시
        if (fail == 0)
        {
            FooterText.Text = rows.Count == 1
                ? $"{Loc.T("ctx.delete.title")}: {rows[0].Name}"
                : string.Format(Loc.T("ctx.delete.done.multi"), ok);
        }
        else
        {
            FooterText.Text = string.Format(Loc.T("ctx.delete.partial"), ok, fail);
            if (lastError != null)
                MessageBox.Show(lastError, Loc.T("ctx.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // 방금 휴지통으로 보낸 행은 USN 모니터의 비동기 인덱스 갱신을 기다리지 않고
        // 화면 목록에서 즉시 제거한다. (삭제 직후엔 인덱스가 아직 갱신 전이라,
        // 곧바로 RunSearch하면 옛 항목이 다시 나타나는 경합이 있었다.)
        if (ResultsList.ItemsSource is List<SearchResultRow> shown)
        {
            // 인스턴스(참조)가 아니라 경로로 비교한다.
            // 확인 대화상자 도중 자동 재검색이 결과 목록을 새 인스턴스로 교체했을 수 있어,
            // 참조 비교로는 제거에 실패해(우클릭 경로) 항목이 늦게 사라지는 문제가 있었다.
            var deletedPaths = new HashSet<string>(
                rows.Where(r => !System.IO.File.Exists(r.Path)
                             && !System.IO.Directory.Exists(r.Path))
                    .Select(r => r.Path),
                StringComparer.OrdinalIgnoreCase);
            if (deletedPaths.Count > 0)
            {
                foreach (var p in deletedPaths) _recentlyDeletedPaths.Add(p);
                var remaining = shown.Where(r => !deletedPaths.Contains(r.Path)).ToList();
                ResultsList.ItemsSource = null;
                ResultsList.ItemsSource = remaining;
            }
        }
    }

    // 시스템에 치명적인 "최상위" 경로인지 판정 (그 안의 개별 파일·폴더는 위험으로 보지 않음).
    private static bool IsDangerousPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // 경로 정규화: 뒤쪽 슬래시 제거, 대소문자 무시 비교용
        string p = path.TrimEnd('\\', '/');

        // 드라이브 루트 자체 (예: "C:" 또는 "C:\")
        if (p.Length <= 2 && p.Length >= 1 && p.EndsWith(":")) return true;
        if (p.Length == 2 && char.IsLetter(p[0]) && p[1] == ':') return true;

        string win  = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
        string pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\');
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\');
        string users = Path.Combine(Path.GetPathRoot(win) ?? "C:\\", "Users").TrimEnd('\\');
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');

        // Windows / Program Files: 폴더 자체와 그 하위 전체를 위험으로 본다
        if (StartsWithDir(p, win) || StartsWithDir(p, pf) || StartsWithDir(p, pf86)) return true;

        // Users 폴더 자체, 각 사용자 홈 폴더 자체는 위험. 단 그 "안"의 항목은 허용.
        if (p.Equals(users, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Equals(userHome, StringComparison.OrdinalIgnoreCase)) return true;

        return false;

        // p가 baseDir이거나 그 하위인지
        static bool StartsWithDir(string p, string baseDir)
        {
            if (string.IsNullOrEmpty(baseDir)) return false;
            if (p.Equals(baseDir, StringComparison.OrdinalIgnoreCase)) return true;
            return p.StartsWith(baseDir + "\\", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>간단한 한 줄 입력 다이얼로그 (WinForms 기반).</summary>
    /// <summary>
    /// 간단한 한 줄 입력 다이얼로그.
    /// selectExtension=false면 확장자 부분은 선택에서 제외 (탐색기 이름 바꾸기와 동일).
    /// </summary>
    private static string? PromptForText(string title, string prompt, string defaultValue, bool selectExtension = true)
    {
        var form = new System.Windows.Forms.Form
        {
            Text = title,
            Width = 420,
            Height = 170,
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var label = new System.Windows.Forms.Label
        {
            Text = prompt,
            Left = 12,
            Top = 14,
            Width = 380
        };
        var textBox = new System.Windows.Forms.TextBox
        {
            Text = defaultValue,
            Left = 12,
            Top = 38,
            Width = 380
        };
        var ok = new System.Windows.Forms.Button
        {
            Text = "OK",
            DialogResult = System.Windows.Forms.DialogResult.OK,
            Left = 226,
            Top = 78,
            Width = 80
        };
        var cancel = new System.Windows.Forms.Button
        {
            Text = "Cancel",
            DialogResult = System.Windows.Forms.DialogResult.Cancel,
            Left = 312,
            Top = 78,
            Width = 80
        };

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (selectExtension)
        {
            textBox.SelectAll();
        }
        else
        {
            // 탐색기와 동일: 이름 부분만 선택, 확장자(마지막 점 이후)는 선택 제외
            int dot = defaultValue.LastIndexOf('.');
            if (dot > 0)
            {
                textBox.Select(0, dot);
            }
            else
            {
                textBox.SelectAll();
            }
        }
        textBox.Focus();

        return form.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? textBox.Text.Trim()
            : null;
    }

    // Windows 표준 "속성" 대화상자를 띄우려면 ShellExecuteEx에 "properties" verb를 줘야 함
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    private static void ShowFileProperties(string path)
    {
        var info = new SHELLEXECUTEINFO();
        info.cbSize = Marshal.SizeOf(info);
        info.lpVerb = "properties";
        info.lpFile = path;
        info.nShow = 1;       // SW_SHOWNORMAL
        info.fMask = SEE_MASK_INVOKEIDLIST;
        ShellExecuteEx(ref info);
    }

    // ==================================================
    //  종료 정리
    // ==================================================
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _searchCts?.Cancel();
        foreach (var p in _preloaders) p.Stop();
        _globalHotkey?.Dispose();
        _trayIcon?.Dispose();

        foreach (var slot in _multi.Slots)
        {
            if (slot.Index is null) continue;
            if (!_cacheMeta.TryGetValue(slot.DriveLetter, out var meta)) continue;

            // 저널 ID가 없으면(USB의 일부) 저장해도 catch-up 불가 → 스킵
            if (meta.JournalId == 0) continue;

            // Removable 드라이브는 저장 안 함 (다른 PC/USB에 꽂으면 무효)
            try
            {
                var driveInfo = new System.IO.DriveInfo(slot.DriveLetter + "\\");
                if (driveInfo.DriveType == System.IO.DriveType.Removable) continue;
            }
            catch { continue; }

            try
            {
                long lastUsn = slot.Monitor?.CurrentUsn ?? 0;
                IndexStore.Save(new IndexStore.CacheData
                {
                    DriveLetter = slot.DriveLetter,
                    VolumeSerial = meta.VolumeSerial,
                    JournalId = meta.JournalId,
                    LastUsn = lastUsn,
                    Index = slot.Index
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{slot.DriveLetter}] Save failed: {ex.Message}");
            }
        }

        _watcher.Dispose();
        _multi.DisposeAll();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_settings.MinimizeToTrayOnClose && !_reallyClose && _trayIcon is not null)
        {
            e.Cancel = true;
            _trayIcon.HideWindow();
        }
    }

    // ==================================================
    //  타이틀바
    // ==================================================
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

        if (WindowState == WindowState.Maximized)
        {
            // WindowChrome 커스텀 창은 최대화 시 작업표시줄을 침범하고
            // 화면 밖으로 넘쳐 하단 상태바가 잘린다.
            // 작업 영역(WorkArea, 작업표시줄 제외)에 맞춰 크기와 위치를 강제한다.
            var wa = SystemParameters.WorkArea;
            MaxHeight = wa.Height + 2;   // +2: 테두리 보정
            MaxWidth  = wa.Width + 2;
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth  = double.PositiveInfinity;
        }

        // 최대화↔복귀로 창 크기가 바뀌면 경로(*) 컬럼이 너비를 재계산하는데,
        // 가상화 ListView에서는 화면 밖 행이 재활용되며 크기·수정날짜 셀이 빈 채로
        // 남는다. UpdateLayout만으로는 가상화된 항목이 갱신되지 않으므로,
        // ItemsPresenter/내부 ItemsHost를 찾아 강제로 다시 측정·정렬시킨다.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshResultListLayout();
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    // 가상화 ListView의 모든 (실재) 컨테이너와 내부 패널을 강제로 다시 레이아웃한다.
    private void RefreshResultListLayout()
    {
        // 가상화 ListView는 창 크기 변경 후 화면 밖 행이 재활용되며 크기·수정날짜 셀이
        // 빈 채로 남는다(데이터가 많을 때). 가장 확실한 해결은 ItemsSource를 잠깐
        // 떼었다 같은 리스트로 다시 붙여 모든 컨테이너를 새로 생성하는 것이다.
        var src = ResultsList.ItemsSource;
        if (src is null) return;

        double offset = 0;
        var sv = FindVisualChild<System.Windows.Controls.ScrollViewer>(ResultsList);
        if (sv is not null) offset = sv.VerticalOffset;  // 스크롤 위치 보존

        ResultsList.ItemsSource = null;
        ResultsList.ItemsSource = src;
        ResultsList.UpdateLayout();

        if (sv is not null) sv.ScrollToVerticalOffset(offset);  // 스크롤 위치 복원
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var deeper = FindVisualChild<T>(child);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    private void MenuDropdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        var menu = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        // 설정
        var settingsItem = new MenuItem
        {
            Header = Loc.T("menu.settings"),
            InputGestureText = "Ctrl+,"
        };
        settingsItem.Click += (_, _) => SettingsButton_Click(sender, e);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        // 인덱스 새로 고침
        var rebuildItem = new MenuItem
        {
            Header = Loc.T("menu.refresh"),
            InputGestureText = "F5"
        };
        rebuildItem.Click += async (_, _) => await RebuildIndex();
        menu.Items.Add(rebuildItem);

        // 컬럼 너비 초기화
        var resetColsItem = new MenuItem { Header = Loc.T("menu.resetCols") };
        resetColsItem.Click += (_, _) => ResetColumnWidths();
        menu.Items.Add(resetColsItem);

        menu.Items.Add(new Separator());

        // 배율 (서브메뉴)
        var zoomItem = new MenuItem { Header = Loc.T("menu.zoom") };

        var zoomInItem = new MenuItem { Header = Loc.T("menu.zoom.in"), InputGestureText = "Ctrl++" };
        zoomInItem.Click += (_, _) => ZoomIn();
        zoomItem.Items.Add(zoomInItem);

        var zoomOutItem = new MenuItem { Header = Loc.T("menu.zoom.out"), InputGestureText = "Ctrl+-" };
        zoomOutItem.Click += (_, _) => ZoomOut();
        zoomItem.Items.Add(zoomOutItem);

        zoomItem.Items.Add(new Separator());

        var zoomResetItem = new MenuItem { Header = Loc.T("menu.zoom.reset"), InputGestureText = "Ctrl+0" };
        zoomResetItem.Click += (_, _) => ZoomReset();
        zoomItem.Items.Add(zoomResetItem);

        menu.Items.Add(zoomItem);

        menu.Items.Add(new Separator());

        // 필터링 (서브메뉴)
        var filterItem = new MenuItem { Header = Loc.T("menu.filter") };

        void AddFilter(string key, string locKey)
        {
            var mi = new MenuItem { Header = Loc.T(locKey) };
            mi.Click += (_, _) => ApplyFilter(key);
            filterItem.Items.Add(mi);
        }

        AddFilter("doc",   "filter.doc");
        AddFilter("img",   "filter.img");
        AddFilter("media", "filter.media");
        AddFilter("exe",   "filter.exe");
        AddFilter("zip",   "filter.zip");

        menu.Items.Add(filterItem);

        menu.Items.Add(new Separator());

        // 단축키
        var shortcutItem = new MenuItem { Header = Loc.T("menu.shortcuts") };
        shortcutItem.Click += (_, _) => ShowShortcuts();
        menu.Items.Add(shortcutItem);

        menu.Items.Add(new Separator());

        // 한영 전환을 위한 서브메뉴
        var langItem = new MenuItem { Header = Loc.T("menu.language") };

        bool isKo = Loc.Current == Loc.Lang.Korean;

        var koItem = new MenuItem
        {
            Header = (isKo ? "●  " : "      ") + "한국어"
        };
        koItem.Click += (_, _) => ChangeLanguage(Loc.Lang.Korean);
        langItem.Items.Add(koItem);

        var enItem = new MenuItem
        {
            Header = (!isKo ? "●  " : "      ") + "English"
        };
        enItem.Click += (_, _) => ChangeLanguage(Loc.Lang.English);
        langItem.Items.Add(enItem);

        menu.Items.Add(langItem);

        menu.Items.Add(new Separator());

        // 정보
        var aboutItem = new MenuItem { Header = Loc.T("menu.about") };
        aboutItem.Click += (_, _) => ShowAbout();
        menu.Items.Add(aboutItem);

        menu.Items.Add(new Separator());

        // 종료 (앱 완전 종료 — 트레이로 숨기지 않음)
        var exitItem = new MenuItem { Header = Loc.T("tray.exit") };
        exitItem.Click += (_, _) =>
        {
            _reallyClose = true;
            Close();
        };
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    private void ChangeLanguage(Loc.Lang lang)
    {
        if (Loc.Current == lang) return;

        Loc.Current = lang;
        _settings.Language = (lang == Loc.Lang.English) ? "en" : "ko";
        _settings.Save();

        // 즉시 반영되는 부분 갱신 (메뉴는 다음에 열 때 자동 반영됨)
        ApplyLocalizedTexts();
    }

    private void ApplyLocalizedTexts()
    {
        // 컬럼 헤더
        HdrDrive.Content = Loc.T("col.drive");
        HdrName.Content  = Loc.T("col.name");
        HdrPath.Content  = Loc.T("col.path");
        HdrSize.Content  = Loc.T("col.size");
        HdrDate.Content  = Loc.T("col.date");

        // 타이틀바 버튼 툴팁
        MenuDropdownButton.ToolTip = Loc.T("tip.menu");
        MinimizeButton.ToolTip     = Loc.T("tip.minimize");
        MaximizeButton.ToolTip     = Loc.T("tip.maximize");
        CloseButton.ToolTip        = Loc.T("tip.close");
        SearchButton.ToolTip       = Loc.T("tip.search");
        // 히스토리 삭제 버튼(DataTemplate 안)은 동적 리소스로 갱신
        Resources["TipDelete"]     = Loc.T("tip.delete");

        // 검색 결과 우클릭 메뉴
        CtxOpen.Header       = Loc.T("ctx.open");
        CtxRunAsAdmin.Header = Loc.T("ctx.runAsAdmin");
        CtxOpenWith.Header   = Loc.T("ctx.openWith");
        CtxReveal.Header     = Loc.T("ctx.reveal");
        CtxCopyPath.Header   = Loc.T("ctx.copyPath");
        CtxCopyName.Header   = Loc.T("ctx.copyName");
        CtxRename.Header     = Loc.T("ctx.rename");
        CtxCopyFile.Header   = Loc.T("ctx.copyFile");
        CtxDelete.Header     = Loc.T("ctx.delete");
        CtxProperties.Header = Loc.T("ctx.properties");

        // 푸터 요약 갱신 (총 N개 등)
        UpdateFooterSummary();
    }

    private async Task RebuildIndex()
    {
        var result = MessageBox.Show(
            Loc.T("refresh.confirm.msg"),
            Loc.T("refresh.confirm.title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.OK) return;

        try
        {
            // ── UI 즉시 반응 ──
            _searchCts?.Cancel();
            SearchBox.Clear();
            ResultsList.ItemsSource = null;
            SearchBox.IsEnabled = false;
            StatusBanner.Visibility = Visibility.Visible;
            StatusText.Text = Loc.T("status.indexing");

            // 화면 갱신 보장 (다음 프레임으로 양보)
            await Task.Yield();

            // ── 정리 작업은 백그라운드에서 ──
            await Task.Run(() =>
            {
                // 메타 사전로딩 중지
                foreach (var p in _preloaders) p.Stop();

                // 모니터 + 인덱스 정리
                _multi.DisposeAll();

                // 캐시 파일 삭제
                var cacheDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HdrTracer", "indexes");
                if (System.IO.Directory.Exists(cacheDir))
                {
                    try
                    {
                        foreach (var f in System.IO.Directory.GetFiles(cacheDir, "*.dat"))
                            System.IO.File.Delete(f);
                    }
                    catch { }
                }
            });

            _preloaders.Clear();
            _cacheMeta.Clear();

            // ── 다시 처음부터 빌드 ──
            var drives = DriveDetector.GetIndexableDrives(_settings.IndexRemovableDrives);
            foreach (var letter in drives)
            {
                _multi.AddSlot(new MultiDriveIndex.DriveSlot { DriveLetter = letter });
            }
            UpdateFooterSummary();

            var sw = Stopwatch.StartNew();
            var tasks = _multi.Slots.Select(slot => Task.Run(() => BuildOneDrive(slot))).ToArray();
            await Task.WhenAll(tasks);
            sw.Stop();

            // ── 모니터 + 사전로딩 다시 시작 ──
            foreach (var slot in _multi.Slots)
            {
                StartMonitorIfReady(slot);
                StartMetadataPreloader(slot);
            }

            // 다시 빌드 직후 2초간은 USN/메타 변경에 의한 자동 재검색 억제
            _ignoreIndexChangesUntil = DateTime.UtcNow.AddSeconds(2);

            StatusBanner.Visibility = Visibility.Collapsed;
            SearchBox.IsEnabled = true;
            SearchBox.Focus();
            UpdateFooterSummary();

            FooterText.Text = $"{Loc.T("status.refreshDone")} ({sw.ElapsedMilliseconds}ms)";
        }
        catch (Exception ex)
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            SearchBox.IsEnabled = true;
            MessageBox.Show($"{Loc.T("refresh.fail")}: {ex.Message}", Loc.T("common.error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowShortcuts()
    {
        var dlg = new ShortcutsWindow { Owner = this };
        dlg.ShowDialog();
    }

    private void ShowAbout()
    {
        var dlg = new AboutWindow() { Owner = this };
        dlg.ShowDialog();
    }

    // ==================================================
    //  설정 창
    // ==================================================
    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        var result = dlg.ShowDialog();
        if (result == true)
        {
            // 숨김+시스템 표시 토글 반영 (엔진에 적용 후 재검색)
            if (dlg.HiddenSystemChanged)
            {
                _engine.HideHiddenSystemItems = !_settings.ShowHiddenSystemItems;
                if (!string.IsNullOrEmpty(_lastSearchQuery))
                {
                    if (SearchBox.Text != _lastSearchQuery)
                        SearchBox.Text = _lastSearchQuery;
                    RunSearch();
                }
            }

            if (dlg.RemovableChanged)
            {
                await ApplyRemovableSettingChange();
            }
        }
    }

    private async Task ApplyRemovableSettingChange()
    {
        if (_settings.IndexRemovableDrives)
        {
            // 켜졌음 → 현재 꽂힌 USB들 인덱싱
            var indexable = DriveDetector.GetIndexableDrives(includeRemovable: true);
            foreach (var letter in indexable)
            {
                if (_multi.ContainsDrive(letter)) continue;
                if (!DriveDetector.IsRemovable(letter)) continue;

                var slot = new MultiDriveIndex.DriveSlot { DriveLetter = letter };
                _multi.AddSlot(slot);
                UpdateFooterSummary();

                await Task.Run(() => BuildOneDrive(slot));
                StartMonitorIfReady(slot);
                StartMetadataPreloader(slot);
                UpdateFooterSummary();
            }

            if (!string.IsNullOrEmpty(_lastSearchQuery) && SearchBox.Text == _lastSearchQuery)
                RunSearch();
        }
        else
        {
            // 꺼졌음 → 현재 인덱싱된 USB 제거
            var slots = _multi.Slots;
            foreach (var slot in slots)
            {
                if (DriveDetector.IsRemovable(slot.DriveLetter))
                {
                    _multi.RemoveDrive(slot.DriveLetter);
                }
            }
            UpdateFooterSummary();

            if (!string.IsNullOrEmpty(_lastSearchQuery) && SearchBox.Text == _lastSearchQuery)
                RunSearch();
        }
    }

    private void ZoomIn()
    {
        ApplyZoom(Math.Min(ZoomMax, ContentScale.ScaleX + ZoomStep));
    }

    private void ZoomOut()
    {
        ApplyZoom(Math.Max(ZoomMin, ContentScale.ScaleX - ZoomStep));
    }

    private void ZoomReset()
    {
        ApplyZoom(1.0);
    }

    private void ApplyZoom(double scale)
    {
        scale = Math.Round(Math.Clamp(scale, ZoomMin, ZoomMax), 2);
        ContentScale.ScaleX = scale;
        ContentScale.ScaleY = scale;
        _settings.UiZoom = scale;
        _settings.Save();
    }

    private void Window_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Delta > 0) ZoomIn();
            else if (e.Delta < 0) ZoomOut();
            e.Handled = true;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class SearchResultRow
{
    public required HdrTracer.Core.FileIndex SourceIndex { get; init; }
    public required int EntryIndex { get; init; }
    public required string Drive { get; init; }
    public required string Kind { get; init; }

    private string? _name;
    private string? _path;
    private string _sizeText = "";
    private string _modifiedText = "";
    private long _sizeBytes;
    private DateTime _modifiedUtc;
    private bool _pathResolved;
    private bool _metaResolved;

    private System.Windows.Media.ImageSource? _icon;
    private bool _iconResolved;

    public string Name { get { ResolveName(); return _name!; } }
    public string Path { get { ResolvePath(); return _path!; } }
    public string SizeText { get { ResolveMeta(); return _sizeText; } }
    public string ModifiedText { get { ResolveMeta(); return _modifiedText; } }
    public long SizeBytes { get { ResolveMeta(); return _sizeBytes; } }
    public DateTime ModifiedUtc { get { ResolveMeta(); return _modifiedUtc; } }

    public System.Windows.Media.ImageSource? Icon
    {
        get
        {
            if (!_iconResolved)
            {
                _iconResolved = true;
                ResolvePath(); // 경로 필요
                _icon = IconCache.GetIcon(_path!, Kind == "폴더");
            }
            return _icon;
        }
    }

    // 이름만 가볍게 해석 (전체 경로 조립 없이 GetName 사용 → 이름 정렬이 빠름)
    private bool _nameResolved;
    private void ResolveName()
    {
        if (_nameResolved) return;
        _nameResolved = true;
        // 경로가 이미 만들어졌으면 그걸 쓰고, 아니면 인덱스에서 이름만 직접 가져온다
        _name = _pathResolved ? System.IO.Path.GetFileName(_path) : SourceIndex.GetName(EntryIndex);
    }

    private void ResolvePath()
    {
        if (_pathResolved) return;
        _pathResolved = true;
        _path = SourceIndex.GetFullPath(EntryIndex) ?? "";
        _name = System.IO.Path.GetFileName(_path);
        _nameResolved = true;
    }

    private void ResolveMeta()
    {
        if (_metaResolved) return;
        _metaResolved = true;

        if (SourceIndex.HasMetadata(EntryIndex))
        {
            _sizeBytes = SourceIndex.GetSize(EntryIndex);
            _modifiedUtc = SourceIndex.GetModifiedUtc(EntryIndex);

            // 무명 $DATA가 비어 있어(예: WOF 압축 파일, ADS에 실제 데이터가 있는 경우)
            // 인덱스 크기가 0으로 저장된 파일은, 표시 시점에 디스크에서 실제 크기를 보충한다.
            // (날짜는 인덱스 값이 정확하므로 그대로 사용)
            if (_sizeBytes == 0 && Kind != "폴더")
            {
                ResolvePath();
                var disk = HdrTracer.Core.FileInfoFetcher.Get(_path!);
                if (disk.Found && disk.Size > 0)
                    _sizeBytes = disk.Size;
            }

            if (Kind != "폴더")
                _sizeText = HdrTracer.Core.FileInfoFetcher.FormatSize(_sizeBytes);
            _modifiedText = HdrTracer.Core.FileInfoFetcher.FormatDate(_modifiedUtc);
            return;
        }

        ResolvePath();
        var info = HdrTracer.Core.FileInfoFetcher.Get(_path!);
        if (info.Found)
        {
            _sizeBytes = info.Size;
            _modifiedUtc = info.ModifiedUtc;
            if (Kind != "폴더")
                _sizeText = HdrTracer.Core.FileInfoFetcher.FormatSize(info.Size);
            _modifiedText = HdrTracer.Core.FileInfoFetcher.FormatDate(info.ModifiedUtc);
        }
    }
}