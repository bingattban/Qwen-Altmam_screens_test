using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeMonthlyExportSchedulePhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly string Folder=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"altamam-export-test"));
    private static NativeSettingsOverview Data(NativeIdentity actor)=>new(actor.Username,actor.Role,Folder,true,"16:00",true,3,null,"","light")
        {MonthlyExport=new(false,"21:00","","","")};
    [Fact]
    public async Task Explicit_review_required_and_edit_invalidates_prepared_intent()
    {
        var backend=new Probe();var vm=new NativeMonthlyExportScheduleReview();var snapshot=Data(Actor);
        Assert.True(vm.Open(backend,Actor,snapshot,()=>Actor,()=>snapshot));
        vm.Enabled=true;vm.Directory=Folder;vm.Preview();Assert.True(vm.CanConfirm);
        vm.Time="24:00";Assert.False(vm.CanConfirm);vm.Preview();Assert.False(vm.CanConfirm);
        vm.Time="22:00";vm.Preview();Assert.True(vm.CanConfirm);
        await vm.ConfirmAsync();Assert.Equal(1,backend.Writes);Assert.NotNull(vm.Receipt);
        await vm.ConfirmAsync();Assert.Equal(1,backend.Writes);Assert.True(vm.Close());
    }
    [Fact]
    public async Task Supervisor_and_replaced_session_cannot_write_or_read_receipt()
    {
        var backend=new Probe();var vm=new NativeMonthlyExportScheduleReview();var supervisor=Actor with{Role=UserRole.Supervisor};
        var denied=Data(supervisor);
        Assert.False(vm.Open(backend,supervisor,denied,()=>supervisor,()=>denied));
        NativeIdentity? live=Actor;var snapshot=Data(Actor);
        Assert.True(vm.Open(backend,Actor,snapshot,()=>live,()=>snapshot));
        vm.Enabled=true;vm.Directory=Folder;vm.Preview();Assert.True(vm.CanConfirm);
        live=Actor with{SessionNonce="NONCE-B"};await vm.ConfirmAsync();
        Assert.Equal(0,backend.Writes);Assert.Null(vm.Prepared);Assert.Null(vm.Receipt);
        vm.ForceClear();
    }
    [Fact]
    public async Task Unverified_receipt_blocks_repeat_and_success_message()
    {
        var backend=new Probe{Forged=true};var vm=new NativeMonthlyExportScheduleReview();var snapshot=Data(Actor);
        Assert.True(vm.Open(backend,Actor,snapshot,()=>Actor,()=>snapshot));
        vm.Enabled=true;vm.Directory=Folder;vm.Preview();await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);Assert.Empty(vm.CompletionMessage);Assert.False(vm.CanClose);
        await vm.ConfirmAsync();Assert.Equal(1,backend.Writes);vm.ForceClear();
    }
    private sealed class Probe:INativeMonthlyExportScheduleBackend
    {
        public int Writes;public bool Forged;
        public Task<NativeAutomaticMonthlyExcelState> SaveMonthlyExportScheduleAsync(NativeIdentity actor,
            NativeMonthlyExportScheduleIntent intent,CancellationToken ct=default)
        {
            Writes++;
            return Task.FromResult(new NativeAutomaticMonthlyExcelState(intent.Enabled,Forged?"00:00":intent.Time,intent.Directory,"",""));
        }
    }
}
