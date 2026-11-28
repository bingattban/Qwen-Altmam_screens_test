using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeDefaultStatusPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeSettingsOverview Data(NativeIdentity actor)=>new(actor.Username,actor.Role,@"E:\AlTamam",true,"16:00",true,3,null,"","light")
    {
        AttendanceSettings=new("08:00","20:00",null,null,false),
        DefaultStatusChoices=new NativeDefaultStatusChoice[]{new("STS-PRESENT","موجود"),new("STS-ABSENT","غياب")}
    };
    [Fact]
    public async Task Only_canonical_eligible_choice_can_be_reviewed_and_saved_once()
    {
        var data=Data(Actor);var backend=new Probe();var vm=new NativeDefaultStatusReview();
        Assert.True(vm.Open(backend,Actor,data,()=>Actor,()=>data));
        vm.Selected=new("STS-LEAVE","إجازة");vm.Preview();Assert.False(vm.CanConfirm);
        vm.Selected=new("STS-PRESENT","موجود");vm.Preview();Assert.False(vm.CanConfirm);
        vm.Selected=vm.Choices[0];vm.Preview();Assert.True(vm.CanConfirm);
        vm.Selected=vm.Choices[1];Assert.False(vm.CanConfirm);
        vm.Preview();await vm.ConfirmAsync();Assert.NotNull(vm.Receipt);
        Assert.Equal("STS-ABSENT",vm.Receipt!.DefaultStatusId);
        await vm.ConfirmAsync();Assert.Equal(1,backend.Writes);Assert.True(vm.Close());
    }
    [Fact]
    public async Task Replaced_session_and_unverified_receipt_cannot_repeat_write()
    {
        var data=Data(Actor);var backend=new Probe{Forged=true};var vm=new NativeDefaultStatusReview();
        NativeIdentity? live=Actor;
        Assert.True(vm.Open(backend,Actor,data,()=>live,()=>data));
        vm.Selected=vm.Choices[0];vm.Preview();live=Actor with{SessionNonce="NONCE-B"};
        await vm.ConfirmAsync();Assert.Equal(0,backend.Writes);Assert.Empty(vm.Choices);vm.ForceClear();
        live=Actor;Assert.True(vm.Open(backend,Actor,data,()=>live,()=>data));
        vm.Selected=vm.Choices[0];vm.Preview();await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);Assert.False(vm.CanClose);Assert.Null(vm.Receipt);
        await vm.ConfirmAsync();Assert.Equal(1,backend.Writes);vm.ForceClear();
    }
    [Fact]
    public void Snapshot_rejects_supervisor_bounded_and_duplicate_choices()
    {
        var data=Data(Actor);Assert.True(data.Matches(Actor));
        Assert.False((data with{DefaultStatusChoices=new NativeDefaultStatusChoice[]{new("STS-PERMISSION","إذن")}}).Matches(Actor));
        Assert.False((data with{DefaultStatusChoices=new[]{data.DefaultStatusChoices[0],data.DefaultStatusChoices[0]}}).Matches(Actor));
        var sv=Actor with{Role=UserRole.Supervisor};
        Assert.False(Data(sv).Matches(sv));
    }
    private sealed class Probe:INativeDefaultStatusBackend
    {
        public int Writes;public bool Forged;
        public Task<NativeAttendanceSettingsState> SaveDefaultStatusAsync(NativeIdentity actor,NativeDefaultStatusIntent intent,CancellationToken ct=default)
        {
            Writes++;
            return Task.FromResult(Forged?intent.Before:intent.Before with
                {DefaultStatusId=intent.Choice.Id,DefaultStatusName=intent.Choice.Name,Configured=true});
        }
    }
}
