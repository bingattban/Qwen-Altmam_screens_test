using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 9 slice 5 test FIRST: restrict only the canonical PersonId movement
/// stream without distorting attendance, monthly, identity or audit evidence.
/// </summary>
public sealed class NativeReportMovementTypePhase9Tests
{
    private static readonly NativeIdentity Manager =
        new("U-MOVEMENTS", "admin", UserRole.GeneralAdministration, "N-MOVEMENTS");

    private static NativePersonnelDisplayRow Person() =>
        new(1, "PER-MOVEMENTS", "0099", "رقيب", "الاسم الحالي",
            "الموقع الحالي", "على القوة", true, 2, []);

    private static NativePersonnelReportSnapshot Snapshot(
        NativeIndividualReportPages pages, int? fakeType = null)
    {
        var movementTotal = pages.MovementType == 0 ? 101 : 1;
        var count = pages.MovementPage == 1 ? Math.Min(100, movementTotal) :
                    pages.MovementPage == 2 ? Math.Max(0, movementTotal - 100) : 0;
        string movementName = pages.MovementType switch
        {
            1 => "نقل داخلي",
            2 => "نقل خارجي",
            3 => "فصل",
            4 => "عودة للخدمة",
            _ => "نقل داخلي"
        };
        return new NativePersonnelReportSnapshot(
            pages.PersonId, "الاسم الحالي", "0099", "2024-01-01", true,
            1, movementTotal, 0, 100,
            pages.AttendancePage, false,
            pages.MovementPage, pages.MovementPage == 1 && movementTotal > 100,
            pages.IdentityPage, false,
            [new NativePersonnelReportStatusTotal("S-P", "حاضر", 1)],
            [new NativePersonnelReportAttendance("2026-11-20", "حاضر",
                "موقع تاريخي", "MANUAL", "الاسم وقتها", "0099", 1)],
            Enumerable.Range(1, count).Select(_ =>
                new NativePersonnelReportMovement(
                    movementName, "موقع 1", "موقع 2", "2026-11-20",
                    "الاسم وقت الحركة", "0099", "المنفذ المحفوظ")).ToArray(),
            [])
        {
            SitePage = pages.SitePage,
            RevisionPage = pages.RevisionPage,
            AuditPage = pages.AuditPage,
            DateFrom = pages.DateFrom,
            DateTo = pages.DateTo,
            MovementType = fakeType ?? pages.MovementType,
            MonthlyTotals = [new NativePersonnelReportMonthSummary(
                "2026-11", "S-P", "حاضر", 1)]
        };
    }

    private static NativePersonnelReportViewModel Make(
        FakeReader reader, Func<NativeIdentity?> actor,
        Func<NativePersonnelDisplayRow, bool>? selected = null)
    {
        var vm = new NativePersonnelReportViewModel();
        vm.AttachBackend(reader, actor, selected ?? (_ => true));
        return vm;
    }

    [Fact]
    public async Task Movement_type_filters_original_person_report_and_only_resets_movement_page()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        Assert.Equal(101, vm.Report!.MovementTotal);
        await vm.NextMovementsAsync();
        Assert.Equal(2, vm.Report!.MovementPage);
        vm.MovementTypeDraft = 2;
        await vm.ApplyMovementTypeFilterAsync();
        Assert.Equal(2, reader.Last!.MovementType);
        Assert.Equal(1, reader.Last.MovementPage);
        Assert.Equal(1, reader.Last.AttendancePage);
        Assert.Equal(1, reader.Last.SitePage);
        Assert.Equal(1, reader.Last.AuditPage);
        Assert.Equal(1, vm.Report!.MovementTotal);
        Assert.Equal("نقل خارجي", Assert.Single(vm.Report.Movements).Type);
        Assert.Equal(1, vm.Report.AttendanceTotal);
        Assert.Equal(1, vm.Report.MonthlyTotals.Sum(x => x.Count));
        Assert.Equal(3, reader.Reads);
        Assert.False(vm.CanNextMovements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Only_real_router_compatible_movement_codes_pass(int type)
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.MovementTypeDraft = type;
        await vm.ApplyMovementTypeFilterAsync();
        Assert.Equal(type, reader.Last!.MovementType);
        Assert.Equal(type, vm.Report!.MovementType);
        Assert.Equal(2, reader.Reads);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(99)]
    public async Task Invalid_movement_types_never_reach_canonical_service(int invalid)
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.MovementTypeDraft = invalid;
        await vm.ApplyMovementTypeFilterAsync();
        Assert.Equal(1, reader.Reads);
        Assert.NotNull(vm.Report);
        Assert.Equal(0, vm.Report!.MovementType);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Source_echo_mismatch_fails_closed_and_cannot_display_forced_type()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        reader.ForgedType = 4;
        vm.MovementTypeDraft = 1;
        await vm.ApplyMovementTypeFilterAsync();
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Applying_new_dates_keeps_existing_type_and_clears_independent_page_indices()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.MovementTypeDraft = 3;
        await vm.ApplyMovementTypeFilterAsync();
        vm.DateFromDraft = "2026-11-01";
        vm.DateToDraft = "2026-11-30";
        await vm.ApplyDateFiltersAsync();
        Assert.Equal(3, reader.Last!.MovementType);
        Assert.Equal("2026-11-01", reader.Last.DateFrom);
        Assert.Equal("2026-11-30", reader.Last.DateTo);
        Assert.Equal(1, reader.Last.MovementPage);
        Assert.Equal(3, vm.Report!.MovementType);
    }

    [Fact]
    public async Task Clearing_type_keeps_dates_and_returns_real_unfiltered_movement_total()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        vm.DateFromDraft = "2026-11-01";
        await vm.ApplyDateFiltersAsync();
        vm.MovementTypeDraft = 4;
        await vm.ApplyMovementTypeFilterAsync();
        await vm.ClearMovementTypeFilterAsync();
        Assert.Equal("2026-11-01", reader.Last!.DateFrom);
        Assert.Equal(0, reader.Last.MovementType);
        Assert.Equal(101, vm.Report!.MovementTotal);
        Assert.Equal(0, vm.MovementTypeDraft);
    }

    [Fact]
    public async Task Unauthorized_or_stale_person_cannot_read_filtered_movement_history()
    {
        NativeIdentity? actor = Manager;
        var row = Person();
        bool current = true;
        var reader = new FakeReader();
        var vm = Make(reader, () => actor,
            selected => current && ReferenceEquals(selected, row));
        await vm.OpenAsync(row);
        vm.MovementTypeDraft = 2;
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.ApplyMovementTypeFilterAsync();
        Assert.Equal(1, reader.Reads);
        vm.Close();
        actor = Manager;
        current = false;
        await vm.OpenAsync(row);
        Assert.False(vm.IsOpen);
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public async Task Old_filter_responses_cannot_replace_latest_results()
    {
        var reader = new FakeReader();
        var vm = Make(reader, () => Manager);
        await vm.OpenAsync(Person());
        var late = new TaskCompletionSource<NativePersonnelReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = late.Task;
        vm.MovementTypeDraft = 1;
        var previous = vm.ApplyMovementTypeFilterAsync();
        reader.Pending = null;
        vm.MovementTypeDraft = 2;
        await vm.ApplyMovementTypeFilterAsync();
        late.SetResult(Snapshot(new NativeIndividualReportPages(
            "PER-MOVEMENTS", MovementType: 1)));
        await previous;
        Assert.Equal(2, vm.Report!.MovementType);
        Assert.Equal("نقل خارجي", vm.Report.Movements[0].Type);
    }

    [Fact]
    public void Movement_filter_choices_are_complete_unambiguous_and_bounded()
    {
        var vm = new NativePersonnelReportViewModel();
        Assert.Equal([0, 1, 2, 3, 4],
            vm.MovementTypeOptions.Select(x => x.Value).ToArray());
        Assert.Equal("جميع الحركات", vm.MovementTypeOptions[0].Title);
        Assert.Equal("نقل داخلي", vm.MovementTypeOptions[1].Title);
        Assert.Equal("نقل خارجي", vm.MovementTypeOptions[2].Title);
        Assert.Equal("فصل", vm.MovementTypeOptions[3].Title);
        Assert.Equal("عودة للخدمة", vm.MovementTypeOptions[4].Title);
    }

    private sealed class FakeReader : INativePersonnelReportReader
    {
        public NativeIndividualReportPages? Last;
        public int Reads;
        public int? ForgedType;
        public Task<NativePersonnelReportSnapshot>? Pending;
        public Task<NativePersonnelReportSnapshot> ReadIndividualAsync(
            NativeIdentity actor, NativeIndividualReportPages pages,
            CancellationToken ct = default)
        {
            ++Reads;
            Last = pages;
            return Pending ?? Task.FromResult(Snapshot(pages, ForgedType));
        }
    }
}
