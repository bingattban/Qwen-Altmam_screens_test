using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test-first, immutable SQLCipher separation archive. No restoration or write route.</summary>
public sealed class NativeSeparationArchivePhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-SEP-AR", "admin", UserRole.GeneralAdministration, "SEP-ARCHIVE-NONCE");
    private static NativeSeparationArchiveRow Row() =>
        new("MOV-SEP-1", "PRN-SEP-1", "محمد - الاسم وقت الفصل",
            "001234", "المركز", "سبب إداري موثق للفصل", "2026-11-21",
            "2026-11-21T09:00:00Z", "USR-ADMIN", "admin - مدير");
    private static NativeSeparationArchivePage Page(NativeSeparationArchiveFilter filter,
        bool hasNext = false) => new(filter,
        hasNext ? (long)filter.Page * filter.PageSize + 1 :
            (long)(filter.Page - 1) * filter.PageSize + 1,
        hasNext, [Row()]);
    private static NativeSeparationArchiveDetail Detail() =>
        new("MOV-SEP-1", "PRN-SEP-1", "محمد - الاسم وقت الفصل",
            "001234", "المركز", "سبب إداري موثق للفصل", "2026-11-21",
            "2026-11-21T09:00:00Z", "USR-ADMIN");

    private static NativeSeparationArchiveViewModel Create(FakeBackend backend,
        Func<NativeIdentity?> liveActor)
    {
        var vm = new NativeSeparationArchiveViewModel();
        vm.AttachBackend(backend, liveActor);
        return vm;
    }

    [Fact]
    public async Task Archive_is_denied_without_live_manager_or_unchanged_session()
    {
        NativeIdentity? actor = null;
        var backend = new FakeBackend();
        var vm = Create(backend, () => actor);
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.PageReads);
        actor = Manager;
        await vm.OpenAsync();
        Assert.True(vm.IsOpen);
        actor = Manager with { SessionNonce = "NEW" };
        await vm.RefreshAsync();
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Snapshot_reason_actor_and_date_survive_read_only_paging()
    {
        var backend = new FakeBackend { HasNext = true };
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        Assert.Equal("محمد - الاسم وقت الفصل", Assert.Single(vm.Rows).Name);
        Assert.Equal("سبب إداري موثق للفصل", vm.Rows[0].Reason);
        Assert.Equal("admin - مدير", vm.Rows[0].ActorDisplay);
        Assert.Equal("2026-11-21", vm.Rows[0].EffectiveDate);
        Assert.Equal(50, backend.Filters[0].PageSize);
        await vm.NextPageAsync();
        Assert.Equal(2, backend.Filters[^1].Page);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Search_and_site_filter_reset_page_without_forging_history()
    {
        var backend = new FakeBackend { HasNext = true };
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        await vm.NextPageAsync();
        await vm.SearchAsync("001234");
        Assert.Equal(1, backend.Filters[^1].Page);
        Assert.Equal("001234", backend.Filters[^1].SearchTerm);
        await vm.SelectSiteAsync("SITE-1");
        Assert.Equal("SITE-1", backend.Filters[^1].SiteId);
        Assert.Equal(1, vm.Page);
        await vm.SelectSiteAsync(new string('X', 129));
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.LoadError);
        await vm.SearchAsync(new string('Z', 101));
        Assert.Empty(vm.Rows);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Stale_inflight_page_and_details_are_hidden_after_close()
    {
        NativeIdentity? actor = Manager;
        var backend = new FakeBackend();
        var pending = new TaskCompletionSource<NativeSeparationArchivePage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.DelayedPage = pending.Task;
        var vm = Create(backend, () => actor);
        Task reading = vm.OpenAsync();
        actor = null;
        vm.Close();
        pending.SetResult(Page(new NativeSeparationArchiveFilter("", "", 1)));
        await reading;
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Rows);
        Assert.Null(vm.SelectedDetail);
    }

    [Fact]
    public async Task Forged_details_and_unselected_movement_ids_are_rejected()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        backend.ForgeDetail = true;
        await vm.OpenDetailsAsync(vm.Rows[0]);
        Assert.Null(vm.SelectedDetail);
        Assert.NotEmpty(vm.DetailError);
        int reads = backend.DetailReads;
        await vm.OpenDetailsAsync(vm.Rows[0] with { MovementId = "MOV-INJECTED" });
        Assert.Equal(reads, backend.DetailReads);
        backend.ForgeDetail = false;
        await vm.OpenDetailsAsync(vm.Rows[0]);
        Assert.Equal("سبب إداري موثق للفصل", vm.SelectedDetail?.Reason);
        Assert.Equal("USR-ADMIN", vm.SelectedDetail?.ActorUserId);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Malformed_backend_page_fails_closed_without_reusing_cached_personnel()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        Assert.Single(vm.Rows);
        backend.ForgePage = true;
        await vm.RefreshAsync();
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.LoadError);
        Assert.Equal(0, backend.Writes);
    }

    private sealed class FakeBackend : INativeSeparationArchiveBackend
    {
        public int PageReads, DetailReads;
        public int Writes => 0;
        public bool HasNext, ForgeDetail, ForgePage;
        public Task<NativeSeparationArchivePage>? DelayedPage;
        public readonly List<NativeSeparationArchiveFilter> Filters = [];

        public Task<NativeSeparationArchivePage> ReadSeparationsAsync(
            NativeIdentity actor, NativeSeparationArchiveFilter filter,
            CancellationToken ct = default)
        {
            PageReads++;
            Filters.Add(filter);
            return DelayedPage ?? Task.FromResult(ForgePage
                ? Page(filter) with { Total = -1 }
                : Page(filter, HasNext));
        }

        public Task<NativeSeparationArchiveDetail> ReadSeparationDetailsAsync(
            NativeIdentity actor, string movementId, CancellationToken ct = default)
        {
            DetailReads++;
            return Task.FromResult(ForgeDetail
                ? Detail() with { Reason = "حُرّف السبب الأصلي" }
                : Detail());
        }
    }
}
