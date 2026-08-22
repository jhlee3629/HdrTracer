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
    public ColumnWidths Cols { get; } = new();

    private readonly MultiDriveIndex _multi = new();
    private readonly SearchEngine _engine = new();
    private readonly DriveWatcher _watcher = new();

    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _searchCts;
    private long _searchSequence;
    private const int MaxDisplayResults = 1_000_000;

    private bool _contextMenuOpen;

    private readonly HashSet<string> _recentlyDeletedPaths =
        new(StringComparer.OrdinalIgnoreCase);

    private enum SortColumn { Drive, Name, Path, Size, Date, Kind }
    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAscending = true; 
    private readonly List<MetadataPreloader> _preloaders = new();
    
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
    private long _lastSearchMs;  

    private string _lastSearchQuery = "";

    private int _runawayCount;

    private List<SearchResultRow>? _lastRows;

    private DateTime _runawayStart;

    public MainWindow()
    {
        InitializeComponent();

        LocationChanged += (_, _) => RepositionHistoryPopup();
        SizeChanged     += (_, _) => RepositionHistoryPopup();

        StateChanged += MainWindow_StateChanged;

        _engine.HideHiddenSystemItems = !_settings.ShowHiddenSystemItems;
        _engine.ExcludedFolderNames = _settings.ExcludedFolders.ToArray();

        ApplySavedColumnWidths();

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
            
            if (string.IsNullOrEmpty(_lastSearchQuery)) return;
            
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) return;
            
            if (SearchBox.Text != _lastSearchQuery) return;
            
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

        Deactivated += (_, _) =>
        {
            if (ResultsList.SelectedItems.Count > 2000)
                ClearResultSelectionFast();
        };

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

        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => SettingsButton_Click(this, new RoutedEventArgs())),
            Key.OemComma, ModifierKeys.Control));

        InputBindings.Add(new KeyBinding(
            new RelayCommand(async _ => await RebuildIndex()),
            Key.F5, ModifierKeys.None));

        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomIn()),
            Key.OemPlus, ModifierKeys.Control));   
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomIn()),
            Key.Add, ModifierKeys.Control));        
        
        for (int i = 0; i < 9; i++)
        {
            int idx = i;   
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => RunPinnedSearch(idx)),
                Key.D1 + i, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(
                new RelayCommand(_ => RunPinnedSearch(idx)),
                Key.NumPad1 + i, ModifierKeys.Control));   
        }
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomOut()),
            Key.OemMinus, ModifierKeys.Control));  
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomOut()),
            Key.Subtract, ModifierKeys.Control));   
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomReset()),
            Key.D0, ModifierKeys.Control));         
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ZoomReset()),
            Key.NumPad0, ModifierKeys.Control));    

        ApplyZoom(_settings.UiZoom);

        HdrTracer.Core.Localization.Current =
            HdrTracer.Core.Localization.FromCode(_settings.Language);

        ApplyLocalizedTexts();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        _watcher.AttachTo(src);

        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        src.AddHook(TaskbarCreatedHook);

        if (_settings.GlobalHotkeyEnabled)
            RegisterGlobalHotkey();
    }

    private uint _taskbarCreatedMsg;
    private bool _scrollBarSizerAttached;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    private IntPtr TaskbarCreatedHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreatedMsg != 0 && (uint)msg == _taskbarCreatedMsg)
        {
            _ = Dispatcher.BeginInvoke(new Action(RefreshTaskbarButton),
                    System.Windows.Threading.DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    private void RefreshTaskbarButton()
    {
        try
        {
            bool wasVisible = Visibility == Visibility.Visible;
            ShowInTaskbar = false;
            ShowInTaskbar = wasVisible;   
        }
        catch { }
    }

    private void RegisterGlobalHotkey()
    {
        try
        {
            if (_globalHotkey is null)
            {
                _globalHotkey = new GlobalHotkey(this);
                _globalHotkey.Pressed += (_, _) => SummonWindow();
            }

            bool ok = _globalHotkey.Register(
                GlobalHotkey.Modifiers.Win | GlobalHotkey.Modifiers.Alt,
                0x53);   

            if (!ok)
                ShowFooterNotice(Loc.T("hotkey.fail"));
        }
        catch
        {
            ShowFooterNotice(Loc.T("hotkey.fail"));
        }
    }

    private void UnregisterGlobalHotkey()
    {
        try { _globalHotkey?.Unregister(); } catch { }
    }

    private void SummonWindow()
    {
        if (Visibility != Visibility.Visible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        Activate();
        _trayIcon?.ForceForegroundPublic();   
        Topmost = true;
        Topmost = false;
        Focus();
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void ToggleVisibility()
    {
        if (Visibility != Visibility.Visible || WindowState == WindowState.Minimized)
        {
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

        FooterText.Text = "인덱스 빌드 중";

        foreach (var d in drives)
            _multi.AddSlot(new MultiDriveIndex.DriveSlot { DriveLetter = d });

        var totalSw = Stopwatch.StartNew();

        StartIndexingProgress();   
        var tasks = _multi.Slots.Select(slot => Task.Run(() => BuildOneDrive(slot))).ToArray();
        await Task.WhenAll(tasks);
        StopIndexingProgress();
        totalSw.Stop();

        foreach (var slot in _multi.Slots)
        {
            StartMonitorIfReady(slot);
            StartMetadataPreloader(slot);
        }

        try
        {
            _trayIcon = new TrayIconHelper(this);

            _trayIcon.PinnedSearchesProvider = () => _settings.PinnedSearches;
            _trayIcon.PinnedSearchRequested += (_, idx) =>
            {
                _trayIcon.ShowWindow();
                RunPinnedSearch(idx);
            };
            _trayIcon.SettingsRequested += (_, _) =>
            {
                _trayIcon.ShowWindow();
                SettingsButton_Click(this, new RoutedEventArgs());
            };
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
        
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            Activate();
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }), System.Windows.Threading.DispatcherPriority.Input);

        if (_settings.RestoreLastSearch && !string.IsNullOrWhiteSpace(_settings.LastSearchQuery))
        {
            SearchBox.Text = _settings.LastSearchQuery;
            SearchBox.CaretIndex = SearchBox.Text.Length;
            RunSearch();
        }
    }

    private System.Windows.Threading.DispatcherTimer? _indexProgressTimer;
    private Stopwatch? _indexProgressSw;

    private void StartIndexingProgress()
    {
        StopIndexingProgress();
        _indexProgressSw = Stopwatch.StartNew();
        _indexProgressTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _indexProgressTimer.Tick += (_, _) => UpdateIndexingProgress();
        _indexProgressTimer.Start();
        UpdateIndexingProgress();
    }

    private void UpdateIndexingProgress()
    {
        if (_indexProgressSw is null) return;

        var parts = new List<string>();
        foreach (var slot in _multi.Slots)
        {
            var idx = slot.Index;
            parts.Add(idx is not null
                ? string.Format(Loc.T("status.driveDone"), slot.DriveLetter, idx.Count)
                : string.Format(Loc.T("status.driveWorking"), slot.DriveLetter));
        }

        StatusText.Text = string.Format(Loc.T("status.indexingProgress"),
            (int)_indexProgressSw.Elapsed.TotalSeconds,
            string.Join("  ·  ", parts));
    }

    private void StopIndexingProgress()
    {
        _indexProgressTimer?.Stop();
        _indexProgressTimer = null;
        _indexProgressSw = null;
    }

    private void BuildOneDrive(MultiDriveIndex.DriveSlot slot)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            uint volSerial = IndexStore.GetVolumeSerial(slot.DriveLetter);

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

            var (idx, journalId, startUsn) = RawMftReader.BuildIndexWithJournalInfo(slot.DriveLetter);
            idx.BuildNgramIndex();   
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

    private async void OnDriveArrived(string driveLetter)
    {
        bool isIndexable = false;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var list = DriveDetector.GetIndexableDrives(_settings.IndexRemovableDrives);
            if (list.Contains(driveLetter, StringComparer.OrdinalIgnoreCase))
            {
                isIndexable = true;
                break;
            }
            await Task.Delay(500); 
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
        UpdateFooterSummary();   

        await Task.Run(() => BuildOneDrive(slot));

        StartMonitorIfReady(slot);
        StartMetadataPreloader(slot);

        UpdateFooterSummary();

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

        _watcher.UnregisterVolume(driveLetter); 
        _multi.RemoveDrive(driveLetter);
        UpdateFooterSummary();

        if (!string.IsNullOrEmpty(_lastSearchQuery))
        {
            if (SearchBox.Text != _lastSearchQuery)
                SearchBox.Text = _lastSearchQuery;
            RunSearch();
        }
    }

    private void OnDriveQueryRemove(string driveLetter)
    {
        if (!_multi.ContainsDrive(driveLetter)) return;

        _watcher.UnregisterVolume(driveLetter);

        for (int i = _preloaders.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_preloaders[i].DriveLetter, driveLetter, StringComparison.OrdinalIgnoreCase))
            {
                _preloaders[i].Stop();
                _preloaders.RemoveAt(i);
            }
        }

        _multi.RemoveDrive(driveLetter);
        UpdateFooterSummary();

        if (!string.IsNullOrEmpty(_lastSearchQuery))
        {
            if (SearchBox.Text != _lastSearchQuery)
                SearchBox.Text = _lastSearchQuery;
            RunSearch();
        }
    }

    private void UpdateFooterSummary()
    {
        if (DateTime.UtcNow < _footerNoticeUntil) return; 

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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholder != null)
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrEmpty(SearchBox.Text) && HistoryPopup.IsOpen)
            HistoryPopup.IsOpen = false;
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        RunSearch();
    }

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

        var keep = string.Join(" ",
            SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !t.StartsWith("*.") &&
                            !(t.StartsWith(".") && t.Length > 1 && !t.Contains('\\'))));

        SearchBox.Text = string.IsNullOrWhiteSpace(keep)
            ? pattern
            : $"{pattern} {keep}";

        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();

        HistoryPopup.IsOpen = false;
        RunSearch();
    }

    private void ApplyKindFilter(string? token)
    {
        var kept = SplitQueryTokens(SearchBox.Text)
            .Where(t => !t.Equals("folder:", StringComparison.OrdinalIgnoreCase)
                     && !t.Equals("dir:", StringComparison.OrdinalIgnoreCase)
                     && !t.Equals("file:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (int i = 0; i < kept.Count; i++)
            if (kept[i].Contains(' ')) kept[i] = "\"" + kept[i] + "\"";

        if (token != null) kept.Add(token);

        SearchBox.Text = string.Join(" ", kept);
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();
        HistoryPopup.IsOpen = false;
        RunSearch();
    }

    private void ApplyAttrFilter(bool isSize, string? token)
    {
        var kept = SplitQueryTokens(SearchBox.Text)
            .Where(t =>
            {
                if (t.Length < 2 || (t[0] != '>' && t[0] != '<')) return true;  
                bool tokenIsSize = char.ToUpperInvariant(t[^1]) == 'B';
                return tokenIsSize != isSize;                                   
            })
            .ToList();

        for (int i = 0; i < kept.Count; i++)
            if (kept[i].Contains(' ')) kept[i] = "\"" + kept[i] + "\"";

        if (token != null) kept.Add(token);

        SearchBox.Text = string.Join(" ", kept);
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();
        HistoryPopup.IsOpen = false;
        RunSearch();
    }

    private const int MaxHistory = 10;

    private bool _historyKeyboardNav;

    private void AddToHistory(string query)
    {
        query = query.Trim();
        if (string.IsNullOrWhiteSpace(query)) return;

        var h = _settings.SearchHistory;
        h.RemoveAll(x => string.Equals(x, query, StringComparison.OrdinalIgnoreCase));
        h.Insert(0, query);

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

        _ = Dispatcher.BeginInvoke(new Action(AttachHistoryScrollBarSizer),
                System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RepositionHistoryPopup()
    {
        if (!HistoryPopup.IsOpen) return;
        double o = HistoryPopup.HorizontalOffset;
        HistoryPopup.HorizontalOffset = o + 0.1;
        HistoryPopup.HorizontalOffset = o;
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
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

        RunSearch();
    }

    private void HistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not HistoryItem item) return;

        _settings.PinnedSearches.RemoveAll(
            x => string.Equals(x, item.Query, StringComparison.OrdinalIgnoreCase));
        _settings.SearchHistory.RemoveAll(
            x => string.Equals(x, item.Query, StringComparison.OrdinalIgnoreCase));
        _settings.Save();

        ShowHistoryPopup();  

        e.Handled = true;  
    }

    private void HistoryPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not HistoryItem item) return;

        var p = _settings.PinnedSearches;
        p.RemoveAll(x => string.Equals(x, item.Query, StringComparison.OrdinalIgnoreCase));
        if (!item.IsPinned)
            p.Insert(0, item.Query);  
        _settings.Save();

        ShowHistoryPopup();   

        e.Handled = true;
    }

    private async void RunSearch(bool isAuto = false)
    {
        var query = SearchBox.Text;

        if (!isAuto) _footerNoticeUntil = DateTime.MinValue; 

        foreach (var p in _preloaders) p.Pause();
        _preloadResumeTimer.Stop();   

        var indexes = _multi.GetActiveIndexes();

        if (string.IsNullOrWhiteSpace(query) || indexes.Count == 0)
        {
            SetResultRows(null);
            EmptyHint.Visibility = Visibility.Collapsed;
            UpdateFooterSummary();
            return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        long mySeq = ++_searchSequence;

        try
        {
            var sw = Stopwatch.StartNew();

            var hits = await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                lock (_multi)
                {
                    return _engine.Search(indexes, query, maxResults: MaxDisplayResults);
                }
            }, cts.Token);

            if (mySeq != _searchSequence) return;

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

            var sortedRows = await Task.Run(() => SortRows(rows), cts.Token);

            if (mySeq != _searchSequence) return;

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

            var prevSelectedPaths = new HashSet<string>(
                ResultsList.SelectedItems.OfType<SearchResultRow>().Select(r => r.Path),
                StringComparer.OrdinalIgnoreCase);

            sw.Stop();
            SetResultRows(sortedRows);
            EmptyHint.Visibility = sortedRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _lastSearchQuery = query;

            if (prevSelectedPaths.Count > 0)
            {
                ResultsList.SelectedItems.Clear();
                foreach (var r in sortedRows)
                {
                    if (prevSelectedPaths.Contains(r.Path))
                        ResultsList.SelectedItems.Add(r);
                }
            }

            if (sortedRows.Count > 0 && !string.IsNullOrWhiteSpace(query))
                AddToHistory(query);

            _preloadResumeTimer.Stop();
            _preloadResumeTimer.Start();

            if (!isAuto)
                _lastSearchMs = sw.ElapsedMilliseconds;

            if (!_scrollBarSizerAttached)
            {
                _scrollBarSizerAttached = true;
                _ = Dispatcher.BeginInvoke(new Action(AttachScrollBarSizer),
                        System.Windows.Threading.DispatcherPriority.Loaded);
            }

            if (!isAuto || DateTime.UtcNow >= _footerNoticeUntil)
            {
                FooterText.Text = sortedRows.Count >= MaxDisplayResults
                    ? $"{sortedRows.Count:N0}+ {Loc.T("status.results")} ({_lastSearchMs}ms)"
                    : $"{sortedRows.Count:N0} {Loc.T("status.results")} ({_lastSearchMs}ms)";
            }

            _footerBeforeSelection = null; 
        }
        catch (OperationCanceledException)
        { }
    }

    private void RunPinnedSearch(int index)
    {
        var p = _settings.PinnedSearches;
        if (index < 0 || index >= p.Count) return; 

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

    private async void HeaderButton_Click(object sender, RoutedEventArgs e)
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

        bool sameColumnToggle = (newCol == _sortColumn); 

        if (sameColumnToggle)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = newCol;
            _sortAscending = true;
        }

        _settings.SortColumn = _sortColumn.ToString();
        _settings.SortAscending = _sortAscending;
        _settings.Save();

        if (ResultsList.ItemsSource is List<SearchResultRow> currentRows)
        {
            if (sameColumnToggle)
            {
                var reversed = new List<SearchResultRow>(currentRows.Count);
                for (int i = currentRows.Count - 1; i >= 0; i--)
                    reversed.Add(currentRows[i]);
                SetResultRows(reversed);
            }
            else
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                try
                {
                    var sorted = await Task.Run(() => SortRows(currentRows));
                    SetResultRows(sorted);
                }
                finally
                {
                    System.Windows.Input.Mouse.OverrideCursor = null;
                }
            }
        }
    }

    private void ResultsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer sv) return;

        double w = sv.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 11 : 0;
        var lastCol = HeaderGrid.ColumnDefinitions[^1];
        if (lastCol.Width.Value != w)
            lastCol.Width = new GridLength(w);
    }

    private const double MinColumnWidth = 30;

    private double _lastDragMouseX = -1;

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

    private void ApplySavedColumnWidths()
    {
        bool allValid =
            _settings.ColWidthDrive >= MinWidthOf("ColDrive") &&
            _settings.ColWidthName  >= MinWidthOf("ColName")  &&
            _settings.ColWidthPath  >= MinWidthOf("ColPath")  &&
            _settings.ColWidthSize  >= MinWidthOf("ColSize")  &&
            _settings.ColWidthDate  >= MinWidthOf("ColDate");

        if (!allValid) return;   

        Cols.SetDrive(_settings.ColWidthDrive);
        Cols.SetName(_settings.ColWidthName);
        Cols.SetPath(_settings.ColWidthPath);
        Cols.SetSize(_settings.ColWidthSize);
        Cols.SetDate(_settings.ColWidthDate);
    }

    private void RestoreWindowPlacement()
    {
        var s = _settings;
        if (s.WinWidth < 100 || s.WinHeight < 100) return;

        Width  = Math.Max(MinWidth,  s.WinWidth);
        Height = Math.Max(MinHeight, s.WinHeight);

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

        var titleRect = new System.Drawing.Rectangle(
            (int)(s.WinLeft * sx), (int)(s.WinTop * sy),
            (int)(Width * sx), (int)(30 * sy));

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

        if (s.WinMaximized)
            WindowState = WindowState.Maximized;
    }

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

    private double HeaderActualWidth(int index)
    {
        if (index < HeaderGrid.ColumnDefinitions.Count)
            return HeaderGrid.ColumnDefinitions[index].ActualWidth;
        return 0;
    }

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

    public void BringToFront()
    {
        Dispatcher.Invoke(() =>
        {
            if (_trayIcon is not null)
            {
                _trayIcon.ShowWindow();   
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

    private void FocusSearchCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void ClearAndFocusCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ResultsList.SelectedItems.Count > 0)
        {
            ClearResultSelectionFast();
            return;
        }

        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void OpenCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        OpenSelected();
    }

    private void PropertiesCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var row = GetSelectedRow();
        if (row is null) return;
        try { ShowFileProperties(row.Path); }
        catch (Exception ex) { FooterText.Text = $"속성 보기 실패: {ex.Message}"; }
    }

    private void CopyFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        MenuCopyFile_Click(sender, e);
    }

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

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (HistoryPopup.IsOpen)
            {
                HistoryPopup.IsOpen = false;
            }
            else if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                ClearResultSelectionFast();   
                SearchBox.Clear();
                RunSearch();          
            }
            else
            {
                _trayIcon?.HideWindow();
            }
            e.Handled = true;
            return;
        }

        if (HistoryPopup.IsOpen && HistoryList.Items.Count > 0 &&
            (e.Key == Key.Down || e.Key == Key.Up))
        {
            if (e.Key == Key.Down)
            {
                int next = HistoryList.SelectedIndex < 0
                    ? 0
                    : Math.Min(HistoryList.SelectedIndex + 1, HistoryList.Items.Count - 1);
                _historyKeyboardNav = true;
                HistoryList.SelectedIndex = next;
                if (HistoryList.SelectedItem is not null)
                    HistoryList.ScrollIntoView(HistoryList.SelectedItem);
            }
            else 
            {
                if (HistoryList.SelectedIndex <= 0)
                {
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

        if (e.Key == Key.Enter)
        {
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

        private void ResultsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        bool bulk = ResultsList.SelectedItems.Count > 2000;

        if (IsClickOnEmptySpace(e))
        {
            ClearResultSelectionFast();
            _dragArmed = false;

            if (bulk)
            {
                e.Handled = true;
                return;
            }
        }
        else
        {
            bool plainClick = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0;
            if (plainClick && bulk)
            {
                ClearResultSelectionFast();

                e.Handled = true;
                return;
            }

            _dragStart = e.GetPosition(null);
            _dragArmed = true;
        }
    }

    private void ResultsList_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;  

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
        catch { }
    }

    private string? _footerBeforeSelection;

    private DateTime _footerNoticeUntil = DateTime.MinValue;

    private void ShowFooterNotice(string text)
    {
        FooterText.Text = text;
        _footerBeforeSelection = null;                       
        //_footerNoticeUntil = DateTime.UtcNow.AddSeconds(6);  
        _footerNoticeUntil = DateTime.MaxValue;  
    }

    private bool _historySizerAttached;

    private void AttachHistoryScrollBarSizer()
    {
        if (_historySizerAttached) return;
        try
        {
            HistoryList.ApplyTemplate();
            var sv = FindVisualChild<ScrollViewer>(HistoryList);
            if (sv is null) return;
            sv.ApplyTemplate();
            if (sv.Template.FindName("PART_VerticalScrollBar", sv) is System.Windows.Controls.Primitives.ScrollBar vbar)
            {
                ScrollBarThumbSizer.Attach(vbar, sv);
                _historySizerAttached = true;
            }
        }
        catch { }
    }

    private void AttachScrollBarSizer()
    {
        try
        {
            var sv = GetResultsScrollViewer();
            if (sv is null) return;
            sv.ApplyTemplate();
            if (sv.Template.FindName("PART_VerticalScrollBar", sv) is System.Windows.Controls.Primitives.ScrollBar vbar)
                ScrollBarThumbSizer.Attach(vbar, sv);
        }
        catch { }
    }

    private ScrollViewer? GetResultsScrollViewer()
    {
        try
        {
            if (System.Windows.Media.VisualTreeHelper.GetChildrenCount(ResultsList) == 0) return null;
            var deco = System.Windows.Media.VisualTreeHelper.GetChild(ResultsList, 0) as Decorator;
            return deco?.Child as ScrollViewer;
        }
        catch { return null; }
    }

    private void SetResultRows(List<SearchResultRow>? rows)
    {
        bool many = ResultsList.SelectedItems.Count > 2000;
        _lastRows = rows;

        _selectionSummaryTimer?.Stop();
        _suppressSelectionSummary = true;
        try
        {
            if (many) ResultsList.ItemsSource = null;
            ResultsList.ItemsSource = rows;
        }
        finally
        {
            _suppressSelectionSummary = false;
        }

        UpdateSelectionSummary();
    }

        private void ClearResultSelectionFast()
    {

        if (System.Windows.Input.Mouse.Captured is not null)
            System.Windows.Input.Mouse.Capture(null);

        if (ResultsList.SelectedItems.Count == 0) return;

        int n = ResultsList.SelectedItems.Count;

        _selectionSummaryTimer?.Stop();
        _suppressSelectionSummary = true;

        try
        {
            if (n > 2000 && ResultsList.ItemsSource is List<SearchResultRow> rows)
            {
                var sv = GetResultsScrollViewer();
                double offset = sv?.VerticalOffset ?? 0;

                ResultsList.ItemsSource = null;
                ResultsList.ItemsSource = rows;

                if (sv is not null && offset > 0)
                    _ = Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(offset)),
                            System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                ResultsList.UnselectAll();
            }
        }
        finally
        {
            _suppressSelectionSummary = false;
        }

        UpdateSelectionSummary();

        _runawayCount = 0;
    }

    private bool _suppressSelectionSummary;

    private const int SelectionSummarySizeLimit = 20_000;

    private System.Windows.Threading.DispatcherTimer? _selectionSummaryTimer;

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 1 && e.RemovedItems.Count == 0)
        {
            if (_runawayCount == 0) _runawayStart = DateTime.UtcNow;
            _runawayCount++;

            if (_runawayCount > 60 && (DateTime.UtcNow - _runawayStart).TotalSeconds < 3)
            {
                _runawayCount = 0;

                if (System.Windows.Input.Mouse.Captured is not null)
                    System.Windows.Input.Mouse.Capture(null);

                ResultsList.ItemsSource = null;
                ResultsList.ItemsSource = _lastRows;
                return;
            }
        }
        else
        {
            _runawayCount = 0;
        }

        if (_suppressSelectionSummary) return;

        if (_selectionSummaryTimer is null)
        {
            _selectionSummaryTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _selectionSummaryTimer.Tick += (_, _) =>
            {
                _selectionSummaryTimer!.Stop();
                UpdateSelectionSummary();
            };
        }

        _selectionSummaryTimer.Stop();
        _selectionSummaryTimer.Start();
    }

    private void UpdateSelectionSummary()
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

        _footerBeforeSelection ??= FooterText.Text;

        if (n > SelectionSummarySizeLimit)
        {
            FooterText.Text = string.Format(Loc.T("status.selectedMany"), n);
            return;
        }

        long total = 0;
        foreach (var it in ResultsList.SelectedItems)
            if (it is SearchResultRow r) total += r.SizeBytes;

        FooterText.Text = string.Format(Loc.T("status.selected"),
            n, HdrTracer.Core.FileInfoFetcher.FormatSize(total));
    }

    private void ResultsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var pos = Mouse.GetPosition(ResultsList);
        var hit = ResultsList.InputHitTest(pos) as DependencyObject;
        while (hit != null && hit is not ListViewItem)
        {
            if (hit == ResultsList) break;
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        }
        if (hit is not ListViewItem)
        {
            e.Handled = true;
        }
    }

    private bool IsClickOnEmptySpace(MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not ListViewItem)
        {
            if (dep == ResultsList) return true;
            dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
        }
        return dep is not ListViewItem;
    }

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearResultSelectionFast();
            SearchBox.Focus();
            e.Handled = true;
            return;
        }

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

        if (e.Key == Key.F2)
        {
            if (ResultsList.SelectedItems.Count > 0)
            {
                MenuRename_Click(sender, e);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.N
            && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            MenuCopyName_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0)
                return;

            int count = ResultsList.Items.Count;
            if (count == 0) return;

            int cur = ResultsList.SelectedIndex;

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

    private SearchResultRow? GetSelectedRow()
    {
        return ResultsList.SelectedItem as SearchResultRow;
    }

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

        if (rows.Count == 1)
        {
            OpenSelected();
            return;
        }

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

        if (ok > 0 && !string.IsNullOrWhiteSpace(SearchBox.Text))
            AddToHistory(SearchBox.Text);

        ShowFooterNotice(fail == 0
            ? string.Format(Loc.T("ctx.open.multi"), ok)
            : string.Format(Loc.T("ctx.open.partial"), ok, fail));
    }

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

    private void ResultsContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = true;
        var row = GetSelectedRow();
        CtxRunAsAdmin.IsEnabled = row is not null && IsExecutablePath(row.Path);
    }

    private void ResultsContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = false;
        if (!string.IsNullOrEmpty(_lastSearchQuery))
            _indexChangedDebounce.Start();
    }

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
                Verb = "runas"
            };
            Process.Start(psi);
            FooterText.Text = $"{Loc.T("ctx.runAsAdmin")}: {row.Name}";

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                AddToHistory(SearchBox.Text);
        }
        catch (System.ComponentModel.Win32Exception wex) when (wex.NativeErrorCode == 1223)
        {
            
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Loc.T("ctx.error")}: {ex.Message}",
                Loc.T("ctx.error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var info = new OPENASINFO
            {
                pcszFile  = row.Path,
                pcszClass = null,
                oaifInFlags = OAIF.OAIF_EXEC | OAIF.OAIF_ALLOW_REGISTRATION
            };
            int hr = SHOpenWithDialog(helper.Handle, ref info);
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
                UseShellExecute = true 
            };
            Process.Start(psi);

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

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    private static void RevealInExplorer(string path)
    {
        bool isDirectory = Directory.Exists(path);
        if (isDirectory && string.IsNullOrEmpty(Path.GetDirectoryName(path)))
        {
            OpenInExplorer(path);
            return;
        }

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
        catch { }
    }

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

        var kept = new List<string>();
        foreach (var t in SplitQueryTokens(SearchBox.Text))
            if (!t.Contains('\\') && !t.Contains('/')) kept.Add(t);
        kept.Add(filter);

        SearchBox.Text = string.Join(" ", kept);
        RunSearch();
    }

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

    private void ExportAllResults()
    {
        if (ResultsList.ItemsSource is List<SearchResultRow> rows && rows.Count > 0)
            ExportRowsToCsv(rows);
    }

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
        catch { }
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

            bool isDir = System.IO.Directory.Exists(row.Path);
            string? newName = PromptForText(
                Loc.T("ctx.rename.title"),
                Loc.T("ctx.rename.prompt"),
                oldName,
                selectExtension: isDir);
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

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

        if (dangerous.Count > 0)
            confirmMsg = Loc.T("ctx.delete.danger") + "\n\n" + confirmMsg;

        if (!ConfirmDialog.Show(this, Loc.T("ctx.delete.title"), confirmMsg))
            return;

        var report = RobustDelete.Run(this, rows.Select(r => r.Path).ToList(), IsDangerousPath);

        ShowFooterNotice(DeleteNotice.Build(report, rows.Count == 1 ? rows[0].Name : null));

        if (report.FailCount > 0)
            RobustDelete.ShowFailureReport(this, report);

        if (ResultsList.ItemsSource is List<SearchResultRow> shown && report.DeletedPaths.Count > 0)
        {
            foreach (var p in report.DeletedPaths) _recentlyDeletedPaths.Add(p);
            var remaining = shown.Where(r => !report.DeletedPaths.Contains(r.Path)).ToList();
            SetResultRows(remaining);
        }
    }

    private static bool IsDangerousPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string p = path.TrimEnd('\\', '/');

        if (p.Length <= 2 && p.Length >= 1 && p.EndsWith(":")) return true;
        if (p.Length == 2 && char.IsLetter(p[0]) && p[1] == ':') return true;

        string win  = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
        string pf   = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\');
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\');
        string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).TrimEnd('\\');
        string users = Path.Combine(Path.GetPathRoot(win) ?? "C:\\", "Users").TrimEnd('\\');
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');

        if (StartsWithDir(p, win) || StartsWithDir(p, pf) || StartsWithDir(p, pf86)
            || StartsWithDir(p, progData)) return true;

        if (p.Equals(users, StringComparison.OrdinalIgnoreCase)) return true;
        if (p.Equals(userHome, StringComparison.OrdinalIgnoreCase)) return true;

        return false;

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
        info.nShow = 1;       
        info.fMask = SEE_MASK_INVOKEIDLIST;
        ShellExecuteEx(ref info);
    }

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

            if (meta.JournalId == 0) continue;

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
                _settings.LastSearchQuery = SearchBox.Text;
                _settings.Save();
            }
        }
        catch { }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

        if (WindowState == WindowState.Maximized)
        {
            var wa = SystemParameters.WorkArea;
            MaxHeight = wa.Height + 2; 
            MaxWidth  = wa.Width + 2;
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth  = double.PositiveInfinity;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshResultListLayout();
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void RefreshResultListLayout()
    {
        var src = ResultsList.ItemsSource;
        if (src is null) return;

        double offset = 0;
        var sv = FindVisualChild<System.Windows.Controls.ScrollViewer>(ResultsList);
        if (sv is not null) offset = sv.VerticalOffset; 

        ResultsList.ItemsSource = null;
        ResultsList.ItemsSource = src;
        ResultsList.UpdateLayout();

        if (sv is not null) sv.ScrollToVerticalOffset(offset);
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

        var settingsItem = new MenuItem
        {
            Header = Loc.T("menu.settings"),
            InputGestureText = "Ctrl+,"
        };
        settingsItem.Click += (_, _) => SettingsButton_Click(sender, e);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var rebuildItem = new MenuItem
        {
            Header = Loc.T("menu.refresh"),
            InputGestureText = "F5"
        };
        rebuildItem.Click += async (_, _) => await RebuildIndex();
        menu.Items.Add(rebuildItem);

        var resetColsItem = new MenuItem { Header = Loc.T("menu.resetCols") };
        resetColsItem.Click += (_, _) => ResetColumnWidths();
        menu.Items.Add(resetColsItem);

        menu.Items.Add(new Separator());

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

        var kindItem = new MenuItem { Header = Loc.T("filter.kind") };
        void AddKind(string locKey, string? token)
        {
            var mi = new MenuItem { Header = Loc.T(locKey) };
            mi.Click += (_, _) => ApplyKindFilter(token);
            kindItem.Items.Add(mi);
        }
        AddKind("filter.kind.folder", "folder:");
        AddKind("filter.kind.file",   "file:");
        kindItem.Items.Add(new Separator());
        AddKind("filter.clear", null);
        filterItem.Items.Add(kindItem);

        menu.Items.Add(filterItem);

        menu.Items.Add(new Separator());

        var shortcutItem = new MenuItem { Header = Loc.T("menu.shortcuts") };
        shortcutItem.Click += (_, _) => ShowShortcuts();
        menu.Items.Add(shortcutItem);

        menu.Items.Add(new Separator());

        var langItem = new MenuItem { Header = Loc.T("menu.language") };

        foreach (var lang in Loc.SupportedLanguages)
        {
            var target = lang;

            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = Loc.Current == target ? "\u2713" : "",   
                Width = 18,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = Loc.T(Loc.NameKey(target)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var item = new MenuItem { Header = row };
            item.Click += (_, _) => ChangeLanguage(target);
            langItem.Items.Add(item);
        }

        menu.Items.Add(langItem);

        menu.Items.Add(new Separator());

        var searchHelpItem = new MenuItem { Header = Loc.T("menu.searchHelp") };
        searchHelpItem.Click += (_, _) => InfoDialog.Show(this, Loc.T("help.search.title"), Loc.T("help.search.body"));
        menu.Items.Add(searchHelpItem);

        var exportItem = new MenuItem
        {
            Header = Loc.T("menu.export"),
            IsEnabled = ResultsList.ItemsSource is List<SearchResultRow> { Count: > 0 }
        };
        exportItem.Click += (_, _) => ExportAllResults();
        menu.Items.Add(exportItem);

        var aboutItem = new MenuItem { Header = Loc.T("menu.about") };
        aboutItem.Click += (_, _) => ShowAbout();
        menu.Items.Add(aboutItem);

        menu.Items.Add(new Separator());

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
        _settings.Language = Loc.ToCode(lang);
        _settings.Save();

        ApplyLocalizedTexts();
    }

    private void ApplyLocalizedTexts()
    {
        HdrDrive.Content = Loc.T("col.drive");
        HdrName.Content  = Loc.T("col.name");
        HdrPath.Content  = Loc.T("col.path");
        HdrSize.Content  = Loc.T("col.size");
        HdrDate.Content  = Loc.T("col.date");

        MenuDropdownButton.ToolTip = Loc.T("tip.menu");
        MinimizeButton.ToolTip     = Loc.T("tip.minimize");
        MaximizeButton.ToolTip     = Loc.T("tip.maximize");
        CloseButton.ToolTip        = Loc.T("tip.close");
        SearchButton.ToolTip       = Loc.T("tip.search");
        
        Resources["TipDelete"]     = Loc.T("tip.delete");
        Resources["TipPin"]        = Loc.T("tip.pin");

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

        UpdateFooterSummary();
    }

    private async Task RebuildIndex()
    {
        if (!ConfirmDialog.Show(this, Loc.T("refresh.confirm.title"), Loc.T("refresh.confirm.msg")))
            return;

        try
        {
            _searchCts?.Cancel();
            SearchBox.Clear();
            SetResultRows(null);
            SearchBox.IsEnabled = false;
            StatusBanner.Visibility = Visibility.Visible;
            StatusText.Text = Loc.T("status.indexing");

            await Task.Yield();

            await Task.Run(() =>
            {
                foreach (var p in _preloaders) p.Stop();

                _multi.DisposeAll();

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

            var drives = DriveDetector.GetIndexableDrives(_settings.IndexRemovableDrives);
            foreach (var letter in drives)
            {
                _multi.AddSlot(new MultiDriveIndex.DriveSlot { DriveLetter = letter });
            }
            UpdateFooterSummary();

            var sw = Stopwatch.StartNew();
            StartIndexingProgress();   
            var tasks = _multi.Slots.Select(slot => Task.Run(() => BuildOneDrive(slot))).ToArray();
            await Task.WhenAll(tasks);
            StopIndexingProgress();
            sw.Stop();

            foreach (var slot in _multi.Slots)
            {
                StartMonitorIfReady(slot);
                StartMetadataPreloader(slot);
            }

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

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        var result = dlg.ShowDialog();
        if (result == true)
        {
            if (dlg.HotkeyChanged)
            {
                if (_settings.GlobalHotkeyEnabled) RegisterGlobalHotkey();
                else UnregisterGlobalHotkey();
            }

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

            if (!string.IsNullOrEmpty(SearchBox.Text))
                RunSearch();
        }
        else
        {
            var slots = _multi.Slots;
            foreach (var slot in slots)
            {
                if (DriveDetector.IsRemovable(slot.DriveLetter))
                {
                    _multi.RemoveDrive(slot.DriveLetter);
                }
            }
            UpdateFooterSummary();

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
                ResolvePath(); 
                _icon = IconCache.GetIcon(_path!, Kind == "폴더");
            }
            return _icon;
        }
    }

    private bool _nameResolved;
    private void ResolveName()
    {
        if (_nameResolved) return;
        _nameResolved = true;
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

public sealed class HistoryItem
{
    public string Query { get; init; } = "";
    public bool IsPinned { get; init; }
    public string Display  => (IsPinned ? "\uD83D\uDCCC " : "") + Query;   
    public string PinGlyph => IsPinned ? "\uE77A" : "\uE718";              
}
