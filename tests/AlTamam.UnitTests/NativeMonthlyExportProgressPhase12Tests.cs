using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeMonthlyExportProgressPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","admin",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeMonthlyExcelFilter Filter=>new(DateTime.Now.ToString("yyyy-MM",System.Globalization.CultureInfo.InvariantCulture),"SITE-A","");
    private static NativeMonthlyExcelExportViewModel Create(Probe b,Func<NativeIdentity?> live)
    {
        var vm=new NativeMonthlyExcelExportViewModel();var filter=Filter;
        vm.Attach(b,live,()=>filter);return vm;
    }

    [Fact]
    public async Task Progress_never_claims_completion_before_receipt()
    {
        var b=new Probe();var vm=Create(b,()=>Actor);
        await vm.OpenPreviewAsync();Assert.Equal(0,b.Exports);
        var work=vm.ConfirmAsync();
        Assert.True(vm.IsExporting);Assert.False(vm.CloseCommand.CanExecute(null));
        b.Report!(4,10);Assert.Equal(40,vm.ProgressPercent);
        b.Report(10,10);Assert.Equal(99,vm.ProgressPercent);
        b.Report(2,10);Assert.Equal(99,vm.ProgressPercent);
        Assert.Null(vm.Receipt);
        b.Pending.SetResult(b.Receipt());await work;
        Assert.Equal(100,vm.ProgressPercent);Assert.NotNull(vm.Receipt);
        Assert.False(vm.IsExporting);Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task Cancellation_requests_stop_and_never_retries_unverified_result()
    {
        var b=new Probe();var vm=Create(b,()=>Actor);
        await vm.OpenPreviewAsync();var work=vm.ConfirmAsync();
        vm.CancelExportCommand.Execute(null);
        Assert.True(b.Token.IsCancellationRequested);
        Assert.True(vm.IsExporting);Assert.True(vm.IsCancelRequested);
        Assert.False(vm.CancelExportCommand.CanExecute(null));
        b.Pending.SetCanceled(b.Token);await work;
        Assert.False(vm.IsExporting);Assert.Null(vm.Receipt);Assert.False(vm.CanConfirm);
        Assert.NotEmpty(vm.Error);await vm.ConfirmAsync();Assert.Equal(1,b.Exports);
    }

    [Fact]
    public async Task Cancellation_after_publication_does_not_hide_verified_receipt()
    {
        var b=new Probe();var vm=Create(b,()=>Actor);
        await vm.OpenPreviewAsync();var work=vm.ConfirmAsync();
        vm.CancelExportCommand.Execute(null);
        b.Pending.SetResult(b.Receipt());await work;
        Assert.NotNull(vm.Receipt);Assert.Equal(100,vm.ProgressPercent);
    }

    [Fact]
    public async Task Revoked_session_ignores_late_progress_and_receipt()
    {
        NativeIdentity? live=Actor;var b=new Probe();var vm=Create(b,()=>live);
        await vm.OpenPreviewAsync();var work=vm.ConfirmAsync();
        live=null;vm.Close();
        b.Report!(10,10);b.Pending.SetResult(b.Receipt());await work;
        Assert.False(vm.IsOpen);Assert.Null(vm.Receipt);Assert.Equal(0,vm.ProgressPercent);
    }

    [Fact]
    public async Task Monthly_scope_is_frozen_until_canonical_export_finishes()
    {
        var b=new Probe();
        var page=new MonthlyAttendancePageViewModel();
        page.AttachReader(b,()=>Actor);
        await page.RefreshAsync();
        await page.SelectSiteAsync("SITE-A");
        await page.Excel.OpenPreviewAsync();
        var work=page.Excel.ConfirmAsync();
        Assert.True(page.Excel.IsExporting);
        Assert.False(page.CanChangeMonthlyScope);
        Assert.False(page.RefreshCommand.CanExecute(null));
        await page.SelectSiteAsync("");
        page.SearchDraft="unapplied";
        Assert.Equal("SITE-A",page.SelectedSiteId);
        Assert.Equal("",page.SearchDraft);
        b.Report!(-1,10);b.Report(11,10);b.Report(0,0);
        Assert.True(page.Excel.IsProgressIndeterminate);
        b.Report(5,10);Assert.Equal(50,page.Excel.ProgressPercent);
        b.Pending.SetResult(b.Receipt());await work;
        Assert.True(page.CanChangeMonthlyScope);
        Assert.NotNull(page.Excel.Receipt);
    }

    private sealed class Probe:INativeMonthlyExcelExportBackend,INativeMonthlyExcelProgressBackend,INativeMonthlyAttendanceReader
    {
        public Task<NativeMonthlyCatalogue> ReadCatalogueAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult(new NativeMonthlyCatalogue([Filter.Period],[new NativeDashboardSite("SITE-A","الموقع")]));
        public Task<NativeMonthlySnapshot> ReadMonthAsync(NativeIdentity actor,NativeMonthlyFilter filter,CancellationToken ct=default)
            =>Task.FromResult(new NativeMonthlySnapshot(filter.Period,filter.SiteId,filter.Page,
                DateTime.DaysInMonth(DateTime.Now.Year,DateTime.Now.Month),1,false,
                [],[new NativeMonthlyRow("P-1","123","أحمد","الموقع",[],
                    new NativeMonthlyCell?[DateTime.DaysInMonth(DateTime.Now.Year,DateTime.Now.Month)])]));
        public Action<int,int>? Report;
        public CancellationToken Token;
        public int Exports;
        public TaskCompletionSource<NativeMonthlyExcelReceipt> Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NativeMonthlyExcelReceipt Receipt()=>new(Filter.Period,Filter.SiteId,Filter.SearchTerm,Path.Combine(Path.GetTempPath(),"altamam-test-reports","month.xlsx"),1,10)
        {Files=[new(Filter.SiteId,"الموقع",Path.Combine(Path.GetTempPath(),"altamam-test-reports","month.xlsx"),10)]};
        public Task<NativeMonthlyExcelPreview> PreviewExcelAsync(NativeIdentity actor,NativeMonthlyExcelFilter filter,CancellationToken ct=default)
            =>Task.FromResult(new NativeMonthlyExcelPreview(filter.Period,filter.SiteId,filter.SearchTerm,"الموقع","month.xlsx",10,DateTime.DaysInMonth(DateTime.Now.Year,DateTime.Now.Month)));
        public Task<NativeMonthlyExcelReceipt> ExportExcelAsync(NativeIdentity actor,NativeMonthlyExcelFilter filter,CancellationToken ct=default)
            =>throw new InvalidOperationException("Progress-capable backend must be used.");
        public Task<NativeMonthlyExcelReceipt> ExportWithProgressAsync(NativeIdentity actor,NativeMonthlyExcelFilter filter,Action<int,int> progress,CancellationToken ct=default)
        { Exports++;Report=progress;Token=ct;return Pending.Task; }
    }
}
