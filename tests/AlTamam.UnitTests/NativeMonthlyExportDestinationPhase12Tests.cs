using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeMonthlyExportDestinationPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","admin",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly NativeMonthlyExcelFilter Filter=new(DateTime.Now.ToString("yyyy-MM",System.Globalization.CultureInfo.InvariantCulture),"SITE-A","");
    private static string Folder=>Path.GetFullPath(Path.Combine(Path.GetTempPath(),"altamam-export-test"));
    private static string OtherFolder=>Path.GetFullPath(Path.Combine(Path.GetTempPath(),"altamam-export-other"));

    [Theory]
    [InlineData("../escape.xlsx")]
    [InlineData("CON.xlsx")]
    [InlineData("COM4.xlsx")]
    [InlineData("LPT9.xlsx")]
    [InlineData("report.xls")]
    [InlineData("a:b.xlsx")]
    public async Task Unsafe_filename_blocks_confirmation(string name)
    {
        var b=new Probe();
        var vm=new NativeMonthlyExcelExportViewModel();
        vm.Attach(b,()=>Actor,()=>Filter);
        await vm.OpenPreviewAsync();
        vm.OutputFileName=name;
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Null(b.Exported);
    }

    [Fact]
    public async Task Selected_destination_and_safe_name_are_frozen_for_confirm()
    {
        var b=new Probe();
        var vm=new NativeMonthlyExcelExportViewModel();
        vm.Attach(b,()=>Actor,()=>Filter);
        await vm.OpenPreviewAsync();
        Assert.Equal(Folder,vm.OutputDirectory);
        Assert.Equal("month.xlsx",vm.OutputFileName);
        Assert.True(vm.TrySelectOutputDirectory(OtherFolder));
        vm.OutputFileName="كشف نوفمبر.xlsx";
        Assert.True(vm.CanConfirm);
        var work=vm.ConfirmAsync();
        Assert.Equal(OtherFolder,b.Exported!.OutputDirectory);
        Assert.Equal("كشف نوفمبر.xlsx",b.Exported.OutputFileName);
        Assert.False(vm.TrySelectOutputDirectory(Folder));
        vm.OutputFileName="different.xlsx";
        Assert.Equal("كشف نوفمبر.xlsx",vm.OutputFileName);
        b.Pending.SetResult(b.Receipt());await work;
        Assert.NotNull(vm.Receipt);
        Assert.Equal(100,vm.ProgressPercent);
    }

    [Fact]
    public async Task Unavailable_default_requires_explicit_selection_and_receipt_path_must_match()
    {
        var b=new Probe{Directory=""};
        var vm=new NativeMonthlyExcelExportViewModel();
        vm.Attach(b,()=>Actor,()=>Filter);
        await vm.OpenPreviewAsync();
        Assert.False(vm.CanConfirm);
        Assert.False(vm.TrySelectOutputDirectory("relative"));
        Assert.False(vm.TrySelectOutputDirectory(@"\\server\share"));
        Assert.True(vm.TrySelectOutputDirectory(Folder));
        var work=vm.ConfirmAsync();
        b.Pending.SetResult(b.Receipt() with {PrimaryFilePath=Path.Combine(OtherFolder,"month.xlsx")});
        await work;
        Assert.Null(vm.Receipt);Assert.NotEmpty(vm.Error);Assert.False(vm.CanConfirm);
        Assert.NotEqual(100,vm.ProgressPercent);
    }

    [Fact]
    public async Task Replaced_session_cannot_edit_destination_or_export()
    {
        NativeIdentity? actor=Actor;var b=new Probe();
        var vm=new NativeMonthlyExcelExportViewModel();
        vm.Attach(b,()=>actor,()=>Filter);
        await vm.OpenPreviewAsync();actor=Actor with {SessionNonce="NEW"};
        Assert.False(vm.TrySelectOutputDirectory(OtherFolder));
        vm.OutputFileName="different.xlsx";Assert.Equal("month.xlsx",vm.OutputFileName);
        Assert.False(vm.CanConfirm);await vm.ConfirmAsync();Assert.Null(b.Exported);
    }

    private sealed class Probe:INativeMonthlyExcelExportBackend,INativeMonthlyExcelProgressBackend
    {
        public string Directory=Folder;
        public NativeMonthlyExcelFilter? Exported;
        public TaskCompletionSource<NativeMonthlyExcelReceipt> Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<NativeMonthlyExcelPreview> PreviewExcelAsync(NativeIdentity actor,NativeMonthlyExcelFilter filter,CancellationToken ct=default)
            =>Task.FromResult(new NativeMonthlyExcelPreview(filter.Period,filter.SiteId,filter.SearchTerm,"الموقع","month.xlsx",3,DateTime.DaysInMonth(DateTime.Now.Year,DateTime.Now.Month)){OutputDirectory=Directory});
        public Task<NativeMonthlyExcelReceipt> ExportExcelAsync(NativeIdentity actor,NativeMonthlyExcelFilter filter,CancellationToken ct=default)
            =>throw new InvalidOperationException();
        public Task<NativeMonthlyExcelReceipt> ExportWithProgressAsync(NativeIdentity actor,NativeMonthlyExcelFilter filter,Action<int,int> progress,CancellationToken ct=default)
        { Exported=filter;return Pending.Task; }
        public NativeMonthlyExcelReceipt Receipt()=>new(Filter.Period,Filter.SiteId,Filter.SearchTerm,Path.Combine(Exported!.OutputDirectory!,Exported.OutputFileName!),1,3);
    }
}
