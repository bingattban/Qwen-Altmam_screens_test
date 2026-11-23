using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Test-first Phase 9 slice 3: original monthly attendance aggregation
/// and independently paged append-only individual audit timeline.
/// </summary>
public sealed class NativeMonthlyHistoryAndAuditPhase9Tests
{
    private static readonly NativeIdentity Manager =
        new("U-AUDIT", "manager", UserRole.GeneralAdministration, "N-AUDIT");
    private static NativePersonnelDisplayRow Person() =>
        new(1, "PER-AUDIT", "900", "رقيب", "الاسم الحالي", "موقع",
            "على القوة", true, 2, []);

    private static NativePersonnelReportSnapshot Snapshot(
        NativeIndividualReportPages pages)
    {
        int pageCount = pages.AuditPage == 1 ? 100 : 1;
        return new NativePersonnelReportSnapshot(
            pages.PersonId, "الاسم الحالي", "900", "2024-01-01", true,
            4, 0, 0, 100,
            pages.AttendancePage, false, pages.MovementPage, false,
            pages.IdentityPage, false,
            [new NativePersonnelReportStatusTotal("S-P", "حاضر", 4)],
            [new NativePersonnelReportAttendance("2026-11-01", "حاضر",
                "الموقع التاريخي", "MANUAL", "اسم محفوظ", "900", 1),
             new NativePersonnelReportAttendance("2026-11-02", "حاضر",
                "الموقع التاريخي", "MANUAL", "اسم محفوظ", "900", 1),
             new NativePersonnelReportAttendance("2026-10-01", "حاضر",
                "الموقع التاريخي", "MANUAL", "اسم محفوظ", "900", 1),
             new NativePersonnelReportAttendance("2026-10-02", "حاضر",
                "الموقع التاريخي", "MANUAL", "اسم محفوظ", "900", 1)])
        {
            SitePage = pages.SitePage, RevisionPage = pages.RevisionPage,
            MonthlyTotals =
            [
                new NativePersonnelReportMonthSummary("2026-11", "S-P", "حاضر", 2),
                new NativePersonnelReportMonthSummary("2026-10", "S-P", "حاضر", 2)
            ],
            AuditPage = pages.AuditPage,
            AuditTotal = 101,
            AuditHasNext = pages.AuditPage == 1,
            Audit = Enumerable.Range(1, pageCount)
                .Select(_ => new NativePersonnelReportAuditRecord(
                    "اعتماد", "operator-at-action", "موظف منفذ",
                    "2026-11-01", "2026-11-01T15:00:00Z",
                    "حفظ عملية معتمدة", "سبب محفوظ")).ToArray()
        };
    }

    private static NativePersonnelReportViewModel Make(
        MockReader backend, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool>? rowCheck = null)
    {
        var vm = new NativePersonnelReportViewModel();
        vm.AttachBackend(backend, actor, rowCheck ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Monthly_totals_preserve_source_periods_and_status_names()
    {
        var vm = Make(new MockReader(), () => Manager);
        await vm.OpenAsync(Person());
        Assert.Equal(2, vm.Report!.MonthlyTotals.Count);
        Assert.Equal("2026-11", vm.Report.MonthlyTotals[0].Period);
        Assert.Equal(2, vm.Report.MonthlyTotals[0].Count);
        Assert.Equal("حاضر", vm.Report.MonthlyTotals[0].StatusName);
        Assert.Equal(4, vm.Report.MonthlyTotals.Sum(row => row.Count));
    }

    [Fact]
    public async Task Audit_has_own_100_row_pagination_without_mutating_other_pages()
    {
        var reader = new MockReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        Assert.True(vm.CanNextAudit);
        Assert.Equal(100, vm.Report!.Audit.Count);
        await vm.NextAuditAsync();
        Assert.Equal(2, reader.LastRequest!.AuditPage);
        Assert.Equal(1, reader.LastRequest.AttendancePage);
        Assert.Equal(1, reader.LastRequest.SitePage);
        Assert.Equal(1, reader.LastRequest.RevisionPage);
        Assert.Single(vm.Report!.Audit);
        Assert.True(vm.CanPreviousAudit);
        Assert.False(vm.CanNextAudit);
        await vm.PreviousAuditAsync();
        Assert.Equal(1, vm.Report!.AuditPage);
    }

    [Fact]
    public async Task Historical_executor_never_substitutes_current_actor_or_person()
    {
        var vm = Make(new MockReader(), () => Manager);
        await vm.OpenAsync(Person());
        Assert.Equal("operator-at-action", vm.Report!.Audit[0].UsernameSnapshot);
        Assert.Equal("موظف منفذ", vm.Report.Audit[0].FullNameSnapshot);
        Assert.Equal("الاسم الحالي", vm.Report.Name);
    }

    [Theory]
    [InlineData("audit-person")]
    [InlineData("audit-page")]
    [InlineData("audit-count")]
    [InlineData("audit-next")]
    [InlineData("audit-rows")]
    [InlineData("audit-actor")]
    [InlineData("monthly-period")]
    [InlineData("monthly-count")]
    [InlineData("monthly-rows")]
    public async Task Forged_monthly_or_audit_evidence_is_never_displayed(string bad)
    {
        var reader = new MockReader { Corrupt = bad };
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        Assert.True(vm.IsOpen);
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.CanNextAudit);
    }

    [Fact]
    public async Task Stale_row_or_unauthorized_actor_cannot_open_audit()
    {
        NativeIdentity? actor = Manager;
        var reader = new MockReader();
        var selected = Person();
        var vm = Make(reader, () => actor, row => ReferenceEquals(row, selected));
        await vm.OpenAsync(selected with { Name = "مزور" });
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.OpenAsync(selected);
        Assert.Equal(0, reader.Reads);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Logout_discards_late_audit_page_results()
    {
        var reader = new MockReader();
        NativeIdentity? actor = Manager;
        var vm = Make(reader, () => actor);
        await vm.OpenAsync(Person());
        var deferred = new TaskCompletionSource<NativePersonnelReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = deferred.Task;
        var running = vm.NextAuditAsync();
        actor = null;
        vm.Close();
        deferred.SetResult(Snapshot(new NativeIndividualReportPages(
            "PER-AUDIT", AuditPage: 2)));
        await running;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Report);
    }

    private sealed class MockReader : INativePersonnelReportReader
    {
        public NativeIndividualReportPages? LastRequest;
        public int Reads;
        public string Corrupt = "";
        public Task<NativePersonnelReportSnapshot>? Pending;

        public Task<NativePersonnelReportSnapshot> ReadIndividualAsync(
            NativeIdentity actor, NativeIndividualReportPages pages,
            CancellationToken ct = default)
        {
            ++Reads;
            LastRequest = pages;
            if (Pending is not null) return Pending;
            var report = Snapshot(pages);
            report = Corrupt switch
            {
                "audit-person" => report with { PersonId = "PER-FORGED" },
                "audit-page" => report with { AuditPage = 7 },
                "audit-count" => report with { AuditTotal = -1 },
                "audit-next" => report with { AuditHasNext = false },
                "audit-rows" => report with { Audit = [] },
                "audit-actor" => report with
                {
                    Audit = [new NativePersonnelReportAuditRecord(
                        "اعتماد", null!, "موظف منفذ",
                        "2026-11-01", "2026-11-01T15:00:00Z",
                        "حفظ عملية معتمدة", null)]
                },
                "monthly-period" => report with
                {
                    MonthlyTotals = [new NativePersonnelReportMonthSummary(
                        "invalid-date", "S-P", "حاضر", 4)]
                },
                "monthly-count" => report with
                {
                    MonthlyTotals = [new NativePersonnelReportMonthSummary(
                        "2026-11", "S-P", "حاضر", -1)]
                },
                "monthly-rows" => report with { MonthlyTotals = [] },
                _ => report
            };
            return Task.FromResult(report);
        }
    }
}
