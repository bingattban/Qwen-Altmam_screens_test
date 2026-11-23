using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 9 slice 6 tests FIRST. Use immutable, person-scoped, unfiltered
/// canonical site/status selectors, never mutable names or a 100-row subset.
/// </summary>
public sealed class NativeReportSiteStatusPhase9Tests
{
    private static readonly NativeIdentity Manager =
        new("U-HISTORY", "admin", UserRole.GeneralAdministration, "N-HISTORY");

    private static NativePersonnelDisplayRow Person() => new(
        1, "PER-HISTORY", "1009", "رقيب", "الاسم الحالي",
        "الموقع الحالي", "على القوة", true, 2, []);

    private static readonly NativeReportFilterOption[] Sites =
        [new("SITE-A", "موقع ألف (محفوظ)"), new("SITE-B", "موقع باء (محفوظ)")];
    private static readonly NativeReportFilterOption[] Statuses =
        [new("S-P", "حاضر"), new("S-L", "إجازة")];

    private static NativePersonnelReportSnapshot Snapshot(
        NativeIndividualReportPages p,
        string? forgedSite = null, string? forgedStatus = null,
        bool duplicateSites = false)
    {
        bool hasAttendance = (p.SiteId.Length == 0 || p.SiteId == "SITE-A") &&
                             (p.StatusId.Length == 0 || p.StatusId == "S-P");
        bool hasMovement = p.SiteId.Length == 0 || p.SiteId == "SITE-A";
        long attendanceTotal = hasAttendance ? 1L : 0L;
        return new NativePersonnelReportSnapshot(
            p.PersonId, "الاسم الحالي", "1009", "2024-01-01", true,
            attendanceTotal, hasMovement ? 1 : 0, 0, 100,
            p.AttendancePage, false, p.MovementPage, false,
            p.IdentityPage, false,
            hasAttendance ? [new NativePersonnelReportStatusTotal("S-P", "حاضر", 1)] : [],
            hasAttendance ? [new NativePersonnelReportAttendance(
                "2026-11-20", "حاضر", "موقع ألف التاريخي",
                "MANUAL", "الاسم القديم", "1009", 1)] : [],
            hasMovement ? [new NativePersonnelReportMovement(
                "نقل داخلي", "موقع ألف", "موقع باء",
                "2026-11-10", "الاسم القديم", "1009", "المشرف")] : [],
            [])
        {
            DateFrom = p.DateFrom,
            DateTo = p.DateTo,
            MovementType = p.MovementType,
            SiteId = forgedSite ?? p.SiteId,
            StatusId = forgedStatus ?? p.StatusId,
            AvailableSites = duplicateSites
                ? [Sites[0], Sites[0]] : Sites,
            AvailableStatuses = Statuses,
            MonthlyTotals = hasAttendance ?
                [new NativePersonnelReportMonthSummary("2026-11", "S-P", "حاضر", 1)] : [],
            SitePage = p.SitePage, AuditPage = p.AuditPage,
            RevisionPage = p.RevisionPage
        };
    }

    private static NativePersonnelReportViewModel Make(
        FakeReader reader, Func<NativeIdentity?> live,
        Func<NativePersonnelDisplayRow, bool>? current = null)
    {
        var vm = new NativePersonnelReportViewModel();
        vm.AttachBackend(reader, live, current ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Site_and_status_selections_are_person_scoped_and_independent_of_pages()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        Assert.Equal(["", "SITE-A", "SITE-B"], vm.SiteFilterOptions.Select(x => x.Id).ToArray());
        Assert.Equal(["", "S-P", "S-L"], vm.StatusFilterOptions.Select(x => x.Id).ToArray());
        vm.SiteIdDraft = "SITE-A";
        vm.StatusIdDraft = "S-P";
        await vm.ApplySiteStatusFiltersAsync();
        Assert.Equal(2, reader.Reads);
        Assert.Equal("PER-HISTORY", reader.Last!.PersonId);
        Assert.Equal("SITE-A", reader.Last.SiteId);
        Assert.Equal("S-P", reader.Last.StatusId);
        Assert.Equal("SITE-A", vm.Report!.SiteId);
        Assert.Equal("S-P", vm.Report.StatusId);
        Assert.Single(vm.Report.Attendance);
        Assert.Single(vm.Report.MonthlyTotals);
        Assert.Contains("موقع ألف", vm.AppliedSiteStatusLabel);
        Assert.Contains("حاضر", vm.AppliedSiteStatusLabel);
    }

    [Theory]
    [InlineData("SITE-A", "")]
    [InlineData("", "S-P")]
    [InlineData("SITE-B", "S-L")]
    [InlineData("", "")]
    public async Task Site_and_status_parameters_are_sent_exactly_to_canonical_service(
        string site, string status)
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.SiteIdDraft = site;
        vm.StatusIdDraft = status;
        await vm.ApplySiteStatusFiltersAsync();
        Assert.Equal(site, reader.Last!.SiteId);
        Assert.Equal(status, reader.Last.StatusId);
        Assert.Equal(site, vm.Report!.SiteId);
        Assert.Equal(status, vm.Report.StatusId);
    }

    [Theory]
    [InlineData("SITE-NOT-OWNED", "")]
    [InlineData("SITE-A", "S-UNKNOWN")]
    [InlineData("SITE-A\n", "S-P")]
    public async Task Unknown_or_dangerous_catalog_keys_fail_before_any_read(
        string site, string status)
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.SiteIdDraft = site;
        vm.StatusIdDraft = status;
        await vm.ApplySiteStatusFiltersAsync();
        Assert.Equal(1, reader.Reads);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.NotNull(vm.Report);
    }

    [Fact]
    public async Task Empty_selection_is_a_genuine_original_unfiltered_read()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.SiteIdDraft = "SITE-B";
        vm.StatusIdDraft = "S-L";
        await vm.ApplySiteStatusFiltersAsync();
        Assert.Equal(0, vm.Report!.AttendanceTotal);
        await vm.ClearSiteStatusFiltersAsync();
        Assert.Equal("", reader.Last!.SiteId);
        Assert.Equal("", reader.Last.StatusId);
        Assert.Equal("", vm.SiteIdDraft);
        Assert.Equal("", vm.StatusIdDraft);
        Assert.Equal(1, vm.Report!.AttendanceTotal);
        Assert.Equal(3, reader.Reads);
    }

    [Fact]
    public async Task Other_filters_are_preserved_and_attendance_movement_revision_pages_reset()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.DateFromDraft = "2026-11-01";
        vm.DateToDraft = "2026-11-30";
        await vm.ApplyDateFiltersAsync();
        vm.MovementTypeDraft = 2;
        await vm.ApplyMovementTypeFilterAsync();
        vm.SiteIdDraft = "SITE-B";
        await vm.ApplySiteStatusFiltersAsync();
        Assert.Equal("2026-11-01", reader.Last!.DateFrom);
        Assert.Equal("2026-11-30", reader.Last.DateTo);
        Assert.Equal(2, reader.Last.MovementType);
        Assert.Equal(1, reader.Last.AttendancePage);
        Assert.Equal(1, reader.Last.MovementPage);
        Assert.Equal(1, reader.Last.RevisionPage);
        Assert.Equal(0, vm.Report!.AttendanceTotal);
    }

    [Fact]
    public async Task Date_and_movement_changes_do_not_silently_clear_site_or_status()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.SiteIdDraft = "SITE-A";
        vm.StatusIdDraft = "S-P";
        await vm.ApplySiteStatusFiltersAsync();
        vm.DateFromDraft = "2026-11-01";
        await vm.ApplyDateFiltersAsync();
        Assert.Equal("SITE-A", reader.Last!.SiteId);
        Assert.Equal("S-P", reader.Last.StatusId);
        vm.MovementTypeDraft = 4;
        await vm.ApplyMovementTypeFilterAsync();
        Assert.Equal("SITE-A", reader.Last!.SiteId);
        Assert.Equal("S-P", reader.Last.StatusId);
        Assert.Equal(4, reader.Last.MovementType);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Forged_filter_echo_is_rejected_before_showing_any_rows(
        bool forgedSite, bool forgedStatus)
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        reader.ForgedSite = forgedSite ? "SITE-B" : null;
        reader.ForgedStatus = forgedStatus ? "S-L" : null;
        vm.SiteIdDraft = "SITE-A";
        vm.StatusIdDraft = "S-P";
        await vm.ApplySiteStatusFiltersAsync();
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Duplicate_source_catalog_keys_are_refused_as_corrupted_evidence()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        reader.DuplicateSites = true;
        vm.StatusIdDraft = "S-P";
        await vm.ApplySiteStatusFiltersAsync();
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Old_response_cannot_replace_new_site_or_status_query()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        var late = new TaskCompletionSource<NativePersonnelReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = late.Task;
        vm.SiteIdDraft = "SITE-A";
        Task older = vm.ApplySiteStatusFiltersAsync();
        reader.Pending = null;
        vm.StatusIdDraft = "S-L";
        await vm.ApplySiteStatusFiltersAsync();
        late.SetResult(Snapshot(new NativeIndividualReportPages(
            "PER-HISTORY", SiteId: "SITE-A")));
        await older;
        Assert.Equal("S-L", vm.Report!.StatusId);
        Assert.Equal("SITE-A", vm.Report.SiteId);
    }

    [Fact]
    public async Task Revoked_role_or_stale_selection_never_reads_site_status_history()
    {
        NativeIdentity? actor = Manager;
        bool current = true;
        var row = Person();
        var reader = new FakeReader();
        var vm = Make(reader, () => actor,
            selected => current && ReferenceEquals(selected, row));
        await vm.OpenAsync(row);
        actor = Manager with { Role = UserRole.Supervisor };
        vm.SiteIdDraft = "SITE-A";
        await vm.ApplySiteStatusFiltersAsync();
        Assert.Equal(1, reader.Reads);
        vm.Close();
        current = false;
        actor = Manager;
        await vm.OpenAsync(row);
        Assert.Equal(1, reader.Reads);
        Assert.False(vm.IsOpen);
    }

    private sealed class FakeReader : INativePersonnelReportReader
    {
        public int Reads;
        public NativeIndividualReportPages? Last;
        public string? ForgedSite, ForgedStatus;
        public bool DuplicateSites;
        public Task<NativePersonnelReportSnapshot>? Pending;

        public Task<NativePersonnelReportSnapshot> ReadIndividualAsync(
            NativeIdentity actor, NativeIndividualReportPages pages,
            CancellationToken ct = default)
        {
            ++Reads;
            Last = pages;
            return Pending ?? Task.FromResult(
                Snapshot(pages, ForgedSite, ForgedStatus, DuplicateSites));
        }
    }
}
