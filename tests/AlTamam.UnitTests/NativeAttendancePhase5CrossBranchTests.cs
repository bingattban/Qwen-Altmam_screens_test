using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Additional Phase 5 cross-branch regressions retained while resolving the
/// two overlapping WPF leave/permission pull requests. Tests precede the
/// combined workflow/security gate implementation.
/// </summary>
public sealed class NativeAttendancePhase5CrossBranchTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly NativeIdentity Actor =
        new("USR-1", "admin", UserRole.GeneralAdministration, "PHASE5-CROSS-BRANCH-NONCE");
    private static readonly NativeAutoRegisterRow Person =
        new("P-1", "123", "أحمد", "المركز", Today.ToString("yyyy-MM-dd"),
            "تحضير تلقائي 08:00", "2026-11-22T05:00:00Z", "موجود", []);

    private static NativeAttendanceReviewViewModel Create(Backend backend,
        Func<NativeIdentity?>? liveActor = null)
    {
        var vm = new NativeAttendanceReviewViewModel();
        vm.AttachBackend(backend, liveActor ?? (() => Actor));
        return vm;
    }

    private static void SetDates(NativeAttendanceReviewViewModel vm, int days)
    {
        vm.RangeStartDate = Today.ToDateTime(TimeOnly.MinValue);
        vm.RangeEndDate = Today.AddDays(days).ToDateTime(TimeOnly.MinValue);
    }

    [Fact]
    public async Task Controls_and_range_are_frozen_while_canonical_preview_is_pending()
    {
        var backend = new Backend();
        var vm = Create(backend);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        SetDates(vm, 2);
        var hold = new TaskCompletionSource<NativeAttendanceRevisionPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.PendingPreview = hold.Task;

        Task loading = vm.PreviewAsync();
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanEditSelection);
        Assert.False(vm.CanEditRange);
        vm.RangeEndDate = Today.AddDays(3).ToDateTime(TimeOnly.MinValue);
        Assert.Equal(Today.AddDays(2).ToDateTime(TimeOnly.MinValue), vm.RangeEndDate);
        Assert.Empty(backend.Commits);

        hold.SetResult(backend.ValidPreview("STS-LEAVE",
            new NativeAttendanceDateRange(Today.ToString("yyyy-MM-dd"),
                Today.AddDays(2).ToString("yyyy-MM-dd"))));
        await loading;
        Assert.True(vm.HasPreview);
        Assert.True(vm.CanEditSelection);
        Assert.True(vm.CanEditRange);
    }

    [Fact]
    public async Task Dates_outside_canonical_bounds_never_reach_preview_or_commit()
    {
        var backend = new Backend();
        var vm = Create(backend);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-PERMISSION");
        foreach (var (start, end) in new[]
        {
            (Today.AddDays(-1), Today),
            (Today.AddDays(1), Today.AddDays(2)),
            (Today, Today.AddDays(-1)),
            (Today, Today.AddDays(366))
        })
        {
            vm.RangeStartDate = start.ToDateTime(TimeOnly.MinValue);
            vm.RangeEndDate = end.ToDateTime(TimeOnly.MinValue);
            await vm.PreviewAsync();
            Assert.False(vm.HasPreview);
            Assert.NotEmpty(vm.ErrorMessage);
        }
        Assert.Empty(backend.Ranges);
        Assert.Empty(backend.Commits);
    }

    [Fact]
    public async Task Swapped_live_session_after_timed_preview_cannot_approve()
    {
        var backend = new Backend();
        NativeIdentity? active = Actor;
        var vm = Create(backend, () => active);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        SetDates(vm, 1);
        await vm.PreviewAsync();
        Assert.True(vm.HasPreview);
        active = Actor with { SessionNonce = "REPLACED" };
        await vm.ConfirmAsync();
        Assert.Empty(backend.Commits);
        Assert.False(vm.IsOpen);
    }

    private sealed class Backend : INativeAttendanceReviewBackend
    {
        public Task<NativeAttendanceRevisionPreview>? PendingPreview;
        public readonly List<NativeAttendanceDateRange?> Ranges = [];
        public readonly List<(string Token, string OperationId)> Commits = [];

        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
            NativeIdentity actor, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>(
                [new("STS-LEAVE", "إجازة"), new("STS-PERMISSION", "إذن"),
                 new("STS-ABSENT", "غائب")]);

        public Task<NativeAttendanceRevisionPreview> PreviewAsync(
            NativeIdentity actor, DateOnly date, string site,
            IReadOnlyList<string> personIds, string statusId,
            CancellationToken ct = default, NativeAttendanceDateRange? range = null)
        {
            Ranges.Add(range);
            return PendingPreview ?? Task.FromResult(ValidPreview(statusId, range));
        }

        public NativeAttendanceRevisionPreview ValidPreview(
            string statusId, NativeAttendanceDateRange? range) =>
            new("VERIFIED-PREVIEW-TOKEN-AT-LEAST-32-CHARS",
                Today, statusId, statusId,
                [new NativeAttendancePreviewPerson("P-1", "أحمد", "123",
                    "المركز", "موجود", "STS-PRESENT")],
                range?.StartDate, range?.EndDate);

        public Task<NativeAttendanceCommitReceipt> CommitAsync(
            NativeIdentity actor, string previewToken, string operationId,
            CancellationToken ct = default)
        {
            Commits.Add((previewToken, operationId));
            return Task.FromResult(new NativeAttendanceCommitReceipt(1, "AUD-1"));
        }
    }
}
