using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 6 bulk daily selection is bound to a current page and nonce.
/// Writes must still pass the existing canonical preview and explicit confirm.
/// </summary>
public sealed class NativeDailyBulkReviewPhase6Tests
{
    private static readonly NativeIdentity Owner =
        new("USR-1", "admin", UserRole.GeneralAdministration, "DAILY-BULK-NONCE");

    [Fact]
    public async Task Two_selected_persons_are_previewed_together_and_never_committed_implicitly()
    {
        var backend = new Reader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(backend, () => Owner);
        await vm.RefreshAsync();
        Assert.Equal(3, vm.Rows.Count);
        Assert.True(vm.SetSelectedRows([vm.Rows[0], vm.Rows[1]]));
        Assert.Equal(2, vm.SelectedCount);

        await vm.OpenSelectedReviewAsync();
        Assert.True(vm.Review.IsOpen);
        Assert.Equal(2, vm.Review.SelectedCount);
        Assert.Equal(0, backend.PreviewCalls);
        Assert.Equal(0, backend.CommitCalls);

        vm.Review.ChooseStatus("STS-ABSENT");
        await vm.Review.PreviewAsync();
        Assert.True(vm.Review.HasPreview);
        Assert.Equal(new[] { "P-1", "P-2" }, backend.LastPreviewIds);
        Assert.Equal(0, backend.CommitCalls);

        await vm.Review.ConfirmAsync();
        Assert.True(vm.Review.IsCompleted);
        Assert.Equal(2, vm.Review.CommittedCount);
        Assert.Equal(1, backend.CommitCalls);
    }

    [Fact]
    public async Task Duplicate_forged_and_expired_group_members_fail_closed()
    {
        var backend = new Reader();
        NativeIdentity? principal = Owner;
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(backend, () => principal);
        await vm.RefreshAsync();
        var original = vm.Rows[0];

        Assert.False(vm.SetSelectedRows([original, original]));
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.SetSelectedRows([original with { Name = "FORGED" }]));
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(vm.SetSelectedRows([original, original with { PersonId = "FOREIGN" }]));
        Assert.Equal(0, vm.SelectedCount);
        await vm.OpenSelectedReviewAsync();
        Assert.Equal(0, backend.ChoicesCalls);

        // A valid empty selection must never create a preview or a write.
        Assert.True(vm.SetSelectedRows([]));
        await vm.OpenSelectedReviewAsync();
        Assert.Equal(0, backend.ChoicesCalls);
        Assert.Equal(0, backend.CommitCalls);

        Assert.True(vm.SetSelectedRows([original, vm.Rows[1]]));
        await vm.SearchAsync("nobody");
        Assert.Empty(vm.Rows);
        Assert.Equal(0, vm.SelectedCount);
        await vm.OpenSelectedReviewAsync();
        Assert.Equal(0, backend.ChoicesCalls);

        principal = null;
        Assert.False(vm.SetSelectedRows([original]));
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public async Task Selection_is_frozen_during_approved_preview_and_cleared_on_session_logout()
    {
        var backend = new Reader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(backend, () => Owner);
        await vm.RefreshAsync();
        Assert.True(vm.SetSelectedRows([vm.Rows[0], vm.Rows[1]]));
        await vm.OpenSelectedReviewAsync();
        vm.Review.ChooseStatus("STS-ABSENT");
        await vm.Review.PreviewAsync();
        Assert.True(vm.Review.HasPreview);

        Assert.False(vm.SetSelectedRows([vm.Rows[2]]));
        Assert.True(vm.Review.HasPreview);
        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal(0, backend.CommitCalls);

        vm.Clear();
        Assert.Equal(0, vm.SelectedCount);
        Assert.Empty(vm.Rows);
        Assert.False(vm.Review.IsOpen);
    }

    [Fact]
    public async Task Daily_review_freezes_roster_scope_until_cancelled()
    {
        var backend = new Reader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(backend, () => Owner);
        await vm.RefreshAsync();
        var rows = vm.Rows;
        Assert.True(vm.SetSelectedRows([rows[0]]));
        await vm.OpenSelectedReviewAsync();
        vm.Review.ChooseStatus("STS-ABSENT");
        await vm.Review.PreviewAsync();
        Assert.True(vm.Review.HasPreview);
        Assert.False(vm.CanChangeRosterScope);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.SearchCommand.CanExecute(null));
        await vm.RefreshAsync();
        await vm.SelectSiteAsync("SITE-1");
        await vm.SelectStatusAsync("STS-PRESENT");
        await vm.SearchAsync("another");
        await vm.NextPageAsync();
        await vm.PreviousPageAsync();
        Assert.Same(rows, vm.Rows);
        Assert.Equal("", vm.SelectedSiteId);
        Assert.Equal("", vm.SelectedStatusId);
        Assert.Equal("", vm.SearchDraft);
        Assert.Equal(1, backend.ReadCalls);
        Assert.True(vm.Review.HasPreview);
        Assert.Equal(0, backend.CommitCalls);
        vm.Review.Cancel();
        Assert.True(vm.CanChangeRosterScope);
        await vm.SelectSiteAsync("SITE-1");
        Assert.Equal(2, backend.ReadCalls);
        Assert.Equal("SITE-1", vm.SelectedSiteId);
        Assert.Equal(0, vm.SelectedCount);
    }

    [Fact]
    public async Task Verified_commit_refreshes_once_and_clears_selection_but_keeps_receipt()
    {
        var backend = new Reader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(backend, () => Owner);
        await vm.RefreshAsync();
        Assert.True(vm.SetSelectedRows([vm.Rows[0], vm.Rows[1]]));
        await vm.OpenSelectedReviewAsync();
        vm.Review.ChooseStatus("STS-ABSENT");
        await vm.Review.PreviewAsync();
        await vm.Review.ConfirmAsync();
        Assert.Equal(2, backend.ReadCalls);
        Assert.Equal(1, backend.CommitCalls);
        Assert.Equal(0, vm.SelectedCount);
        Assert.True(vm.Review.IsCompleted);
        Assert.True(vm.Review.IsOpen);
        Assert.Equal("AUDIT-BULK", vm.Review.AuditEventId);
        Assert.False(vm.CanChangeRosterScope);
        vm.Review.Cancel();
        Assert.True(vm.CanChangeRosterScope);
    }

    [Fact]
    public async Task Unverified_commit_freezes_scope_and_preserves_original_intent()
    {
        var backend = new Reader { FailCommit = true };
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(backend, () => Owner);
        await vm.RefreshAsync();
        Assert.True(vm.SetSelectedRows([vm.Rows[0]]));
        await vm.OpenSelectedReviewAsync();
        vm.Review.ChooseStatus("STS-ABSENT");
        await vm.Review.PreviewAsync();
        await vm.Review.ConfirmAsync();
        Assert.True(vm.Review.HasUnverifiedCommit);
        Assert.False(vm.CanChangeRosterScope);
        await vm.SearchAsync("different");
        await vm.RefreshAsync();
        vm.Review.Cancel();
        Assert.True(vm.Review.HasUnverifiedCommit);
        Assert.Equal(1, backend.ReadCalls);
        Assert.Equal(1, backend.CommitCalls);
        Assert.Equal(1, vm.SelectedCount);
        backend.FailCommit = false;
        await vm.Review.ConfirmAsync();
        Assert.Equal(2, backend.CommitCalls);
        Assert.Equal(2, backend.ReadCalls);
        Assert.Equal(backend.OperationIds[0], backend.OperationIds[1]);
        Assert.True(vm.Review.IsCompleted);
    }

    private sealed class Reader : INativeDashboardReader, INativeAttendanceReviewBackend
    {
        public int ReadCalls;
        public bool FailCommit;
        public List<string> OperationIds = [];
        public int ChoicesCalls;
        public int PreviewCalls;
        public int CommitCalls;
        public string[] LastPreviewIds = [];

        public Task<NativeDashboardSnapshot> ReadAsync(
            NativeIdentity actor, NativeDashboardFilter filter,
            CancellationToken ct = default)
        {
            ReadCalls++;
            NativeDashboardRow[] rows = filter.SearchTerm == ""
                ? Enumerable.Range(1, 3).Select(i =>
                    new NativeDashboardRow($"P-{i}", $"A-{i}", $"فرد {i}",
                        "المركز", "موجود", "#0D9488", ["رقيب"]) { RowNumber = i })
                    .ToArray()
                : [];
            return Task.FromResult(new NativeDashboardSnapshot(
                filter.BusinessDate, filter.SiteId, rows.Length, 0, 0,
                [new("STS-PRESENT", "موجود", rows.Length, "#0D9488")],
                [new("SITE-1", "المركز")], rows,
                [new("rank", "الرتبة")], rows.Length, filter.Page, false));
        }

        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
            NativeIdentity actor, CancellationToken ct = default)
        {
            ChoicesCalls++;
            return Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>(
                [new("STS-ABSENT", "غائب")]);
        }

        public Task<NativeAttendanceRevisionPreview> PreviewAsync(
            NativeIdentity actor, DateOnly date, string site,
            IReadOnlyList<string> personIds, string statusId,
            CancellationToken ct = default, NativeAttendanceDateRange? range = null)
        {
            PreviewCalls++;
            LastPreviewIds = personIds.ToArray();
            return Task.FromResult(new NativeAttendanceRevisionPreview(
                new string('p', 32), date, statusId, "غائب",
                personIds.Select(id => new NativeAttendancePreviewPerson(
                    id, $"فرد {id}", id, "المركز", "موجود", "STS-PRESENT")).ToArray()));
        }

        public Task<NativeAttendanceCommitReceipt> CommitAsync(
            NativeIdentity actor, string previewToken, string operationId,
            CancellationToken ct = default)
        {
            CommitCalls++;
            OperationIds.Add(operationId);
            if (FailCommit) throw new IOException("Simulated lost commit response.");
            return Task.FromResult(new NativeAttendanceCommitReceipt(
                LastPreviewIds.Length, "AUDIT-BULK"));
        }
    }
}
