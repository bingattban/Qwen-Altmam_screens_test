using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeProgramDiagnosticsPhase12Tests
{
    private static readonly NativeIdentity Actor=new("U1","owner",UserRole.GeneralAdministration,"N1");
    private static NativeProgramInformation Info()=>new("0.0.307-ci","X86","10.0.0",true,12);
    [Theory]
    [InlineData(UserRole.GeneralAdministration)]
    [InlineData(UserRole.OrdinaryAdministration)]
    [InlineData(UserRole.Supervisor)]
    public async Task Authorized_roles_read_info_but_integrity_requires_explicit_action(UserRole role)
    {
        var actor=Actor with { Role=role };
        var backend=new Probe();
        var vm=new NativeProgramDiagnosticsViewModel();
        vm.Attach(backend,()=>actor,()=>true);
        await vm.RefreshAsync();
        Assert.Equal(Info(),vm.Information);
        Assert.Equal(0,backend.Checks);
        Assert.Null(vm.Integrity);
        await vm.CheckAsync();
        Assert.Equal(NativeIntegrityStatus.Passed,vm.Integrity);
        Assert.Equal(1,backend.Checks);
    }
    [Fact]
    public async Task Failed_integrity_and_unavailable_service_are_distinct()
    {
        var backend=new Probe { Result=NativeIntegrityStatus.Failed };
        var vm=new NativeProgramDiagnosticsViewModel();
        vm.Attach(backend,()=>Actor,()=>true);
        await vm.RefreshAsync();
        await vm.CheckAsync();
        Assert.Equal(NativeIntegrityStatus.Failed,vm.Integrity);
        backend.Throw=true;
        await vm.CheckAsync();
        Assert.Null(vm.Integrity);
        Assert.NotEmpty(vm.Error);
    }
    [Fact]
    public async Task Revoked_or_replaced_session_cannot_receive_late_result()
    {
        NativeIdentity? current=Actor;
        var backend=new Probe();
        var vm=new NativeProgramDiagnosticsViewModel();
        vm.Attach(backend,()=>current,()=>true);
        await vm.RefreshAsync();
        var pending=new TaskCompletionSource<NativeIntegrityStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var check=vm.CheckAsync();
        current=Actor with { SessionNonce="N2" };
        vm.Clear();
        pending.SetResult(NativeIntegrityStatus.Passed);
        await check;
        Assert.Null(vm.Integrity);
        Assert.Null(vm.Information);
        Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task Invalid_metadata_and_blocked_review_do_not_allow_check()
    {
        bool allowed=true;
        var backend=new Probe { Information=Info() with { Version="unknown" } };
        var vm=new NativeProgramDiagnosticsViewModel();
        vm.Attach(backend,()=>Actor,()=>allowed);
        await vm.RefreshAsync();
        Assert.Null(vm.Information);
        Assert.NotEmpty(vm.Error);
        backend.Information=Info();
        await vm.RefreshAsync();
        allowed=false;
        await vm.CheckAsync();
        Assert.Equal(0,backend.Checks);
        Assert.Null(vm.Integrity);
    }
    [Fact]
    public async Task Diagnostics_busy_blocks_settings_reviews_and_clear_releases_guard()
    {
        var settings=new SettingsPageViewModel();
        var backend=new Probe();
        settings.Diagnostics.Attach(backend,()=>Actor,()=>!settings.IsReviewOpen);
        await settings.Diagnostics.RefreshAsync();
        var pending=new TaskCompletionSource<NativeIntegrityStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var check=settings.Diagnostics.CheckAsync();
        Assert.True(settings.IsReviewOpen);
        Assert.False(settings.Diagnostics.CanRun);
        await settings.Diagnostics.CheckAsync();
        Assert.Equal(1,backend.Checks);
        settings.Clear();
        pending.SetResult(NativeIntegrityStatus.Passed);
        await check;
        Assert.False(settings.IsReviewOpen);
        Assert.Null(settings.Diagnostics.Information);
        Assert.Null(settings.Diagnostics.Integrity);
    }

    private sealed class Probe:INativeProgramDiagnosticsBackend
    {
        public NativeProgramInformation Information=Info();
        public NativeIntegrityStatus Result=NativeIntegrityStatus.Passed;
        public bool Throw;
        public int Checks;
        public Task<NativeIntegrityStatus>? Pending;
        public Task<NativeProgramInformation> ReadProgramInformationAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult(Information);
        public Task<NativeIntegrityStatus> CheckDatabaseIntegrityAsync(NativeIdentity actor,CancellationToken ct=default)
        {
            Checks++;
            if(Throw) throw new InvalidOperationException();
            return Pending??Task.FromResult(Result);
        }
    }
}
