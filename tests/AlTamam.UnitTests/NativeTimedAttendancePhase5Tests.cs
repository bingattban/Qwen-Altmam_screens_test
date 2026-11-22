using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 5: bounded leave/permission requires an explicit dated preview,
/// and a single audited, non-duplicated operator confirmation.
/// </summary>
public sealed class NativeTimedAttendancePhase5Tests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly NativeIdentity Owner =
        new("USR-1", "admin", UserRole.GeneralAdministration, "LIVE-PHASE5-NONCE");
    private static readonly NativeAutoRegisterRow Person =
        new("P-1", "123", "أحمد", "المركز", Today.ToString("yyyy-MM-dd"),
            "تحضير تلقائي 08:00", "2026-11-22T05:00:00Z", "موجود", []);

    [Theory]
    [InlineData("STS-LEAVE")]
    [InlineData("STS-PERMISSION")]
    public async Task Timed_status_requires_explicit_range_before_canonical_preview(string status)
    {
        var fake = new Fake();
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus(status);
        Assert.True(vm.RequiresDateRange);
        await vm.PreviewAsync();
        Assert.Empty(fake.PreviewRanges);
        Assert.False(vm.HasPreview);
        Assert.NotEmpty(vm.ErrorMessage);
        vm.RangeStartDate = Today.ToDateTime(TimeOnly.MinValue);
        vm.RangeEndDate = Today.AddDays(3).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        Assert.Single(fake.PreviewRanges);
        Assert.Equal(Today.ToString("yyyy-MM-dd"), fake.PreviewRanges[0]!.StartDate);
        Assert.Equal(Today.AddDays(3).ToString("yyyy-MM-dd"), fake.PreviewRanges[0]!.EndDate);
        Assert.True(vm.HasPreview);
        Assert.Empty(fake.CommitCalls);
        await vm.ConfirmAsync();
        Assert.True(vm.IsCompleted);
        Assert.Single(fake.CommitCalls);
        Assert.Equal("AUD-1", vm.AuditEventId);
    }

    [Fact]
    public async Task Changing_a_range_revokes_previous_preview_and_requires_new_explicit_review()
    {
        var fake = new Fake();
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeStartDate = Today.ToDateTime(TimeOnly.MinValue);
        vm.RangeEndDate = Today.AddDays(2).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        Assert.True(vm.HasPreview);
        vm.RangeEndDate = Today.AddDays(4).ToDateTime(TimeOnly.MinValue);
        Assert.False(vm.HasPreview);
        await vm.ConfirmAsync();
        Assert.Empty(fake.CommitCalls);
        await vm.PreviewAsync();
        Assert.Equal(2, fake.PreviewRanges.Count);
        Assert.True(vm.HasPreview);
    }

    [Fact]
    public async Task Rejects_wrong_server_range_and_fails_closed()
    {
        var fake = new Fake { ReturnWrongRange = true };
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-PERMISSION");
        vm.RangeStartDate = Today.ToDateTime(TimeOnly.MinValue);
        vm.RangeEndDate = Today.AddDays(2).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        Assert.False(vm.HasPreview);
        Assert.NotEmpty(vm.ErrorMessage);
        await vm.ConfirmAsync();
        Assert.Empty(fake.CommitCalls);
    }

    [Fact]
    public async Task Ordinary_status_clears_dates_and_does_not_pass_a_range()
    {
        var fake = new Fake();
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeStartDate = Today.ToDateTime(TimeOnly.MinValue);
        vm.RangeEndDate = Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        vm.ChooseStatus("STS-ABSENT");
        Assert.False(vm.RequiresDateRange);
        Assert.Null(vm.RangeStartDate);
        Assert.Null(vm.RangeEndDate);
        await vm.PreviewAsync();
        Assert.Single(fake.PreviewRanges);
        Assert.Null(fake.PreviewRanges[0]);
    }

    [Fact]
    public async Task Invalid_range_never_reaches_backend()
    {
        var fake = new Fake();
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeStartDate = Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        vm.RangeEndDate = Today.AddDays(2).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        Assert.Empty(fake.PreviewRanges);
        Assert.False(vm.HasPreview);
    }

    [Fact]
    public async Task Lost_commit_response_never_mints_a_second_operation_id_or_new_preview()
    {
        var fake = new Fake { LoseFirstCommit = true };
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeStartDate = Today.ToDateTime(TimeOnly.MinValue);
        vm.RangeEndDate = Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        vm.RangeEndDate = Today.AddDays(5).ToDateTime(TimeOnly.MinValue);
        Assert.Equal(Today.AddDays(1).ToDateTime(TimeOnly.MinValue), vm.RangeEndDate);
        await vm.PreviewAsync();
        Assert.Single(fake.PreviewRanges);
        await vm.ConfirmAsync();
        Assert.Equal(2, fake.CommitCalls.Count);
        Assert.Equal(fake.CommitCalls[0], fake.CommitCalls[1]);
        Assert.True(vm.IsCompleted);
    }

    [Fact]
    public async Task Ambiguous_commit_cannot_be_replaced_by_opening_a_new_review()
    {
        var fake = new Fake { LoseFirstCommit = true };
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeStartDate = Today.ToDateTime(TimeOnly.MinValue);
        vm.RangeEndDate = Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        await vm.OpenAsync(Today, "", [Person]);
        Assert.Equal(1, fake.StatusCalls);
        Assert.True(vm.HasUnverifiedCommit);
        Assert.True(vm.HasPreview);
        await vm.ConfirmAsync();
        Assert.Equal(2, fake.CommitCalls.Count);
        Assert.Equal(fake.CommitCalls[0], fake.CommitCalls[1]);
    }

    private static NativeAttendanceReviewViewModel New(Fake fake)
    {
        var vm = new NativeAttendanceReviewViewModel();
        vm.AttachBackend(fake, () => Owner);
        return vm;
    }

    private sealed class Fake : INativeAttendanceReviewBackend
    {
        public int StatusCalls;
        public bool ReturnWrongRange;
        public bool LoseFirstCommit;
        public List<NativeAttendanceDateRange?> PreviewRanges = [];
        public List<(string Token, string Id)> CommitCalls = [];

        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
            NativeIdentity actor, CancellationToken ct = default)
        {
            StatusCalls++;
            return Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>(
                [new("STS-LEAVE", "إجازة"), new("STS-PERMISSION", "إذن"),
                 new("STS-ABSENT", "غائب")]);
        }

        public Task<NativeAttendanceRevisionPreview> PreviewAsync(
            NativeIdentity actor, DateOnly date, string site,
            IReadOnlyList<string> personIds, string statusId,
            CancellationToken ct = default, NativeAttendanceDateRange? range = null)
        {
            PreviewRanges.Add(range);
            return Task.FromResult(new NativeAttendanceRevisionPreview(
                "VALIDATED-TOKEN-WITH-AT-LEAST-32-CHARACTERS",
                date, statusId, statusId,
                [new NativeAttendancePreviewPerson("P-1", "أحمد", "123",
                    "المركز", "موجود", "موجود")],
                range?.StartDate,
                ReturnWrongRange ? "2099-01-01" : range?.EndDate));
        }

        public Task<NativeAttendanceCommitReceipt> CommitAsync(
            NativeIdentity actor, string previewToken, string operationId,
            CancellationToken ct = default)
        {
            CommitCalls.Add((previewToken, operationId));
            if (LoseFirstCommit && CommitCalls.Count == 1)
                throw new IOException("Response lost after possible SQLCipher commit");
            return Task.FromResult(new NativeAttendanceCommitReceipt(1, "AUD-1"));
        }
    }
}
