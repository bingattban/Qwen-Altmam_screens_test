using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeOperationDrainPhase12Tests
{
    [Fact]
    public async Task Drain_waits_for_all_existing_operations_and_denies_new_ones()
    {
        var gate=new NativeOperationDrainGate();
        var first=gate.Enter();
        var second=gate.Enter();
        Task drain=gate.StopAndDrainAsync();
        Assert.False(drain.IsCompleted);
        Assert.Throws<ObjectDisposedException>(()=>gate.Enter());
        first.Dispose();
        first.Dispose();
        Assert.False(drain.IsCompleted);
        second.Dispose();
        await drain;
        Assert.True(drain.IsCompletedSuccessfully);
        Assert.Throws<ObjectDisposedException>(()=>gate.Enter());
    }

    [Fact]
    public async Task Concurrent_stop_callers_share_completion()
    {
        var gate=new NativeOperationDrainGate();
        using var lease=gate.Enter();
        Task first=gate.StopAndDrainAsync();
        Task second=gate.StopAndDrainAsync();
        Assert.Same(first,second);
        Assert.False(first.IsCompleted);
        lease.Dispose();
        await Task.WhenAll(first,second);
    }

    [Fact]
    public async Task Async_wrapper_keeps_lease_until_completion_and_releases_on_failure()
    {
        var gate=new NativeOperationDrainGate();
        var pending=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running=gate.RunAsync(()=>pending.Task);
        Task drain=gate.StopAndDrainAsync();
        Assert.False(drain.IsCompleted);
        pending.SetException(new InvalidOperationException("Operation failed."));
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>{ await running; });
        await drain;
        await Assert.ThrowsAsync<ObjectDisposedException>(async()=>{ await gate.RunAsync(()=>Task.FromResult(1)); });
    }

    [Fact]
    public async Task Idle_gate_stops_immediately_and_cancellation_does_not_leak()
    {
        var gate=new NativeOperationDrainGate();
        using var canceled=new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>
        {
            await gate.RunAsync(()=>Task.FromCanceled<int>(canceled.Token));
        });
        Assert.True(gate.StopAndDrainAsync().IsCompletedSuccessfully);
    }
}
