using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Native XLSX import: tests committed before any implementing source.</summary>
public sealed class NativePersonnelExcelImportPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-IMPORT", "admin", UserRole.GeneralAdministration, "IMPORT-NONCE");
    private static readonly byte[] SmallXlsx = [0x50, 0x4B, 0x03, 0x04, 0x01, 0x02];
    private static NativePersonnelExcelImportViewModel Create(FakeBackend backend,
        Func<NativeIdentity?> principal)
    {
        var vm = new NativePersonnelExcelImportViewModel();
        vm.AttachBackend(backend, principal);
        return vm;
    }

    [Fact]
    public async Task Only_original_authenticated_manager_can_open_or_stage_workbook()
    {
        NativeIdentity? actor = null;
        var backend = new FakeBackend();
        var vm = Create(backend, () => actor);
        vm.Open();
        Assert.False(vm.IsOpen);
        await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
        actor = Manager with { Role = UserRole.Supervisor };
        vm.Open();
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.Stages);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Non_xlsx_empty_or_oversize_files_fail_before_canonical_staging()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("people.xls", SmallXlsx);
        await vm.LoadWorkbookAsync("people.xlsx", []);
        await vm.LoadWorkbookAsync("people.xlsx", new byte[160 * 1024 + 1]);
        Assert.Equal(0, backend.Stages);
        Assert.False(vm.CanPreview);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Staging_and_preview_never_commit_existing_people()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
        Assert.Equal("people.xlsx", vm.Selection?.FileName);
        Assert.Equal(1, backend.Stages);
        Assert.Equal(0, backend.Commits);
        await vm.PreviewAsync();
        Assert.Equal(1, backend.Previews);
        Assert.Equal(2, vm.Preview?.NewCount);
        Assert.Equal(0, backend.Commits);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task Rejected_rows_new_sites_and_similar_names_each_need_explicit_consent()
    {
        var backend = new FakeBackend { HasIssues = true, HasNewSite = true, HasSimilar = true };
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
        await vm.PreviewAsync();
        Assert.Equal(1, vm.Preview?.IssueCount);
        Assert.Equal(1, vm.Preview?.NewSites.Count);
        Assert.Equal(1, vm.Preview?.SimilarNameCount);
        vm.PersonnelApproved = true;
        Assert.False(vm.CanConfirm);
        vm.RejectedRowsReviewed = true;
        Assert.False(vm.CanConfirm);
        vm.NewSitesApproved = true;
        Assert.False(vm.CanConfirm);
        vm.SimilarNamesReviewed = true;
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Commits);
        Assert.Equal(2, vm.Receipt?.CreatedCount);
        Assert.Equal(1, vm.Receipt?.RejectedCount);
        Assert.Equal(1, vm.Receipt?.CreatedSitesCount);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Commits);
    }

    [Fact]
    public async Task Forged_or_inconsistent_preview_counts_or_tokens_fail_closed()
    {
        foreach (string forge in new[] { "token", "count", "file", "custom", "sites", "truncated" })
        {
            var backend = new FakeBackend { Forge = forge };
            var vm = Create(backend, () => Manager);
            vm.Open();
            await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
            await vm.PreviewAsync();
            Assert.Null(vm.Preview);
            Assert.False(vm.CanConfirm);
            Assert.Equal(0, backend.Commits);
        }
    }

    [Fact]
    public async Task Changing_sheet_selection_clears_previous_signed_import_plan()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
        await vm.PreviewAsync();
        vm.PersonnelApproved = true;
        Assert.True(vm.CanConfirm);
        vm.ImportAllSheets = true;
        Assert.Null(vm.Preview);
        Assert.Null(vm.Selection);
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Commits);
        await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
        Assert.True(backend.LastAllSheets);
    }

    [Fact]
    public async Task Logout_or_navigation_drops_outstanding_file_preview()
    {
        NativeIdentity? actor = Manager;
        var backend = new FakeBackend();
        var vm = Create(backend, () => actor);
        vm.Open();
        await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
        var signal = new TaskCompletionSource<NativePersonnelExcelImportPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.PendingPreview = signal.Task;
        Task reading = vm.PreviewAsync();
        actor = Manager with { SessionNonce = "CHANGED" };
        vm.ForceCancel();
        signal.SetResult(backend.MakePreview());
        await reading;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Unknown_commit_or_forged_receipt_never_auto_retries_or_reopens()
    {
        foreach (bool lost in new[] { true, false })
        {
            var backend = new FakeBackend { LostReply = lost, BadReceipt = !lost };
            var vm = Create(backend, () => Manager);
            vm.Open();
            await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
            await vm.PreviewAsync();
            vm.PersonnelApproved = true;
            await vm.ConfirmAsync();
            Assert.True(vm.HasUnverifiedCommit);
            Assert.Null(vm.Receipt);
            await vm.ConfirmAsync();
            Assert.Equal(1, backend.Commits);
            vm.ForceCancel();
            vm.Open();
            Assert.False(vm.IsOpen);
        }
    }

    [Fact]
    public async Task Import_without_custom_update_approval_never_overwrites_existing_identity()
    {
        var backend = new FakeBackend { Forge = "custom" };
        var vm = Create(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("people.xlsx", SmallXlsx);
        await vm.PreviewAsync();
        Assert.False(vm.CanConfirm);
        Assert.Equal(0, backend.Commits);
        Assert.False(backend.AllowCustomUpdates);
    }

    private sealed class FakeBackend : INativePersonnelExcelImportBackend
    {
        public int Stages, Previews, Commits;
        public bool HasIssues, HasNewSite, HasSimilar, LostReply, BadReceipt;
        public bool LastAllSheets, AllowCustomUpdates;
        public string Forge = "";
        public Task<NativePersonnelExcelImportPreview>? PendingPreview;
        public readonly List<string> OperationIds = [];

        public Task<NativePersonnelExcelWorkbookSelection> StageWorkbookAsync(
            NativeIdentity actor, string filename, byte[] bytes, bool allSheets,
            CancellationToken ct = default)
        {
            Stages++;
            LastAllSheets = allSheets;
            AllowCustomUpdates = false;
            return Task.FromResult(new NativePersonnelExcelWorkbookSelection(
                filename, new string('a', 64),
                HasIssues ? 3 : 2, ["الورقة الأولى"]));
        }

        public NativePersonnelExcelImportPreview MakePreview()
        {
            var rows = new[]
            {
                new NativePersonnelExcelCandidate("الورقة الأولى", 2, "فرد أول",
                    "00001", "غير محدد"),
                new NativePersonnelExcelCandidate("الورقة الأولى", 3, "فرد ثان",
                    "00002", "غير محدد")
            };
            NativePersonnelExcelIssue[] issues = HasIssues ?
                [new NativePersonnelExcelIssue("الورقة الأولى", 4,
                    "00001", "فرد مكرر", "غير محدد", "الرقم (ع) مكرر")] : [];
            NativePersonnelExcelSite[] sites = HasNewSite ?
                [new NativePersonnelExcelSite("الموقع الجديد", 2)] : [];
            int total = HasIssues ? 3 : 2;
            return new NativePersonnelExcelImportPreview(
                Forge == "file" ? "other.xlsx" : "people.xlsx",
                new string('a', 64),
                Forge == "token" ? "bad" : new string('b', 64),
                Forge == "count" ? total + 1 : total,
                2, HasIssues ? 1 : 0,
                Forge == "custom" ? 1 : 0,
                HasSimilar ? 1 : 0,
                Forge == "truncated" ? rows.Take(1).ToArray() : rows,
                issues,
                Forge == "sites" ? [new NativePersonnelExcelSite("", -1)] : sites,
                true);
        }

        public Task<NativePersonnelExcelImportPreview> PreviewWorkbookAsync(
            NativeIdentity actor, NativePersonnelExcelWorkbookSelection selection,
            CancellationToken ct = default)
        {
            Previews++;
            return PendingPreview ?? Task.FromResult(MakePreview());
        }

        public Task<NativePersonnelExcelImportReceipt> CommitWorkbookAsync(
            NativeIdentity actor, NativePersonnelExcelImportPreview preview,
            string operationId, CancellationToken ct = default)
        {
            Commits++;
            OperationIds.Add(operationId);
            if (LostReply) throw new IOException("Commit may have succeeded");
            return Task.FromResult(new NativePersonnelExcelImportReceipt(
                "IMP-ABC123", BadReceipt ? 999 : preview.NewCount,
                preview.IssueCount, preview.NewSites.Count, 0,
                "2026-11-23T17:00:00Z"));
        }
    }
}
