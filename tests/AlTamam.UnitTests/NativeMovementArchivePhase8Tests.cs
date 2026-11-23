using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Test-first safety expectations for the canonical, immutable movement archive.</summary>
public sealed class NativeMovementArchivePhase8Tests
{
    private static readonly NativeIdentity Manager =
        new("USR-1", "admin", UserRole.GeneralAdministration, "NONCE-1");

    private static NativeMovementArchiveRow Row() =>
        new("MOV-001", "PRN-1", "الاسم التاريخي", "012345", "داخلي",
            "الموقع السابق", "الموقع اللاحق", "2026-11-22", "2026-11-22T09:00:00Z");

    private static NativeMovementArchivePage Page(NativeMovementArchiveFilter filter,
        bool hasNext = false) => new(filter, hasNext ? (long)filter.Page * filter.PageSize + 1
            : (long)(filter.Page - 1) * filter.PageSize + 1, hasNext, [Row()]);

    private static NativeMovementArchiveDetail Detail() =>
        new("MOV-001", "PRN-1", "الاسم التاريخي", "012345", "داخلي",
            "الموقع السابق", "الموقع اللاحق", "2026-11-22",
            "2026-11-22T09:00:00Z", "operator");

    private static NativeMovementArchiveViewModel Create(FakeBackend backend,
        Func<NativeIdentity?> actor)
    {
        var vm = new NativeMovementArchiveViewModel();
        vm.AttachBackend(backend, actor);
        return vm;
    }

    [Fact]
    public async Task Unauthenticated_or_supervisor_cannot_open_or_read_archive()
    {
        var backend = new FakeBackend();
        NativeIdentity? actor = null;
        var vm = Create(backend, () => actor);
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.OpenAsync();
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.PageReads);
        Assert.Equal(0, backend.DetailReads);
    }

    [Fact]
    public async Task Archive_reads_historical_snapshots_in_bounded_canonical_pages()
    {
        var backend = new FakeBackend { HasNext = true };
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        Assert.True(vm.IsOpen);
        Assert.Equal("الاسم التاريخي", Assert.Single(vm.Rows).Name);
        Assert.Equal("الموقع السابق", vm.Rows[0].SourceSite);
        Assert.Equal("all", Assert.Single(backend.Filters).Type);
        Assert.Equal(50, backend.Filters[0].PageSize);
        await vm.NextPageAsync();
        Assert.Equal(2, backend.Filters[^1].Page);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Type_and_search_changes_invalidate_prior_page_and_result()
    {
        var backend = new FakeBackend { HasNext = true };
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        await vm.NextPageAsync();
        await vm.SelectTypeAsync("external");
        Assert.Equal(1, vm.Page);
        Assert.Equal("external", backend.Filters[^1].Type);
        await vm.SearchAsync("أحمد");
        Assert.Equal("أحمد", backend.Filters[^1].SearchTerm);
        Assert.Equal(1, backend.Filters[^1].Page);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task Invalid_filters_fail_closed_without_reusing_cached_personnel()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        int calls = backend.PageReads;
        await vm.SelectTypeAsync("unknown");
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.LoadError);
        Assert.Equal(calls, backend.PageReads);
        await vm.SearchAsync(new string('A', 101));
        Assert.Empty(vm.Rows);
        Assert.Equal(calls, backend.PageReads);
    }

    [Fact]
    public async Task Logout_or_close_cancels_late_archive_results()
    {
        var backend = new FakeBackend();
        var pending = new TaskCompletionSource<NativeMovementArchivePage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.DelayedPage = pending.Task;
        NativeIdentity? actor = Manager;
        var vm = Create(backend, () => actor);
        Task load = vm.OpenAsync();
        actor = Manager with { SessionNonce = "CHANGED" };
        vm.Close();
        pending.SetResult(Page(new NativeMovementArchiveFilter("", "all", 1)));
        await load;
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Rows);
        Assert.Null(vm.SelectedDetail);
    }

    [Fact]
    public async Task Forged_or_stale_details_never_replace_immutable_archive_identity()
    {
        var backend = new FakeBackend();
        var vm = Create(backend, () => Manager);
        await vm.OpenAsync();
        backend.ForgedDetail = true;
        await vm.OpenDetailsAsync(vm.Rows[0]);
        Assert.Null(vm.SelectedDetail);
        Assert.NotEmpty(vm.DetailError);
        backend.ForgedDetail = false;
        var forgedSelection = vm.Rows[0] with { MovementId = "MOV-FORGED" };
        int calls = backend.DetailReads;
        await vm.OpenDetailsAsync(forgedSelection);
        Assert.Equal(calls, backend.DetailReads);
        await vm.OpenDetailsAsync(vm.Rows[0]);
        Assert.Equal("MOV-001", vm.SelectedDetail?.MovementId);
        Assert.Equal("الاسم التاريخي", vm.SelectedDetail?.Name);
        Assert.Equal(0, backend.Writes);
    }

    private sealed class FakeBackend : INativeMovementArchiveBackend
    {
        public int PageReads, DetailReads;
        public int Writes => 0; // Read-only interface deliberately exposes no mutation operation.
        public bool HasNext, ForgedDetail;
        public Task<NativeMovementArchivePage>? DelayedPage;
        public readonly List<NativeMovementArchiveFilter> Filters = [];

        public Task<NativeMovementArchivePage> ReadMovementsAsync(
            NativeIdentity actor, NativeMovementArchiveFilter filter,
            CancellationToken ct = default)
        {
            PageReads++;
            Filters.Add(filter);
            return DelayedPage ?? Task.FromResult(Page(filter, HasNext));
        }

        public Task<NativeMovementArchiveDetail> ReadMovementDetailsAsync(
            NativeIdentity actor, string movementId, CancellationToken ct = default)
        {
            DetailReads++;
            return Task.FromResult(ForgedDetail
                ? Detail() with { PersonId = "PRN-FORGED" }
                : Detail());
        }
    }
}
