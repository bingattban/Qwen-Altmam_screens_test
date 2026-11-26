using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeBackupSchedulePhase12Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeSettingsOverview Snapshot(NativeIdentity actor,bool available=true)=>
        new(actor.Username,actor.Role,@"E:\AlTamam",available,"16:00",true,0,null,"","light");
    private static NativeBackupScheduleIntent Intent(NativeSettingsOverview snapshot,bool enabled=true,string time="17:00",int retention=3)=>
        new(snapshot,enabled,time,retention,Guid.NewGuid().ToString("N"));

    [Fact]
    public void Schedule_validates_role_time_retention_and_available_directory()
    {
        var intent=Intent(Snapshot(Admin));
        Assert.True(intent.IsValid(Admin));
        var ordinary=Admin with { Role=UserRole.OrdinaryAdministration };
        Assert.True(Intent(Snapshot(ordinary)).IsValid(ordinary));
        var supervisor=Admin with { Role=UserRole.Supervisor };
        Assert.False(Intent(Snapshot(supervisor)).IsValid(supervisor));
        Assert.False((intent with { Time="24:00" }).IsValid(Admin));
        Assert.False((intent with { RetentionCount=1 }).IsValid(Admin));
        Assert.False((intent with { RetentionCount=366 }).IsValid(Admin));
        Assert.True((intent with { RetentionCount=0 }).IsValid(Admin));
        Assert.False(Intent(Snapshot(Admin,false)).IsValid(Admin));
        Assert.True(Intent(Snapshot(Admin,false),false).IsValid(Admin));
        Assert.True(new NativeBackupScheduleReceipt(true,"17:00",3).Matches(intent));
        Assert.False(new NativeBackupScheduleReceipt(true,"16:00",3).Matches(intent));
    }

    [Fact]
    public async Task Schedule_requires_exact_snapshot_review_and_explicit_confirmation()
    {
        var reader=new Backend();
        var snapshot=Snapshot(Admin);
        var vm=new NativeBackupScheduleReview();
        Assert.False(vm.Open(reader,Admin,snapshot with { },()=>Admin,()=>snapshot));
        Assert.True(vm.Open(reader,Admin,snapshot,()=>Admin,()=>snapshot));
        await vm.ConfirmAsync();
        Assert.Empty(reader.Intents);
        vm.Time="17:00"; vm.RetentionText="3"; vm.Preview();
        Assert.NotNull(vm.Prepared);
        vm.RetentionText="4";
        Assert.Null(vm.Prepared);
        vm.Preview(); await vm.ConfirmAsync();
        Assert.Single(reader.Intents);
        Assert.NotNull(vm.Receipt);
        Assert.Equal(4,vm.Receipt!.RetentionCount);
        Assert.True(vm.Close());
    }

    [Fact]
    public async Task Schedule_uncertain_retry_preserves_intent_and_rejection_does_not_unlock()
    {
        var reader=new Backend { Fail=true };
        var snapshot=Snapshot(Admin);
        var vm=new NativeBackupScheduleReview();
        Assert.True(vm.Open(reader,Admin,snapshot,()=>Admin,()=>snapshot));
        vm.Time="17:00"; vm.Preview(); await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.Close());
        var first=Assert.Single(reader.Intents);
        vm.Time="18:00"; vm.Preview();
        reader.Fail=false; reader.Rejected=true; await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.Close());
        reader.Rejected=false; await vm.ConfirmAsync();
        Assert.Equal(first,reader.Intents[1]);
        Assert.Equal(first,reader.Intents[2]);
        Assert.NotNull(vm.Receipt);
        Assert.True(vm.Close());
    }

    [Fact]
    public async Task Schedule_first_rejection_requires_reload_and_forged_receipt_is_uncertain()
    {
        var reader=new Backend { Rejected=true };
        var snapshot=Snapshot(Admin);
        var vm=new NativeBackupScheduleReview();
        Assert.True(vm.Open(reader,Admin,snapshot,()=>Admin,()=>snapshot));
        vm.Time="17:00"; vm.Preview(); await vm.ConfirmAsync();
        Assert.True(vm.NeedsReload);
        Assert.True(vm.Close());
        reader.Rejected=false; reader.Forged=true;
        Assert.True(vm.Open(reader,Admin,snapshot,()=>Admin,()=>snapshot));
        vm.Time="17:00"; vm.Preview(); await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.Null(vm.Receipt);
    }

    [Fact]
    public async Task Schedule_late_receipt_cannot_repaint_after_session_teardown()
    {
        var reader=new Backend();
        var snapshot=Snapshot(Admin);
        NativeIdentity? actor=Admin;
        var vm=new NativeBackupScheduleReview();
        Assert.True(vm.Open(reader,Admin,snapshot,()=>actor,()=>snapshot));
        vm.Time="17:00"; vm.Preview();
        var pending=new TaskCompletionSource<NativeBackupScheduleReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending=pending.Task;
        var task=vm.ConfirmAsync();
        actor=null; vm.ForceClear();
        pending.SetResult(new(true,"17:00",0));
        await task;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Receipt);
        Assert.Null(vm.Prepared);
    }

    private sealed class Backend:INativeBackupScheduleBackend
    {
        public List<NativeBackupScheduleIntent> Intents=[];
        public bool Fail,Rejected,Forged;
        public Task<NativeBackupScheduleReceipt>? Pending;
        public Task<NativeBackupScheduleReceipt> SaveBackupScheduleAsync(NativeIdentity actor,
            NativeBackupScheduleIntent intent,CancellationToken ct=default)
        {
            Intents.Add(intent);
            if(Fail) throw new IOException("response lost");
            if(Rejected) throw new NativeBackupScheduleRejectedException();
            return Pending??Task.FromResult(new NativeBackupScheduleReceipt(intent.Enabled,
                Forged?"09:00":intent.Time,intent.RetentionCount));
        }
    }
}
