using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 9, slice 1: tests FIRST for the existing single canonical
/// PersonId-keyed report, not a competing report engine or export.
/// </summary>
public sealed class NativeIndividualPersonnelReportPhase9Tests
{
    private static readonly NativeIdentity Actor =
        new("USR-REPORT", "admin", UserRole.GeneralAdministration, "NONCE-REPORT");
    private static NativePersonnelDisplayRow Person(string id = "PER-ONE") =>
        new(1, id, "00077", "جندي", "الفرد الأول", "موقع", "على القوة", true, 3, []);
    private static NativePersonnelReportSnapshot Snapshot(
        NativeIndividualReportPages pages, string? personId = null, bool forgedPage = false) =>
        new(personId ?? pages.PersonId, "الفرد الأول", "00077", "2024-01-01", true,
            101, 1, 1, 100, forgedPage ? 9 : pages.AttendancePage,
            pages.AttendancePage == 1, pages.MovementPage, false,
            pages.IdentityPage, false,
            [new NativePersonnelReportStatusTotal("STS-P", "حاضر", 101)],
            Enumerable.Range(1, pages.AttendancePage == 1 ? 100 : 1)
                .Select(i => new NativePersonnelReportAttendance(
                    "2026-11-20", "حاضر", "موقع", "MANUAL", null, null, 1)).ToArray(),
            [new NativePersonnelReportMovement("نقل داخلي", "موقع 1", "موقع 2",
                "2026-01-01", "الاسم التاريخي", "00077", "المسؤول")],
            [new NativePersonnelReportIdentity("الاسم التاريخي", "00077",
                "2024-01-01", null, "إضافة")])
        {
            MonthlyTotals = [new NativePersonnelReportMonthSummary(
                "2026-11", "STS-P", "حاضر", 101)]
        };

    private static NativePersonnelReportViewModel Make(FakeReader backend,
        Func<NativeIdentity?> principal, Func<NativePersonnelDisplayRow, bool>? allowed = null)
    {
        var vm = new NativePersonnelReportViewModel();
        vm.AttachBackend(backend, principal, allowed ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Anonymous_and_supervisor_cannot_read_any_report_data()
    {
        NativeIdentity? principal = null;
        var backend = new FakeReader();
        var vm = Make(backend, () => principal);
        await vm.OpenAsync(Person());
        principal = Actor with { Role = UserRole.Supervisor };
        await vm.OpenAsync(Person());
        Assert.False(vm.IsOpen);
        Assert.Equal(0, backend.ReadCount);
    }

    [Fact]
    public async Task Only_the_current_selected_row_can_resolve_a_person_id_report()
    {
        var backend = new FakeReader();
        var selected = Person();
        var vm = Make(backend, () => Actor, row => ReferenceEquals(selected, row));
        await vm.OpenAsync(selected with { Name = "مزور" });
        Assert.Equal(0, backend.ReadCount);
        await vm.OpenAsync(selected);
        Assert.Equal(1, backend.ReadCount);
        Assert.Equal(selected.PersonId, vm.Report?.PersonId);
    }

    [Fact]
    public async Task Historical_identity_must_not_be_invented_from_current_identity()
    {
        var backend = new FakeReader();
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        Assert.True(vm.IsOpen);
        Assert.Equal("غير موثق تاريخيا",
            Assert.Single(vm.Report!.Attendance.Skip(0).Take(1)).HistoricalNameDisplay);
        Assert.Equal("الفرد الأول", vm.Report.Name);
        Assert.Equal(101, vm.Report.AttendanceTotal);
    }

    [Fact]
    public async Task Forged_person_or_pagination_is_hidden_instead_of_rendered()
    {
        var backend = new FakeReader { ForgePerson = true };
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
        vm.Close();
        backend.ForgePerson = false;
        backend.ForgePage = true;
        await vm.OpenAsync(Person());
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Attendance_page_two_uses_original_100_row_paging()
    {
        var backend = new FakeReader();
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        Assert.True(vm.CanNextAttendance);
        await vm.NextAttendanceAsync();
        Assert.Equal(2, vm.Report?.AttendancePage);
        Assert.Single(vm.Report!.Attendance);
        Assert.False(vm.CanNextAttendance);
        Assert.Equal(2, backend.ReadCount);
        Assert.Equal(2, backend.LastRequest?.AttendancePage);
    }

    [Fact]
    public async Task Logout_during_report_read_discards_late_response()
    {
        var backend = new FakeReader();
        NativeIdentity? actor = Actor;
        var vm = Make(backend, () => actor);
        var pending = new TaskCompletionSource<NativePersonnelReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending = pending.Task;
        Task opening = vm.OpenAsync(Person());
        actor = Actor with { SessionNonce = "NEW" };
        vm.Close();
        pending.SetResult(Snapshot(new NativeIndividualReportPages("PER-ONE", 1, 1, 1)));
        await opening;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Report);
    }

    [Fact]
    public async Task Changed_selected_person_invalidates_the_previous_report()
    {
        var backend = new FakeReader();
        var vm = Make(backend, () => Actor);
        await vm.OpenAsync(Person());
        Assert.NotNull(vm.Report);
        vm.Close();
        Assert.Null(vm.Report);
        Assert.False(vm.IsOpen);
        Assert.False(vm.CanNextAttendance);
    }

    private sealed class FakeReader : INativePersonnelReportReader
    {
        public int ReadCount;
        public bool ForgePerson, ForgePage;
        public NativeIndividualReportPages? LastRequest;
        public Task<NativePersonnelReportSnapshot>? Pending;
        public Task<NativePersonnelReportSnapshot> ReadIndividualAsync(
            NativeIdentity actor, NativeIndividualReportPages pages,
            CancellationToken ct = default)
        {
            ReadCount++;
            LastRequest = pages;
            return Pending ?? Task.FromResult(
                Snapshot(pages, ForgePerson ? "PER-ANOTHER" : null, ForgePage));
        }
    }
}
