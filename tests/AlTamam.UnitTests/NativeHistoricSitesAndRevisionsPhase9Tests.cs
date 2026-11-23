using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Tests FIRST: Phase 9 slice 2 adds two more immutable evidence panels to the
/// SAME unified individual report, never a second SQL or UI report engine.
/// </summary>
public sealed class NativeHistoricSitesAndRevisionsPhase9Tests
{
    private static readonly NativeIdentity Actor =
        new("USR-HISTORY", "history-admin", UserRole.GeneralAdministration, "NONCE-HISTORY");

    private static NativePersonnelDisplayRow Person() =>
        new(1, "PER-HISTORY", "9007", "جندي", "الشخص الحالي", "الموقع الحالي",
            "على القوة", true, 2, []);

    private static NativePersonnelReportSnapshot Snapshot(NativeIndividualReportPages p)
    {
        int sites = p.SitePage == 1 ? 100 : 1;
        int revisions = p.RevisionPage == 1 ? 100 : 1;
        return new NativePersonnelReportSnapshot(
            p.PersonId, "الشخص الحالي", "9007", "2024-01-01", true,
            0, 0, 0, 100, p.AttendancePage, false,
            p.MovementPage, false, p.IdentityPage, false,
            [], [], [], [])
        {
            SitePage = p.SitePage, SiteTotal = 101, SiteHasNext = p.SitePage == 1,
            RevisionPage = p.RevisionPage, RevisionTotal = 101,
            RevisionHasNext = p.RevisionPage == 1,
            Sites = Enumerable.Range(1, sites).Select(_ =>
                new NativePersonnelReportSite(
                    "SITE-HISTORIC", "موقع تاريخي محفوظ",
                    "2024-01-01", null, "2024-01-01T00:00:00Z")).ToArray(),
            Revisions = Enumerable.Range(1, revisions).Select(_ =>
                new NativePersonnelReportRevision(
                    "2026-11-01", "حاضر", "إجازة", "تصحيح معتمد",
                    "2026-11-01T15:00:00Z", "AUTHORIZED",
                    "مدير النظام وقتها")).ToArray()
        };
    }

    private static NativePersonnelReportViewModel Make(
        HistoryReader reader, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool>? allowed = null)
    {
        var vm = new NativePersonnelReportViewModel();
        vm.AttachBackend(reader, actor, allowed ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Sites_have_independent_real_100_row_pagination()
    {
        var backend = new HistoryReader();
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        Assert.True(vm.CanNextSites);
        Assert.Equal(100, vm.Report!.Sites.Count);
        await vm.NextSitesAsync();
        Assert.Equal(2, backend.Request!.SitePage);
        Assert.Equal(1, backend.Request.RevisionPage);
        Assert.Equal(1, backend.Request.AttendancePage);
        Assert.Equal(2, vm.Report!.SitePage);
        Assert.Single(vm.Report.Sites);
        Assert.True(vm.CanPreviousSites);
        Assert.False(vm.CanNextSites);
        await vm.PreviousSitesAsync();
        Assert.Equal(1, vm.Report!.SitePage);
        Assert.Equal(3, backend.ReadCount);
    }

    [Fact]
    public async Task Corrections_have_independent_real_100_row_pagination()
    {
        var backend = new HistoryReader();
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        Assert.True(vm.CanNextRevisions);
        await vm.NextRevisionsAsync();
        Assert.Equal(2, backend.Request!.RevisionPage);
        Assert.Equal(1, backend.Request.SitePage);
        Assert.Equal(1, backend.Request.IdentityPage);
        Assert.Single(vm.Report!.Revisions);
        Assert.Equal("حاضر", vm.Report.Revisions[0].Before);
        Assert.Equal("إجازة", vm.Report.Revisions[0].After);
        Assert.Equal("مدير النظام وقتها", vm.Report.Revisions[0].ExecutorSnapshot);
        Assert.False(vm.CanNextRevisions);
        Assert.True(vm.CanPreviousRevisions);
    }

    [Fact]
    public async Task Historical_site_name_and_correction_actor_are_never_derived_from_current_roster()
    {
        var backend = new HistoryReader();
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        Assert.Equal("موقع تاريخي محفوظ", vm.Report!.Sites[0].SiteName);
        Assert.Equal("مدير النظام وقتها", vm.Report.Revisions[0].ExecutorSnapshot);
        Assert.NotEqual("الموقع الحالي", vm.Report.Sites[0].SiteName);
    }

    [Theory]
    [InlineData("site-page")]
    [InlineData("revision-page")]
    [InlineData("site-total")]
    [InlineData("revision-total")]
    [InlineData("site-next")]
    [InlineData("revision-next")]
    [InlineData("site-row")]
    [InlineData("revision-row")]
    public async Task Forged_history_pages_or_counts_must_be_hidden(string corruption)
    {
        var backend = new HistoryReader { Corruption = corruption };
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        Assert.True(vm.IsOpen);
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.CanNextSites);
        Assert.False(vm.CanNextRevisions);
    }

    [Fact]
    public async Task New_history_panels_reject_stale_selected_row_or_non_manager()
    {
        var selected = Person();
        var backend = new HistoryReader();
        NativeIdentity? actor = Actor;
        var vm = Make(backend, () => actor, row => ReferenceEquals(row, selected));
        await vm.OpenAsync(selected with { Name = "مغاير" });
        Assert.Equal(0, backend.ReadCount);
        actor = Actor with { Role = UserRole.Supervisor };
        await vm.OpenAsync(selected);
        Assert.Equal(0, backend.ReadCount);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Closing_during_history_request_discards_the_late_response()
    {
        var backend = new HistoryReader();
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        var deferred = new TaskCompletionSource<NativePersonnelReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = deferred.Task;
        var pending = vm.NextSitesAsync();
        vm.Close();
        deferred.SetResult(Snapshot(new NativeIndividualReportPages(
            "PER-HISTORY", SitePage: 2)));
        await pending;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Report);
    }

    private sealed class HistoryReader : INativePersonnelReportReader
    {
        public int ReadCount;
        public NativeIndividualReportPages? Request;
        public Task<NativePersonnelReportSnapshot>? Pending;
        public string Corruption = "";

        public Task<NativePersonnelReportSnapshot> ReadIndividualAsync(
            NativeIdentity actor, NativeIndividualReportPages pages,
            CancellationToken ct = default)
        {
            Request = pages;
            ReadCount++;
            if (Pending is not null) return Pending;
            var snapshot = Snapshot(pages);
            snapshot = Corruption switch
            {
                "site-page" => snapshot with { SitePage = 44 },
                "revision-page" => snapshot with { RevisionPage = 44 },
                "site-total" => snapshot with { SiteTotal = -1 },
                "revision-total" => snapshot with { RevisionTotal = -1 },
                "site-next" => snapshot with { SiteHasNext = false },
                "revision-next" => snapshot with { RevisionHasNext = false },
                "site-row" => snapshot with { Sites = [] },
                "revision-row" => snapshot with { Revisions = [] },
                _ => snapshot
            };
            return Task.FromResult(snapshot);
        }
    }
}
