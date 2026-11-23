using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test-first Phase 8 authorized create/edit: canonical identity preview,
/// explicit human commit, immutable PersonId, nonce and stale-list protections.</summary>
public sealed class NativePersonnelMutationsPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-1", "admin", UserRole.GeneralAdministration, "PERSON-MUTATION-NONCE");

    [Fact]
    public async Task Missing_actor_and_supervisor_cannot_preview_or_write()
    {
        var backend = new Backend();
        NativeIdentity? current = null;
        var vm = New(backend, () => current);
        vm.OpenCreate([new NativeDashboardSite("SITE-A", "المركز")]);
        Assert.False(vm.IsOpen);
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Previews);
        Assert.Equal(0, backend.Writes);

        current = Manager with { Role = UserRole.Supervisor };
        vm.OpenCreate([new NativeDashboardSite("SITE-A", "المركز")]);
        await vm.PreviewAsync();
        Assert.Equal(0, backend.Previews);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Create_requires_real_archive_preview_and_explicit_one_time_confirmation()
    {
        var backend = new Backend();
        var vm = New(backend, () => Manager);
        vm.OpenCreate([new NativeDashboardSite("SITE-A", "المركز")]);
        vm.FormName = "أحمد صالح";
        vm.FormNumber = "001234";
        vm.FormSiteId = "SITE-A";
        Assert.False(vm.CanConfirm);
        await vm.PreviewAsync();
        Assert.Equal(1, backend.Previews);
        Assert.Equal(0, backend.Writes);
        Assert.True(vm.CanConfirm);
        Assert.Equal("SITE-A", backend.Drafts[0].SiteId);
        Assert.Equal("001234", backend.Drafts[0].Number);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.NotNull(vm.Receipt);
        Assert.Equal("PRN-NEW", vm.Receipt.PersonId);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Archived_identity_collision_and_changed_preview_fields_never_commit()
    {
        var backend = new Backend { ArchivedConflict = true };
        var vm = New(backend, () => Manager);
        vm.OpenCreate([new NativeDashboardSite("SITE-A", "المركز")]);
        vm.FormName = "أحمد صالح";
        vm.FormNumber = "001234";
        vm.FormSiteId = "SITE-A";
        await vm.PreviewAsync();
        Assert.NotNull(vm.Preview);
        Assert.True(vm.Preview.RequiresIdentityResolution);
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);

        backend.ArchivedConflict = false;
        vm.FormNumber = "009999"; // revokes old preview
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
        await vm.PreviewAsync();
        vm.FormName = "اسم جديد";
        Assert.False(vm.CanConfirm);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Edit_uses_frozen_real_person_id_revision_reason_and_authoritative_before_after()
    {
        var backend = new Backend();
        NativePersonnelDisplayRow row = Person();
        var vm = New(backend, () => Manager, item => ReferenceEquals(item, row));
        vm.OpenEdit(row);
        vm.FormName = "أحمد الجديد";
        vm.FormNumber = "000125";
        vm.FormReason = "تصحيح الرقم والاسم";
        await vm.PreviewAsync();
        Assert.True(vm.CanConfirm);
        Assert.False(vm.Preview!.Draft.IsCreate);
        Assert.Equal("PRN-1", vm.Preview.Draft.PersonId);
        Assert.Equal(5, vm.Preview.Draft.BaseRevision);
        Assert.Equal("أحمد", vm.Preview.OldName);
        Assert.Equal("1234", vm.Preview.OldNumber);
        Assert.Equal(0, backend.Writes);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Equal("PRN-1", vm.Receipt!.PersonId);
        Assert.Equal(6, vm.Receipt.Revision);
        Assert.Equal("تصحيح الرقم والاسم", Assert.Single(backend.CommitPreviews).Draft.Reason);
    }

    [Fact]
    public async Task Replaced_row_and_nonce_logout_cancel_stale_preview_and_display()
    {
        var backend = new Backend();
        NativeIdentity? actor = Manager;
        NativePersonnelDisplayRow row = Person();
        bool onPage = true;
        var vm = New(backend, () => actor, item => onPage && ReferenceEquals(item, row));
        vm.OpenEdit(row);
        vm.FormName = "أحمد الجديد";
        vm.FormNumber = "001236";
        vm.FormReason = "تصحيح الاسم";
        var pending = new TaskCompletionSource<NativePersonnelMutationPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.PendingPreview = pending.Task;
        var reading = vm.PreviewAsync();
        actor = Manager with { SessionNonce = "CHANGED" };
        onPage = false;
        vm.ForceCancel();
        pending.SetResult(backend.MakePreview(backend.Drafts[^1]));
        await reading;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        Assert.Equal(0, backend.Writes);
        vm.OpenEdit(row);
        Assert.False(vm.IsOpen); // row is no longer on the active canonical page
    }

    [Fact]
    public async Task Ambiguous_commit_error_locks_operation_instead_of_retrying_or_claiming_success()
    {
        var backend = new Backend { ThrowAfterWrite = true };
        var vm = New(backend, () => Manager);
        vm.OpenCreate([new NativeDashboardSite("SITE-A", "المركز")]);
        vm.FormName = "أحمد صالح";
        vm.FormNumber = "001234";
        vm.FormSiteId = "SITE-A";
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.Null(vm.Receipt);
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    private static NativePersonnelDisplayRow Person() =>
        new(1, "PRN-1", "1234", "رقيب", "أحمد", "المركز",
            "على القوة", true, 5, []);

    private static NativePersonnelMutationViewModel New(
        Backend backend, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool>? onPage = null)
    {
        var vm = new NativePersonnelMutationViewModel();
        vm.AttachBackend(backend, actor, onPage ?? (_ => true));
        return vm;
    }

    private sealed class Backend : INativePersonnelMutationBackend
    {
        public int Previews, Writes;
        public bool ArchivedConflict, ThrowAfterWrite;
        public Task<NativePersonnelMutationPreview>? PendingPreview;
        public readonly List<NativePersonnelMutationDraft> Drafts = [];
        public readonly List<NativePersonnelMutationPreview> CommitPreviews = [];

        public Task<NativePersonnelMutationPreview> PreviewAsync(
            NativeIdentity actor, NativePersonnelMutationDraft draft,
            CancellationToken ct = default)
        {
            Previews++;
            Drafts.Add(draft);
            return PendingPreview ?? Task.FromResult(MakePreview(draft));
        }

        public NativePersonnelMutationPreview MakePreview(
            NativePersonnelMutationDraft draft) =>
            new(draft, draft.Number, DateTime.Now.ToString("yyyy-MM-dd"),
                draft.IsCreate ? "" : new string('a', 64),
                draft.IsCreate && ArchivedConflict,
                draft.IsCreate && ArchivedConflict ? 1 : 0,
                draft.OriginalName, draft.OriginalNumber);

        public Task<NativePersonnelMutationReceipt> CommitAsync(
            NativeIdentity actor, NativePersonnelMutationPreview preview,
            string operationId, CancellationToken ct = default)
        {
            Writes++;
            CommitPreviews.Add(preview);
            if (ThrowAfterWrite) throw new IOException("Lost confirmation after commit");
            return Task.FromResult(new NativePersonnelMutationReceipt(
                preview.Draft.IsCreate, preview.Draft.IsCreate ? "PRN-NEW" :
                preview.Draft.PersonId, preview.Draft.IsCreate ? 1 : preview.Draft.BaseRevision + 1));
        }
    }
}
