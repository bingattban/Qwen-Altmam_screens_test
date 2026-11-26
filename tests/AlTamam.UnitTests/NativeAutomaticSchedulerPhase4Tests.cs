using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAutomaticSchedulerPhase4Tests
{
    [Fact]
    public async Task Starting_twice_starts_canonical_scheduler_only_once()
    {
        var scheduler = new Probe();
        await using (var gate = new NativeAutomaticSchedulerGate(scheduler))
        {
            gate.StartOnce();
            gate.StartOnce();
            Assert.Equal(1, scheduler.Started);
        }
        Assert.Equal(1, scheduler.Stopped);
    }

    [Fact]
    public async Task Shutdown_without_start_still_disposes_reserved_scheduler_once()
    {
        var scheduler = new Probe();
        var gate = new NativeAutomaticSchedulerGate(scheduler);
        await gate.DisposeAsync();
        await gate.DisposeAsync();
        Assert.Equal(0, scheduler.Started);
        Assert.Equal(1, scheduler.Stopped);
    }

    [Fact]
    public async Task Shutdown_awaits_scheduler_drain_before_returning()
    {
        var scheduler = new Probe();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.OnStop = () => new ValueTask(pending.Task);
        var gate = new NativeAutomaticSchedulerGate(scheduler);
        gate.StartOnce();
        Task draining = gate.DisposeAsync().AsTask();
        Assert.False(draining.IsCompleted);
        Assert.Equal(1, scheduler.Stopped);
        pending.SetResult();
        await draining;
        Assert.True(draining.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Closed_gate_never_restarts_a_background_job()
    {
        var scheduler = new Probe();
        var gate = new NativeAutomaticSchedulerGate(scheduler);
        await gate.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => gate.StartOnce());
        Assert.Equal(0, scheduler.Started);
        Assert.Equal(1, scheduler.Stopped);
    }

    [Fact]
    public async Task Start_failure_is_not_treated_as_success()
    {
        var scheduler = new Probe { ThrowOnStart = true };
        await using var gate = new NativeAutomaticSchedulerGate(scheduler);
        Assert.Throws<InvalidOperationException>(() => gate.StartOnce());
        Assert.False(gate.IsStarted);
    }

    [Fact]
    public async Task Concurrent_shutdown_callers_share_the_same_drain()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new Probe { OnStop = () => new ValueTask(pending.Task) };
        var gate = new NativeAutomaticSchedulerGate(scheduler);
        gate.StartOnce();
        Task first = gate.DisposeAsync().AsTask();
        Task second = gate.DisposeAsync().AsTask();
        Assert.Same(first, second);
        Assert.False(second.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => gate.StartOnce());
        Assert.Equal(1, scheduler.Stopped);
        pending.SetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Failed_drain_never_reports_success_to_a_later_disposal()
    {
        var scheduler = new Probe
        {
            OnStop = () => new ValueTask(Task.FromException(new InvalidOperationException("Drain failed.")))
        };
        var gate = new NativeAutomaticSchedulerGate(scheduler);
        gate.StartOnce();
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.DisposeAsync().AsTask());
        Assert.Equal(1, scheduler.Stopped);
    }

    private sealed class Probe : INativeAutomaticScheduler
    {
        public int Started;
        public int Stopped;
        public bool ThrowOnStart;
        public Func<ValueTask>? OnStop;

        public void Start()
        {
            if (ThrowOnStart) throw new InvalidOperationException("Canonical scheduler start failed.");
            Started++;
        }

        public ValueTask StopAsync()
        {
            Stopped++;
            return OnStop?.Invoke() ?? ValueTask.CompletedTask;
        }
    }
}
