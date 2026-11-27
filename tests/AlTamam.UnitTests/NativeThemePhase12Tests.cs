using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeThemePhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeSettingsOverview Data(NativeIdentity actor,string theme="light")=>new(actor.Username,actor.Role,
        @"E:\AlTamam",true,"16:00",true,3,"2026-11-25","",theme);
    [Theory]
    [InlineData(UserRole.GeneralAdministration)]
    [InlineData(UserRole.OrdinaryAdministration)]
    [InlineData(UserRole.Supervisor)]
    public async Task Save_uses_canonical_receipt_for_each_authorized_role(UserRole role)
    {
        var actor=Actor with {Role=role};
        var backend=new Probe();
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(backend,()=>actor);
        vm.AttachTheme(backend,()=>true);
        await vm.RefreshAsync();
        await vm.SaveThemeAsync("dark");
        Assert.Equal(1,backend.Saves);
        Assert.Equal("dark",vm.Snapshot!.Theme);
        Assert.False(vm.IsSavingTheme);
        Assert.Equal("light",await SaveLight());
        async Task<string> SaveLight()
        {
            await vm.SaveThemeAsync("light");
            return vm.Snapshot!.Theme;
        }
    }

    [Fact]
    public async Task Invalid_same_or_disallowed_theme_is_never_sent()
    {
        var backend=new Probe();
        var vm=new NativeSettingsOverviewViewModel();
        bool allowed=true;
        vm.Attach(backend,()=>Actor);
        vm.AttachTheme(backend,()=>allowed);
        await vm.SaveThemeAsync("dark");
        await vm.RefreshAsync();
        await vm.SaveThemeAsync("automatic");
        await vm.SaveThemeAsync("light");
        allowed=false;
        await vm.SaveThemeAsync("dark");
        Assert.Equal(0,backend.Saves);
    }

    [Fact]
    public async Task Invalid_receipt_clears_uncertain_snapshot()
    {
        var backend=new Probe {Forged=true};
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(backend,()=>Actor);
        vm.AttachTheme(backend,()=>true);
        await vm.RefreshAsync();
        await vm.SaveThemeAsync("dark");
        Assert.Null(vm.Snapshot);
        Assert.NotEmpty(vm.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Late_save_never_repaints_new_session_or_cleared_view(bool changeSession)
    {
        NativeIdentity? actor=Actor;
        var backend=new Probe {Pending=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var vm=new NativeSettingsOverviewViewModel();
        vm.Attach(backend,()=>actor);
        vm.AttachTheme(backend,()=>true);
        await vm.RefreshAsync();
        var task=vm.SaveThemeAsync("dark");
        Assert.True(vm.IsSavingTheme);
        await vm.SaveThemeAsync("dark");
        Assert.Equal(1,backend.Saves);
        if(changeSession) actor=Actor with {SessionNonce="NONCE-B"}; else vm.Clear();
        backend.Pending.SetResult(Data(Actor,"dark"));
        await task;
        Assert.Null(vm.Snapshot);
        Assert.False(vm.IsSavingTheme);
    }

    private sealed class Probe:INativeSettingsOverviewReader,INativeThemeBackend
    {
        public int Saves;
        public bool Forged;
        public TaskCompletionSource<NativeSettingsOverview>? Pending;
        public Task<NativeSettingsOverview> ReadSettingsOverviewAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult(Data(actor));
        public Task<NativeSettingsOverview> SaveThemeAsync(NativeIdentity actor,string theme,CancellationToken ct=default)
        {
            Saves++;
            return Pending?.Task??Task.FromResult(Data(actor,Forged?"light":theme));
        }
    }
}
