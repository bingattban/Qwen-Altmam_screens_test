using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 9 slice 4 — tests FIRST: native individual report date filtering
/// must send exact, inclusive ISO dates to the ORIGINAL PersonId report.
/// </summary>
public sealed class NativeReportDateFiltersPhase9Tests
{
    private static readonly NativeIdentity Manager =
        new("U-DATES", "admin", UserRole.GeneralAdministration, "N-DATES");
    private static NativePersonnelDisplayRow Person() =>
        new(1, "PER-DATES", "0033", "جندي", "الاسم الحالي", "الموقع",
            "على القوة", true, 2, []);

    private static NativePersonnelReportSnapshot Snapshot(
        NativeIndividualReportPages p, string? forgedFrom = null,
        string? forgedTo = null)
    {
        int total = p.DateFrom.Length == 0 && p.DateTo.Length == 0 ? 101 : 1;
        int count = p.AttendancePage == 1 ? Math.Min(100, total) :
            p.AttendancePage == 2 ? total - 100 : 0;
        return new NativePersonnelReportSnapshot(
            p.PersonId, "الاسم الحالي", "0033", "2024-01-01", true,
            total, 0, 0, 100,
            p.AttendancePage, p.AttendancePage * 100L < total,
            p.MovementPage, false, p.IdentityPage, false,
            [new NativePersonnelReportStatusTotal("S-01", "حاضر", total)],
            Enumerable.Range(1, count).Select(_ =>
                new NativePersonnelReportAttendance(
                    "2026-11-20", "حاضر", "الموقع المحفوظ", "MANUAL",
                    "الاسم وقتها", "0033", 1)).ToArray(),
            [], [])
        {
            SitePage = p.SitePage, RevisionPage = p.RevisionPage,
            AuditPage = p.AuditPage,
            DateFrom = forgedFrom ?? p.DateFrom,
            DateTo = forgedTo ?? p.DateTo,
            MonthlyTotals = [new NativePersonnelReportMonthSummary(
                "2026-11", "S-01", "حاضر", total)]
        };
    }

    private static NativePersonnelReportViewModel Make(
        FakeReader reader, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool>? rowCheck = null)
    {
        var vm = new NativePersonnelReportViewModel();
        vm.AttachBackend(reader, actor, rowCheck ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Applying_valid_inclusive_dates_resets_all_pages_and_echoes_source_dates()
    {
        var backend = new FakeReader();
        var vm = Make(backend, () => Manager);
        await vm.OpenAsync(Person());
        await vm.NextAttendanceAsync();
        Assert.Equal(2, vm.Report!.AttendancePage);

        vm.DateFromDraft = "2026-11-01";
        vm.DateToDraft = "2026-11-30";
        await vm.ApplyDateFiltersAsync();

        Assert.Equal(3, backend.Reads);
        Assert.Equal("2026-11-01", backend.Last!.DateFrom);
        Assert.Equal("2026-11-30", backend.Last.DateTo);
        Assert.Equal(1, backend.Last.AttendancePage);
        Assert.Equal(1, backend.Last.AuditPage);
        Assert.Equal(1, backend.Last.SitePage);
        Assert.Equal(1, backend.Last.RevisionPage);
        Assert.Equal(1, backend.Last.MovementPage);
        Assert.Equal(1, backend.Last.IdentityPage);
        Assert.Equal("2026-11-01", vm.Report!.DateFrom);
        Assert.Equal("2026-11-30", vm.Report.DateTo);
        Assert.Single(vm.Report.Attendance);
        Assert.Contains("2026-11-01", vm.AppliedDateRangeLabel);
    }

    [Theory]
    [InlineData("2026-11-01", "")]
    [InlineData("", "2026-11-30")]
    [InlineData("2026-11-01", "2026-11-01")]
    public async Task Open_ended_and_equal_boundaries_keep_canonical_inclusive_contract(
        string from, string to)
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.DateFromDraft = from;
        vm.DateToDraft = to;
        await vm.ApplyDateFiltersAsync();
        Assert.Equal(from, reader.Last!.DateFrom);
        Assert.Equal(to, reader.Last.DateTo);
        Assert.Equal(from, vm.Report!.DateFrom);
        Assert.Equal(to, vm.Report.DateTo);
    }

    [Theory]
    [InlineData("2026-02-29", "")]
    [InlineData("2026-11-31", "")]
    [InlineData("2026/11/01", "")]
    [InlineData("٢٠٢٦-١١-٠١", "")]
    [InlineData("2026-11-12 ", "")]
    [InlineData("2026-12-01", "2026-11-30")]
    [InlineData("", "not-a-date")]
    public async Task Invalid_or_reversed_ranges_cannot_query_original_data(
        string from, string to)
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.DateFromDraft = from;
        vm.DateToDraft = to;
        await vm.ApplyDateFiltersAsync();
        Assert.Equal(1, reader.Reads);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal("", vm.Report!.DateFrom);
        Assert.Equal("", vm.Report.DateTo);
    }

    [Fact]
    public async Task Clearing_filters_restores_unbounded_query_not_a_fake_local_subset()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.DateFromDraft = "2026-11-01";
        await vm.ApplyDateFiltersAsync();
        Assert.Equal(1, vm.Report!.AttendanceTotal);
        await vm.ClearDateFiltersAsync();
        Assert.Equal("", vm.DateFromDraft);
        Assert.Equal("", vm.DateToDraft);
        Assert.Equal("", reader.Last!.DateFrom);
        Assert.Equal("", reader.Last.DateTo);
        Assert.Equal(101, vm.Report!.AttendanceTotal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Mismatched_date_filter_echo_is_rejected_before_render(
        bool forgeFrom, bool forgeTo)
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.DateFromDraft = "2026-11-01";
        vm.DateToDraft = "2026-11-30";
        reader.ForgedFrom = forgeFrom ? "2026-11-02" : null;
        reader.ForgedTo = forgeTo ? "2026-11-29" : null;
        await vm.ApplyDateFiltersAsync();
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Changed_dates_discard_pending_response_from_old_filter()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        var stale = new TaskCompletionSource<NativePersonnelReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = stale.Task;
        vm.DateFromDraft = "2026-10-01";
        Task first = vm.ApplyDateFiltersAsync();
        reader.Pending = null;
        vm.DateFromDraft = "2026-11-01";
        await vm.ApplyDateFiltersAsync();
        stale.SetResult(Snapshot(new NativeIndividualReportPages(
            "PER-DATES", DateFrom: "2026-10-01")));
        await first;
        Assert.Equal("2026-11-01", vm.Report!.DateFrom);
        Assert.Equal(3, reader.Reads);
    }

    [Fact]
    public async Task Session_revocation_and_stale_roster_selection_block_date_requests()
    {
        NativeIdentity? actor = Manager;
        var reader = new FakeReader();
        var person = Person();
        var selected = true;
        var vm = Make(reader, () => actor, row => selected &&
            ReferenceEquals(row, person));
        await vm.OpenAsync(person);
        vm.DateFromDraft = "2026-11-01";
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.ApplyDateFiltersAsync();
        Assert.Equal(1, reader.Reads);
        vm.Close();
        actor = Manager;
        selected = false;
        await vm.OpenAsync(person);
        Assert.False(vm.IsOpen);
        Assert.Equal(1, reader.Reads);
    }

    private sealed class FakeReader : INativePersonnelReportReader
    {
        public int Reads;
        public NativeIndividualReportPages? Last;
        public string? ForgedFrom, ForgedTo;
        public Task<NativePersonnelReportSnapshot>? Pending;

        public Task<NativePersonnelReportSnapshot> ReadIndividualAsync(
            NativeIdentity actor, NativeIndividualReportPages pages,
            CancellationToken ct = default)
        {
            ++Reads;
            Last = pages;
            return Pending ?? Task.FromResult(Snapshot(pages, ForgedFrom, ForgedTo));
        }
    }
}
