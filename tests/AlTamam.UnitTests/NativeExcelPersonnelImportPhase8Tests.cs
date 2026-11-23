using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// TEST FIRST: native XLSX ingestion must only commit genuinely new people,
/// only after original service preview and distinct user acknowledgements.
/// </summary>
public sealed class NativeExcelPersonnelImportPhase8Tests
{
    private static readonly NativeIdentity Manager = new(
        "USR-IMPORT", "admin", UserRole.GeneralAdministration, "IMPORT-NONCE");
    private static byte[] Workbook() => [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8];
    private static NativeExcelImportPerson Person(string id = "01234") =>
        new("الورقة الأولى", 2, "محمد سالم باعلي", id, "الموقع الأول");

    private static NativeExcelPersonnelImportViewModel Create(
        FakeBackend backend, Func<NativeIdentity?> identity)
    {
        var vm = new NativeExcelPersonnelImportViewModel();
        vm.AttachBackend(backend, identity);
        return vm;
    }

    [Fact]
    public async Task Missing_actor_and_supervisor_cannot_open_or_import()
    {
        NativeIdentity? actor = null;
        var backend = new FakeBackend();
        var vm = Create(backend, () => actor);
        vm.Open();
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        vm.Open();
        Assert.False(vm.IsOpen);
        await vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
        Assert.Equal(0, backend.StageCalls);
        Assert.Equal(0, backend.CommitCalls);
    }

    [Fact]
    public async Task Only_small_local_xlsx_and_exact_canonical_preview_are_accepted()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("unsafe.xls", Workbook());
        Assert.Null(vm.Review);
        await vm.LoadWorkbookAsync("كشف.xlsx", new byte[160 * 1024 + 1]);
        Assert.Null(vm.Review);
        Assert.Equal(0, backend.StageCalls);
        await vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
        Assert.NotNull(vm.Review);
        Assert.Equal(1, backend.StageCalls);
        Assert.Equal("كشف.xlsx", vm.Review?.FileName);
        Assert.Equal(1, vm.Review?.NewCount);
        Assert.Equal("01234", vm.Review?.NewPeople[0].ServiceNumberA);
        Assert.Equal(0, backend.CommitCalls);
    }

    [Fact]
    public async Task New_sites_and_similar_name_warnings_need_independent_consent()
    {
        var backend = new FakeBackend { NewSites = true, SimilarNames = true };
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
        Assert.False(vm.CanCommit);
        vm.ReviewedPeopleAndIssues = true;
        Assert.False(vm.CanCommit);
        vm.ApproveCreatingSites = true;
        Assert.False(vm.CanCommit);
        vm.AcknowledgeSimilarNames = true;
        Assert.True(vm.CanCommit);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.CommitCalls);
        Assert.Equal("IMP-001", vm.Receipt?.ImportBatchId);
        Assert.Equal(1, vm.Receipt?.CreatedCount);
        Assert.Equal(1, vm.Receipt?.CreatedSitesCount);
    }

    [Fact]
    public async Task Duplicate_service_number_and_archived_identity_remain_issues_not_automatic_merges()
    {
        var backend = new FakeBackend { RejectedRows = 2 };
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
        Assert.Equal(2, vm.Review?.RejectedCount);
        Assert.Equal(3, vm.Review?.TotalRows);
        Assert.Equal(2, vm.Review?.Issues.Count);
        Assert.Equal(0, backend.CommitCalls);
        vm.ReviewedPeopleAndIssues = true;
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.CommitCalls);
        Assert.Equal(2, vm.Receipt?.RejectedCount);
    }

    [Fact]
    public async Task All_rows_must_be_visible_and_limit_500_in_native_import()
    {
        var backend = new FakeBackend { NewCountOverride = 501 };
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
        Assert.Null(vm.Review);
        Assert.False(vm.CanCommit);
        Assert.Equal(0, backend.CommitCalls);
    }

    [Fact]
    public async Task Existing_custom_field_updates_are_rejected_even_if_preview_was_forged()
    {
        var backend = new FakeBackend { ForbiddenCustomUpdates = true };
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
        Assert.Null(vm.Review);
        Assert.False(vm.CanCommit);
        Assert.Equal(0, backend.CommitCalls);
    }

    [Fact]
    public async Task New_file_invalidates_all_old_consent_and_original_preview()
    {
        var backend = new FakeBackend { NewSites = true };
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
        vm.ReviewedPeopleAndIssues = true;
        vm.ApproveCreatingSites = true;
        Assert.True(vm.CanCommit);
        await vm.LoadWorkbookAsync("نسخة.xlsx", Workbook());
        Assert.False(vm.ReviewedPeopleAndIssues);
        Assert.False(vm.ApproveCreatingSites);
        Assert.False(vm.CanCommit);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.CommitCalls);
    }

    [Fact]
    public async Task Logout_during_very_late_import_preview_drops_results()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        var vm = Create(backend, () => actor);
        var pending = new TaskCompletionSource<NativeExcelImportReview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        vm.Open();
        Task loading = vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
        actor = Manager with { SessionNonce = "OTHER" };
        vm.ForceCancel();
        pending.SetResult(backend.Make("كشف.xlsx"));
        await loading;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Review);
        Assert.Equal(0, backend.CommitCalls);
    }

    [Fact]
    public async Task Missing_or_forged_commit_receipt_never_retries_in_same_session()
    {
        foreach (bool lost in new[] { true, false })
        {
            var backend = new FakeBackend { LostReceipt = lost, ForgeReceipt = !lost };
            var vm = Create(backend, () => Manager);
            vm.Open();
            await vm.LoadWorkbookAsync("كشف.xlsx", Workbook());
            vm.ReviewedPeopleAndIssues = true;
            await vm.ConfirmAsync();
            Assert.True(vm.HasUnverifiedCommit);
            Assert.Null(vm.Receipt);
            await vm.ConfirmAsync();
            Assert.Equal(1, backend.CommitCalls);
            vm.ForceCancel();
            vm.Open();
            Assert.False(vm.IsOpen);
        }
    }

    private sealed class FakeBackend : INativeExcelPersonnelImportBackend
    {
        public int StageCalls, CommitCalls;
        public int RejectedRows, NewCountOverride = -1;
        public bool NewSites, SimilarNames, ForbiddenCustomUpdates;
        public bool LostReceipt, ForgeReceipt;
        public Task<NativeExcelImportReview>? Pending;
        public NativeExcelImportReview Make(string fileName)
        {
            int count = NewCountOverride >= 0 ? NewCountOverride : 1;
            var items = Enumerable.Range(0, Math.Min(count, 500))
                .Select(i => Person((1234 + i).ToString("D5"))).ToArray();
            var issues = Enumerable.Range(0, RejectedRows)
                .Select(i => new NativeExcelImportIssue("الورقة الأولى", 3 + i,
                    "12345", "مكرر", "الموقع الأول", "الرقم (ع) موجود")).ToArray();
            return new NativeExcelImportReview(
                fileName, new string('a', 64), new string('b', 64),
                count + RejectedRows, count, RejectedRows, 0,
                NewSites ? [new NativeExcelImportSite("الموقع الأول", 1)] : [],
                SimilarNames ? 1 : 0, items, issues,
                SimilarNames ?
                    [new NativeExcelImportCollision("الورقة الأولى", 2,
                        "محمد سالم باعلي", "01234",
                        [new NativeExcelImportMatch("PRN-OLD", "محمد سالم باعلي",
                            "00001", true)])] : []);
        }
        public Task<NativeExcelImportReview> StageAndPreviewAsync(
            NativeIdentity actor, string fileName, byte[] workbook,
            CancellationToken ct = default)
        {
            StageCalls++;
            var result = Make(fileName);
            if (ForbiddenCustomUpdates) result = result with { CustomUpdateCount = 1 };
            return Pending ?? Task.FromResult(result);
        }
        public Task<NativeExcelImportReceipt> CommitAsync(
            NativeIdentity actor, NativeExcelImportReview reviewed,
            string operationId, CancellationToken ct = default)
        {
            CommitCalls++;
            if (LostReceipt) throw new IOException("Commit response missing");
            return Task.FromResult(new NativeExcelImportReceipt(
                ForgeReceipt ? "IMP-OTHER" : "IMP-001",
                reviewed.NewCount + (ForgeReceipt ? 2 : 0),
                reviewed.RejectedCount,
                reviewed.NewSites.Count, 0,
                "2026-11-23T16:00:00Z"));
        }
    }
}
