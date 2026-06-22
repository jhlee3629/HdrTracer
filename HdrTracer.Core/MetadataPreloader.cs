namespace HdrTracer.Core;

/// <summary>
/// 인덱스의 모든 항목에 대해 크기/수정날짜를 백그라운드에서 사전 로딩한다.
/// GetFileAttributesEx를 병렬 호출해서 100만 건 기준 ~10초 내에 완료.
/// </summary>
public sealed class MetadataPreloader
{
    private readonly FileIndex _index;
    private CancellationTokenSource? _cts;

    // 이 사전로더가 담당하는 드라이브 (USB 안전 제거 시 해당 드라이브만 멈추기 위함)
    public string DriveLetter { get; }

    public int TotalCount { get; private set; }
    public int LoadedCount;
    public bool IsRunning { get; private set; }
    public bool IsCompleted { get; private set; }

    public event Action<int, int>? Progress;  // (loaded, total)
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

            // 청크 단위로 나누어 병렬 처리
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

                    // 일시정지 중이면 대기
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
                        // 인덱스는 lock 없이 set 가능 (각 항목은 한 스레드만 쓰는 구조)
                        // 단, 동시에 USN monitor가 같은 항목을 건드릴 수 있으니 lock 사용
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