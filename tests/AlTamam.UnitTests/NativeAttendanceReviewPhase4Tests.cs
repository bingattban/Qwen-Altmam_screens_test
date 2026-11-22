using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Explicit native operator intent must be separate from SQLCipher preview
/// and separate again from its idempotent, audited commit. A delayed reply
/// cannot replay a mutation, swap an identity or silently retry a write.
/// </summary>
public sealed class NativeAttendanceReviewPhase4Tests
{
    private static readonly NativeIdentity Owner =
        new("USR-1", "admin", UserRole.GeneralAdministration, "LIVE-NONCE");
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);
    private static readonly NativeAutoRegisterRow Person =
        new("P-1", "123", "أحمد", "المركز", Today.ToString("yyyy-MM-dd"),
            "تحضير تلقائي 08:00", "2026-11-22T05:00:00Z", "موجود", []);

    [Fact]
    public async Task Without_live_principal_selection_does_not_show_or_query_operator_actions()
    {
        var fake = new Fake();
        var vm = new NativeAttendanceReviewViewModel();
        vm.AttachBackend(fake, () => null);
        await vm.OpenAsync(Today, "", [Person]);
        Assert.False(vm.IsOpen);
        Assert.Equal(0, fake.StatusCalls);
        Assert.Empty(fake.PreviewCalls);
        Assert.Empty(fake.CommitCalls);
    }

    [Fact]
    public async Task Open_and_choose_status_are_nonmutating_and_do_not_issue_preview()
    {
        var fake = new Fake();
        var vm = New(fake);
        await vm.OpenAsync(Today, "SITE-1", [Person]);
        Assert.True(vm.IsOpen);
        Assert.False(vm.HasPreview);
        Assert.Single(vm.StatusChoices);
        Assert.Equal("STS-ABSENT", vm.StatusChoices[0].StatusId);
        vm.ChooseStatus("STS-ABSENT");
        Assert.Empty(fake.PreviewCalls);
        Assert.Empty(fake.CommitCalls);
    }

    [Fact]
    public async Task Preview_requires_active_choice_and_exact_selected_people()
    {
        var fake = new Fake();
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        await vm.PreviewAsync();
        Assert.Empty(fake.PreviewCalls);
        vm.ChooseStatus("UNKNOWN");
        Assert.False(vm.HasPreview);
        vm.ChooseStatus("STS-ABSENT");
        await vm.PreviewAsync();
        Assert.Single(fake.PreviewCalls);
        Assert.True(vm.HasPreview);
        Assert.False(vm.IsCommitting);
        Assert.Empty(fake.CommitCalls);
        Assert.Equal("أحمد", vm.PreviewPersons[0].Name);
    }

    [Fact]
    public async Task Mismatched_server_preview_fails_closed_without_commit()
    {
        var fake = new Fake { ReturnDifferentPerson = true };
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-ABSENT");
        await vm.PreviewAsync();
        Assert.False(vm.HasPreview);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Empty(fake.CommitCalls);
    }

    [Fact]
    public async Task Confirmation_is_only_action_that_commits_and_uses_one_stable_intent_id()
    {
        var fake = new Fake();
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-ABSENT");
        await vm.PreviewAsync();
        Assert.Empty(fake.CommitCalls);
        await vm.ConfirmAsync();
        Assert.Single(fake.CommitCalls);
        Assert.Equal("VALIDATED-TOKEN-WITH-AT-LEAST-32-CHARACTERS",
            fake.CommitCalls[0].Token);
        Assert.InRange(fake.CommitCalls[0].OperationId.Length, 16, 128);
        Assert.True(vm.IsCompleted);
        Assert.False(vm.HasPreview);
        Assert.Equal(1, vm.CommittedCount);
        Assert.Equal("AUD-1", vm.AuditEventId);
    }

    [Fact]
    public async Task Failed_or_lost_commit_response_never_auto_retries_new_intent()
    {
        var fake = new Fake { LoseFirstCommit = true };
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-ABSENT");
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.Single(fake.CommitCalls);
        Assert.True(vm.HasPreview);
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.IsCompleted);
        Assert.NotEmpty(vm.ErrorMessage);
        // An ambiguous approved write may already have committed: never
        // silently mint a new one-time token or new intent identifier.
        await vm.PreviewAsync();
        Assert.Single(fake.PreviewCalls);
        vm.Cancel();
        Assert.True(vm.IsOpen);
        Assert.True(vm.HasUnverifiedCommit);
        await vm.ConfirmAsync(); // separate explicit operator action
        Assert.Equal(2, fake.CommitCalls.Count);
        Assert.Equal(fake.CommitCalls[0].OperationId, fake.CommitCalls[1].OperationId);
        Assert.Equal(fake.CommitCalls[0].Token, fake.CommitCalls[1].Token);
        Assert.True(vm.IsCompleted);
    }

    [Fact]
    public async Task Logout_or_nonce_replacement_revokes_preview_and_blocks_commit()
    {
        var fake = new Fake();
        NativeIdentity? current = Owner;
        var vm = new NativeAttendanceReviewViewModel();
        vm.AttachBackend(fake, () => current);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-ABSENT");
        await vm.PreviewAsync();
        current = Owner with { SessionNonce = "REPLACED" };
        await vm.ConfirmAsync();
        Assert.Empty(fake.CommitCalls);
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.PreviewPersons);
    }

    [Fact]
    public async Task Cancel_after_preview_does_not_write()
    {
        var fake = new Fake();
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-ABSENT");
        await vm.PreviewAsync();
        vm.Cancel();
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.PreviewPersons);
        Assert.Empty(fake.CommitCalls);
    }

    [Fact]
    public async Task More_than_one_hundred_people_are_rejected_before_status_lookup()
    {
        var fake = new Fake();
        var vm = New(fake);
        var people = Enumerable.Range(0, 101).Select(i =>
            Person with { PersonId = "P-" + i }).ToArray();
        await vm.OpenAsync(Today, "", people);
        Assert.False(vm.IsOpen);
        Assert.Equal(0, fake.StatusCalls);
    }

    [Fact]
    public async Task Late_preview_after_cancel_never_restores_approval()
    {
        var fake = new Fake();
        var pending = new TaskCompletionSource<NativeAttendanceRevisionPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.DelayedPreview = pending.Task;
        var vm = New(fake);
        await vm.OpenAsync(Today, "", [Person]);
        vm.ChooseStatus("STS-ABSENT");
        var read = vm.PreviewAsync();
        vm.Cancel();
        pending.SetResult(Fake.ValidPreview);
        await read;
        Assert.False(vm.IsOpen);
        Assert.False(vm.HasPreview);
        Assert.Empty(fake.CommitCalls);
    }

    private static NativeAttendanceReviewViewModel New(Fake fake)
    {
        var vm = new NativeAttendanceReviewViewModel();
        vm.AttachBackend(fake, () => Owner);
        return vm;
    }

    private sealed class Fake : INativeAttendanceReviewBackend
    {
        public static NativeAttendanceRevisionPreview ValidPreview =>
            new("VALIDATED-TOKEN-WITH-AT-LEAST-32-CHARACTERS",
                Today, "STS-ABSENT", "غائب",
                [new NativeAttendancePreviewPerson("P-1", "أحمد", "123",
                    "المركز", "موجود", "موجود")]);
        public int StatusCalls;
        public bool ReturnDifferentPerson;
        public bool LoseFirstCommit;
        public Task<NativeAttendanceRevisionPreview>? DelayedPreview;
        public List<(string[] Persons, string Site, string Status)> PreviewCalls = [];
        public List<(string Token, string OperationId)> CommitCalls = [];

        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
            NativeIdentity actor, CancellationToken ct = default)
        {
            StatusCalls++;
            return Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>(
                [new("STS-ABSENT", "غائب")]);
        }

        public async Task<NativeAttendanceRevisionPreview> PreviewAsync(
            NativeIdentity actor, DateOnly date, string site,
            IReadOnlyList<string> persons, string statusId, CancellationToken ct = default)
        {
            PreviewCalls.Add((persons.ToArray(), site, statusId));
            if (DelayedPreview is { } delayed) return await delayed;
            return ReturnDifferentPerson
                ? ValidPreview with { Persons =
                    [new NativeAttendancePreviewPerson("WRONG", "غير معروف", "99",
                        "المركز", "موجود", "موجود")] }
                : ValidPreview;
        }

        public Task<NativeAttendanceCommitReceipt> CommitAsync(
            NativeIdentity actor, string previewToken, string operationId,
            CancellationToken ct = default)
        {
            CommitCalls.Add((previewToken, operationId));
            if (LoseFirstCommit && CommitCalls.Count == 1)
                throw new IOException("Connection timed out after possible SQLCipher write");
            return Task.FromResult(new NativeAttendanceCommitReceipt(1, "AUD-1"));
        }
    }
}
