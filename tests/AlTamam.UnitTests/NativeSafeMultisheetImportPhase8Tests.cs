using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Test FIRST: preserving the original 100-accepted-person full-review gate
/// while enabling canonical multi-worksheet parsing, never hidden 101..500 writes.
/// </summary>
public sealed class NativeSafeMultisheetImportPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-IMPORT-SHEETS", "admin", UserRole.GeneralAdministration, "IMPORT-SHEETS-NONCE");
    private static byte[] Workbook() =>
        [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    private static NativeExcelPersonnelImportViewModel Make(FakeBackend backend,
        Func<NativeIdentity?> principal)
    {
        var vm = new NativeExcelPersonnelImportViewModel();
        vm.AttachBackend(backend, principal);
        return vm;
    }

    [Fact]
    public async Task Default_first_worksheet_mode_preserves_original_safe_preview()
    {
        var backend = new FakeBackend();
        var vm = Make(backend, () => Manager);
        vm.Open();
        Assert.False(vm.ImportAllSheets);
        await vm.LoadWorkbookAsync("multi.xlsx", Workbook());
        Assert.Equal([false], backend.Modes);
        Assert.Equal(1, vm.Review?.NewCount);
        Assert.Equal(1, vm.Review?.NewPeople.Count);
        Assert.Equal("الورقة الأولى", vm.Review?.NewPeople[0].Worksheet);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Explicit_all_worksheets_uses_original_canonical_parser_and_shows_every_accepted_person()
    {
        var backend = new FakeBackend();
        var vm = Make(backend, () => Manager);
        vm.Open();
        vm.ImportAllSheets = true;
        await vm.LoadWorkbookAsync("multi.xlsx", Workbook());
        Assert.Equal([true], backend.Modes);
        Assert.Equal(2, vm.Review?.NewCount);
        Assert.Equal(2, vm.Review?.NewPeople.Count);
        Assert.Equal(["الورقة الأولى", "الورقة الثانية"],
            vm.Review!.NewPeople.Select(x => x.Worksheet));
        Assert.Equal(["10001", "10002"],
            vm.Review.NewPeople.Select(x => x.ServiceNumberA));
        Assert.False(vm.CanCommit);
        vm.ReviewedPeopleAndIssues = true;
        Assert.True(vm.CanCommit);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Commits);
        Assert.Equal(2, vm.Receipt?.CreatedCount);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Commits);
    }

    [Fact]
    public async Task Changing_sheet_mode_revokes_previous_signed_review_and_all_explicit_approvals()
    {
        var backend = new FakeBackend { HasNewSites = true, HasCollisions = true };
        var vm = Make(backend, () => Manager);
        vm.Open();
        await vm.LoadWorkbookAsync("multi.xlsx", Workbook());
        vm.ReviewedPeopleAndIssues = true;
        vm.ApproveCreatingSites = true;
        vm.AcknowledgeSimilarNames = true;
        Assert.True(vm.CanCommit);
        vm.ImportAllSheets = true;
        Assert.Null(vm.Review);
        Assert.False(vm.ReviewedPeopleAndIssues);
        Assert.False(vm.ApproveCreatingSites);
        Assert.False(vm.AcknowledgeSimilarNames);
        Assert.False(vm.CanCommit);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Commits);
        await vm.LoadWorkbookAsync("multi.xlsx", Workbook());
        Assert.Equal([false, true], backend.Modes);
        Assert.Equal(2, vm.Review?.NewCount);
        Assert.False(vm.CanCommit);
    }

    [Fact]
    public async Task All_worksheets_above_100_accepted_people_are_rejected_even_if_first_100_are_visible()
    {
        var backend = new FakeBackend { NewAccepted = 101 };
        var vm = Make(backend, () => Manager);
        vm.Open();
        vm.ImportAllSheets = true;
        await vm.LoadWorkbookAsync("multi.xlsx", Workbook());
        Assert.Null(vm.Review);
        Assert.False(vm.CanCommit);
        Assert.Contains("100", vm.Error);
        vm.ReviewedPeopleAndIssues = true;
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Forged_multisheet_total_or_existing_person_update_fails_closed_without_write()
    {
        foreach (bool isCustomUpdate in new[] { true, false })
        {
            var backend = new FakeBackend
            {
                CorruptCount = !isCustomUpdate,
                CustomUpdate = isCustomUpdate
            };
            var vm = Make(backend, () => Manager);
            vm.Open();
            vm.ImportAllSheets = true;
            await vm.LoadWorkbookAsync("multi.xlsx", Workbook());
            Assert.Null(vm.Review);
            Assert.False(vm.CanCommit);
            await vm.ConfirmAsync();
            Assert.Equal(0, backend.Commits);
        }
    }

    [Fact]
    public async Task Logout_during_multiworksheet_preview_cancels_late_reply_and_future_write()
    {
        var backend = new FakeBackend();
        NativeIdentity? identity = Manager;
        var pending = new TaskCompletionSource<NativeExcelImportReview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        var vm = Make(backend, () => identity);
        vm.Open();
        vm.ImportAllSheets = true;
        Task loading = vm.LoadWorkbookAsync("multi.xlsx", Workbook());
        identity = Manager with { SessionNonce = "CHANGED" };
        vm.ForceCancel();
        pending.SetResult(backend.Make("multi.xlsx", true));
        await loading;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Review);
        Assert.Equal(0, backend.Commits);
    }

    [Fact]
    public async Task Unauthorized_and_supervisor_cannot_select_multiworksheet_mode_or_stage_file()
    {
        NativeIdentity? identity = null;
        var backend = new FakeBackend();
        var vm = Make(backend, () => identity);
        vm.Open();
        vm.ImportAllSheets = true;
        Assert.False(vm.ImportAllSheets);
        identity = Manager with { Role = UserRole.Supervisor };
        vm.Open();
        vm.ImportAllSheets = true;
        await vm.LoadWorkbookAsync("multi.xlsx", Workbook());
        Assert.False(vm.IsOpen);
        Assert.Empty(backend.Modes);
    }

    private sealed class FakeBackend : INativeExcelPersonnelImportBackend
    {
        public readonly List<bool> Modes = [];
        public int Commits, NewAccepted = 2;
        public bool CorruptCount, CustomUpdate, HasNewSites, HasCollisions;
        public Task<NativeExcelImportReview>? Pending;

        public NativeExcelImportReview Make(string file, bool allSheets)
        {
            int total = allSheets ? NewAccepted : 1;
            var people = Enumerable.Range(1, Math.Min(total, 100))
                .Select(i => new NativeExcelImportPerson(
                    i % 2 == 1 ? "الورقة الأولى" : "الورقة الثانية",
                    2 + i, "الفرد رقم " + i, (10000 + i).ToString("D5"),
                    "الموقع الأول")).ToArray();
            var sites = HasNewSites
                ? new NativeExcelImportSite[] { new("الموقع الثاني", 1) }
                : [];
            var collisions = HasCollisions
                ? new NativeExcelImportCollision[]
                {
                    new("الورقة الأولى", 3, "الفرد رقم 1", "10001",
                        [new NativeExcelImportMatch("PRN-OLD", "الفرد رقم 1",
                            "11223", true)])
                }
                : [];
            return new NativeExcelImportReview(
                file, new string('a',64), new string('b',64),
                CorruptCount ? total + 3 : total, total, 0,
                CustomUpdate ? 1 : 0, sites,
                collisions.Length, people, [], collisions);
        }

        public Task<NativeExcelImportReview> StageAndPreviewAsync(
            NativeIdentity actor, string file, byte[] bytes,
            CancellationToken ct = default) =>
            StageAndPreviewAsync(actor, file, bytes, false, ct);

        public Task<NativeExcelImportReview> StageAndPreviewAsync(
            NativeIdentity actor, string file, byte[] bytes,
            bool allSheets, CancellationToken ct = default)
        {
            Modes.Add(allSheets);
            return Pending ?? Task.FromResult(Make(file, allSheets));
        }

        public Task<NativeExcelImportReceipt> CommitAsync(
            NativeIdentity actor, NativeExcelImportReview review,
            string operationId, CancellationToken ct = default)
        {
            Commits++;
            return Task.FromResult(new NativeExcelImportReceipt(
                "IMP-SHEETS-001", review.NewCount, review.RejectedCount,
                review.NewSites.Count, 0, "2026-11-23T20:00:00Z"));
        }
    }
}
