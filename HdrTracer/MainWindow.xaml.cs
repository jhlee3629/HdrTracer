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

    private System.Windows.Point _dragStart;
    private bool _dragArmed;

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

        // 창 이동/크기조절 시 히스토리 팝업이 검색창을 따라오도록 재배치
        LocationChanged += (_, _) => RepositionHistoryPopup();
        SizeChanged     += (_, _) => RepositionHistoryPopup();

        StateChanged += MainWindow_StateChanged;

        // 저장된 설정을 검색 엔진에 반영 (기본 false → 숨김+시스템 항목 숨김)
        _engine.HideHiddenSystemItems = !_settings.ShowHiddenSystemItems;
        _engine.ExcludedFolderNames = _settings.ExcludedFolders.ToArray();

        // 저장된 컬럼 너비 복원
        ApplySavedColumnWidths();

        // 저장된 정렬 상태 복원 (해석 실패 시 기본값 Name/오름차순 유지)
        if (Enum.TryParse<SortColumn>(_settings.SortColumn, out var savedSort))
            _sortColumn = savedSort;
        _sortAscending = _settings.SortAscending;

        RestoreWindowPlacement();

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
        // 고정 검색 단축키: Ctrl+1~9 → 고정 목록의 위에서 n번째 검색을 즉시 실행
        for (int i = 0; i < 9; i++)
        {
            int idx = i;   // 클로저 캡처 (루프 변수 직접 캡처 금지)
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => RunPinnedSearch(idx)),
                Key.D1 + i, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => RunPinnedSearch(idx)),
                Key.NumPad1 + i, ModifierKeys.Control));   // 숫자패드도 지원
        }
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
        if (DateTime.UtcNow < _footerNoticeUntil) return;   // 알림 문구 보호 중엔 덮지 않음

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
        if (SearchPlaceholder != null)
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

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

    // 크기/날짜 조건 토큰을 검색창에 넣는다. 같은 종류의 기존 조건은 교체, token이 null이면 제거만.
    // (조건 토큰 구분: '>' 또는 '<'로 시작하고, 끝이 B/KB/MB/GB/TB면 크기, 아니면 날짜)
    private void ApplyAttrFilter(bool isSize, string? token)
    {
        var kept = SplitQueryTokens(SearchBox.Text)
            .Where(t =>
            {
                if (t.Length < 2 || (t[0] != '>' && t[0] != '<')) return true;  // 조건 토큰 아님 → 유지
                bool tokenIsSize = char.ToUpperInvariant(t[^1]) == 'B';
                return tokenIsSize != isSize;                                    // 같은 종류만 제거
            })
            .ToList();

        // SplitQueryTokens가 따옴표를 벗기므로, 공백 포함 토큰(경로)은 따옴표 복원
        for (int i = 0; i < kept.Count; i++)
            if (kept[i].Contains(' ')) kept[i] = "\"" + kept[i] + "\"";

        if (token != null) kept.Add(token);

        SearchBox.Text = string.Join(" ", kept);
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();
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
        if (!string.IsNullOrEmpty(SearchBox.Text))
        {
            HistoryPopup.IsOpen = false;
            return;
        }

        // 고정 검색을 위에, 일반 히스토리를 아래에 (고정과 같은 검색어는 히스토리에서 숨김)
        var pinned = _settings.PinnedSearches;
        var items = new List<HistoryItem>();
        foreach (var p in pinned)
            items.Add(new HistoryItem { Query = p, IsPinned = true });
        foreach (var q in _settings.SearchHistory)
            if (!pinned.Any(p => string.Equals(p, q, StringComparison.OrdinalIgnoreCase)))
                items.Add(new HistoryItem { Query = q });

        if (items.Count == 0)
        {
            HistoryPopup.IsOpen = false;
            return;
        }

        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = items;
        HistoryPopup.IsOpen = true;
    }

    // WPF Popup은 창 이동을 자동으로 따라오지 않음 → 오프셋을 살짝 바꿨다 되돌려 위치 재계산 유도
    private void RepositionHistoryPopup()
    {
        if (!HistoryPopup.IsOpen) return;
        double o = HistoryPopup.HorizontalOffset;
        HistoryPopup.HorizontalOffset = o + 0.1;
        HistoryPopup.HorizontalOffset = o;
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

        if (HistoryList.SelectedItem is not HistoryItem picked) return;

        HistoryPopup.IsOpen = false;
        HistoryList.SelectedItem = null;

        SearchBox.Text = picked.Query;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();

        // 검색 실행 (히스토리 기록/순서 갱신은 RunSearch가 담당)
        RunSearch();
    }

    private void HistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not HistoryItem item) return;

        // ✕ = 완전 삭제: 고정 목록과 히스토리 양쪽에서 제거
        // (고정된 검색어도 히스토리에 남아 있어, 한쪽만 지우면 "고정 해제"처럼 보인다)
        _settings.PinnedSearches.RemoveAll(
            x => string.Equals(x, item.Query, StringComparison.OrdinalIgnoreCase));
        _settings.SearchHistory.RemoveAll(
            x => string.Equals(x, item.Query, StringComparison.OrdinalIgnoreCase));
        _settings.Save();

        ShowHistoryPopup();   // 목록 재구성 (전부 비면 스스로 닫음)

        e.Handled = true;  // ListBox 선택으로 전파 방지
    }

    private void HistoryPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not HistoryItem item) return;

        var p = _settings.PinnedSearches;
        p.RemoveAll(x => string.Equals(x, item.Query, StringComparison.OrdinalIgnoreCase));
        if (!item.IsPinned)
            p.Insert(0, item.Query);   // 고정: 맨 위로
        _settings.Save();

        ShowHistoryPopup();   // 팝업 열린 채 목록만 갱신

        e.Handled = true;
    }

    private async void RunSearch(bool isAuto = false)
    {
        var query = SearchBox.Text;

        if (!isAuto) _footerNoticeUntil = DateTime.MinValue;   // 직접 행동 → 알림 보호 해제

        // 검색 시작 → 사전로딩 일시 중단, 검색 끝난 후 1.5초 뒤 재개
        foreach (var p in _preloaders) p.Pause();
        _preloadResumeTimer.Stop();   

        var indexes = _multi.GetActiveIndexes();

        if (string.IsNullOrWhiteSpace(query) || indexes.Count == 0)
        {
            ResultsList.ItemsSource = null;
            EmptyHint.Visibility = Visibility.Collapsed;
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
            EmptyHint.Visibility = sortedRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

            // 직접 검색은 즉시 갱신. 자동 재검색은 최근 알림(삭제 결과 등)을 3초간 존중.
            if (!isAuto || DateTime.UtcNow >= _footerNoticeUntil)
            {
                FooterText.Text = sortedRows.Count >= MaxDisplayResults
                    ? $"{sortedRows.Count:N0}+ {Loc.T("status.results")} ({_lastSearchMs}ms)"
                    : $"{sortedRows.Count:N0} {Loc.T("status.results")} ({_lastSearchMs}ms)";
            }

            _footerBeforeSelection = null;   // 새 검색 → 이전 선택 요약의 복원 문구 무효화
        }
        catch (OperationCanceledException)
        { }
    }

    // Ctrl+1~9: 고정 검색 실행. 번호는 히스토리 팝업의 📌 목록 순서(위에서부터).
    private void RunPinnedSearch(int index)
    {
        var p = _settings.PinnedSearches;
        if (index < 0 || index >= p.Count) return;   // 그 번호에 고정 검색 없으면 무시

        SearchBox.Text = p[index];
        SearchBox.CaretIndex = SearchBox.Text.Length;
        HistoryPopup.IsOpen = false;
        RunSearch();
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

        // 정렬 상태 저장 (재시작 시 복원)
        _settings.SortColumn = _sortColumn.ToString();
        _settings.SortAscending = _sortAscending;
        _settings.Save();

        // 현재 검색 결과를 다시 정렬해서 표시
        if (ResultsList.ItemsSource is List<SearchResultRow> currentRows)
        {
            var sorted = SortRows(currentRows);
            ResultsList.ItemsSource = sorted;
        }
    }

    // 결과 리스트의 세로 스크롤바 유무에 맞춰 헤더 오른쪽 여백(16px) 컬럼을 동적으로 조정.
    // 스크롤바가 없을 때 크기/날짜 데이터가 헤더와 어긋나는 문제 방지.
    private void ResultsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer sv) return;

        double w = sv.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 16 : 0;
        var lastCol = HeaderGrid.ColumnDefinitions[^1];
        if (lastCol.Width.Value != w)
            lastCol.Width = new GridLength(w);
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
        "ColPath"  => PathMinWidth,
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

        NormalizeColumnWeights();

        switch (tag)
        {
            case "ColDrive":   AdjustTwoPixel("ColDrive", "ColName", d); break; 
            case "ColName":    AdjustTwoPixel("ColName", "ColPath", d); break; 
            case "ColSizeLeft":AdjustTwoPixel("ColPath", "ColSize", d); break; 
            case "ColSize":    AdjustTwoPixel("ColSize", "ColDate", d); break; 
        }
    }

    // 별(*) 가중치를 현재 실제 렌더 픽셀로 재설정(화면 변화 없음).
    // 창 크기 변경 후 드래그할 때 무관한 컬럼이 출렁이는 것을 방지.
    private void NormalizeColumnWeights()
    {
        Cols.SetDrive(HeaderActualWidth(0));
        Cols.SetName(HeaderActualWidth(1));
        Cols.SetPath(HeaderActualWidth(2));
        Cols.SetSize(HeaderActualWidth(3));
        Cols.SetDate(HeaderActualWidth(4));
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
        _settings.ColWidthPath  = HeaderActualWidth(2);
        _settings.ColWidthSize  = HeaderActualWidth(3);
        _settings.ColWidthDate  = HeaderActualWidth(4);
        _settings.Save();
    }

    // 시작 시: 저장된 컬럼 너비를 Cols에 적용.
    // 다섯 값이 모두 정상일 때만 "한 세트"로 적용한다 — 일부만 적용하면
    // 옛 저장값과 기본값이 섞여 비율(별 가중치)이 틀어지기 때문.
    private void ApplySavedColumnWidths()
    {
        bool allValid =
            _settings.ColWidthDrive >= MinWidthOf("ColDrive") &&
            _settings.ColWidthName  >= MinWidthOf("ColName")  &&
            _settings.ColWidthPath  >= MinWidthOf("ColPath")  &&
            _settings.ColWidthSize  >= MinWidthOf("ColSize")  &&
            _settings.ColWidthDate  >= MinWidthOf("ColDate");

        if (!allValid) return;   // 하나라도 없거나 비정상 → 전부 기본 비율로 시작

        Cols.SetDrive(_settings.ColWidthDrive);
        Cols.SetName(_settings.ColWidthName);
        Cols.SetPath(_settings.ColWidthPath);
        Cols.SetSize(_settings.ColWidthSize);
        Cols.SetDate(_settings.ColWidthDate);
    }

    // 저장된 창 위치·크기·최대화 상태 복원.
    // 원칙: 저장값을 그대로 믿지 않고, "현재 연결된 모니터 중 하나에 타이틀바가
    // 충분히 보이는지" 검증한다. 유효하면 그대로(두 번째 모니터·걸친 배치 존중),
    // 아니면(모니터 분리·해상도 변경 등) 위치만 버리고 기본(화면 중앙)으로 시작한다.
    private void RestoreWindowPlacement()
    {
        var s = _settings;
        if (s.WinWidth < 100 || s.WinHeight < 100) return;   // 저장값 없음/비정상 → 기본 크기·중앙

        // 크기는 항상 복원 (최소 크기 미만이면 최소로 보정)
        Width  = Math.Max(MinWidth,  s.WinWidth);
        Height = Math.Max(MinHeight, s.WinHeight);

        // DIP(WPF 단위) → 물리 픽셀 변환 배율 (주 모니터 기준 근사)
        double sx = 1.0, sy = 1.0;
        try
        {
            var ps = System.Windows.Forms.Screen.PrimaryScreen;
            if (ps is not null && SystemParameters.PrimaryScreenWidth > 0)
            {
                sx = ps.Bounds.Width  / SystemParameters.PrimaryScreenWidth;
                sy = ps.Bounds.Height / SystemParameters.PrimaryScreenHeight;
            }
        }
        catch { }

        // 타이틀바 영역(창 상단 30 DIP)을 픽셀 사각형으로
        var titleRect = new System.Drawing.Rectangle(
            (int)(s.WinLeft * sx), (int)(s.WinTop * sy),
            (int)(Width * sx), (int)(30 * sy));

        // 연결된 모든 모니터의 작업 영역과 교차 검사:
        // 타이틀바가 가로 50px·세로 10px 이상 보이면 "잡을 수 있는 위치"로 판정
        bool visible = false;
        try
        {
            foreach (var scr in System.Windows.Forms.Screen.AllScreens)
            {
                var inter = System.Drawing.Rectangle.Intersect(scr.WorkingArea, titleRect);
                if (inter.Width >= 50 && inter.Height >= 10) { visible = true; break; }
            }
        }
        catch { }

        if (visible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WinLeft;
            Top  = s.WinTop;
        }
        // visible == false → 위치는 버리고 XAML의 CenterScreen 유지 (크기만 복원)

        // 최대화는 위치를 정한 뒤에: 창 좌표가 속한 모니터에서 최대화된다
        if (s.WinMaximized)
            WindowState = WindowState.Maximized;
    }

    // 메뉴: 컬럼 너비 초기화 → 디자인 기본값으로 되돌리고 저장
    private void ResetColumnWidths()
    {
        Cols.SetDrive(50);
        Cols.SetName(280);
        Cols.SetPath(300);
        Cols.SetSize(80);
        Cols.SetDate(120);

        _settings.ColWidthDrive = 50;
        _settings.ColWidthName  = 280;
        _settings.ColWidthPath  = 300;
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

    // group → Cols의 현재 픽셀값 읽기 / 쓰기
    private double ColPx(string group) => group switch
    {
        "ColDrive" => Cols.DrivePx,
        "ColName"  => Cols.NamePx,
        "ColPath"  => Cols.PathPx,
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
            case "ColPath":  Cols.SetPath(px); break;
            case "ColSize":  Cols.SetSize(px);  break;
            case "ColDate":  Cols.SetDate(px);  break;
        }
    }
    // group → 헤더 인덱스 (실제 렌더 폭 읽기용)
    private static int HeaderIndexOf(string group) => group switch
    {
        "ColDrive" => 0,
        "ColName"  => 1,
        "ColPath"  => 2,
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
            if (rows.Count >= 50_000)
            {
                var sorted = _sortAscending
                    ? rows.AsParallel().OrderBy(r => r.SizeBytes)
                    : rows.AsParallel().OrderByDescending(r => r.SizeBytes);
                return sorted.ToList();
            }
            var copy = new List<SearchResultRow>(rows);
            if (_sortAscending) copy.Sort((a, b) => a.SizeBytes.CompareTo(b.SizeBytes));
            else copy.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            return copy;
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
            if (HistoryPopup.IsOpen && HistoryList.SelectedItem is HistoryItem highlighted)
            {
                SearchBox.Text = highlighted.Query;
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
            _dragArmed = false;
        }
        else
        {
            // 항목 위에서 눌렀으면 드래그 시작 후보로 기록
            _dragStart = e.GetPosition(null);
            _dragArmed = true;
        }
    }

    // 선택한 파일/폴더를 탐색기 등 외부로 끌어다 놓기 (FileDrop).
    // 주의: 이 앱은 관리자 권한이라, 일반 권한 탐색기로의 드롭은 Windows(UIPI)가 막을 수 있음.
    private void ResultsList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;   // 아직 클릭 수준의 움직임 → 드래그 아님

        _dragArmed = false;

        var rows = GetSelectedRows();
        if (rows.Count == 0) return;

        var paths = rows.Select(r => r.Path)
                        .Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p))
                        .ToArray();
        if (paths.Length == 0) return;

        var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, paths);
        try
        {
            System.Windows.DragDrop.DoDragDrop(ResultsList, data,
                System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Move);
        }
        catch { /* 드래그 취소 등은 무시 */ }
    }

    // 선택 항목 요약: 하단에 "N개 선택, 총 크기"를 표시하고, 해제되면 이전 문구 복원.
    private string? _footerBeforeSelection;

    // 하단 알림 문구(삭제 결과 등)를 자동 재검색이 잠시 덮지 못하게 보호하는 시각
    private DateTime _footerNoticeUntil = DateTime.MinValue;

    /// <summary>하단에 알림 문구를 표시하고 3초간 자동 갱신으로부터 보호한다.</summary>
    private void ShowFooterNotice(string text)
    {
        FooterText.Text = text;
        _footerBeforeSelection = null;                       // 선택 해제 복원에 덮이지 않게
        //_footerNoticeUntil = DateTime.UtcNow.AddSeconds(6);  // 자동 재검색 갱신으로부터 보호
        _footerNoticeUntil = DateTime.MaxValue;  // 사용자가 직접 행동할 때까지 유지
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int n = ResultsList.SelectedItems.Count;
        if (n == 0)
        {
            if (_footerBeforeSelection != null)
            {
                FooterText.Text = _footerBeforeSelection;
                _footerBeforeSelection = null;
            }
            return;
        }

        _footerBeforeSelection ??= FooterText.Text;   // 처음 선택될 때의 문구를 기억

        long total = 0;
        foreach (var it in ResultsList.SelectedItems)
            if (it is SearchResultRow r) total += r.SizeBytes;

        FooterText.Text = string.Format(Loc.T("status.selected"),
            n, HdrTracer.Core.FileInfoFetcher.FormatSize(total));
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

        ShowFooterNotice(fail == 0
            ? string.Format(Loc.T("ctx.open.multi"), ok)
            : string.Format(Loc.T("ctx.open.partial"), ok, fail));
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

    // 선택 항목의 폴더를 경로 필터로 검색창에 넣고 재검색.
    // 파일이면 부모 폴더, 폴더면 그 폴더 자체. 공백 포함 경로는 따옴표로 묶는다.
    private void MenuSearchInFolder_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRows();
        if (rows.Count == 0) return;
        var row = rows[0];

        string folder;
        try
        {
            folder = System.IO.Directory.Exists(row.Path)
                ? row.Path
                : (System.IO.Path.GetDirectoryName(row.Path) ?? row.Path);
        }
        catch { return; }

        folder = folder.TrimEnd('\\') + "\\";
        string filter = folder.Contains(' ') ? "\"" + folder + "\"" : folder;

        // 기존 검색어에서 이전 경로 필터를 제거하고 새 필터로 교체
        var kept = new List<string>();
        foreach (var t in SplitQueryTokens(SearchBox.Text))
            if (!t.Contains('\\') && !t.Contains('/')) kept.Add(t);
        kept.Add(filter);

        SearchBox.Text = string.Join(" ", kept);
        RunSearch();
    }

    // 검색창 텍스트를 따옴표 인식하며 토큰으로 분리 (경로 필터 교체용)
    private static List<string> SplitQueryTokens(string raw)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        foreach (char c in raw)
        {
            if (c == '"') { inQuote = !inQuote; continue; }
            if (c == ' ' && !inQuote)
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    // 공통: 행 목록을 CSV(UTF-8 BOM, 엑셀 호환)로 저장.
    private void ExportRowsToCsv(IReadOnlyList<SearchResultRow> rows)
    {
        if (rows.Count == 0) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "HdrTracer_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv",
            DefaultExt = ".csv"
        };
        if (dlg.ShowDialog(this) != true) return;

        static string Esc(string s)
            => s.Contains('"') || s.Contains(',') || s.Contains('\n')
               ? "\"" + s.Replace("\"", "\"\"") + "\""
               : s;

        try
        {
            var sb = new System.Text.StringBuilder(rows.Count * 96);
            sb.Append(Loc.T("col.name")).Append(',')
              .Append(Loc.T("col.path")).Append(',')
              .Append(Loc.T("col.size")).Append(',')
              .AppendLine(Loc.T("col.date"));

            foreach (var r in rows)
            {
                sb.Append(Esc(r.Name)).Append(',')
                  .Append(Esc(r.Path)).Append(',')
                  .Append(r.SizeBytes).Append(',')
                  .AppendLine(Esc(r.ModifiedText));
            }

            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));
            FooterText.Text = string.Format(Loc.T("export.done"), rows.Count);
        }
        catch (Exception ex)
        {
            InfoDialog.Show(this, Loc.T("common.error"), ex.Message);
        }
    }

    // 앱 메뉴: 현재 검색 결과 전체 내보내기
    private void ExportAllResults()
    {
        if (ResultsList.ItemsSource is List<SearchResultRow> rows && rows.Count > 0)
            ExportRowsToCsv(rows);
    }

    // 우클릭 메뉴: 선택한 항목만 내보내기
    private void MenuExportSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = GetSelectedRows();
        if (rows.Count > 0) ExportRowsToCsv(rows);
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
                    if (!ConfirmDialog.Show(this, Loc.T("ctx.rename.title"), Loc.T("ctx.rename.extWarn")))
                        return;
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

        // 너무 긴 이름/경로는 가운데를 줄여 한 줄로 (다이얼로그 줄바꿈 난립 방지)
        static string Shorten(string s, int max = 60)
            => s.Length <= max ? s : s[..(max / 2 - 1)] + "…" + s[^(max / 2 - 1)..];

        var dangerous = rows.Select(r => r.Path)
                            .Where(IsDangerousPath)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

        string confirmMsg;
        if (rows.Count == 1)
        {
            confirmMsg = $"{Loc.T("ctx.delete.confirm")}\n\n{Shorten(rows[0].Path, 90)}";
        }
        else
        {
            // 목록은 하나만: 파일명 최대 10개, 위험 항목은 ⚠ 로 표시
            const int previewMax = 10;
            var names = rows.Take(previewMax)
                            .Select(r =>
                            {
                                string n = System.IO.Path.GetFileName(r.Path.TrimEnd('\\'));
                                if (string.IsNullOrEmpty(n)) n = r.Path;
                                n = Shorten(n);
                                return IsDangerousPath(r.Path) ? "⚠ " + n : n;
                            })
                            .ToList();
            string list = string.Join("\n", names);
            if (rows.Count > previewMax)
                list += "\n" + string.Format(Loc.T("ctx.delete.more"), rows.Count - previewMax);

            confirmMsg = string.Format(Loc.T("ctx.delete.confirm.multi"), rows.Count)
                         + "\n\n" + list;
        }

        // 위험 항목이 있으면 경고 문구를 맨 위에 한 줄만 (경로 목록 중복 제거)
        if (dangerous.Count > 0)
            confirmMsg = Loc.T("ctx.delete.danger") + "\n\n" + confirmMsg;

        if (!ConfirmDialog.Show(this, Loc.T("ctx.delete.title"), confirmMsg))
            return;

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

        // 결과 푸터 표시 (알림 보호: 자동 갱신이 덮지 못하게)
        if (fail == 0)
        {
            ShowFooterNotice(rows.Count == 1
                ? $"{Loc.T("ctx.delete.title")}: {rows[0].Name}"
                : string.Format(Loc.T("ctx.delete.done.multi"), ok));
        }
        else
        {
            ShowFooterNotice(string.Format(Loc.T("ctx.delete.partial"), ok, fail));
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

    private static string? PromptForText(string title, string prompt, string defaultValue, bool selectExtension = true)
    {
        return InputDialog.Show(
            System.Windows.Application.Current?.MainWindow,
            title, prompt, defaultValue, selectAll: selectExtension);
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
            return;
        }

        // 진짜 종료 확정: 창 위치·크기·최대화 상태 저장.
        // (Closed에서는 RestoreBounds가 Empty가 되어 최대화 상태 저장이 안 됨 → 반드시 여기서)
        try
        {
            var b = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            if (b.Width >= 100 && b.Height >= 100)
            {
                _settings.WinLeft = b.Left;
                _settings.WinTop = b.Top;
                _settings.WinWidth = b.Width;
                _settings.WinHeight = b.Height;
                _settings.WinMaximized = WindowState == WindowState.Maximized;
                _settings.Save();
            }
        }
        catch { /* 저장 실패해도 종료는 계속 */ }
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

        filterItem.Items.Add(new Separator());

        // 크기 조건 (클릭하면 검색창에 문법이 들어가고 즉시 검색)
        var sizeItem = new MenuItem { Header = Loc.T("filter.size") };
        void AddSize(string locKey, string? token)
        {
            var mi = new MenuItem { Header = Loc.T(locKey) };
            mi.Click += (_, _) => ApplyAttrFilter(isSize: true, token);
            sizeItem.Items.Add(mi);
        }
        AddSize("filter.size.10mb",  ">10MB");
        AddSize("filter.size.100mb", ">100MB");
        AddSize("filter.size.1gb",   ">1GB");
        sizeItem.Items.Add(new Separator());
        AddSize("filter.clear", null);
        filterItem.Items.Add(sizeItem);

        // 기간 조건
        var dateItem = new MenuItem { Header = Loc.T("filter.date") };
        void AddDate(string locKey, string? token)
        {
            var mi = new MenuItem { Header = Loc.T(locKey) };
            mi.Click += (_, _) => ApplyAttrFilter(isSize: false, token);
            dateItem.Items.Add(mi);
        }
        AddDate("filter.date.today", ">today");
        AddDate("filter.date.week",  ">week");
        AddDate("filter.date.month", ">month");
        AddDate("filter.date.year",  ">year");
        dateItem.Items.Add(new Separator());
        AddDate("filter.clear", null);
        filterItem.Items.Add(dateItem);

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
            Header = Loc.T("menu.lang.ko"),
            IsChecked = isKo          // 현재 언어에 체크 표시 (텍스트 정렬은 자동)
        };
        koItem.Click += (_, _) => ChangeLanguage(Loc.Lang.Korean);
        langItem.Items.Add(koItem);

        var enItem = new MenuItem
        {
            Header = Loc.T("menu.lang.en"),
            IsChecked = !isKo
        };
        enItem.Click += (_, _) => ChangeLanguage(Loc.Lang.English);
        langItem.Items.Add(enItem);

        menu.Items.Add(langItem);

        menu.Items.Add(new Separator());

        // 검색 도움말
        var searchHelpItem = new MenuItem { Header = Loc.T("menu.searchHelp") };
        searchHelpItem.Click += (_, _) => InfoDialog.Show(this, Loc.T("help.search.title"), Loc.T("help.search.body"));
        menu.Items.Add(searchHelpItem);

        // 결과 내보내기 (현재 검색 결과 전체 → CSV)
        var exportItem = new MenuItem
        {
            Header = Loc.T("menu.export"),
            IsEnabled = ResultsList.ItemsSource is List<SearchResultRow> { Count: > 0 }
        };
        exportItem.Click += (_, _) => ExportAllResults();
        menu.Items.Add(exportItem);

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
        Resources["TipPin"]        = Loc.T("tip.pin");

        // 검색 결과 우클릭 메뉴
        CtxOpen.Header       = Loc.T("ctx.open");
        CtxRunAsAdmin.Header = Loc.T("ctx.runAsAdmin");
        CtxOpenWith.Header   = Loc.T("ctx.openWith");
        CtxReveal.Header     = Loc.T("ctx.reveal");
        CtxSearchFolder.Header = Loc.T("ctx.searchFolder");
        CtxExport.Header     = Loc.T("ctx.export");
        CtxCopyPath.Header   = Loc.T("ctx.copyPath");
        CtxCopyName.Header   = Loc.T("ctx.copyName");
        CtxRename.Header     = Loc.T("ctx.rename");
        CtxCopyFile.Header   = Loc.T("ctx.copyFile");
        CtxDelete.Header     = Loc.T("ctx.delete");
        CtxProperties.Header = Loc.T("ctx.properties");
        
        SearchPlaceholder.Text = Loc.T("search.placeholder");
        EmptyHintTitle.Text = Loc.T("empty.title");
        EmptyHintBody.Text  = Loc.T("empty.body");

        // 푸터 요약 갱신 (총 N개 등)
        UpdateFooterSummary();
    }

    private async Task RebuildIndex()
    {
        if (!ConfirmDialog.Show(this, Loc.T("refresh.confirm.title"), Loc.T("refresh.confirm.msg")))
            return;

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
            if (dlg.HiddenSystemChanged || dlg.ExcludedChanged)
            {
                _engine.HideHiddenSystemItems = !_settings.ShowHiddenSystemItems;
                _engine.ExcludedFolderNames = _settings.ExcludedFolders.ToArray();
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

            // 검색창에 검색어가 있으면 무조건 목록 갱신.
            // (_lastSearchQuery 일치 조건은 껐다 켠 직후 상태에 따라 어긋나
            //  인덱싱은 되는데 목록이 안 바뀌는 문제가 있었음 — USB 도착/제거 경로와 동일한 처방)
            if (!string.IsNullOrEmpty(SearchBox.Text))
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

            // 검색창에 검색어가 있으면 무조건 목록 갱신.
            // (_lastSearchQuery 일치 조건은 껐다 켠 직후 상태에 따라 어긋나
            //  인덱싱은 되는데 목록이 안 바뀌는 문제가 있었음 — USB 도착/제거 경로와 동일한 처방)
            if (!string.IsNullOrEmpty(SearchBox.Text))
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

/// <summary>히스토리 팝업 항목: 고정(📌) 여부를 아는 검색어.</summary>
public sealed class HistoryItem
{
    public string Query { get; init; } = "";
    public bool IsPinned { get; init; }
    public string Display  => (IsPinned ? "\uD83D\uDCCC " : "") + Query;   // 📌 접두
    public string PinGlyph => IsPinned ? "\uE77A" : "\uE718";              // MDL2: Unpin / Pin
}
