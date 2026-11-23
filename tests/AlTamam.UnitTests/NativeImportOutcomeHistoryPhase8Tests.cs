using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 8 slice 15: test-first, read-only evidence from original
/// ExcelImportOutcomeService. No alternative SQL, export, or mutation.
/// </summary>
public sealed class NativeImportOutcomeHistoryPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-OUTCOME", "admin", UserRole.GeneralAdministration, "NONCE-OUTCOME");
    private static NativeImportBatchRow Batch(string id = "IMP-001") =>
        new(id, "كشف_القوة.xlsx", 3, 2, 1, "COMMITTED", "2026-11-23T18:00:00Z");
    private static NativeImportBatchIssue Issue() =>
        new(4, "00012", "اسم مرفوض", "الموقع الأول", "رقم مكرر");
    private static NativeImportBatchDetail Detail() => new(Batch(), [Issue()]);
    private static NativeImportOutcomeHistoryViewModel Make(FakeBackend backend,
        Func<NativeIdentity?> principal)
    {
        var vm = new NativeImportOutcomeHistoryViewModel();
        vm.AttachBackend(backend, principal);
        return vm;
    }

    [Fact]
    public async Task Anonymous_and_supervisor_are_denied_without_any_sql_read()
    {
        NativeIdentity? actor = null;
        var backend = new FakeBackend();
        var vm = Make(backend, () => actor);
        await vm.OpenAsync();
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Batches);
        Assert.Equal(0, backend.ListCalls + backend.DetailCalls);
    }

    [Fact]
    public async Task Manager_reads_only_bounded_committed_batches_and_never_writes()
    {
        var backend = new FakeBackend();
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        Assert.True(vm.IsOpen);
        Assert.Equal("IMP-001", Assert.Single(vm.Batches).ImportBatchId);
        Assert.Equal(2, vm.Batches[0].AcceptedRows);
        Assert.Equal(1, backend.ListCalls);
        Assert.Equal(0, backend.DetailCalls);
    }

    [Fact]
    public async Task Selected_original_batch_exposes_all_rejected_evidence()
    {
        var backend = new FakeBackend();
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        Assert.Equal("IMP-001", vm.SelectedDetail?.Batch.ImportBatchId);
        Assert.Equal("رقم مكرر", Assert.Single(vm.SelectedDetail!.Issues).Reason);
        Assert.Equal(1, backend.DetailCalls);
        Assert.Empty(vm.DetailError);
    }

    [Fact]
    public async Task Forged_batch_or_mismatched_issue_count_fails_closed()
    {
        var backend = new FakeBackend { ForgedDetail = true };
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        Assert.Null(vm.SelectedDetail);
        Assert.NotEmpty(vm.DetailError);
        int calls = backend.DetailCalls;
        await vm.OpenDetailsAsync(Batch() with { ImportBatchId = "IMP-FORGED" });
        Assert.Equal(calls, backend.DetailCalls);
        Assert.Null(vm.SelectedDetail);
    }

    [Fact]
    public async Task Overlong_or_duplicate_history_is_hidden_not_partially_rendered()
    {
        var backend = new FakeBackend { InvalidList = true };
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        Assert.Empty(vm.Batches);
        Assert.NotEmpty(vm.LoadError);
        Assert.False(vm.HasDetails);
    }

    [Fact]
    public async Task Logout_cancels_pending_history_and_discards_late_replies()
    {
        NativeIdentity? actor = Manager;
        var backend = new FakeBackend();
        var pending = new TaskCompletionSource<IReadOnlyList<NativeImportBatchRow>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.DelayedList = pending.Task;
        var vm = Make(backend, () => actor);
        Task loading = vm.OpenAsync();
        actor = Manager with { SessionNonce = "REPLACED" };
        vm.Close();
        pending.SetResult([Batch()]);
        await loading;
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Batches);
        Assert.Null(vm.SelectedDetail);
    }

    [Fact]
    public async Task Refresh_and_close_revoke_old_detail_and_pending_result()
    {
        var backend = new FakeBackend();
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        var original = vm.Batches[0];
        await vm.OpenDetailsAsync(original);
        Assert.True(vm.HasDetails);
        await vm.RefreshAsync();
        Assert.False(vm.HasDetails);
        var delayed = new TaskCompletionSource<NativeImportBatchDetail>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.DelayedDetail = delayed.Task;
        Task opening = vm.OpenDetailsAsync(vm.Batches[0]);
        vm.Close();
        delayed.SetResult(Detail());
        await opening;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.SelectedDetail);
        Assert.Empty(vm.Batches);
    }

    private sealed class FakeBackend : INativeImportOutcomeHistoryBackend
    {
        public int ListCalls, DetailCalls;
        public bool InvalidList, ForgedDetail;
        public Task<IReadOnlyList<NativeImportBatchRow>>? DelayedList;
        public Task<NativeImportBatchDetail>? DelayedDetail;
        public Task<IReadOnlyList<NativeImportBatchRow>> ReadRecentAsync(
            NativeIdentity actor, CancellationToken ct = default)
        {
            ListCalls++;
            return DelayedList ?? Task.FromResult<IReadOnlyList<NativeImportBatchRow>>(
                InvalidList ? [Batch(), Batch()] : [Batch()]);
        }
        public Task<NativeImportBatchDetail> ReadBatchAsync(
            NativeIdentity actor, string batchId, CancellationToken ct = default)
        {
            DetailCalls++;
            return DelayedDetail ?? Task.FromResult(ForgedDetail
                ? Detail() with { Batch = Batch("IMP-OTHER") } : Detail());
        }
    }
}
