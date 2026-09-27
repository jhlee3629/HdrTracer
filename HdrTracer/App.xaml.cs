using System.Threading;
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;
using ExitEventArgs = System.Windows.ExitEventArgs;

namespace HdrTracer.App;

public partial class App : Application
{
    private const string MutexName  = "HdrTracer_SingleInstance_Mutex_8B5F3A2C";
    private const string SignalName = "HdrTracer_SingleInstance_Signal_8B5F3A2C";
    private const string AckName    = "HdrTracer_SingleInstance_Ack_8B5F3A2C";

    private const int AckTimeoutMs = 2500;

    private Mutex? _mutex;
    private EventWaitHandle? _signal;
    private EventWaitHandle? _ack;
    private Thread? _signalThread;
    private bool _isFirstInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: false, MutexName, out _isFirstInstance);
        }
        catch
        {
            _isFirstInstance = true;
            _mutex = null;
        }

        if (!_isFirstInstance && TrySignalRunningInstance())
        {
            Shutdown();
            return;
        }

        _isFirstInstance = true;

        base.OnStartup(e);

        try
        {
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            _ack = new EventWaitHandle(false, EventResetMode.AutoReset, AckName);
            _signalThread = new Thread(SignalWaitLoop) { IsBackground = true, Name = "SingleInstanceSignal" };
            _signalThread.Start();
        }
        catch
        {
            _signal = null;
            _ack = null;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static bool TrySignalRunningInstance()
    {
        EventWaitHandle? signal = null;
        EventWaitHandle? ack = null;
        try
        {
            if (!EventWaitHandle.TryOpenExisting(SignalName, out signal)) return false;

            EventWaitHandle.TryOpenExisting(AckName, out ack);
            if (ack is null) return false;

            ack.Reset();
            signal.Set();
            return ack.WaitOne(AckTimeoutMs);
        }
        catch
        {
            return false;
        }
        finally
        {
            try { signal?.Dispose(); } catch { }
            try { ack?.Dispose(); } catch { }
        }
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
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (Current?.MainWindow is HdrTracer.App.MainWindow mw)
                            mw.BringToFront();
                    }
                    finally
                    {
                        try { _ack?.Set(); } catch { }
                    }
                }));
            }
            catch { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _signal?.Dispose(); } catch { }
        try { _ack?.Dispose(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
