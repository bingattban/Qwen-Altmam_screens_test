using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 8 slice 16 — tests FIRST for explicit, owner-scoped export of
/// persisted Excel import rejection evidence. No independent Excel writer.
/// </summary>
public sealed class NativeImportOutcomeExportPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-EXPORT", "admin", UserRole.GeneralAdministration, "NONCE-EXPORT");
    private static NativeImportBatchRow Batch() =>
        new("IMP-001", "roster.xlsx", 3, 2, 1, "COMMITTED", "2026-11-23T18:00:00Z");
    private static NativeImportBatchDetail Detail() =>
        new(Batch(), [new NativeImportBatchIssue(4, "00012", "اسم", "موقع", "رقم مكرر")]);

    [Fact]
    public async Task Export_requires_live_manager_and_explicit_selected_reviewed_batch()
    {
        NativeIdentity? actor = Manager;
        var backend = new ExportBackend();
        var vm = new NativeImportOutcomeHistoryViewModel();
        vm.AttachBackend(backend, () => actor);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        Assert.Equal(0, backend.Exports);
        Assert.False(vm.CanExportIssues);
        vm.ReviewedExportIssues = true;
        Assert.True(vm.CanExportIssues);
        actor = Manager with { SessionNonce = "CHANGED" };
        await vm.ExportIssuesAsync();
        Assert.Equal(0, backend.Exports);
        Assert.Null(vm.SelectedDetail);
    }

    [Fact]
    public async Task Export_uses_only_original_service_and_verifies_immutable_receipt()
    {
        var backend = new ExportBackend();
        var vm = new NativeImportOutcomeHistoryViewModel();
        vm.AttachBackend(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.ReviewedExportIssues = true;
        await vm.ExportIssuesAsync();
        Assert.Equal(1, backend.Exports);
        Assert.Equal("IMP-001", vm.ExportReceipt?.ImportBatchId);
        Assert.Equal(1, vm.ExportReceipt?.IssueCount);
        Assert.False(vm.CanExportIssues);
    }

    [Fact]
    public async Task Mismatched_export_receipt_must_be_rejected_without_auto_retry()
    {
        var backend = new ExportBackend { ForgedReceipt = true };
        var vm = new NativeImportOutcomeHistoryViewModel();
        vm.AttachBackend(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.ReviewedExportIssues = true;
        await vm.ExportIssuesAsync();
        Assert.Null(vm.ExportReceipt);
        Assert.NotEmpty(vm.ExportError);
        Assert.Equal(1, backend.Exports);
    }

    [Fact]
    public async Task Refresh_and_close_revoke_export_consent_and_clear_receipts()
    {
        var backend = new ExportBackend();
        var vm = new NativeImportOutcomeHistoryViewModel();
        vm.AttachBackend(backend, () => Manager);
        await vm.OpenAsync();
        await vm.OpenDetailsAsync(vm.Batches[0]);
        vm.ReviewedExportIssues = true;
        await vm.RefreshAsync();
        Assert.False(vm.CanExportIssues);
        Assert.Null(vm.ExportReceipt);
        vm.Close();
        Assert.False(vm.CanExportIssues);
    }

    private sealed class ExportBackend : INativeImportOutcomeHistoryBackend
    {
        public int Exports;
        public bool ForgedReceipt;
        public Task<IReadOnlyList<NativeImportBatchRow>> ReadRecentAsync(
            NativeIdentity actor, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NativeImportBatchRow>>([Batch()]);
        public Task<NativeImportBatchDetail> ReadBatchAsync(
            NativeIdentity actor, string batchId, CancellationToken ct = default) =>
            Task.FromResult(Detail());
        public Task<NativeImportIssueExportReceipt> ExportIssuesAsync(
            NativeIdentity actor, string batchId, CancellationToken ct = default)
        {
            Exports++;
            return Task.FromResult(new NativeImportIssueExportReceipt(
                ForgedReceipt ? "IMP-OTHER" : batchId,
                "C:\\Exports\\ImportIssues.xlsx", "ImportIssues.xlsx", 1, "xlsx"));
        }
    }
}
