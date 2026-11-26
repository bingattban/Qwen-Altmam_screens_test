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
    private Task? _stopTask;

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

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _closed = true;
            return new ValueTask(_stopTask ??= StopCoreAsync());
        }
    }

    private async Task StopCoreAsync()
    {
        // Every caller observes the same completion or failure. No caller may
        // proceed with database disposal merely because stop was requested.
        await _scheduler.StopAsync().ConfigureAwait(false);
    }
}
