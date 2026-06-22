using System.Threading;
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;
using ExitEventArgs = System.Windows.ExitEventArgs;

namespace HdrTracer.App;

public partial class App : Application
{
    // 앱 고유 식별자 (다른 프로그램과 충돌하지 않도록 GUID 사용)
    private const string MutexName  = "HdrTracer_SingleInstance_Mutex_8B5F3A2C";
    private const string SignalName = "HdrTracer_SingleInstance_Signal_8B5F3A2C";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private Thread? _signalThread;
    private bool _isFirstInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 단일 인스턴스 확인 — 실패하더라도 앱 실행은 막지 않는다.
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out _isFirstInstance);
        }
        catch
        {
            // Mutex 생성/접근 실패(권한 등) → 단일 인스턴스 기능만 포기하고 정상 실행
            _isFirstInstance = true;
            _mutex = null;
        }

        if (!_isFirstInstance)
        {
            // === 두 번째 인스턴스 ===
            // 기존 인스턴스에 "창을 띄워라" 신호를 보내고 즉시 종료
            try
            {
                if (EventWaitHandle.TryOpenExisting(SignalName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { /* 무시 */ }

            Shutdown();   // 이 인스턴스는 바로 종료
            return;
        }

        // === 첫 번째 인스턴스 ===
        base.OnStartup(e);

        // 신호 대기용 이벤트 생성 (실패해도 앱은 계속 — 단일 인스턴스 알림만 비활성)
        try
        {
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            _signalThread = new Thread(SignalWaitLoop) { IsBackground = true, Name = "SingleInstanceSignal" };
            _signalThread.Start();
        }
        catch
        {
            _signal = null;
        }

        // 메인 윈도우 생성 (StartupUri를 제거했으므로 코드로 띄움)
        // 이 부분은 어떤 경우에도 실행되어 창이 반드시 뜨도록 보장.
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void SignalWaitLoop()
    {
        if (_signal is null) return;

        while (true)
        {
            try
            {
                _signal.WaitOne();   // 두 번째 인스턴스가 Set()하면 깨어남
            }
            catch
            {
                break;
            }

            // 메인 윈도우를 앞으로 가져오기
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (Current?.MainWindow is HdrTracer.App.MainWindow mw)
                        mw.BringToFront();
                });
            }
            catch { /* 종료 중일 수 있음 */ }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _signal?.Dispose(); } catch { }
        try
        {
            if (_isFirstInstance) _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
