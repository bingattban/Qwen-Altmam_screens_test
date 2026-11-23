using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test-first: a return reuses one audited PersonId and never guesses human identity.</summary>
public sealed class NativeArchivedReturnPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-RETURN", "admin", UserRole.GeneralAdministration, "RETURN-NONCE");
    private static readonly NativeDashboardSite[] Sites =
        [new("SITE-RETURN", "موقع العودة"), new("SITE-OTHER", "موقع آخر")];

    private static NativePersonnelDisplayRow Archived() =>
        new(1, "PRN-RETURN", "12345", "رقيب", "فرد مؤرشف",
            "موقع قديم", "مفصول", false, 3, []);

    private static NativeArchivedReturnViewModel Create(FakeBackend backend,
        Func<NativeIdentity?> liveActor,
        Func<NativePersonnelDisplayRow, bool>? current = null)
    {
        var vm = new NativeArchivedReturnViewModel();
        vm.AttachBackend(backend, liveActor, current ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Only_current_archived_row_and_manager_can_start_return()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = null;
        var row = Archived();
        var vm = Create(backend, () => actor, r => ReferenceEquals(r, row));
        vm.Open(row, Sites);
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        vm.Open(row, Sites);
        Assert.False(vm.IsOpen);
        actor = Manager;
        vm.Open(row with { Active = true, StatusName = "على القوة" }, Sites);
        Assert.False(vm.IsOpen);
        vm.Open(row with { PersonId = "INJECTED" }, Sites);
        Assert.False(vm.IsOpen);
        vm.Open(row, Sites);
        Assert.True(vm.IsOpen);
        await vm.PreviewAsync();
        Assert.Equal(0, backend.Previews);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Exact_destination_and_current_identity_precede_nonwriting_preview()
    {
        var backend = new FakeBackend();
        var row = Archived();
        var vm = Create(backend, () => Manager, r => ReferenceEquals(r, row));
        vm.Open(row, Sites);
        vm.SelectedSiteId = "MISSING";
        await vm.PreviewAsync();
        Assert.Equal(0, backend.Previews);
        vm.SelectedSiteId = "SITE-RETURN";
        await vm.PreviewAsync();
        Assert.Equal(1, backend.Previews);
        Assert.Equal("PRN-RETURN", backend.Drafts[^1].PersonId);
        Assert.Equal(3, backend.Drafts[^1].OrganizationalStatus);
        Assert.Equal("SITE-RETURN", backend.Drafts[^1].DestinationSiteId);
        Assert.Equal("فرد مؤرشف", Assert.Single(vm.Preview!.Candidates).Name);
        Assert.False(vm.CanConfirm);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Identity_confirmation_is_explicit_even_when_multiple_candidates_match()
    {
        var backend = new FakeBackend { OtherCandidate = true };
        var row = Archived();
        var vm = Create(backend, () => Manager, r => ReferenceEquals(r, row));
        vm.Open(row, Sites);
        vm.SelectedSiteId = "SITE-RETURN";
        await vm.PreviewAsync();
        Assert.Equal(2, vm.Preview!.Candidates.Count);
        Assert.False(vm.HasNumberConflict);
        Assert.False(vm.CanConfirm);
        vm.IdentityConfirmed = true;
        Assert.True(vm.CanConfirm);
        vm.SelectedSiteId = "SITE-OTHER";
        Assert.Null(vm.Preview);
        Assert.False(vm.CanConfirm);
        Assert.False(vm.IdentityConfirmed);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Duplicate_archived_number_must_be_resolved_before_any_return_commit()
    {
        var backend = new FakeBackend { DuplicateNumber = true };
        var row = Archived();
        var vm = Create(backend, () => Manager, r => ReferenceEquals(r, row));
        vm.Open(row, Sites);
        vm.SelectedSiteId = "SITE-RETURN";
        await vm.PreviewAsync();
        Assert.True(vm.HasNumberConflict);
        Assert.Equal(2, vm.Preview!.Candidates.Count);
        vm.IdentityConfirmed = true;
        Assert.False(vm.CanConfirm);
        Assert.NotEmpty(vm.Error);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Stale_candidate_revision_or_wrong_person_never_enables_confirm()
    {
        var backend = new FakeBackend { Forge = true };
        var row = Archived();
        var vm = Create(backend, () => Manager, r => ReferenceEquals(r, row));
        vm.Open(row, Sites);
        vm.SelectedSiteId = "SITE-RETURN";
        await vm.PreviewAsync();
        Assert.Null(vm.Preview);
        Assert.NotEmpty(vm.Error);
        vm.IdentityConfirmed = true;
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Logout_or_source_page_change_drops_late_preview()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        var row = Archived();
        bool rowCurrent = true;
        var vm = Create(backend, () => actor, r => rowCurrent && ReferenceEquals(r, row));
        vm.Open(row, Sites);
        vm.SelectedSiteId = "SITE-RETURN";
        var pending = new TaskCompletionSource<NativeArchivedReturnPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        Task inFlight = vm.PreviewAsync();
        rowCurrent = false;
        actor = Manager with { SessionNonce = "REPLACED" };
        vm.ForceCancel();
        pending.SetResult(backend.MakePreview(backend.Drafts[^1]));
        await inFlight;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Explicit_same_human_confirmation_commits_once_without_new_identity()
    {
        var backend = new FakeBackend();
        var row = Archived();
        var vm = Create(backend, () => Manager, r => ReferenceEquals(r, row));
        vm.Open(row, Sites);
        vm.SelectedSiteId = "SITE-RETURN";
        await vm.PreviewAsync();
        vm.IdentityConfirmed = true;
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Equal("PRN-RETURN", vm.Receipt?.PersonId);
        Assert.Equal("SITE-RETURN", vm.Receipt?.SiteId);
        Assert.Equal(4, vm.Receipt?.Revision);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Single(backend.OperationIds);
    }

    [Fact]
    public async Task Lost_write_receipt_never_resends_and_blocks_same_session_retry()
    {
        var backend = new FakeBackend { LostReply = true };
        var row = Archived();
        var vm = Create(backend, () => Manager, r => ReferenceEquals(r, row));
        vm.Open(row, Sites);
        vm.SelectedSiteId = "SITE-RETURN";
        await vm.PreviewAsync();
        vm.IdentityConfirmed = true;
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.True(vm.HasUnverifiedCommit);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        vm.ForceCancel();
        vm.Open(row, Sites);
        Assert.False(vm.IsOpen);
        Assert.Single(backend.OperationIds);
    }

    private sealed class FakeBackend : INativeArchivedReturnBackend
    {
        public int Previews, Writes;
        public bool OtherCandidate, DuplicateNumber, Forge, LostReply;
        public Task<NativeArchivedReturnPreview>? Pending;
        public readonly List<NativeArchivedReturnDraft> Drafts = [];
        public readonly List<string> OperationIds = [];

        public NativeArchivedReturnPreview MakePreview(NativeArchivedReturnDraft draft)
        {
            var candidate = new NativeArchivedReturnCandidate(
                Forge ? "FORGED" : draft.PersonId,
                draft.OriginalName, draft.ServiceNumberA,
                Forge ? draft.Revision + 1 : draft.Revision,
                draft.OrganizationalStatus, "2026-11-20", "الموقع القديم",
                "فصل", true, true);
            IReadOnlyList<NativeArchivedReturnCandidate> candidates = DuplicateNumber
                ? [candidate, candidate with { PersonId = "DUPLICATE-NUMBER", Revision = 1 }]
                : OtherCandidate
                    ? [candidate, candidate with {
                        PersonId = "OTHER", Revision = 1,
                        ServiceNumberA = "98765", NumberMatch = false }]
                    : [candidate];
            return new NativeArchivedReturnPreview(
                draft, DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
                new string('a', 64), true, candidates);
        }

        public Task<NativeArchivedReturnPreview> PreviewReturnAsync(
            NativeIdentity actor, NativeArchivedReturnDraft draft,
            CancellationToken ct = default)
        {
            Previews++;
            Drafts.Add(draft);
            return Pending ?? Task.FromResult(MakePreview(draft));
        }

        public Task<NativeArchivedReturnReceipt> CommitReturnAsync(
            NativeIdentity actor, NativeArchivedReturnPreview preview,
            string operationId, CancellationToken ct = default)
        {
            Writes++;
            OperationIds.Add(operationId);
            if (LostReply) throw new IOException("Commit may have persisted; reply lost");
            return Task.FromResult(new NativeArchivedReturnReceipt(
                preview.Draft.PersonId, "MOV-RETURN-001",
                preview.Draft.DestinationSiteId,
                preview.BusinessDate, preview.Draft.Revision + 1, true));
        }
    }
}
