using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Phase 6 tests precede the native daily-roster implementation. They cover
/// real-service projection boundaries, original site/status filters, rank
/// metadata, pagination and fail-closed session changes.
/// </summary>
public sealed class NativeDailyAttendancePhase6Tests
{
    private static readonly NativeIdentity Owner =
        new("USR-1", "admin", UserRole.GeneralAdministration, "REAL-DAILY-NONCE");

    [Fact]
    public async Task No_live_session_means_no_query_and_no_personnel()
    {
        var reader = new Reader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(reader, () => null);
        await vm.RefreshAsync();
        Assert.Empty(reader.Calls);
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Native_daily_screen_uses_existing_typed_roster_with_actual_rank_and_extra_columns()
    {
        var reader = new Reader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(reader, () => Owner);
        await vm.RefreshAsync();
        Assert.True(vm.IsDataConnected);
        Assert.Single(reader.Calls);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), reader.Calls[0].BusinessDate);
        Assert.Equal(100, reader.Calls[0].PageSize);
        Assert.Equal("الرتبة", vm.RankColumnLabel);
        Assert.Equal("رقيب", vm.Rows[0].Rank);
        Assert.Equal("المركز", vm.Rows[0].SiteName);
        Assert.Equal("أحمد", vm.Rows[0].Name);
        Assert.Single(vm.CustomColumns);
        Assert.Equal("ملاحظات", vm.CustomColumns[0].Caption);
        Assert.Equal("تجريبي", vm.Rows[0].CustomCells[0]);
        Assert.Contains(vm.StatusOptions, x => x.StatusId == "__UNPREPARED__");
        Assert.Contains(vm.StatusOptions, x => x.StatusId == "STS-PRESENT");
        Assert.Contains(vm.SiteOptions, x => x.SiteId == "SITE-1");
        Assert.Equal(2, vm.TotalRows);
        Assert.DoesNotContain("حفظ", vm.StatusMessage);
    }

    [Fact]
    public async Task Filter_and_page_changes_reuse_canonical_reader_and_reset_paging()
    {
        var reader = new Reader { Total = 150 };
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(reader, () => Owner);
        await vm.RefreshAsync();
        await vm.NextPageAsync();
        Assert.Equal(2, reader.Calls[^1].Page);
        await vm.SelectSiteAsync("SITE-1");
        Assert.Equal(1, reader.Calls[^1].Page);
        Assert.Equal("SITE-1", reader.Calls[^1].SiteId);
        await vm.SelectStatusAsync("__UNPREPARED__");
        Assert.Equal("__UNPREPARED__", reader.Calls[^1].StatusId);
        await vm.SearchAsync("أحمد 123");
        Assert.Equal("أحمد 123", reader.Calls[^1].SearchTerm);
        Assert.Equal(1, reader.Calls[^1].Page);
        await vm.PreviousPageAsync();
        Assert.Equal(1, vm.Page);
        await vm.SearchAsync("");
        Assert.Equal("", reader.Calls[^1].SearchTerm);
    }

    [Fact]
    public async Task Invalid_filters_are_rejected_before_fetch_and_old_rows_are_cleared()
    {
        var reader = new Reader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(reader, () => Owner);
        await vm.RefreshAsync();
        Assert.True(vm.IsDataConnected);
        await vm.SearchAsync(new string('x', 101));
        Assert.Single(reader.Calls);
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);
        Assert.NotEmpty(vm.LoadError);
        await vm.SelectSiteAsync("SITE-UNKNOWN");
        Assert.Single(reader.Calls);
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.LoadError);
    }

    [Fact]
    public async Task Failed_or_mismatched_backend_read_fails_closed_without_stale_rows()
    {
        var reader = new Reader();
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(reader, () => Owner);
        await vm.RefreshAsync();
        reader.WrongSite = true;
        await vm.RefreshAsync();
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.LoadError);
        reader.WrongSite = false;
        reader.Throw = true;
        await vm.RefreshAsync();
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public async Task Logout_or_replaced_nonce_revokes_all_daily_personnel_and_pending_reads()
    {
        var reader = new Reader();
        NativeIdentity? live = Owner;
        var vm = new DailyAttendancePageViewModel();
        vm.AttachReader(reader, () => live);
        await vm.RefreshAsync();
        Assert.True(vm.IsDataConnected);

        var pending = new TaskCompletionSource<NativeDashboardSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = pending.Task;
        var loading = vm.RefreshAsync();
        vm.Clear();
        live = null;
        pending.SetResult(reader.Valid(reader.Calls[^1]));
        await loading;
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.SiteOptions.Where(x => x.SiteId.Length > 0));
        Assert.Equal(0, vm.TotalRows);

        reader.Pending = null;
        live = Owner;
        await vm.RefreshAsync();
        Assert.True(vm.IsDataConnected);
        live = Owner with { SessionNonce = "REPLACED-NONCE" };
        await vm.RefreshAsync();
        Assert.False(vm.IsDataConnected);
        Assert.Empty(vm.Rows);
    }

    private sealed class Reader : INativeDashboardReader
    {
        public List<NativeDashboardFilter> Calls = [];
        public Task<NativeDashboardSnapshot>? Pending;
        public bool Throw;
        public bool WrongSite;
        public long Total = 2;

        public Task<NativeDashboardSnapshot> ReadAsync(
            NativeIdentity identity, NativeDashboardFilter filter,
            CancellationToken ct = default)
        {
            Calls.Add(filter);
            if (Throw) throw new IOException("SQLCipher unavailable");
            return Pending ?? Task.FromResult(Valid(filter));
        }

        public NativeDashboardSnapshot Valid(NativeDashboardFilter filter) =>
            new(filter.BusinessDate, WrongSite ? "ALTERED-SITE" : filter.SiteId,
                24, 4, 2,
                [new("STS-PRESENT", "موجود", 15, "#0D9488"),
                 new("STS-ABSENT", "غائب", 6, "#BE123C")],
                [new("SITE-1", "المركز")],
                [new NativeDashboardRow("P-1", "123", "أحمد", "المركز",
                    "موجود", "#0D9488", ["رقيب", "تجريبي"]) { RowNumber = 1 }],
                [new("rank-field", "الرتبة"), new("extra-field", "ملاحظات")],
                Total, filter.Page, Total > (long)filter.Page * filter.PageSize);
    }
}
