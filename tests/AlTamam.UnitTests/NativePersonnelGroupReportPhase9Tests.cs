using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 9 group-report RED tests: the only authority is the original
/// read-only SQLCipher report, selected from the current roster by PersonId.
/// </summary>
public sealed class NativePersonnelGroupReportPhase9Tests
{
    private static readonly NativeIdentity Manager =
        new("GROUP-USER", "admin", UserRole.GeneralAdministration, "GROUP-NONCE");
    private static NativePersonnelDisplayRow Row(string id, int number) =>
        new(number, id, number.ToString("D6"), "رقيب", "فرد " + number,
            "موقع محفوظ", "على القوة", true, 1, []);
    private static readonly NativePersonnelDisplayRow One = Row("PER-GROUP-001", 1);
    private static readonly NativePersonnelDisplayRow Two = Row("PER-GROUP-002", 2);

    private static NativePersonnelGroupReportSnapshot Snapshot(
        NativePersonnelGroupReportRequest request)
    {
        var people = request.PersonIds.Select(id => new NativePersonnelGroupPerson(
            id, "الاسم الحالي", "00001", "2024-01-01", 1, true,
            101L, 1L, [new("STS-P", "حاضر", 101)])).ToArray();
        var daily = request.SummaryPage == 1
            ? Enumerable.Range(0, 100).Select(i => new NativePersonnelGroupDailyStatus(
                new DateOnly(2026, 7, 1).AddDays(i).ToString("yyyy-MM-dd"),
                "STS-P", "حاضر", request.PersonIds.Count,
                request.PersonIds.Count)).ToArray()
            : request.SummaryPage == 2
                ? [new NativePersonnelGroupDailyStatus(
                    "2026-10-09", "STS-P", "حاضر",
                    request.PersonIds.Count, request.PersonIds.Count)]
                : [];
        return new NativePersonnelGroupReportSnapshot(
            request.PersonIds, request.PersonIds.Count, 101L * request.PersonIds.Count,
            request.PersonIds.Count, request.DateFrom, request.DateTo,
            request.SiteId, request.StatusId, request.MovementType,
            request.SummaryPage, request.SummaryPage == 1,
            people, [new("STS-P", "حاضر", 101L * request.PersonIds.Count)], daily)
        {
            AvailableSites = [new("SITE-OLD", "موقع تاريخي")],
            AvailableStatuses = [new("STS-P", "حاضر")]
        };
    }

    private static NativePersonnelGroupReportViewModel VM(
        FakeReader reader, Func<NativeIdentity?> actor,
        Func<IReadOnlyList<NativePersonnelDisplayRow>> rows)
    {
        var vm = new NativePersonnelGroupReportViewModel();
        vm.AttachBackend(reader, actor, rows);
        return vm;
    }

    [Fact]
    public async Task Explicit_selected_current_roster_rows_keep_identity_and_source_order()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows([Two, One]);
        await vm.RunAsync();
        Assert.Equal(["PER-GROUP-002", "PER-GROUP-001"], reader.Last!.PersonIds);
        Assert.Equal(2, vm.Report!.PersonCount);
        Assert.Equal(202, vm.Report.AttendanceTotal);
        Assert.Equal(2, vm.Report.MovementTotal);
        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal(1, reader.Reads);
        Assert.True(vm.CanNextSummary);
    }

    [Fact]
    public async Task Missing_explicit_selection_and_outside_page_row_cannot_be_read()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        await vm.RunAsync();
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.Equal(0, reader.Reads);
        vm.SelectRows([Row("PER-GROUP-001", 1)]);
        await vm.RunAsync();
        Assert.Equal(0, reader.Reads);
        Assert.Null(vm.Report);
    }

    [Fact]
    public void More_than_100_rows_or_duplicate_person_ids_fail_closed()
    {
        var reader = new FakeReader();
        var oversized = Enumerable.Range(0, 101)
            .Select(i => Row("GROUP-KEY-" + i.ToString("D3"), i)).ToArray();
        var vm = VM(reader, () => Manager, () => oversized);
        vm.Open(oversized);
        Assert.False(vm.IsOpen);
        var duplicates = new[] { One, Row(One.PersonId, 20) };
        var other = VM(reader, () => Manager, () => duplicates);
        other.Open(duplicates);
        Assert.False(other.IsOpen);
        Assert.Equal(0, reader.Reads);
    }

    [Fact]
    public async Task Inclusive_dates_and_movement_type_survive_source_paging()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows(roster);
        vm.DateFromDraft = "2026-07-01";
        vm.DateToDraft = "2026-10-09";
        vm.MovementTypeDraft = 2;
        await vm.RunAsync();
        Assert.Equal("2026-07-01", reader.Last!.DateFrom);
        Assert.Equal("2026-10-09", reader.Last.DateTo);
        Assert.Equal(2, reader.Last.MovementType);
        await vm.NextSummaryAsync();
        Assert.Equal(2, reader.Last!.SummaryPage);
        Assert.Equal(2, vm.Report!.DailyStatusPage);
        Assert.Single(vm.Report.DailyStatuses);
        Assert.False(vm.CanNextSummary);
        await vm.PreviousSummaryAsync();
        Assert.Equal(1, reader.Last!.SummaryPage);
        Assert.Equal(100, vm.Report!.DailyStatuses.Count);
        Assert.Equal(3, reader.Reads);
    }

    [Theory]
    [InlineData("2026-11-31", "", 0)]
    [InlineData("2026-11-20", "2026-11-01", 0)]
    [InlineData("", "", -1)]
    [InlineData("", "", 5)]
    public async Task Invalid_date_or_movement_filter_does_not_touch_sqlcipher(
        string from, string to, int movementType)
    {
        var roster = new[] { One };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows(roster);
        vm.DateFromDraft = from;
        vm.DateToDraft = to;
        vm.MovementTypeDraft = movementType;
        await vm.RunAsync();
        Assert.Equal(0, reader.Reads);
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Clearing_all_filters_resets_daily_page_and_reloads_real_source()
    {
        var roster = new[] { One };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows(roster);
        vm.DateFromDraft = "2026-07-01";
        vm.MovementTypeDraft = 4;
        await vm.RunAsync();
        await vm.NextSummaryAsync();
        await vm.ClearFiltersAsync();
        Assert.Equal("", reader.Last!.DateFrom);
        Assert.Equal("", reader.Last.DateTo);
        Assert.Equal(0, reader.Last.MovementType);
        Assert.Equal(1, reader.Last.SummaryPage);
        Assert.Equal(1, vm.Report!.DailyStatusPage);
    }

    [Fact]
    public async Task Forged_person_order_or_totals_are_rejected_without_showing_any_data()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader { ForgePersonOrder = true };
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows(roster);
        await vm.RunAsync();
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
        reader.ForgePersonOrder = false;
        reader.ForgeTotals = true;
        await vm.RunAsync();
        Assert.Null(vm.Report);
    }

    [Fact]
    public async Task Wrong_returned_page_or_date_is_rejected()
    {
        var roster = new[] { One };
        var reader = new FakeReader { ForgePage = true };
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows(roster);
        await vm.RunAsync();
        Assert.Null(vm.Report);
        reader.ForgePage = false;
        reader.ForgeDate = true;
        await vm.RunAsync();
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public async Task Session_revocation_and_roster_refresh_invalidate_original_selection()
    {
        var roster = new[] { One, Two };
        IReadOnlyList<NativePersonnelDisplayRow> current = roster;
        NativeIdentity? actor = Manager;
        var reader = new FakeReader();
        var vm = VM(reader, () => actor, () => current);
        vm.Open(roster);
        vm.SelectRows(roster);
        actor = Manager with { Role = UserRole.Supervisor };
        await vm.RunAsync();
        Assert.Equal(0, reader.Reads);
        Assert.Null(vm.Report);
        actor = Manager;
        vm.Open(roster);
        vm.SelectRows(roster);
        current = [One, Two]; // new roster = stale object identities
        await vm.RunAsync();
        Assert.Equal(0, reader.Reads);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task Superseded_async_read_cannot_overwrite_new_selection()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows([One]);
        var late = new TaskCompletionSource<NativePersonnelGroupReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = late.Task;
        Task oldRead = vm.RunAsync();
        vm.SelectRows([Two]);
        reader.Pending = null;
        await vm.RunAsync();
        late.SetResult(Snapshot(new NativePersonnelGroupReportRequest([One.PersonId])));
        await oldRead;
        Assert.Equal(Two.PersonId, Assert.Single(vm.Report!.Persons).PersonId);
        Assert.Equal(2, reader.Reads);
    }

    [Fact]
    public async Task Closing_dialog_clears_sensitive_person_data_and_ignores_late_result()
    {
        var roster = new[] { One };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows(roster);
        var late = new TaskCompletionSource<NativePersonnelGroupReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = late.Task;
        Task oldRead = vm.RunAsync();
        vm.Close();
        late.SetResult(Snapshot(new NativePersonnelGroupReportRequest([One.PersonId])));
        await oldRead;
        Assert.Null(vm.Report);
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.CandidateRows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_selection_clears_previous_approved_group_and_prevents_read(
        bool duplicate)
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows([One]);
        await vm.RunAsync();
        Assert.NotNull(vm.Report);
        Assert.Equal(1, reader.Reads);

        vm.SelectRows(duplicate ? [One, One] : [Row(Two.PersonId, 2)]);
        Assert.Equal(0, vm.SelectedCount);
        Assert.Null(vm.Report);
        Assert.False(vm.RunCommand.CanExecute(null));
        Assert.False(vm.CanNextSummary);
        await vm.RunAsync();
        Assert.Equal(1, reader.Reads);

        vm.SelectRows([Two]);
        await vm.RunAsync();
        Assert.Equal(2, reader.Reads);
        Assert.Equal(Two.PersonId, Assert.Single(vm.Report!.PersonIds));
    }

    [Fact]
    public async Task Rejected_selection_during_read_cancels_old_group_and_blocks_new_read()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows([One]);
        var late = new TaskCompletionSource<NativePersonnelGroupReportSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = late.Task;
        Task oldRead = vm.RunAsync();
        vm.SelectRows([Row(Two.PersonId, 2)]);
        reader.Pending = null;
        await vm.RunAsync();
        late.SetResult(Snapshot(new NativePersonnelGroupReportRequest([One.PersonId])));
        await oldRead;
        Assert.Equal(1, reader.Reads);
        Assert.Equal(0, vm.SelectedCount);
        Assert.Null(vm.Report);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Individual_status_breakdown_is_preserved_for_each_immutable_person()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader { DistinctStatuses = true };
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows([Two, One]);
        await vm.RunAsync();
        var people = vm.Report!.Persons;
        Assert.Equal(Two.PersonId, people[0].PersonId);
        Assert.Equal(One.PersonId, people[1].PersonId);
        Assert.Equal(people[0].ServiceNumberA, people[1].ServiceNumberA);
        Assert.Equal("STS-P", Assert.Single(people[0].StatusTotals).StatusId);
        Assert.Equal("STS-L", Assert.Single(people[1].StatusTotals).StatusId);
        vm.SelectRows([One]);
        Assert.Null(vm.Report);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inconsistent_or_duplicate_status_breakdowns_fail_closed(bool duplicate)
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader { ForgeStatusBreakdown = !duplicate, DuplicateStatus = duplicate };
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows(roster);
        await vm.RunAsync();
        Assert.Null(vm.Report);
        Assert.NotEmpty(vm.ErrorMessage);
        Assert.False(vm.CanNextSummary);
    }

    [Fact]
    public async Task Historical_group_filters_require_verified_choices_and_survive_paging()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows(roster);
        await vm.RunAsync();
        Assert.Contains(vm.SiteFilterOptions, x => x.Id == "SITE-OLD");
        Assert.Contains(vm.StatusFilterOptions, x => x.Id == "STS-P");
        vm.SiteIdDraft = "SITE-OLD";
        vm.StatusIdDraft = "STS-P";
        Assert.Null(vm.Report);
        await vm.RunAsync();
        Assert.Equal("SITE-OLD", reader.Last!.SiteId);
        Assert.Equal("STS-P", reader.Last.StatusId);
        await vm.NextSummaryAsync();
        Assert.Equal(2, reader.Last!.SummaryPage);
        Assert.Equal("SITE-OLD", reader.Last.SiteId);
        Assert.Equal("STS-P", reader.Last.StatusId);
        await vm.ClearFiltersAsync();
        Assert.Equal("", reader.Last!.SiteId);
        Assert.Equal("", reader.Last.StatusId);
        Assert.Equal(1, reader.Last.SummaryPage);
    }

    [Fact]
    public async Task Group_filter_catalog_and_drafts_clear_when_selected_people_change()
    {
        var roster = new[] { One, Two };
        var reader = new FakeReader();
        var vm = VM(reader, () => Manager, () => roster);
        vm.Open(roster);
        vm.SelectRows([One]);
        await vm.RunAsync();
        vm.SiteIdDraft = "SITE-OLD";
        vm.StatusIdDraft = "STS-P";
        vm.SelectRows([Two]);
        Assert.Equal("", vm.SiteIdDraft);
        Assert.Equal("", vm.StatusIdDraft);
        Assert.Single(vm.SiteFilterOptions);
        Assert.Single(vm.StatusFilterOptions);
        vm.SiteIdDraft = "SITE-FOREIGN";
        await vm.RunAsync();
        Assert.Equal(1, reader.Reads);
        Assert.Null(vm.Report);
    }

    private sealed class FakeReader : INativePersonnelGroupReportReader
    {
        public NativePersonnelGroupReportRequest? Last;
        public int Reads;
        public bool ForgePersonOrder, ForgeTotals, ForgePage, ForgeDate;
        public bool DistinctStatuses, ForgeStatusBreakdown, DuplicateStatus;
        public Task<NativePersonnelGroupReportSnapshot>? Pending;
        public Task<NativePersonnelGroupReportSnapshot> ReadGroupAsync(
            NativeIdentity actor, NativePersonnelGroupReportRequest request,
            CancellationToken ct = default)
        {
            Last = request;
            ++Reads;
            if (Pending is { } pending) return pending;
            var result = Snapshot(request);
            if (DistinctStatuses && result.Persons.Count == 2)
            {
                var people = result.Persons.ToArray();
                people[1] = people[1] with { StatusTotals = [new("STS-L", "إجازة", 101)] };
                result = result with
                {
                    Persons = people,
                    StatusTotals = [new("STS-P", "حاضر", 101), new("STS-L", "إجازة", 101)]
                };
            }
            if (ForgeStatusBreakdown)
                result = result with { StatusTotals = [new("STS-L", "إجازة", result.AttendanceTotal)] };
            if (DuplicateStatus)
            {
                var people = result.Persons.ToArray();
                people[0] = people[0] with
                {
                    StatusTotals = [new("STS-P", "حاضر", 50), new("STS-P", "حاضر", 51)]
                };
                result = result with { Persons = people };
            }
            if (ForgePersonOrder)
                result = result with { PersonIds = result.PersonIds.Reverse().ToArray() };
            if (ForgeTotals)
                result = result with { AttendanceTotal = 9999 };
            if (ForgePage)
                result = result with { DailyStatusPage = request.SummaryPage + 1 };
            if (ForgeDate)
                result = result with { DateFrom = "2024-01-01" };
            return Task.FromResult(result);
        }
    }
}
