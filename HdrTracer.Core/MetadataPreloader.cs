namespace HdrTracer.Core;

public sealed class MetadataPreloader
{
    private readonly FileIndex _index;
    private CancellationTokenSource? _cts;

    public string DriveLetter { get; }

    public int TotalCount { get; private set; }
    public int LoadedCount;
    public bool IsRunning { get; private set; }
    public bool IsCompleted { get; private set; }

    public event Action<int, int>? Progress;  
    public event Action? Completed;

    private volatile bool _paused;

    public MetadataPreloader(FileIndex index, string driveLetter)
    {
        _index = index;
        DriveLetter = driveLetter;
    }

    public void Start()
    {
        if (IsRunning || IsCompleted) return;
        IsRunning = true;
        TotalCount = _index.Count;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => Run(token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        IsRunning = false;
    }

    private void Run(CancellationToken token)
    {
        try
        {
            int parallelism = Math.Min(8, Environment.ProcessorCount);
            int total = _index.Count;

            const int chunkSize = 2048;
            int chunkCount = (total + chunkSize - 1) / chunkSize;

            Parallel.For(0, chunkCount, new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = token
            }, chunkIdx =>
            {
                int start = chunkIdx * chunkSize;
                int end = Math.Min(start + chunkSize, total);

                int localLoaded = 0;
                for (int i = start; i < end; i++)
                {
                    if (token.IsCancellationRequested) break;

                    while (_paused && !token.IsCancellationRequested)
                        Thread.Sleep(50);
                    if (token.IsCancellationRequested) break;

                    if (_index.IsDeleted(i)) continue;
                    if (_index.HasMetadata(i)) continue;

                    var path = _index.GetFullPath(i);
                    if (string.IsNullOrEmpty(path)) continue;

                    var info = FileInfoFetcher.Get(path);
                    if (info.Found)
                    {
                        lock (_index)
                        {
                            _index.SetMetadata(i, info.Size, info.ModifiedUtc);
                        }
                    }
                    localLoaded++;
                }

                Interlocked.Add(ref LoadedCount, localLoaded);
                Progress?.Invoke(LoadedCount, total);
            });

            IsCompleted = true;
            Completed?.Invoke();
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
        }
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;
}