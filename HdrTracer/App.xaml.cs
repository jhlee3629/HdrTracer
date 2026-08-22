using System.Threading;
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;
using ExitEventArgs = System.Windows.ExitEventArgs;

namespace HdrTracer.App;

public partial class App : Application
{
    private const string MutexName  = "HdrTracer_SingleInstance_Mutex_8B5F3A2C";
    private const string SignalName = "HdrTracer_SingleInstance_Signal_8B5F3A2C";

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private Thread? _signalThread;
    private bool _isFirstInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out _isFirstInstance);
        }
        catch
        {
            _isFirstInstance = true;
            _mutex = null;
        }

        if (!_isFirstInstance)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(SignalName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { }

            Shutdown();
            return;
        }

        base.OnStartup(e);

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
                _signal.WaitOne();   
            }
            catch
            {
                break;
            }

            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (Current?.MainWindow is HdrTracer.App.MainWindow mw)
                        mw.BringToFront();
                });
            }
            catch { }
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
