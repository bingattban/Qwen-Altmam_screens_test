using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeSettingsOverviewPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeSettingsOverview Data(NativeIdentity actor)=>new(actor.Username,actor.Role,
        @"E:\AlTamam",true,"16:00",true,3,"2026-11-25","","light");
    [Fact]
    public void Overview_validates_identity_schedule_and_retention_without_claiming_scheduler_running()
    {
        var data=Data(Actor);
        Assert.True(data.Matches(Actor));
        Assert.False((data with { Username="other" }).Matches(Actor));
        Assert.False((data with { Role=UserRole.Supervisor }).Matches(Actor));
        Assert.False((data with { BackupTime="25:00" }).Matches(Actor));
        Assert.False((data with { RetentionCount=1 }).Matches(Actor));
        Assert.False((data with { LastBackupDate="2026-02-30" }).Matches(Actor));
        Assert.False((data with { Theme="unknown" }).Matches(Actor));
        Assert.True((data with { DirectoryAvailable=false,LastBackupDate=null }).Matches(Actor));
    }

    [Theory]
    [InlineData(UserRole.GeneralAdministration)]
    [InlineData(UserRole.OrdinaryAdministration)]
    [InlineData(UserRole.Supervisor)]
    public async Task Overview_reads_all_authorized_roles_and_clears_on_exit(UserRole role)
    {
        var actor=Actor with { Role=role };
        var reader=new Reader();
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(reader,()=>actor);
        await vm.RefreshAsync();
        Assert.Equal(1,reader.Reads);
        Assert.Equal(Data(actor),vm.Snapshot);
        vm.Clear();
        Assert.Null(vm.Snapshot);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Overview_denies_missing_session_and_rejects_wrong_identity()
    {
        NativeIdentity? actor=null;
        var reader=new Reader();
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(reader,()=>actor);
        await vm.RefreshAsync();
        Assert.Equal(0,reader.Reads);
        actor=Actor; reader.Forged=true;
        await vm.RefreshAsync();
        Assert.Null(vm.Snapshot);
        Assert.NotEmpty(vm.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Overview_discards_late_result_after_navigation_or_session_change(bool changeSession)
    {
        NativeIdentity? actor=Actor;
        var reader=new Reader();
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(reader,()=>actor);
        var pending=new TaskCompletionSource<NativeSettingsOverview>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending=pending.Task;
        var task=vm.RefreshAsync();
        Assert.True(vm.IsLoading);
        if(changeSession) actor=Actor with { SessionNonce="NONCE-B" }; else vm.Clear();
        pending.SetResult(Data(Actor));
        await task;
        Assert.Null(vm.Snapshot);
        Assert.False(vm.IsLoading);
    }

    private sealed class Reader:INativeSettingsOverviewReader
    {
        public int Reads;
        public bool Forged;
        public Task<NativeSettingsOverview>? Pending;
        public Task<NativeSettingsOverview> ReadSettingsOverviewAsync(NativeIdentity actor,CancellationToken ct=default)
        {
            Reads++;
            return Pending??Task.FromResult(Forged?Data(actor) with { Username="other" }:Data(actor));
        }
    }
}
