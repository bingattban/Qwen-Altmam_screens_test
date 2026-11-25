using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeIndividualExcelViewModelTests
{
    private static readonly NativeIdentity Manager = new("USER-1","admin",UserRole.GeneralAdministration,"NONCE");
    private static NativePersonnelReportSnapshot Report() => new("PER-001","اسم","0001","2026-01-01",true,
        12,3,2,100,1,false,1,false,1,false,[],[],[],[])
        { SiteTotal=1, RevisionTotal=2, AuditTotal=4, DateFrom="2026-01-01", StatusId="STS-1" };

    [Fact]
    public async Task Explicit_confirmation_uses_current_report_and_one_time_token()
    {
        var report=Report();
        var backend=new Fake();
        var vm=new NativeIndividualExcelViewModel();
        vm.Attach(backend,()=>Manager,()=>report);
        Assert.True(vm.OpenPreviewCommand.CanExecute(null));
        await vm.OpenPreviewAsync();
        Assert.True(vm.CanConfirm);
        Assert.Equal(0,backend.Exports);
        Assert.Equal("STS-1",backend.Request!.StatusId);
        await vm.ConfirmAsync();
        Assert.NotNull(vm.Receipt);
        Assert.Equal(1,backend.Exports);
        Assert.Equal(new string('b',64),backend.Token);
        Assert.False(vm.CanConfirm);
        await vm.ConfirmAsync();
        Assert.Equal(1,backend.Exports);
    }

    [Fact]
    public async Task Changed_report_or_session_cancels_approval_and_clears_sensitive_data()
    {
        NativePersonnelReportSnapshot? report=Report();
        NativeIdentity? actor=Manager;
        var backend=new Fake();
        var vm=new NativeIndividualExcelViewModel();
        vm.Attach(backend,()=>actor,()=>report);
        await vm.OpenPreviewAsync();
        report=report with { StatusId="STS-2" };
        vm.NotifySourceChanged();
        Assert.Null(vm.Preview);
        Assert.False(vm.IsOpen);
        Assert.True(backend.Invalidations>0);
        await vm.OpenPreviewAsync();
        actor=null;
        await vm.ConfirmAsync();
        Assert.Null(vm.Preview);
        Assert.Equal(0,backend.Exports);
    }

    [Fact]
    public async Task Forged_preview_totals_or_receipt_path_are_rejected()
    {
        var report=Report();
        var backend=new Fake { ForgePreview=true };
        var vm=new NativeIndividualExcelViewModel();
        vm.Attach(backend,()=>Manager,()=>report);
        await vm.OpenPreviewAsync();
        Assert.Null(vm.Preview);
        Assert.False(vm.CanConfirm);
        vm.Close();
        backend.ForgePreview=false;
        backend.ForgeReceipt=true;
        await vm.OpenPreviewAsync();
        await vm.ConfirmAsync();
        Assert.Null(vm.Receipt);
        Assert.False(vm.CanConfirm);
        Assert.NotEmpty(vm.Error);
    }

    [Fact]
    public async Task Late_preview_after_close_is_discarded()
    {
        var report=Report();
        var backend=new Fake { Delay=new TaskCompletionSource<NativeIndividualExcelPreview>() };
        var vm=new NativeIndividualExcelViewModel();
        vm.Attach(backend,()=>Manager,()=>report);
        var pending=vm.OpenPreviewAsync();
        vm.Close();
        backend.Delay.SetResult(backend.BuildPreview(backend.Request!));
        await pending;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public async Task Failed_confirmation_is_not_retried()
    {
        var report=Report();
        var backend=new Fake { Fail=true };
        var vm=new NativeIndividualExcelViewModel();
        vm.Attach(backend,()=>Manager,()=>report);
        await vm.OpenPreviewAsync();
        await vm.ConfirmAsync();
        await vm.ConfirmAsync();
        Assert.Equal(1,backend.Exports);
        Assert.Null(vm.Preview);
        Assert.Null(vm.Receipt);
    }

    [Fact]
    public async Task Pre_cancelled_preview_does_not_call_backend_or_open_panel()
    {
        var report=Report();
        var backend=new Fake();
        var vm=new NativeIndividualExcelViewModel();
        vm.Attach(backend,()=>Manager,()=>report);
        using var cancellation=new CancellationTokenSource();
        cancellation.Cancel();
        await vm.OpenPreviewAsync(cancellation.Token);
        Assert.Null(backend.Request);
        Assert.False(vm.IsOpen);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Pre_cancelled_confirmation_invalidates_approval_without_exporting()
    {
        var report=Report();
        var backend=new Fake();
        var vm=new NativeIndividualExcelViewModel();
        vm.Attach(backend,()=>Manager,()=>report);
        await vm.OpenPreviewAsync();
        Assert.True(vm.CanConfirm);
        using var cancellation=new CancellationTokenSource();
        cancellation.Cancel();
        await vm.ConfirmAsync(cancellation.Token);
        Assert.Equal(0,backend.Exports);
        Assert.Null(vm.Preview);
        Assert.Null(vm.Receipt);
        Assert.False(vm.CanConfirm);
        Assert.False(vm.IsBusy);
        Assert.True(backend.Invalidations>0);
        Assert.NotEmpty(vm.Error);
    }

    [Fact]
    public async Task Late_confirmation_cannot_replace_new_report_preview()
    {
        var report=Report();
        var backend=new Fake { ExportDelay=new TaskCompletionSource<NativeIndividualExcelReceipt>() };
        var vm=new NativeIndividualExcelViewModel();
        vm.Attach(backend,()=>Manager,()=>report);
        await vm.OpenPreviewAsync();
        var oldRequest=backend.Request!;
        var pending=vm.ConfirmAsync();
        vm.Close();
        report=report with { StatusId="STS-2" };
        await vm.OpenPreviewAsync();
        var current=vm.Preview;
        Assert.NotNull(current);
        backend.ExportDelay.SetResult(backend.BuildReceipt(oldRequest));
        await pending;
        Assert.Same(current,vm.Preview);
        Assert.Null(vm.Receipt);
        Assert.True(vm.CanConfirm);
        Assert.False(vm.IsBusy);
    }

    private sealed class Fake : INativeIndividualExcelBackend
    {
        public int Exports,Invalidations;
        public bool ForgePreview,ForgeReceipt,Fail;
        public string? Token;
        public NativeIndividualExcelRequest? Request;
        public TaskCompletionSource<NativeIndividualExcelPreview>? Delay;
        public TaskCompletionSource<NativeIndividualExcelReceipt>? ExportDelay;
        private readonly string _directory=Path.GetFullPath(Path.GetTempPath());
        public NativeIndividualExcelPreview BuildPreview(NativeIndividualExcelRequest request) =>
            new(request,"اسم","0001",_directory,ForgePreview?13:12,2,3,2,4,1,
                new string('a',64),new string('b',64));
        public Task<NativeIndividualExcelPreview> PreviewIndividualExcelAsync(NativeIdentity actor,
            NativeIndividualExcelRequest request,CancellationToken ct=default)
        {
            Request=request;
            return Delay?.Task??Task.FromResult(BuildPreview(request));
        }
        public Task<NativeIndividualExcelReceipt> ExportIndividualExcelAsync(NativeIdentity actor,
            NativeIndividualExcelRequest request,string approvalToken,CancellationToken ct=default)
        {
            Exports++; Token=approvalToken;
            if(Fail) throw new InvalidOperationException();
            return ExportDelay?.Task??Task.FromResult(BuildReceipt(request));
        }
        public NativeIndividualExcelReceipt BuildReceipt(NativeIndividualExcelRequest request) =>
            new(request,ForgeReceipt?"relative.xlsx":Path.Combine(_directory,"report.xlsx"),
                new string('a',64),9);
        public void InvalidateIndividualExcelApproval()=>Invalidations++;
    }
}
