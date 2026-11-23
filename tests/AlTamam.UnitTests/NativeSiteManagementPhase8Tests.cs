using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test FIRST: native sites preserve original SQLCipher versioned catalogue.</summary>
public sealed class NativeSiteManagementPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-SITES", "admin", UserRole.GeneralAdministration, "SITES-NONCE");
    private static readonly NativeSiteRow Active = new("SITE-001", "الموقع القديم", true, 3);
    private static readonly NativeSiteRow Protected =
        new("SITE-UNSPECIFIED", "غير محدد", true, 1);
    private static readonly NativeSiteRow Retired =
        new("SITE-RETIRED", "موقع سابق", false, 5);

    private static NativeSiteCatalogViewModel Create(FakeBackend backend,
        Func<NativeIdentity?> principal)
    {
        var vm = new NativeSiteCatalogViewModel();
        vm.AttachBackend(backend, principal);
        return vm;
    }

    [Fact]
    public async Task No_session_or_supervisor_must_not_read_or_edit_site_catalogue()
    {
        NativeIdentity? actor = null;
        var backend = new FakeBackend();
        var vm = Create(backend, () => actor);
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.Reads);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Sites_are_canonical_including_retired_and_locked_system_rows()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        Assert.True(vm.IsOpen);
        Assert.Equal(3, vm.Sites.Count);
        Assert.Equal(5, vm.Sites[^1].Revision);
        Assert.False(vm.Sites[^1].Active);
        vm.BeginRename(vm.Sites[1]);
        Assert.False(vm.IsReviewing);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Create_requires_valid_name_separate_review_and_exact_audited_receipt()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginCreate();
        vm.DraftName = "x";
        vm.Review();
        Assert.False(vm.IsReviewing);
        Assert.Equal(0, backend.Writes);
        vm.DraftName = "موقع جديد";
        vm.Review();
        Assert.True(vm.IsReviewing);
        Assert.Equal(0, backend.Writes);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Equal("SITE-NEW", vm.Receipt?.SiteId);
        Assert.Equal(1, vm.Receipt?.Revision);
        Assert.Equal("موقع جديد", vm.Receipt?.Name);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Rename_uses_selected_row_identity_and_expected_revision_only()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginRename(Active with { Name = "forged" });
        Assert.False(vm.IsReviewing);
        vm.BeginRename(vm.Sites[1]); // protected SITE-UNSPECIFIED
        Assert.False(vm.IsReviewing);
        vm.BeginRename(vm.Sites[0]);
        vm.DraftName = "الموقع المعتمد الجديد";
        vm.Review();
        Assert.True(vm.IsReviewing);
        await vm.ConfirmAsync();
        Assert.Equal("SITE-001", backend.LastSiteId);
        Assert.Equal(3, backend.LastRevision);
        Assert.Equal("الموقع المعتمد الجديد", backend.LastName);
        Assert.Equal(4, vm.Receipt?.Revision);
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Changing_name_invalidates_prior_review_without_writing()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginRename(vm.Sites[0]);
        vm.DraftName = "موقع مراجعة جديد";
        vm.Review();
        Assert.True(vm.CanConfirm);
        vm.DraftName = "اسم ثالث";
        Assert.False(vm.IsReviewing);
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Async_page_result_cannot_leak_after_logout_or_close()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        var waiting = new TaskCompletionSource<IReadOnlyList<NativeSiteRow>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.PendingRead = waiting.Task;
        var vm = Create(backend, () => actor);
        Task opening = vm.OpenAsync();
        actor = Manager with { SessionNonce = "NEW-NONCE" };
        vm.Close();
        waiting.SetResult([Active]);
        await opening;
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Sites);
    }

    [Fact]
    public async Task Forged_or_stale_confirmation_receipt_never_looks_successful()
    {
        var backend = new FakeBackend { ForgeReceipt = true };
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginRename(vm.Sites[0]);
        vm.DraftName = "الموقع بعد التعديل";
        vm.Review();
        await vm.ConfirmAsync();
        Assert.Null(vm.Receipt);
        Assert.True(vm.HasUnverifiedCommit);
        Assert.Equal(1, backend.Writes);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Lost_write_reply_never_retries_and_locks_same_manager_session()
    {
        var backend = new FakeBackend { LostReply = true };
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        vm.BeginCreate();
        vm.DraftName = "موقع أصلي";
        vm.Review();
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.Equal(1, backend.Writes);
        vm.Close();
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        Assert.Equal(1, backend.Writes);
    }

    private sealed class FakeBackend : INativeSiteCatalogBackend
    {
        public int Reads, Writes;
        public bool ForgeReceipt, LostReply;
        public Task<IReadOnlyList<NativeSiteRow>>? PendingRead;
        public string LastSiteId = "", LastName = "";
        public long LastRevision;
        public Task<IReadOnlyList<NativeSiteRow>> ReadSitesAsync(
            NativeIdentity actor, CancellationToken ct = default)
        {
            Reads++;
            return PendingRead ?? Task.FromResult<IReadOnlyList<NativeSiteRow>>(
                [Active, Protected, Retired]);
        }
        public Task<NativeSiteMutationReceipt> CreateSiteAsync(
            NativeIdentity actor, string name, string operationId,
            CancellationToken ct = default)
        {
            Writes++;
            LastName = name;
            if (LostReply) throw new IOException("Unknown write outcome");
            return Task.FromResult(new NativeSiteMutationReceipt(
                ForgeReceipt ? "FORGED" : "SITE-NEW", name, 1));
        }
        public Task<NativeSiteMutationReceipt> RenameSiteAsync(
            NativeIdentity actor, string siteId, long revision, string name,
            string operationId, CancellationToken ct = default)
        {
            Writes++;
            LastSiteId = siteId;
            LastName = name;
            LastRevision = revision;
            if (LostReply) throw new IOException("Unknown write outcome");
            return Task.FromResult(new NativeSiteMutationReceipt(
                ForgeReceipt ? "SITE-FORGED" : siteId,
                name, revision + 1));
        }
    }
}
