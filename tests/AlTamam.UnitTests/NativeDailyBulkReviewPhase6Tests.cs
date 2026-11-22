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

    private sealed class Reader : INativeDashboardReader, INativeAttendanceReviewBackend
    {
        public int ChoicesCalls;
        public int PreviewCalls;
        public int CommitCalls;
        public string[] LastPreviewIds = [];

        public Task<NativeDashboardSnapshot> ReadAsync(
            NativeIdentity actor, NativeDashboardFilter filter,
            CancellationToken ct = default)
        {
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
            return Task.FromResult(new NativeAttendanceCommitReceipt(
                LastPreviewIds.Length, "AUDIT-BULK"));
        }
    }
}
