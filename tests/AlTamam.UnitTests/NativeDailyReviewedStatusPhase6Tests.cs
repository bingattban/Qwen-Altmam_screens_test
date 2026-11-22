using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 6 daily editor: no direct writes, only the same canonical
/// server preview and explicit commit already used by the auto-register.
/// </summary>
public sealed class NativeDailyReviewedStatusPhase6Tests
{
    private static readonly NativeIdentity Owner =
        new("USR-1", "admin", UserRole.GeneralAdministration, "DAILY-EDIT-NONCE");

    [Fact]
    public async Task Live_daily_row_reuses_server_review_and_writes_only_after_confirmation()
    {
        var service = new ReviewReader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(service, () => Owner);
        await vm.RefreshAsync();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("P-1", row.PersonId);
        await vm.OpenReviewAsync(row);
        Assert.True(vm.Review.IsOpen);
        Assert.Equal(1, vm.Review.SelectedCount);
        Assert.Equal(1, service.ChoicesCalls);
        Assert.Equal(0, service.PreviewCalls);
        Assert.Equal(0, service.CommitCalls);

        vm.Review.ChooseStatus("STS-ABSENT");
        await vm.Review.PreviewAsync();
        Assert.True(vm.Review.HasPreview);
        Assert.Equal(new[] { "P-1" }, service.LastPreviewIds);
        Assert.Equal(0, service.CommitCalls);

        await vm.Review.ConfirmAsync();
        Assert.True(vm.Review.IsCompleted);
        Assert.Equal(1, service.CommitCalls);
        Assert.Equal(1, vm.Review.CommittedCount);
    }

    [Fact]
    public async Task Forged_or_expired_daily_selection_cannot_open_review()
    {
        var service = new ReviewReader();
        NativeIdentity? principal = Owner;
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(service, () => principal);
        await vm.RefreshAsync();
        var original = Assert.Single(vm.Rows);

        await vm.OpenReviewAsync(original with { PersonId = "FORGED" });
        Assert.False(vm.Review.IsOpen);
        Assert.Equal(0, service.ChoicesCalls);

        principal = null;
        await vm.OpenReviewAsync(original);
        Assert.False(vm.Review.IsOpen);
        Assert.Equal(0, service.ChoicesCalls);
        vm.Clear();
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Selection_from_previous_page_or_filter_is_not_authorized()
    {
        var service = new ReviewReader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(service, () => Owner);
        await vm.RefreshAsync();
        var oldRow = Assert.Single(vm.Rows);

        await vm.SearchAsync("another");
        await vm.OpenReviewAsync(oldRow);
        Assert.False(vm.Review.IsOpen);
        Assert.Equal(0, service.ChoicesCalls);
    }

    private sealed class ReviewReader : INativeDashboardReader, INativeAttendanceReviewBackend
    {
        public int ChoicesCalls;
        public int PreviewCalls;
        public int CommitCalls;
        public string[] LastPreviewIds = [];

        public Task<NativeDashboardSnapshot> ReadAsync(NativeIdentity actor,
            NativeDashboardFilter filter, CancellationToken ct = default)
        {
            var rows = filter.SearchTerm == ""
                ? new[] { new NativeDashboardRow("P-1", "123", "أحمد",
                    "المركز", "موجود", "#0D9488", ["رقيب"]) { RowNumber = 1 } }
                : Array.Empty<NativeDashboardRow>();
            return Task.FromResult(new NativeDashboardSnapshot(
                filter.BusinessDate, filter.SiteId, 1, 0, 0,
                [new("STS-PRESENT", "موجود", 1, "#0D9488")],
                [new("SITE-1", "المركز")],
                rows, [new("rank", "الرتبة")],
                rows.Length, filter.Page, false));
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
                    id, "أحمد", "123", "المركز", "موجود", "STS-PRESENT")).ToArray()));
        }

        public Task<NativeAttendanceCommitReceipt> CommitAsync(
            NativeIdentity actor, string previewToken, string operationId,
            CancellationToken ct = default)
        {
            CommitCalls++;
            return Task.FromResult(new NativeAttendanceCommitReceipt(1, "AUDIT-1"));
        }
    }
}
