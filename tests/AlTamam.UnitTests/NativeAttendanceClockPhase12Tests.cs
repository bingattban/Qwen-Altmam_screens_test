using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAttendanceClockPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeSettingsOverview Data(NativeIdentity actor)=>new(actor.Username,actor.Role,@"E:\AlTamam",true,"16:00",true,3,null,"","light")
        {AttendanceSettings=new("08:00","20:00",null,null,false)};
    [Theory]
    [InlineData(NativeAttendanceClockKind.Due)]
    [InlineData(NativeAttendanceClockKind.Lock)]
    public async Task Clock_requires_exact_review_and_never_repeats_write(NativeAttendanceClockKind kind)
    {
        var backend=new Probe();var vm=new NativeAttendanceClockReview();var data=Data(Actor);
        Assert.True(vm.Open(backend,Actor,data,()=>Actor,()=>data,kind));
        vm.Time="24:00";vm.Preview();Assert.False(vm.CanConfirm);
        vm.Time="07:30";vm.Preview();Assert.True(vm.CanConfirm);
        vm.Time="07:31";Assert.False(vm.CanConfirm);
        vm.Preview();await vm.ConfirmAsync();Assert.NotNull(vm.Receipt);
        Assert.Equal(kind,backend.Last!.Kind);
        Assert.Equal(kind==NativeAttendanceClockKind.Due?"dueTimeLocal":"dailyLockTimeLocal",backend.Last!.PayloadKey);
        await vm.ConfirmAsync();Assert.Equal(1,backend.Writes);Assert.True(vm.Close());
    }
    [Fact]
    public async Task Clock_denies_supervisor_and_locks_on_unverified_receipt()
    {
        var backend=new Probe{Forged=true};var vm=new NativeAttendanceClockReview();
        var sv=Actor with{Role=UserRole.Supervisor};var denied=Data(sv);
        Assert.False(vm.Open(backend,sv,denied,()=>sv,()=>denied,NativeAttendanceClockKind.Due));
        var data=Data(Actor);Assert.True(vm.Open(backend,Actor,data,()=>Actor,()=>data,NativeAttendanceClockKind.Lock));
        vm.Time="19:00";vm.Preview();await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);Assert.False(vm.CanClose);Assert.Null(vm.Receipt);
        await vm.ConfirmAsync();Assert.Equal(1,backend.Writes);vm.ForceClear();
    }
    private sealed class Probe:INativeAttendanceClockBackend
    {
        public int Writes; public bool Forged; public NativeAttendanceClockIntent? Last;
        public Task<NativeAttendanceSettingsState> SaveAttendanceClockAsync(NativeIdentity actor,NativeAttendanceClockIntent intent,CancellationToken ct=default)
        {
            Writes++;Last=intent;
            return Task.FromResult(Forged?intent.Before:intent.Kind==NativeAttendanceClockKind.Due?
                intent.Before with{DueTime=intent.Time}:intent.Before with{LockTime=intent.Time});
        }
    }
}
