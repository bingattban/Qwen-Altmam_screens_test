using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test first: retirement never silently rewrites earlier attendance or moves the wrong site.</summary>
public sealed class NativeSiteRetirementPhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-RETIRE", "manager", UserRole.GeneralAdministration, "SITE-RETIRE-NONCE");
    private static readonly NativeSiteRow Site =
        new("SITE-RETIRE", "الموقع القديم", true, 4);
    private static readonly NativeSiteRow Protected =
        new("SITE-UNSPECIFIED", "غير محدد", true, 1);
    private static NativeSiteRetirementViewModel Create(
        FakeBackend backend, Func<NativeIdentity?> principal,
        Func<NativeSiteRow, bool> liveRow)
    {
        var vm = new NativeSiteRetirementViewModel();
        vm.AttachBackend(backend, principal, liveRow);
        return vm;
    }

    [Fact]
    public async Task Only_authorized_current_active_non_system_site_can_retire()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = null;
        var vm = Create(backend, () => actor, row => ReferenceEquals(row, Site));
        vm.Open(Site);
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        vm.Open(Site);
        Assert.False(vm.IsOpen);
        actor = Manager;
        vm.Open(Protected);
        Assert.False(vm.IsOpen);
        vm.Open(Site with { Name = "forged" });
        Assert.False(vm.IsOpen);
        vm.Open(Site with { Active = false });
        Assert.False(vm.IsOpen);
        vm.Open(Site);
        Assert.True(vm.IsOpen);
        Assert.Equal(0, backend.Reads);
        Assert.Equal(0, backend.Writes);
        await vm.PreviewAsync();
        Assert.Equal(1, backend.Reads);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Preview_shows_exact_site_version_count_date_and_fixed_destination()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager, x => ReferenceEquals(x, Site));
        vm.Open(Site);
        await vm.PreviewAsync();
        Assert.Equal("SITE-RETIRE", vm.Preview?.SiteId);
        Assert.Equal(4, vm.Preview?.Revision);
        Assert.Equal(2, vm.Preview?.ActiveCount);
        Assert.Equal("SITE-UNSPECIFIED", vm.Preview?.DestinationSiteId);
        Assert.Equal("غير محدد", vm.Preview?.DestinationSiteName);
        Assert.False(vm.CanConfirm);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Ineligible_or_stale_preview_cannot_enable_commit()
    {
        var backend = new FakeBackend { Blocked = true };
        var vm = Create(backend, () => Manager, x => ReferenceEquals(x, Site));
        vm.Open(Site);
        await vm.PreviewAsync();
        Assert.False(vm.CanConfirm);
        vm.ImpactConfirmed = true;
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Explicit_impact_checkbox_required_before_one_atomic_commit()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager, x => ReferenceEquals(x, Site));
        vm.Open(Site);
        await vm.PreviewAsync();
        Assert.False(vm.CanConfirm);
        vm.ImpactConfirmed = true;
        Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
        Assert.Equal("SITE-RETIRE", backend.LastSiteId);
        Assert.Equal(4, backend.LastRevision);
        Assert.Equal(2, backend.LastCount);
        Assert.True(backend.LastConfirmed);
        Assert.Equal("SITE-RETIRE", vm.Receipt?.SiteId);
        Assert.Equal("SITE-UNSPECIFIED", vm.Receipt?.DestinationSiteId);
        Assert.False(vm.Receipt?.Active);
        await vm.ConfirmAsync();
        Assert.Equal(1, backend.Writes);
    }

    [Fact]
    public async Task Forged_person_count_revision_or_destination_invalidates_preview()
    {
        foreach (string change in new[] { "count", "revision", "destination", "name", "date" })
        {
            var backend = new FakeBackend { Forge = change };
            var vm = Create(backend, () => Manager, x => ReferenceEquals(x, Site));
            vm.Open(Site);
            await vm.PreviewAsync();
            Assert.Null(vm.Preview);
            Assert.NotEmpty(vm.Error);
            vm.ImpactConfirmed = true;
            Assert.False(vm.CanConfirm);
            Assert.Equal(0, backend.Writes);
        }
    }

    [Fact]
    public async Task Logout_during_preview_discards_old_snapshot_and_forbids_write()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = Manager;
        bool current = true;
        var vm = Create(backend, () => actor, row => current && ReferenceEquals(row, Site));
        vm.Open(Site);
        var pending = new TaskCompletionSource<NativeSiteRetirementPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        Task loading = vm.PreviewAsync();
        actor = Manager with { SessionNonce = "CHANGED" };
        current = false;
        vm.ForceCancel();
        pending.SetResult(backend.Make(Site));
        await loading;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Lost_write_receipt_or_bad_receipt_disables_same_session_retry()
    {
        foreach (bool loseReply in new[] { true, false })
        {
            var backend = new FakeBackend { LostReply = loseReply, BadReceipt = !loseReply };
            var vm = Create(backend, () => Manager, x => ReferenceEquals(x, Site));
            vm.Open(Site);
            await vm.PreviewAsync();
            vm.ImpactConfirmed = true;
            await vm.ConfirmAsync();
            Assert.Null(vm.Receipt);
            Assert.True(vm.HasUnverifiedCommit);
            await vm.ConfirmAsync();
            Assert.Equal(1, backend.Writes);
            vm.ForceCancel();
            vm.Open(Site);
            Assert.False(vm.IsOpen);
        }
    }

    [Fact]
    public async Task Retirement_never_targets_an_operator_selected_destination()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager, x => ReferenceEquals(x, Site));
        vm.Open(Site);
        await vm.PreviewAsync();
        Assert.DoesNotContain("SITE-OTHER", vm.Preview!.DestinationSiteId);
        Assert.Contains("غير محدد", vm.ImpactNote);
        vm.ImpactConfirmed = true;
        await vm.ConfirmAsync();
        Assert.Equal("SITE-UNSPECIFIED", backend.LastDestination);
    }

    private sealed class FakeBackend : INativeSiteRetirementBackend
    {
        public int Reads, Writes;
        public bool Blocked, LostReply, BadReceipt;
        public string Forge = "";
        public string LastSiteId = "", LastDestination = "";
        public long LastRevision, LastCount;
        public bool LastConfirmed;
        public Task<NativeSiteRetirementPreview>? Pending;

        public NativeSiteRetirementPreview Make(NativeSiteRow row) =>
            new( row.SiteId,
                Forge == "name" ? "موقع مغشوش" : row.Name,
                Forge == "revision" ? row.Revision + 1 : row.Revision,
                Forge == "count" ? -1 : 2,
                Forge == "date" ? "2099-01-01" :
                    DateTime.Now.ToString("yyyy-MM-dd"),
                Forge == "destination" ? "SITE-OTHER" : "SITE-UNSPECIFIED",
                "غير محدد", !Blocked, Blocked ? "إغلاق اليوم" : "");

        public Task<NativeSiteRetirementPreview> PreviewRetirementAsync(
            NativeIdentity actor, string siteId, long revision,
            CancellationToken ct = default)
        {
            Reads++;
            return Pending ?? Task.FromResult(Make(Site));
        }

        public Task<NativeSiteRetirementReceipt> CommitRetirementAsync(
            NativeIdentity actor, NativeSiteRetirementPreview preview,
            string operationId, CancellationToken ct = default)
        {
            Writes++;
            LastSiteId = preview.SiteId;
            LastRevision = preview.Revision;
            LastCount = preview.ActiveCount;
            LastDestination = preview.DestinationSiteId;
            LastConfirmed = true;
            if (LostReply) throw new IOException("Commit may have persisted");
            return Task.FromResult(new NativeSiteRetirementReceipt(
                preview.SiteId, preview.SiteName,
                BadReceipt ? preview.Revision + 7 : preview.Revision + 1,
                preview.ActiveCount, preview.EffectiveDate,
                preview.DestinationSiteId, false));
        }
    }
}
