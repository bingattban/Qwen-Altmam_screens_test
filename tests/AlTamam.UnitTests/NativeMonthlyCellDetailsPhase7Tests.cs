using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Monthly historical cell drill-down: canonical SQLCipher only; no implied
/// status, no write, no stale or cross-session personal details.
/// </summary>
public sealed class NativeMonthlyCellDetailsPhase7Tests
{
    private static readonly NativeIdentity Actor =
        new("USR-1", "admin", UserRole.GeneralAdministration, "MONTH-DETAIL-NONCE");
    private static string Current => DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM");

    [Fact]
    public async Task A_real_day_cell_opens_historical_identity_status_movement_and_revisions()
    {
        var reader = new Reader();
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => Actor);
        await vm.RefreshAsync();
        Assert.Equal(0, reader.DetailReads);
        Assert.Null(vm.SelectedCellDetails);

        var row = Assert.Single(vm.Rows);
        await vm.OpenCellDetailsAsync(row, 1);
        Assert.Equal(1, reader.DetailReads);
        Assert.True(vm.IsCellDetailsOpen);
        Assert.False(vm.IsCellDetailsLoading);
        Assert.NotNull(vm.SelectedCellDetails);
        Assert.Equal("أحمد في التاريخ", vm.SelectedCellDetails.PersonName);
        Assert.Equal("أحمد من الكشف", row.Name);
        Assert.Equal("حاضر", vm.SelectedCellDetails.AttendanceStatus);
        Assert.Equal("نقل داخلي من الموقع القديم إلى الموقع الجديد", vm.SelectedCellDetails.MovementSummary);
        Assert.Single(vm.SelectedCellDetails.Revisions);
        Assert.Equal("غائب", vm.SelectedCellDetails.Revisions[0].BeforeStatus);
        Assert.Equal("حاضر", vm.SelectedCellDetails.Revisions[0].AfterStatus);
    }

    [Fact]
    public async Task Forged_old_or_out_of_range_rows_never_start_detail_reads()
    {
        var reader = new Reader();
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => Actor);
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Rows);
        await vm.OpenCellDetailsAsync(row with { PersonId = "FORGED" }, 1);
        await vm.OpenCellDetailsAsync(row, 0);
        await vm.OpenCellDetailsAsync(row, 32);
        Assert.Equal(0, reader.DetailReads);
        Assert.False(vm.IsCellDetailsOpen);

        await vm.SearchAsync("another");
        await vm.OpenCellDetailsAsync(row, 1);
        Assert.Equal(0, reader.DetailReads);
        Assert.False(vm.IsCellDetailsOpen);
    }

    [Fact]
    public async Task Logout_and_nonce_change_revoke_in_flight_historical_details()
    {
        var reader = new Reader();
        NativeIdentity? live = Actor;
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => live);
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Rows);

        var pending = new TaskCompletionSource<NativeMonthlyCellDetails>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending = pending.Task;
        var loading = vm.OpenCellDetailsAsync(row, 1);
        Assert.True(vm.IsCellDetailsOpen);
        Assert.True(vm.IsCellDetailsLoading);
        vm.Clear();
        live = null;
        pending.SetResult(reader.ValidDetail(new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1)));
        await loading;
        Assert.False(vm.IsCellDetailsOpen);
        Assert.Null(vm.SelectedCellDetails);

        reader.Pending = null;
        live = Actor;
        await vm.RefreshAsync();
        live = Actor with { SessionNonce = "CHANGED" };
        await vm.OpenCellDetailsAsync(Assert.Single(vm.Rows), 1);
        Assert.False(vm.IsCellDetailsOpen);
        Assert.Equal(1, reader.DetailReads);
    }

    [Fact]
    public async Task Invalid_canonical_detail_echo_cannot_show_old_or_foreign_person()
    {
        var reader = new Reader();
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => Actor);
        await vm.RefreshAsync();
        var row = Assert.Single(vm.Rows);
        await vm.OpenCellDetailsAsync(row, 1);
        Assert.NotNull(vm.SelectedCellDetails);
        vm.CloseCellDetails();
        Assert.Null(vm.SelectedCellDetails);

        reader.ForgeEcho = true;
        await vm.OpenCellDetailsAsync(row, 1);
        Assert.True(vm.IsCellDetailsOpen);
        Assert.Null(vm.SelectedCellDetails);
        Assert.NotEmpty(vm.CellDetailsError);
        Assert.False(vm.IsCellDetailsLoading);
    }

    private sealed class Reader : INativeMonthlyAttendanceReader, INativeMonthlyCellDetailsReader
    {
        public int DetailReads;
        public bool ForgeEcho;
        public Task<NativeMonthlyCellDetails>? Pending;

        public Task<NativeMonthlyCatalogue> ReadCatalogueAsync(
            NativeIdentity actor, CancellationToken ct = default) =>
            Task.FromResult(new NativeMonthlyCatalogue(
                [Current], [new NativeDashboardSite("SITE-A", "المركز")]));

        public Task<NativeMonthlySnapshot> ReadMonthAsync(
            NativeIdentity actor, NativeMonthlyFilter filter, CancellationToken ct = default)
        {
            int days = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month);
            NativeMonthlyRow[] rows = filter.SearchTerm.Length == 0
                ? [new("P-1", "123", "أحمد من الكشف", "المركز",
                    ["رقيب"], new NativeMonthlyCell?[days])
                    { RowNumber = 1 }]
                : [];
            return Task.FromResult(new NativeMonthlySnapshot(
                filter.Period, filter.SiteId, filter.Page, days, rows.Length, false,
                [new NativeDashboardColumn("rank", "الرتبة")], rows));
        }

        public Task<NativeMonthlyCellDetails> ReadCellDetailsAsync(
            NativeIdentity actor, NativeMonthlyCellRequest request,
            CancellationToken ct = default)
        {
            DetailReads++;
            return Pending ?? Task.FromResult(ValidDetail(request.BusinessDate));
        }

        public NativeMonthlyCellDetails ValidDetail(DateOnly date) =>
            new(ForgeEcho ? "FORGED-PERSON" : "P-1", date,
                "أحمد في التاريخ", "987", "الموقع القديم",
                "حاضر", "ح", "الموقع القديم", "manual", "2026-11-01T09:00:00Z",
                "نقل داخلي من الموقع القديم إلى الموقع الجديد", "",
                [new("غائب", "حاضر", "تصحيح", "USR-1",
                    "2026-11-01T09:10:00Z", "password")]);
    }
}
