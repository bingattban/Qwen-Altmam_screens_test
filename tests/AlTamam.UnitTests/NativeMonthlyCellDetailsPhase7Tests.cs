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

    [Fact]
    public async Task Correction_uses_selected_historical_cell_and_refreshes_only_after_verified_receipt()
    {
        var reader = new Reader();
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => Actor);
        await vm.RefreshAsync();
        await vm.SelectPeriodAsync(Previous);
        await vm.OpenCellDetailsAsync(Assert.Single(vm.Rows), 1);
        Assert.True(vm.CanOpenCorrection);
        await vm.OpenCorrectionAsync();
        Assert.True(vm.Correction.IsOpen);
        Assert.Single(vm.Correction.StatusChoices);
        var original = vm.Rows;
        await vm.SearchAsync("another");
        await vm.RefreshAsync();
        vm.CloseCellDetails();
        Assert.Same(original, vm.Rows);
        Assert.True(vm.IsCellDetailsOpen);
        vm.Correction.SelectedStatusId = "STS-ABSENT";
        vm.Correction.Reason = "تصحيح موثق من الإدارة";
        await vm.Correction.PreviewSelectedAsync();
        Assert.NotNull(vm.Correction.Preview);
        Assert.Equal(0, reader.CorrectionCommits);
        await vm.Correction.ConfirmAsync();
        Assert.NotNull(vm.Correction.Receipt);
        Assert.Equal(1, reader.CorrectionCommits);
        await vm.CloseCorrectionAsync();
        Assert.False(vm.Correction.IsOpen);
        Assert.False(vm.IsCellDetailsOpen);
        Assert.NotSame(original, vm.Rows);
        Assert.Equal(Previous, vm.SelectedPeriod);
    }

    [Fact]
    public async Task Correction_rejects_supervisor_and_discards_late_statuses_on_logout()
    {
        var reader = new Reader();
        NativeIdentity? live = Actor with { Role = UserRole.Supervisor };
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(reader, () => live);
        await vm.RefreshAsync();
        await vm.SelectPeriodAsync(Previous);
        await vm.OpenCellDetailsAsync(Assert.Single(vm.Rows), 1);
        Assert.False(vm.CanOpenCorrection);
        await vm.OpenCorrectionAsync();
        Assert.Equal(0, reader.ChoiceReads);
        vm.Clear();
        live = Actor;
        await vm.RefreshAsync();
        await vm.SelectPeriodAsync(Previous);
        await vm.OpenCellDetailsAsync(Assert.Single(vm.Rows), 1);
        var pending = new TaskCompletionSource<IReadOnlyList<NativeAttendanceStatusChoice>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        reader.PendingChoices = pending.Task;
        var opening = vm.OpenCorrectionAsync();
        Assert.True(vm.Correction.IsOpen);
        live = null;
        vm.Clear();
        pending.SetResult([new("STS-ABSENT", "غائب")]);
        await opening;
        Assert.False(vm.Correction.IsOpen);
        Assert.Empty(vm.Correction.StatusChoices);
        Assert.Null(vm.SelectedCellDetails);
    }

    private static string Previous => DateOnly.FromDateTime(DateTime.Now).AddMonths(-1).ToString("yyyy-MM");

    private sealed class Reader : INativeMonthlyAttendanceReader, INativeMonthlyCellDetailsReader,
        INativeClosedDayCorrectionBackend
    {
        public int DetailReads, ChoiceReads, CorrectionCommits;
        public Task<IReadOnlyList<NativeAttendanceStatusChoice>>? PendingChoices;
        private NativeClosedDayCorrectionRequest? _request;
        public bool ForgeEcho;
        public Task<NativeMonthlyCellDetails>? Pending;

        public Task<NativeMonthlyCatalogue> ReadCatalogueAsync(
            NativeIdentity actor, CancellationToken ct = default) =>
            Task.FromResult(new NativeMonthlyCatalogue(
                [Current, Previous], [new NativeDashboardSite("SITE-A", "المركز")]));

        public Task<NativeMonthlySnapshot> ReadMonthAsync(
            NativeIdentity actor, NativeMonthlyFilter filter, CancellationToken ct = default)
        {
            var period = DateOnly.ParseExact(filter.Period + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            int days = DateTime.DaysInMonth(period.Year, period.Month);
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

        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadCorrectionChoicesAsync(
            NativeIdentity actor, CancellationToken ct = default)
        {
            ChoiceReads++;
            return PendingChoices ?? Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>(
                [new("STS-ABSENT", "غائب")]);
        }

        public Task<NativeClosedDayCorrectionPreview> PreviewAsync(NativeIdentity actor,
            NativeClosedDayCorrectionRequest request, CancellationToken ct = default)
        {
            _request = request;
            return Task.FromResult(new NativeClosedDayCorrectionPreview(request,
                new string('a', 64), 2, "أحمد في التاريخ", "987", "الموقع القديم",
                "STS-PRESENT", "حاضر", "غائب", true));
        }

        public Task<NativeClosedDayCorrectionReceipt> CommitAsync(NativeIdentity actor,
            string token, string operationId, CancellationToken ct = default)
        {
            CorrectionCommits++;
            return Task.FromResult(new NativeClosedDayCorrectionReceipt(
                _request!.PersonId, _request.BusinessDate, 3));
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
