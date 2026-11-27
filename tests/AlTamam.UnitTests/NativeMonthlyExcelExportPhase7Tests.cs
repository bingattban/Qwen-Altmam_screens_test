using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

/// <summary>
/// Native XLSX export tests are added BEFORE the implementation. The canonical
/// service owns the output folder, safe filenames and historical row pages.
/// The WPF UI requires a user preview and a separate confirmation.
/// </summary>
public sealed class NativeMonthlyExcelExportPhase7Tests
{
    private static readonly NativeIdentity Actor =
        new("USR-1", "admin", UserRole.GeneralAdministration, "MONTH-EXCEL-NONCE");
    private static string Current =>
        DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM");

    [Fact]
    public async Task Preview_does_not_write_and_confirm_uses_the_same_month_site_and_applied_filter()
    {
        var backend = new Backend();
        var vm = NewPage(backend, () => Actor);
        await vm.RefreshAsync();
        await vm.SelectSiteAsync("SITE-A");
        await vm.SearchAsync("أحمد");

        await vm.Excel.OpenPreviewAsync();
        Assert.True(vm.Excel.IsOpen);
        Assert.NotNull(vm.Excel.Preview);
        Assert.Equal(0, backend.Exports);
        Assert.Equal(new NativeMonthlyExcelFilter(Current, "SITE-A", "أحمد"),
            Assert.Single(backend.PreviewRequests));

        await vm.Excel.ConfirmAsync();
        Assert.Equal(1, backend.Exports);
        Assert.Equal(backend.PreviewRequests[0], Assert.Single(backend.ExportRequests));
        Assert.NotNull(vm.Excel.Receipt);
        Assert.Equal(Path.Combine(Path.GetTempPath(),"altamam-test-reports","monthly.xlsx"), vm.Excel.Receipt.PrimaryFilePath);
        Assert.Equal(1, vm.Excel.Receipt.FileCount);
    }

    [Fact]
    public async Task All_sites_export_uses_canonical_consolidated_and_per_site_files_only_after_confirm()
    {
        var backend = new Backend();
        var vm = NewPage(backend, () => Actor);
        await vm.RefreshAsync();
        await vm.Excel.OpenPreviewAsync();
        Assert.Equal("", vm.Excel.Preview!.SiteId);
        Assert.Equal(0, backend.Exports);
        await vm.Excel.ConfirmAsync();
        Assert.Equal(1, backend.Exports);
        Assert.Equal(2, vm.Excel.Receipt!.FileCount);
        Assert.Equal("", Assert.Single(backend.ExportRequests).SiteId);
        Assert.Equal(2,vm.Excel.ResultFiles.Count);
        Assert.Equal("SITE-A",vm.Excel.ResultFiles[1].SiteId);
        vm.Excel.Close();
        Assert.Empty(vm.Excel.ResultFiles);
    }

    [Fact]
    public async Task Unapplied_search_and_stale_period_site_or_filter_cannot_export()
    {
        var backend = new Backend();
        var vm = NewPage(backend, () => Actor);
        await vm.RefreshAsync();
        vm.SearchDraft = "غير مطبق";
        await vm.Excel.OpenPreviewAsync();
        Assert.Equal(0, backend.Previews);
        Assert.False(vm.Excel.IsOpen);

        await vm.SearchAsync("");
        await vm.Excel.OpenPreviewAsync();
        Assert.NotNull(vm.Excel.Preview);
        await vm.SelectSiteAsync("SITE-A");
        Assert.False(vm.Excel.IsOpen);
        await vm.Excel.ConfirmAsync();
        Assert.Equal(0, backend.Exports);

        await vm.Excel.OpenPreviewAsync();
        vm.Excel.Close();
        await vm.Excel.ConfirmAsync();
        Assert.Equal(0, backend.Exports);
    }

    [Fact]
    public async Task Logout_and_nonce_replacement_hide_inflight_preview_and_export_results()
    {
        var backend = new Backend();
        NativeIdentity? live = Actor;
        var vm = NewPage(backend, () => live);
        await vm.RefreshAsync();

        var previewPending = new TaskCompletionSource<NativeMonthlyExcelPreview>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.PendingPreview = previewPending.Task;
        var previewRead = vm.Excel.OpenPreviewAsync();
        vm.Clear();
        live = null;
        previewPending.SetResult(backend.MakePreview(
            new NativeMonthlyExcelFilter(Current, "", "")));
        await previewRead;
        Assert.False(vm.Excel.IsOpen);
        Assert.Null(vm.Excel.Preview);

        backend.PendingPreview = null;
        live = Actor;
        await vm.RefreshAsync();
        await vm.Excel.OpenPreviewAsync();
        var exportPending = new TaskCompletionSource<NativeMonthlyExcelReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        backend.PendingExport = exportPending.Task;
        var write = vm.Excel.ConfirmAsync();
        vm.Clear();
        live = Actor with { SessionNonce = "REPLACED" };
        exportPending.SetResult(backend.MakeReceipt(
            new NativeMonthlyExcelFilter(Current, "", "")));
        await write;
        Assert.False(vm.Excel.IsOpen);
        Assert.Null(vm.Excel.Receipt);
        Assert.Equal(1, backend.Exports);
    }

    [Fact]
    public async Task Forged_preview_or_export_receipt_is_rejected_and_never_claimed_successful()
    {
        var backend = new Backend { ForgePreview = true };
        var vm = NewPage(backend, () => Actor);
        await vm.RefreshAsync();
        await vm.Excel.OpenPreviewAsync();
        Assert.Null(vm.Excel.Preview);
        Assert.NotEmpty(vm.Excel.Error);
        await vm.Excel.ConfirmAsync();
        Assert.Equal(0, backend.Exports);

        vm.Excel.Close();
        backend.ForgePreview = false;
        backend.ForgeReceipt = true;
        await vm.Excel.OpenPreviewAsync();
        Assert.NotNull(vm.Excel.Preview);
        await vm.Excel.ConfirmAsync();
        Assert.Equal(1, backend.Exports);
        Assert.Null(vm.Excel.Receipt);
        Assert.NotEmpty(vm.Excel.Error);
    }

    private static MonthlyAttendancePageViewModel NewPage(
        Backend backend, Func<NativeIdentity?> live)
    {
        var vm = new MonthlyAttendancePageViewModel();
        vm.AttachReader(backend, live);
        return vm;
    }

    private sealed class Backend : INativeMonthlyAttendanceReader, INativeMonthlyExcelExportBackend
    {
        public int Previews;
        public int Exports;
        public bool ForgePreview;
        public bool ForgeReceipt;
        public Task<NativeMonthlyExcelPreview>? PendingPreview;
        public Task<NativeMonthlyExcelReceipt>? PendingExport;
        public readonly List<NativeMonthlyExcelFilter> PreviewRequests = [];
        public readonly List<NativeMonthlyExcelFilter> ExportRequests = [];

        public Task<NativeMonthlyCatalogue> ReadCatalogueAsync(
            NativeIdentity actor, CancellationToken ct = default) =>
            Task.FromResult(new NativeMonthlyCatalogue(
                [Current], [new NativeDashboardSite("SITE-A", "المركز")]));

        public Task<NativeMonthlySnapshot> ReadMonthAsync(
            NativeIdentity actor, NativeMonthlyFilter filter,
            CancellationToken ct = default) =>
            Task.FromResult(new NativeMonthlySnapshot(
                filter.Period, filter.SiteId, filter.Page,
                DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month), 1, false,
                [], [new NativeMonthlyRow("P-1", "123", "أحمد", "المركز",
                    [], new NativeMonthlyCell?[
                        DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month)])]));

        public Task<NativeMonthlyExcelPreview> PreviewExcelAsync(
            NativeIdentity actor, NativeMonthlyExcelFilter filter, CancellationToken ct = default)
        {
            Previews++;
            PreviewRequests.Add(filter);
            return PendingPreview ?? Task.FromResult(MakePreview(filter));
        }

        public Task<NativeMonthlyExcelReceipt> ExportExcelAsync(
            NativeIdentity actor, NativeMonthlyExcelFilter filter, CancellationToken ct = default)
        {
            Exports++;
            ExportRequests.Add(filter);
            return PendingExport ?? Task.FromResult(MakeReceipt(filter));
        }

        public NativeMonthlyExcelPreview MakePreview(NativeMonthlyExcelFilter filter) =>
            new(ForgePreview ? "1900-01" : filter.Period, filter.SiteId,
                filter.SearchTerm, "المركز", "month.xlsx", 6,
                DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month));

        public NativeMonthlyExcelReceipt MakeReceipt(NativeMonthlyExcelFilter filter) =>
            new(ForgeReceipt ? "1900-01" : filter.Period, filter.SiteId,
                filter.SearchTerm, Path.Combine(Path.GetTempPath(),"altamam-test-reports","monthly.xlsx"),
                filter.SiteId == "" ? 2 : 1, 6)
            {
                Files=filter.SiteId==""?
                    [new("","جميع المواقع",Path.Combine(Path.GetTempPath(),"altamam-test-reports","monthly.xlsx"),6),new("SITE-A","المركز",Path.Combine(Path.GetTempPath(),"altamam-test-reports","site.xlsx"),6)]:
                    [new(filter.SiteId,"المركز",Path.Combine(Path.GetTempPath(),"altamam-test-reports","monthly.xlsx"),6)]
            };
    }
}
