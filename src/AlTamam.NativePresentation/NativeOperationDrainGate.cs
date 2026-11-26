namespace AlTamam.NativePresentation;

/// <summary>
/// Host-owned admission fence for native database operations. Once stopped it
/// never reopens: a restored database must be opened by a fresh process.
/// The caller must not await the drain from inside an admitted operation.
/// </summary>
public sealed class NativeOperationDrainGate
{
    private readonly object _sync=new();
    private readonly TaskCompletionSource _drained=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _stopping;

    public IDisposable Enter()
    {
        lock(_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping,this);
            ++_active;
            return new Lease(this);
        }
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        using var lease=Enter();
        return await action().ConfigureAwait(false);
    }

    public async Task RunAsync(Func<Task> action)
    {
        using var lease=Enter();
        await action().ConfigureAwait(false);
    }

    public Task StopAndDrainAsync()
    {
        lock(_sync)
        {
            _stopping=true;
            if(_active==0) _drained.TrySetResult();
            return _drained.Task;
        }
    }

    private void Leave()
    {
        lock(_sync)
        {
            --_active;
            if(_stopping && _active==0) _drained.TrySetResult();
        }
    }

    private sealed class Lease(NativeOperationDrainGate gate):IDisposable
    {
        private NativeOperationDrainGate? _gate=gate;
        public void Dispose()=>Interlocked.Exchange(ref _gate,null)?.Leave();
    }
}
