using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>Monthly WPF tests first: only canonical SQLCipher historical rows;
/// no fabricated cells, stale session leaks or independent attendance writers.</summary>
public sealed class NativeMonthlyAttendancePhase7Tests
{
    private static readonly NativeIdentity Actor =
        new("USR-1", "admin", UserRole.GeneralAdministration, "MONTH-NONCE");
    private static string Current => DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM");
    private static string Previous => DateOnly.FromDateTime(DateTime.Now).AddMonths(-1).ToString("yyyy-MM");

    [Fact]
    public async Task No_live_session_does_not_read_months_or_people()
    {
        var reader = new Reader();
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => null);
        await vm.RefreshAsync();
        Assert.Equal(0, reader.Catalogues);
        Assert.Empty(reader.Requests);
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);
    }

    [Fact]
    public async Task Native_monthly_page_uses_real_historical_cells_and_ranks_without_inventing_attendance()
    {
        var reader = new Reader();
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => Actor);
        await vm.RefreshAsync();

        Assert.True(vm.IsDataConnected);
        Assert.Equal(Current, vm.SelectedPeriod);
        Assert.Single(reader.Requests);
        Assert.Equal(100, reader.Requests[0].PageSize);
        Assert.Equal("الرتبة", vm.RankColumnLabel);
        var row = Assert.Single(vm.Rows);
        Assert.Equal("رقيب", row.Rank);
        Assert.Equal("أحمد", row.Name);
        Assert.Equal("ح", row.Days[0].Symbol);
        Assert.Contains("حضور", row.Days[0].Description);
        Assert.Equal("", row.Days[1].Symbol);
        Assert.Equal(DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month), vm.Days.Count);
        Assert.Single(vm.CustomColumns);
        Assert.Equal("ملاحظة", vm.CustomColumns[0].Caption);
        Assert.Equal("منضبط", row.CustomCells[0]);
        Assert.Contains(vm.Periods, x => x.Period == Previous);
    }

    [Fact]
    public async Task Site_search_archive_and_pagination_use_canonical_filter_and_reset_page()
    {
        var reader = new Reader { Total = 150 };
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => Actor);
        await vm.RefreshAsync();
        await vm.NextPageAsync();
        Assert.Equal(2, reader.Requests[^1].Page);
        await vm.SelectPeriodAsync(Previous);
        Assert.Equal(Previous, reader.Requests[^1].Period);
        Assert.Equal(1, reader.Requests[^1].Page);
        await vm.SelectSiteAsync("SITE-A");
        Assert.Equal("SITE-A", reader.Requests[^1].SiteId);
        await vm.SearchAsync("أحمد");
        Assert.Equal("أحمد", reader.Requests[^1].SearchTerm);
        Assert.Equal(1, reader.Requests[^1].Page);
        await vm.PreviousPageAsync();
        Assert.Equal(1, vm.Page);
    }

    [Fact]
    public async Task Future_or_unlisted_month_and_invalid_site_or_search_are_rejected()
    {
        var reader = new Reader();
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => Actor);
        await vm.RefreshAsync();
        int count = reader.Requests.Count;
        await vm.SelectPeriodAsync("2200-12");
        Assert.Equal(count, reader.Requests.Count);
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.LoadError);
        await vm.SelectSiteAsync("SITE-FORGED");
        Assert.Equal(count, reader.Requests.Count);
        Assert.Empty(vm.Rows);
        await vm.SearchAsync(new string('x', 101));
        Assert.Equal(count, reader.Requests.Count);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Logout_and_replaced_nonce_scrub_personnel_and_reject_stale_async_monthly_reads()
    {
        var reader = new Reader();
        NativeIdentity? live = Actor;
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => live);
        await vm.RefreshAsync();
        Assert.True(vm.IsDataConnected);

        var pending = new TaskCompletionSource<NativeMonthlySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = pending.Task;
        var loading = vm.RefreshAsync();
        vm.Clear();
        live = null;
        pending.SetResult(reader.Valid(reader.Requests[^1]));
        await loading;
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.Periods);
        Assert.Equal(0, vm.TotalRows);

        reader.Pending = null;
        live = Actor;
        await vm.RefreshAsync();
        Assert.True(vm.IsDataConnected);
        live = Actor with { SessionNonce = "NEW-NONCE" };
        await vm.RefreshAsync();
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Malformed_service_response_never_leaves_cached_personnel_visible()
    {
        var reader = new Reader();
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => Actor);
        await vm.RefreshAsync();
        reader.ChangeEcho = true;
        await vm.RefreshAsync();
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);
        Assert.NotEmpty(vm.LoadError);
    }

    private sealed class Reader : INativeMonthlyAttendanceReader
    {
        public int Catalogues;
        public List<NativeMonthlyFilter> Requests = [];
        public Task<NativeMonthlySnapshot>? Pending;
        public bool ChangeEcho;
        public long Total = 1;

        public Task<NativeMonthlyCatalogue> ReadCatalogueAsync(
            NativeIdentity actor, CancellationToken ct = default)
        {
            Catalogues++;
            return Task.FromResult(new NativeMonthlyCatalogue(
                [Current, Previous],
                [new NativeDashboardSite("SITE-A", "المركز")]));
        }

        public Task<NativeMonthlySnapshot> ReadMonthAsync(
            NativeIdentity actor, NativeMonthlyFilter filter,
            CancellationToken ct = default)
        {
            Requests.Add(filter);
            return Pending ?? Task.FromResult(Valid(filter));
        }

        public NativeMonthlySnapshot Valid(NativeMonthlyFilter filter)
        {
            string period = ChangeEcho ? Previous : filter.Period;
            int year = int.Parse(period[..4]);
            int month = int.Parse(period[5..7]);
            int days = DateTime.DaysInMonth(year, month);
            NativeMonthlyCell?[] cells = Enumerable.Range(0, days)
                .Select(i => i == 0
                    ? new NativeMonthlyCell("ح", "حضور", "#0D9488", "")
                    : null).ToArray();
            NativeMonthlyRow[] rows =
            [
                new("P-1", "123", "أحمد", "المركز", ["رقيب", "منضبط"], cells)
                { RowNumber = (filter.Page - 1) * filter.PageSize + 1 }
            ];
            return new NativeMonthlySnapshot(
                period, filter.SiteId, filter.Page, days, Total,
                (long)filter.Page * filter.PageSize < Total,
                [new("rank", "الرتبة"), new("note", "ملاحظة")], rows);
        }
    }
}
