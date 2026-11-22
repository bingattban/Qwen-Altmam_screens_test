namespace AlTamam.NativePresentation;

/// <summary>
/// The real scheduler is host-owned and starts once per process. The host
/// awaits its termination before disposing SQLCipher or releasing its mutex.
/// Neither the Dashboard timer nor a user login can call RunPendingAsync.
/// </summary>
public interface INativeAutomaticScheduler
{
    void Start();
    ValueTask StopAsync();
}

public sealed class NativeAutomaticSchedulerGate : IAsyncDisposable
{
    private readonly INativeAutomaticScheduler _scheduler;
    private readonly object _sync = new();
    private bool _started, _closed;

    public NativeAutomaticSchedulerGate(INativeAutomaticScheduler scheduler)
        => _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

    public bool IsStarted
    {
        get { lock (_sync) return _started; }
    }

    public void StartOnce()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_started) return;
            _scheduler.Start();
            _started = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_closed) return;
            _closed = true;
        }
        // Stop must be awaited before SQLCipher disposal. Cancellation is
        // implemented in the same durable engine used by DesktopHost.
        await _scheduler.StopAsync().ConfigureAwait(false);
    }
}
