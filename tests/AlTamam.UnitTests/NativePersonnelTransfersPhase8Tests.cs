using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test-first: internal/external WPF movement must reuse canonical,
/// actor-bound SQLCipher one-use preview and idempotent movement commit.</summary>
public sealed class NativePersonnelTransfersPhase8Tests
{
    private static readonly NativeIdentity Manager = new(
        "USR-MOVE", "admin", UserRole.GeneralAdministration, "MOVE-NONCE");
    private static readonly NativeDashboardSite[] Sites =
    [
        new("SITE-A", "المركز"), new("SITE-B", "الفرع")
    ];
    private static NativePersonnelDisplayRow Person() =>
        new(1, "PRN-001", "001234", "رقيب", "أحمد",
            "المركز", "على القوة", true, 7, []);

    private static NativePersonnelTransferViewModel Vm(
        FakeMovementBackend backend, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool>? isCurrent = null)
    {
        var vm = new NativePersonnelTransferViewModel();
        vm.AttachBackend(backend, actor, isCurrent ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Missing_session_supervisor_or_unselected_source_cannot_preview_or_commit()
    {
        var backend = new FakeMovementBackend();
        NativeIdentity? actor = null;
        var row = Person();
        var vm = Vm(backend, () => actor);
        vm.Open(row, "SITE-A", Sites, false);
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        vm.Open(row, "SITE-A", Sites, false);
        Assert.False(vm.IsOpen);
        actor = Manager;
        vm.Open(row, "", Sites, false);
        Assert.False(vm.IsOpen);
        vm.Open(row with { SiteName = "غير صحيح" }, "SITE-A", Sites, false);
        Assert.False(vm.IsOpen);
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Previews);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Internal_move_requires_real_preview_explicit_confirmation_and_one_write()
    {
        var backend = new FakeMovementBackend();
        var row = Person();
        var vm = Vm(backend, () => Manager, x => ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites, false);
        Assert.True(vm.IsOpen);
        Assert.False(vm.CanConfirm);
        vm.FormDestinationSiteId = "SITE-B";
        await vm.PreviewAsync();
        Assert.Equal(1, backend.Previews);
        Assert.Equal(0, backend.Writes);
        Assert.True(vm.CanConfirm);
        Assert.Equal("SITE-A", vm.Preview!.Draft.SourceSiteId);
        Assert.Equal("SITE-B", vm.Preview.Draft.DestinationSiteId);
        Assert.Equal("PRN-001", Assert.Single(vm.Preview.Persons).PersonId);
        Assert.Equal(7, Assert.Single(vm.Preview.Persons).Revision);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.NotNull(vm.Receipt);
        Assert.Equal("الفرع", vm.Receipt.DestinationName);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task External_move_requires_named_destination_and_keeps_original_identity()
    {
        var backend = new FakeMovementBackend();
        var row = Person();
        var vm = Vm(backend, () => Manager, x => ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites, true);
        await vm.PreviewAsync();
        Assert.Equal(0, backend.Previews);
        vm.FormExternalName = "جهة عمل خارجية";
        await vm.PreviewAsync();
        Assert.True(vm.CanConfirm);
        Assert.True(vm.Preview!.Draft.IsExternal);
        Assert.Equal("جهة عمل خارجية", vm.Preview.DestinationName);
        Assert.Empty(vm.Preview.Draft.DestinationSiteId);
        Assert.Equal("001234", Assert.Single(vm.Preview.Persons).ServiceNumberA);
        vm.FormExternalName = "جهة أخرى";
        Assert.Null(vm.Preview);
        Assert.False(vm.CanConfirm);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Changed_source_page_or_nonce_revokes_inflight_preview()
    {
        var backend = new FakeMovementBackend();
        NativeIdentity? actor = Manager;
        var row = Person();
        bool current = true;
        var vm = Vm(backend, () => actor, x => current && ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites, false);
        vm.FormDestinationSiteId = "SITE-B";
        var pending = new TaskCompletionSource<NativePersonnelMovementPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        Task previewing = vm.PreviewAsync();
        current = false;
        actor = Manager with { SessionNonce = "NEXT-SESSION" };
        vm.ForceCancel();
        pending.SetResult(backend.MakePreview(backend.Drafts[^1]));
        await previewing;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
        vm.Open(row, "SITE-A", Sites, false);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Forged_preview_snapshot_and_stale_row_cannot_be_approved()
    {
        var backend = new FakeMovementBackend { ForgePersonId = true };
        var row = Person();
        var vm = Vm(backend, () => Manager, x => ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites, false);
        vm.FormDestinationSiteId = "SITE-B";
        await vm.PreviewAsync();
        Assert.False(vm.CanConfirm);
        Assert.Null(vm.Preview);
        Assert.NotEmpty(vm.Error);
        Assert.Equal(0, backend.Writes);
        vm.ForceCancel();
        backend.ForgePersonId = false;
        vm.Open(row with { PersonId = "FORGED" }, "SITE-A", Sites, false);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Ambiguous_committed_reply_never_retries_with_new_operation_id()
    {
        var backend = new FakeMovementBackend { ThrowAfterWrite = true };
        var row = Person();
        var vm = Vm(backend, () => Manager, x => ReferenceEquals(x, row));
        vm.Open(row, "SITE-A", Sites, false);
        vm.FormDestinationSiteId = "SITE-B";
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.CanConfirm);
        Assert.Null(vm.Receipt);
        await vm.ConfirmAsync();
        Assert.Single(backend.OperationIds);
        Assert.Equal(1, backend.Writes);
    }

    private sealed class FakeMovementBackend : INativePersonnelMovementBackend
    {
        public int Previews, Writes;
        public bool ForgePersonId, ThrowAfterWrite;
        public Task<NativePersonnelMovementPreview>? Pending;
        public readonly List<NativePersonnelMovementDraft> Drafts = [];
        public readonly List<string> OperationIds = [];

        public Task<NativePersonnelMovementPreview> PreviewMovementAsync(
            NativeIdentity actor, NativePersonnelMovementDraft draft,
            CancellationToken ct = default)
        {
            Previews++;
            Drafts.Add(draft);
            return Pending ?? Task.FromResult(MakePreview(draft));
        }

        public NativePersonnelMovementPreview MakePreview(NativePersonnelMovementDraft draft) =>
            new(draft, DateTime.Now.ToString("yyyy-MM-dd"),
                new string('a', 64),
                draft.IsExternal ? draft.ExternalDestinationName : "الفرع",
                [new NativePersonnelMovementPerson(
                    ForgePersonId ? "OTHER-PERSON" : draft.PersonId,
                    draft.PersonName, draft.ServiceNumberA,
                    draft.Revision, draft.SourceSiteId, draft.SourceSiteName,
                    "ASSIGN-1")]);

        public Task<NativePersonnelMovementReceipt> CommitMovementAsync(
            NativeIdentity actor, NativePersonnelMovementPreview preview,
            string operationId, CancellationToken ct = default)
        {
            Writes++;
            OperationIds.Add(operationId);
            if (ThrowAfterWrite) throw new IOException("commit receipt unavailable");
            return Task.FromResult(new NativePersonnelMovementReceipt(
                preview.Draft.IsExternal, preview.BusinessDate, 1,
                preview.DestinationName));
        }
    }
}
