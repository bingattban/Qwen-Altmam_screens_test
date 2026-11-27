using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAttendanceDraftDeletePhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.Supervisor,"NONCE-A");
    private static NativeAttendanceDraft Draft()=>new("DRF-"+new string('a',32),
        DateOnly.FromDateTime(DateTime.Now).AddDays(-1),"SITE-A","STS-PRESENT",DateTimeOffset.UnixEpoch,["P-1"]);

    [Fact]
    public async Task Delete_requires_live_selection_and_explicit_confirmation()
    {
        var probe=new Probe();
        var vm=new NativeAttendanceDraftsViewModel();
        vm.Attach(probe,()=>Actor);
        await vm.OpenAsync();
        var row=Assert.Single(vm.Rows);
        vm.BeginDelete(row with {});
        await vm.ConfirmDeleteAsync();
        Assert.Equal(0,probe.Deletes);
        vm.BeginDelete(row);
        Assert.True(vm.IsDeleteReviewOpen);
        Assert.Equal(0,probe.Deletes);
        Assert.False(vm.RefreshCommand.CanExecute(null));
        Assert.False(vm.CloseCommand.CanExecute(null));
        await vm.ConfirmDeleteAsync();
        Assert.Equal(1,probe.Deletes);
        Assert.Equal(Actor,probe.DeletedBy);
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDeleteReviewOpen);
        Assert.NotEmpty(vm.DeleteNotice);
        await vm.ConfirmDeleteAsync();
        Assert.Equal(1,probe.Deletes);
    }

    [Fact]
    public async Task Cancel_never_calls_delete_and_preserves_catalogue()
    {
        var probe=new Probe();
        var vm=new NativeAttendanceDraftsViewModel();
        vm.Attach(probe,()=>Actor);
        await vm.OpenAsync();
        vm.BeginDelete(Assert.Single(vm.Rows));
        vm.CancelDelete();
        await vm.ConfirmDeleteAsync();
        Assert.Equal(0,probe.Deletes);
        Assert.Single(vm.Rows);
        Assert.False(vm.IsDeleteReviewOpen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ambiguous_or_stale_receipt_never_claims_success_or_retries(bool stillPresent)
    {
        var probe=new Probe{StillPresent=stillPresent,Fail=!stillPresent};
        var vm=new NativeAttendanceDraftsViewModel();
        vm.Attach(probe,()=>Actor);
        await vm.OpenAsync();
        vm.BeginDelete(Assert.Single(vm.Rows));
        await vm.ConfirmDeleteAsync();
        Assert.Equal(1,probe.Deletes);
        Assert.Empty(vm.DeleteNotice);
        Assert.NotEmpty(vm.Error);
        Assert.Empty(vm.Rows);
        await vm.ConfirmDeleteAsync();
        Assert.Equal(1,probe.Deletes);
    }

    [Fact]
    public async Task Logout_scrubs_delayed_delete_result()
    {
        NativeIdentity? live=Actor;
        var probe=new Probe{Pending=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var vm=new NativeAttendanceDraftsViewModel();
        vm.Attach(probe,()=>live);
        await vm.OpenAsync();
        vm.BeginDelete(Assert.Single(vm.Rows));
        var work=vm.ConfirmDeleteAsync();
        Assert.True(vm.IsDeleting);
        Assert.False(vm.CloseCommand.CanExecute(null));
        Assert.False(vm.CancelDeleteCommand.CanExecute(null));
        live=null;
        vm.Clear();
        probe.Pending.SetResult([]);
        await work;
        Assert.False(vm.IsOpen);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.DeleteNotice);
    }

    private sealed class Probe:INativeAttendanceDraftReader,INativeAttendanceDraftDeleter
    {
        private readonly NativeAttendanceDraft _draft=Draft();
        public int Deletes;
        public bool StillPresent,Fail;
        public NativeIdentity? DeletedBy;
        public TaskCompletionSource<IReadOnlyList<NativeAttendanceDraft>>? Pending;
        public Task<IReadOnlyList<NativeAttendanceDraft>> ReadAttendanceDraftsAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult<IReadOnlyList<NativeAttendanceDraft>>([_draft]);
        public Task<IReadOnlyList<NativeAttendanceDraft>> DeleteAttendanceDraftAsync(NativeIdentity actor,NativeAttendanceDraft expected,CancellationToken ct=default)
        {
            Deletes++;DeletedBy=actor;
            Assert.Equal(_draft.DraftId,expected.DraftId);
            Assert.Equal(_draft.UpdatedAtUtc,expected.UpdatedAtUtc);
            if(Fail)throw new InvalidOperationException("Stale draft.");
            return Pending?.Task??Task.FromResult<IReadOnlyList<NativeAttendanceDraft>>(StillPresent?[_draft]:[]);
        }
    }
}
