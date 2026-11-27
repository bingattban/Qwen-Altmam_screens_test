using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAttendanceDraftPhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeAttendanceDraft Draft()=>new("DRF-"+new string('a',32),new DateOnly(2026,10,23),"SITE-1","STS-PRESENT",
        DateTimeOffset.Parse("2026-10-23T10:00:00Z"),["P-1","P-2"]);
    [Fact]
    public void Catalogue_validates_identity_bounds_and_unique_selection()
    {
        var draft=Draft();
        Assert.True(NativeAttendanceDraft.ValidCatalogue([draft]));
        Assert.False(NativeAttendanceDraft.ValidCatalogue([draft,draft]));
        Assert.False(NativeAttendanceDraft.ValidCatalogue([draft with {DraftId="foreign"}]));
        Assert.False(NativeAttendanceDraft.ValidCatalogue([draft with {PersonIds=["P-1","P-1"]}]));
        Assert.False(NativeAttendanceDraft.ValidCatalogue([draft with {PersonIds=[]}]));
        Assert.False(NativeAttendanceDraft.ValidCatalogue([draft with {PersonIds=Enumerable.Range(0,101).Select(i=>"P-"+i).ToArray()}]));
        Assert.False(NativeAttendanceDraft.ValidCatalogue([draft with {TargetStatusId=""}]));
    }
    [Theory]
    [InlineData(UserRole.GeneralAdministration)]
    [InlineData(UserRole.OrdinaryAdministration)]
    [InlineData(UserRole.Supervisor)]
    public async Task All_standard_roles_read_only_their_own_draft_catalogue(UserRole role)
    {
        NativeIdentity? live=null;
        var reader=new Probe();
        var vm=new NativeAttendanceDraftsViewModel();
        vm.Attach(reader,()=>live);
        await vm.OpenAsync();
        Assert.Equal(0,reader.Reads);
        live=Actor with {Role=role};
        await vm.OpenAsync();
        Assert.Equal(live,reader.LastActor);
        Assert.Single(vm.Rows);
        Assert.Equal(2,vm.Rows[0].PersonCount);
        vm.Clear();
        Assert.Empty(vm.Rows);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Closed_or_replaced_session_cannot_receive_late_draft_rows(bool replaced)
    {
        NativeIdentity? live=Actor;
        var reader=new Probe{Pending=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var vm=new NativeAttendanceDraftsViewModel();
        vm.Attach(reader,()=>live);
        var work=vm.OpenAsync();
        Assert.True(vm.IsBusy);
        if(replaced)live=Actor with {SessionNonce="NONCE-B"};else vm.Clear();
        reader.Pending.SetResult([Draft()]);
        await work;
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsOpen);
        Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task Malformed_catalogue_is_hidden_and_refresh_recovers()
    {
        var reader=new Probe{Malformed=true};
        var vm=new NativeAttendanceDraftsViewModel();
        vm.Attach(reader,()=>Actor);
        await vm.OpenAsync();
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.Error);
        reader.Malformed=false;
        await vm.RefreshAsync();
        Assert.Single(vm.Rows);
        Assert.Empty(vm.Error);
    }
    private sealed class Probe:INativeAttendanceDraftReader
    {
        public int Reads;
        public NativeIdentity? LastActor;
        public bool Malformed;
        public TaskCompletionSource<IReadOnlyList<NativeAttendanceDraft>>? Pending;
        public Task<IReadOnlyList<NativeAttendanceDraft>> ReadAttendanceDraftsAsync(NativeIdentity actor,CancellationToken ct=default)
        {
            Reads++;LastActor=actor;
            return Pending?.Task??Task.FromResult<IReadOnlyList<NativeAttendanceDraft>>(Malformed?[Draft(),Draft()]:[Draft()]);
        }
    }
}
