using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test first: original owned Excel rejected-row XLSX export only after explicit approval.</summary>
public sealed class NativeImportIssuesExportPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-ISSUES", "admin", UserRole.GeneralAdministration, "NONCE-ISSUES");
    private static NativeImportBatchRow Batch(int rejected = 1) =>
        new("IMP-ISSUES-001", "الدفعة.xlsx", 2, 2 - rejected, rejected,
            "COMMITTED", "2026-11-23T18:00:00Z");
    private static NativeImportBatchDetail Detail(int rejected = 1) =>
        new(Batch(rejected), rejected == 0 ? [] :
            [new NativeImportBatchIssue(3, "00009", "اسم", "موقع", "رقم مكرر")]);
    private static string SafePath() => Path.Combine(Path.GetTempPath(), "AlTamamIssues.xlsx");
    private static NativeImportOutcomeHistoryViewModel Make(FakeBackend backend,
        Func<NativeIdentity?> principal)
    {
        var vm = new NativeImportOutcomeHistoryViewModel();
        vm.AttachBackend(backend, principal);
        return vm;
    }

    [Fact]
    public async Task Browsing_and_reading_do_not_export_without_explicit_issue_acknowledgement()
    {
        var backend = new FakeBackend();
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        Assert.False(vm.CanExport);
        await vm.ExportIssuesAsync();
        Assert.Equal(0, backend.ExportCalls);
        Assert.Null(vm.ExportReceipt);
        vm.AcknowledgeSavedIssues = true;
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task Approved_owner_batch_exports_exactly_once_from_original_service()
    {
        var backend = new FakeBackend();
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.AcknowledgeSavedIssues = true;
        await vm.ExportIssuesAsync();
        Assert.Equal(1, backend.ExportCalls);
        Assert.Equal("IMP-ISSUES-001", vm.ExportReceipt?.ImportBatchId);
        Assert.Equal(1, vm.ExportReceipt?.IssueCount);
        Assert.False(vm.CanExport);
        await vm.ExportIssuesAsync();
        Assert.Equal(1, backend.ExportCalls);
    }

    [Fact]
    public async Task Zero_rejected_rows_cannot_create_an_empty_spreadsheet()
    {
        var backend = new FakeBackend { Rejected = 0 };
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.AcknowledgeSavedIssues = true;
        Assert.False(vm.CanExport);
        await vm.ExportIssuesAsync();
        Assert.Equal(0, backend.ExportCalls);
    }

    [Fact]
    public async Task Forged_export_receipt_is_refused_and_no_second_write_is_sent()
    {
        var backend = new FakeBackend { ForgeReceipt = true };
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.AcknowledgeSavedIssues = true;
        await vm.ExportIssuesAsync();
        Assert.Null(vm.ExportReceipt);
        Assert.True(vm.HasUnverifiedExport);
        Assert.False(vm.CanExport);
        await vm.ExportIssuesAsync();
        Assert.Equal(1, backend.ExportCalls);
    }

    [Fact]
    public async Task Lost_response_cannot_be_replayed_after_closing_and_reopening_same_session()
    {
        var backend = new FakeBackend { LostReceipt = true };
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.AcknowledgeSavedIssues = true;
        await vm.ExportIssuesAsync();
        Assert.True(vm.HasUnverifiedExport);
        vm.Close();
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.AcknowledgeSavedIssues = true;
        Assert.False(vm.CanExport);
        await vm.ExportIssuesAsync();
        Assert.Equal(1, backend.ExportCalls);
    }

    [Fact]
    public async Task Logout_during_export_discards_late_success_as_unverified()
    {
        var backend = new FakeBackend();
        NativeIdentity? principal = Manager;
        var vm = Make(backend, () => principal);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.AcknowledgeSavedIssues = true;
        var pending = new TaskCompletionSource<NativeImportIssuesExportReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.PendingExport = pending.Task;
        Task operation = vm.ExportIssuesAsync();
        principal = Manager with { SessionNonce = "CHANGED" };
        vm.Close();
        pending.SetResult(backend.Receipt());
        await operation;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.ExportReceipt);
        Assert.Equal(1, backend.ExportCalls);
    }

    [Fact]
    public async Task Anonymous_and_supervisor_cannot_export_a_batch()
    {
        NativeIdentity? principal = null;
        var backend = new FakeBackend();
        var vm = Make(backend, () => principal);
        await vm.OpenAsync();
        principal = Manager with { Role = UserRole.Supervisor };
        await vm.OpenAsync();
        vm.AcknowledgeSavedIssues = true;
        await vm.ExportIssuesAsync();
        Assert.Equal(0, backend.ExportCalls);
    }

    private sealed class FakeBackend : INativeImportOutcomeHistoryBackend
    {
        public bool ForgeReceipt, LostReceipt;
        public int Rejected = 1, ExportCalls;
        public Task<NativeImportIssuesExportReceipt>? PendingExport;
        public Task<IReadOnlyList<NativeImportBatchRow>> ReadRecentAsync(
            NativeIdentity actor, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NativeImportBatchRow>>([Batch(Rejected)]);
        public Task<NativeImportBatchDetail> ReadBatchAsync(
            NativeIdentity actor, string batchId, CancellationToken ct = default) =>
            Task.FromResult(Detail(Rejected));
        public NativeImportIssuesExportReceipt Receipt() =>
            new("IMP-ISSUES-001", "AlTamamIssues.xlsx",
                ForgeReceipt ? SafePath() + ".forged" : SafePath(), 1, "xlsx");
        public Task<NativeImportIssuesExportReceipt> ExportIssuesAsync(
            NativeIdentity actor, string batchId, CancellationToken ct = default)
        {
            ExportCalls++;
            if (LostReceipt) throw new IOException("Network-like result lost after file export");
            return PendingExport ?? Task.FromResult(Receipt());
        }
    }
}
