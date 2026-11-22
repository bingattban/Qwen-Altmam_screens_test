using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 5 tests were written before implementing the native bounded-date editor.
/// A date-range revision must never fall back to indefinite leave or a blind write.
/// </summary>
public sealed class NativeAttendanceDateRangePhase5Tests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly NativeIdentity Actor =
        new("USR-1", "admin", UserRole.GeneralAdministration, "ACTIVE-NONCE");
    private static readonly NativeAutoRegisterRow Person =
        new("P-1", "123", "أحمد", "المركز", Today.ToString("yyyy-MM-dd"),
            "تحضير تلقائي", "2026-11-22T05:00:00Z", "موجود", []);

    private static (NativeAttendanceReviewViewModel View, RangeBackend Backend) Create()
    {
        var backend = new RangeBackend();
        var view = new NativeAttendanceReviewViewModel();
        view.AttachBackend(backend, () => Actor);
        return (view, backend);
    }

    [Fact]
    public async Task Range_statuses_are_available_but_no_dates_means_no_server_preview()
    {
        var (vm, backend) = Create();
        await vm.OpenAsync(Today, "SITE-1", [Person]);
        Assert.Equal(3, vm.StatusChoices.Count);
        vm.ChooseStatus("STS-LEAVE");
        Assert.True(vm.RequiresDateRange);
        Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), vm.RangeStartDate);
        Assert.Null(vm.RangeEndDate);
        await vm.PreviewAsync();
        Assert.Empty(backend.Previews);
        Assert.Empty(backend.Commits);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Permission_rejects_backdated_future_started_reversed_and_overlong_ranges()
    {
        var (vm, backend) = Create();
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-PERMISSION");
        foreach (var (start, end) in new[] {
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
        Assert.Empty(backend.Previews);
        Assert.Empty(backend.Commits);
    }

    [Fact]
    public async Task Canonical_range_preview_then_explicit_confirmation_keeps_exact_dates()
    {
        var (vm, backend) = Create();
        await vm.OpenAsync(Today, "SITE-1", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeEndDate = Today.AddDays(5).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        Assert.True(vm.HasPreview);
        Assert.Contains("إلى", vm.PreviewMessage);
        Assert.Single(backend.Previews);
        Assert.Equal(new NativeAttendanceDateRange(Today, Today.AddDays(5)),
            backend.Previews[0].Range);
        Assert.Empty(backend.Commits);
        await vm.ConfirmAsync();
        Assert.True(vm.IsCompleted);
        Assert.Single(backend.Commits);
    }

    [Fact]
    public async Task Changing_end_date_invalidates_existing_token_before_write()
    {
        var (vm, backend) = Create();
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-PERMISSION");
        vm.RangeEndDate = Today.ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        Assert.True(vm.HasPreview);
        vm.RangeEndDate = Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        Assert.False(vm.HasPreview);
        await vm.ConfirmAsync();
        Assert.Empty(backend.Commits);
        await vm.PreviewAsync();
        Assert.True(vm.HasPreview);
        Assert.Equal(new NativeAttendanceDateRange(Today, Today.AddDays(1)),
            backend.Previews[^1].Range);
    }

    [Fact]
    public async Task Mismatched_server_echo_is_not_approvable()
    {
        var (vm, backend) = Create();
        backend.AlterEcho = true;
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeEndDate = Today.AddDays(2).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        Assert.False(vm.HasPreview);
        Assert.NotEmpty(vm.ErrorMessage);
        await vm.ConfirmAsync();
        Assert.Empty(backend.Commits);
    }

    [Fact]
    public async Task Uncertain_commit_never_allows_date_change_or_new_operation_id()
    {
        var (vm, backend) = Create();
        backend.LoseFirstCommit = true;
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeEndDate = Today.AddDays(3).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        vm.RangeEndDate = Today.AddDays(4).ToDateTime(TimeOnly.MinValue);
        Assert.Equal(Today.AddDays(3).ToDateTime(TimeOnly.MinValue), vm.RangeEndDate);
        await vm.PreviewAsync();
        Assert.Single(backend.Previews);
        await vm.ConfirmAsync();
        Assert.True(vm.IsCompleted);
        Assert.Equal(backend.Commits[0], backend.Commits[1]);
    }

    private sealed class RangeBackend : INativeAttendanceReviewBackend
    {
        public bool AlterEcho;
        public bool LoseFirstCommit;
        public List<(string Status, NativeAttendanceDateRange? Range)> Previews = [];
        public List<(string Token, string OperationId)> Commits = [];

        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
            NativeIdentity actor, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>(
                [new("STS-LEAVE", "إجازة", true),
                 new("STS-PERMISSION", "إذن", true),
                 new("STS-ABSENT", "غائب")]);

        public Task<NativeAttendanceRevisionPreview> PreviewAsync(
            NativeIdentity actor, DateOnly date, string site,
            IReadOnlyList<string> persons, string statusId, CancellationToken ct = default,
            NativeAttendanceDateRange? range = null)
        {
            Previews.Add((statusId, range));
            NativeAttendanceDateRange? echoed = AlterEcho && range is { } r
                ? r with { End = r.End.AddDays(1) } : range;
            return Task.FromResult(new NativeAttendanceRevisionPreview(
                "VERIFIED-PREVIEW-TOKEN-AT-LEAST-32-CHARS",
                date, statusId, statusId,
                [new NativeAttendancePreviewPerson("P-1", "أحمد", "123",
                    "المركز", "موجود", "STS-PRESENT")], echoed));
        }

        public Task<NativeAttendanceCommitReceipt> CommitAsync(
            NativeIdentity actor, string previewToken, string operationId,
            CancellationToken ct = default)
        {
            Commits.Add((previewToken, operationId));
            if (LoseFirstCommit && Commits.Count == 1)
                throw new IOException("Commit receipt lost");
            return Task.FromResult(new NativeAttendanceCommitReceipt(1, "AUD-1"));
        }
    }
}
