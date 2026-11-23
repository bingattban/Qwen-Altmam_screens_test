using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test-first: one-person dismissal is an audited SQLCipher transition,
/// not physical deletion, and never bypasses canonical five-second confirmation.</summary>
public sealed class NativePersonnelSeparationPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-SEP", "admin", UserRole.GeneralAdministration, "SEP-NONCE");
    private static readonly NativeDashboardSite[] Sites =
        [new("SITE-A", "المركز"), new("SITE-B", "الفرع")];

    private static NativePersonnelDisplayRow Person() =>
        new(1, "PRN-SEP", "0001234", "رقيب", "أحمد", "المركز",
            "على القوة", true, 7, []);

    private static NativePersonnelSeparationViewModel Create(
        FakeBackend backend, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool>? current = null)
    {
        var vm = new NativePersonnelSeparationViewModel();
        vm.AttachBackend(backend, actor, current ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Supervisor_missing_session_and_unselected_source_are_denied()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = null;
        var row = Person();
        var vm = Create(backend, () => actor);
        vm.Open(row, "SITE-A", Sites);
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        vm.Open(row, "SITE-A", Sites);
        Assert.False(vm.IsOpen);
        actor = Manager;
        vm.Open(row, "", Sites);
        Assert.False(vm.IsOpen);
        vm.Open(row with { SiteName = "موقع مزور" }, "SITE-A", Sites);
        Assert.False(vm.IsOpen);
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Previews);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Reason_and_exact_source_must_precede_canonical_nonwriting_preview()
    {
        var backend = new FakeBackend();
        var row = Person();
        var vm = Create(backend, () => Manager, x => ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites);
        vm.FormReason = "قصير";
        await vm.PreviewAsync();
        Assert.Equal(0, backend.Previews);
        Assert.Equal(0, backend.Writes);
        Assert.False(vm.CanConfirm);
        vm.FormReason = "سبب قانوني واضح ومحدد للفصل";
        await vm.PreviewAsync();
        Assert.Equal(1, backend.Previews);
        Assert.Equal("SITE-A", backend.Drafts[^1].SourceSiteId);
        Assert.Equal("PRN-SEP", backend.Drafts[^1].PersonId);
        Assert.Equal(0, backend.Writes);
        vm.FormReason = "سبب مختلف غير المعتمد";
        Assert.Null(vm.Preview);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task Mandatory_five_second_review_blocks_early_confirmation()
    {
        var backend = new FakeBackend();
        var row = Person();
        var vm = Create(backend, () => Manager, x => ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites);
        vm.FormReason = "سبب الفصل المسجل في سجل العمليات";
        await vm.PreviewAsync();
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
        await Task.Delay(TimeSpan.FromMilliseconds(5200));
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.NotNull(vm.Receipt);
        Assert.Equal("PRN-SEP", vm.Receipt!.PersonId);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Forged_person_assignment_or_revision_preview_cannot_be_approved()
    {
        var backend = new FakeBackend { Forge = true };
        var row = Person();
        var vm = Create(backend, () => Manager, x => ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites);
        vm.FormReason = "سبب فصل قانوني موثق";
        await vm.PreviewAsync();
        Assert.Null(vm.Preview);
        Assert.False(vm.CanConfirm);
        Assert.NotEmpty(vm.Error);
        Assert.Equal(0, backend.Writes);
        vm.ForceCancel();
        backend.Forge = false;
        vm.Open(row with { PersonId = "FORGED" }, "SITE-A", Sites);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Navigation_or_nonce_change_revokes_pending_preview_and_delay()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        var row = Person();
        bool current = true;
        var vm = Create(backend, () => actor, x => current && ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites);
        vm.FormReason = "سبب فصل موثق في الملف";
        var pending = new TaskCompletionSource<NativePersonnelSeparationPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        Task operation = vm.PreviewAsync();
        current = false;
        actor = Manager with { SessionNonce = "REPLACED" };
        vm.ForceCancel();
        pending.SetResult(backend.MakePreview(backend.Drafts[^1]));
        await operation;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Lost_commit_receipt_never_automatically_retries_or_reuses_approval()
    {
        var backend = new FakeBackend { LostReply = true };
        var row = Person();
        var vm = Create(backend, () => Manager, x => ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites);
        vm.FormReason = "سبب الفصل الرسمي الموثق";
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
        await Task.Delay(TimeSpan.FromMilliseconds(5200));
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.True(vm.HasUnverifiedCommit);
        Assert.Null(vm.Receipt);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Single(backend.OperationIds);
        vm.ForceCancel();
        Assert.False(vm.IsOpen);
        vm.Open(row, "SITE-A", Sites);
        Assert.False(vm.IsOpen); // ambiguous operation locks this same session
    }

    private sealed class FakeBackend : INativePersonnelSeparationBackend
    {
        public int Previews, Writes;
        public bool Forge, LostReply;
        public Task<NativePersonnelSeparationPreview>? Pending;
        public readonly List<NativePersonnelSeparationDraft> Drafts = [];
        public readonly List<string> OperationIds = [];
        public NativePersonnelSeparationPreview MakePreview(NativePersonnelSeparationDraft draft) =>
            new(Forge ? draft with { Revision = draft.Revision + 1 } : draft,
                DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd"),
                new string('a', 64), "ASSIGN-A", 5);
        public Task<NativePersonnelSeparationPreview> PreviewSeparationAsync(
            NativeIdentity actor, NativePersonnelSeparationDraft draft,
            CancellationToken ct = default)
        {
            Previews++;
            Drafts.Add(draft);
            return Pending ?? Task.FromResult(MakePreview(draft));
        }
        public Task<NativePersonnelSeparationReceipt> CommitSeparationAsync(
            NativeIdentity actor, NativePersonnelSeparationPreview preview,
            string operationId, CancellationToken ct = default)
        {
            Writes++;
            OperationIds.Add(operationId);
            if (LostReply) throw new IOException("Response lost after possible commit");
            return Task.FromResult(new NativePersonnelSeparationReceipt(
                preview.Draft.PersonId, "MOV-SEP-ONE", preview.BusinessDate, true));
        }
    }
}
