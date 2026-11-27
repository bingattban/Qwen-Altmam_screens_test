using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAttendanceDraftRestorePhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.Supervisor,"NONCE-A");
    private static DateOnly Today=>DateOnly.FromDateTime(DateTime.Now);
    private static NativeAttendanceDraft Draft()=>new("DRF-"+new string('a',32),Today,"SITE-A",
        "STS-PRESENT",DateTimeOffset.UnixEpoch,["P-1","P-2"]);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restore_uses_explicit_mode_and_fresh_preview_never_commits(bool initial)
    {
        var backend=new Probe{Initial=initial};
        var vm=new DailyAttendancePageViewModel();
        vm.AttachReader(backend,()=>Actor);
        await vm.Drafts.OpenAsync();
        var row=Assert.Single(vm.Drafts.Rows);
        Assert.Equal(0,backend.Previews);
        await vm.Drafts.RestoreAsync(row,initial);
        Assert.Equal(2,backend.Reads);
        Assert.Equal(1,backend.Previews);
        Assert.Equal(0,backend.Commits);
        Assert.False(vm.Drafts.IsOpen);
        Assert.True(vm.Review.IsOpen);
        Assert.True(vm.Review.HasPreview);
        Assert.Equal(initial,vm.Review.IsInitial);
        Assert.Equal("STS-PRESENT",vm.Review.SelectedStatusId);
        Assert.Equal(new[]{"P-1","P-2"},vm.Review.PreviewPersons.Select(p=>p.PersonId));
        Assert.False(vm.Review.IsCompleted);
    }

    [Fact]
    public async Task Forged_or_changed_draft_cannot_open_review()
    {
        var backend=new Probe();
        var vm=new DailyAttendancePageViewModel();
        vm.AttachReader(backend,()=>Actor);
        await vm.Drafts.OpenAsync();
        var row=Assert.Single(vm.Drafts.Rows);
        await vm.Drafts.RestoreAsync(row with {},false);
        Assert.Equal(1,backend.Reads);
        backend.Current=backend.Current with {UpdatedAtUtc=DateTimeOffset.UnixEpoch.AddSeconds(1)};
        await vm.Drafts.RestoreAsync(row,false);
        Assert.Equal(2,backend.Reads);
        Assert.False(vm.Review.IsOpen);
        Assert.Equal(0,backend.Previews+backend.Commits);
        Assert.Empty(vm.Drafts.Rows);
        Assert.NotEmpty(vm.Drafts.Error);
    }

    [Theory]
    [InlineData("old")]
    [InlineData("range")]
    public async Task Old_or_incomplete_range_draft_cannot_be_restored(string variant)
    {
        var backend=new Probe();
        backend.Current=variant=="old"?backend.Current with {BusinessDate=Today.AddDays(-1)}:
            backend.Current with {TargetStatusId="STS-LEAVE"};
        var vm=new DailyAttendancePageViewModel();
        vm.AttachReader(backend,()=>Actor);
        await vm.Drafts.OpenAsync();
        var row=Assert.Single(vm.Drafts.Rows);
        Assert.False(vm.Drafts.RestoreInitialCommand.CanExecute(row));
        Assert.False(vm.Drafts.RestoreRevisionCommand.CanExecute(row));
        await vm.Drafts.RestoreAsync(row,true);
        Assert.Equal(1,backend.Reads);
        Assert.False(vm.Review.IsOpen);
    }

    [Fact]
    public async Task Wrong_mode_or_inactive_status_is_rejected_without_automatic_fallback()
    {
        var backend=new Probe{Initial=true};
        var vm=new DailyAttendancePageViewModel();
        vm.AttachReader(backend,()=>Actor);
        await vm.Drafts.OpenAsync();
        await vm.Drafts.RestoreAsync(Assert.Single(vm.Drafts.Rows),false);
        Assert.True(vm.Drafts.IsOpen);
        Assert.False(vm.Review.IsOpen);
        Assert.Equal(1,backend.Previews);
        Assert.Equal(0,backend.Commits);
        Assert.NotEmpty(vm.Drafts.Error);
        backend.NoStatus=true;
        await vm.Drafts.RestoreAsync(Assert.Single(vm.Drafts.Rows),true);
        Assert.Equal(1,backend.Previews);
        Assert.False(vm.Review.IsOpen);
    }

    [Fact]
    public async Task Logout_cancels_restore_and_discards_delayed_owner_read()
    {
        NativeIdentity? live=Actor;
        var backend=new Probe();
        var vm=new DailyAttendancePageViewModel();
        vm.AttachReader(backend,()=>live);
        await vm.Drafts.OpenAsync();
        backend.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var work=vm.Drafts.RestoreAsync(Assert.Single(vm.Drafts.Rows),false);
        Assert.True(vm.Drafts.IsRestoring);
        Assert.False(vm.Drafts.CloseCommand.CanExecute(null));
        live=null;vm.Clear();
        backend.Pending.SetResult([backend.Current]);
        await work;
        Assert.False(vm.Drafts.IsOpen);
        Assert.False(vm.Review.IsOpen);
        Assert.Empty(vm.Drafts.Rows);
        Assert.Equal(0,backend.Previews+backend.Commits);
    }

    [Fact]
    public async Task Cancellation_during_canonical_preview_cannot_leave_an_approved_intent()
    {
        var backend=new Probe{PreviewPending=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var vm=new DailyAttendancePageViewModel();
        vm.AttachReader(backend,()=>Actor);
        await vm.Drafts.OpenAsync();
        using var cts=new CancellationTokenSource();
        var work=vm.Drafts.RestoreAsync(Assert.Single(vm.Drafts.Rows),false,cts.Token);
        Assert.True(vm.Drafts.IsRestoring);
        Assert.True(vm.Review.IsBusy);
        cts.Cancel();
        backend.PreviewPending.SetResult(new NativeAttendanceRevisionPreview(new string('a',64),Today,
            "STS-PRESENT","موجود",[]));
        await work;
        Assert.True(vm.Drafts.IsOpen);
        Assert.False(vm.Drafts.IsRestoring);
        Assert.False(vm.Review.IsOpen);
        Assert.False(vm.Review.HasPreview);
        Assert.Equal(0,backend.Commits);
    }

    private sealed class Probe:INativeDashboardReader,INativeAttendanceDraftReader,INativeAttendanceReviewBackend
    {
        public NativeAttendanceDraft Current=Draft();
        public int Reads,Previews,Commits;
        public bool Initial,NoStatus;
        public TaskCompletionSource<IReadOnlyList<NativeAttendanceDraft>>? Pending;
        public TaskCompletionSource<NativeAttendanceRevisionPreview>? PreviewPending;
        public Task<NativeDashboardSnapshot> ReadAsync(NativeIdentity actor,NativeDashboardFilter filter,CancellationToken ct=default)
            =>Task.FromResult(new NativeDashboardSnapshot(filter.BusinessDate,filter.SiteId,0,0,0,[],[],[],[],0,filter.Page,false));
        public Task<IReadOnlyList<NativeAttendanceDraft>> ReadAttendanceDraftsAsync(NativeIdentity actor,CancellationToken ct=default)
        { Reads++;return Pending?.Task??Task.FromResult<IReadOnlyList<NativeAttendanceDraft>>([Current]); }
        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>(NoStatus?[]:[new("STS-PRESENT","موجود")]);
        public Task<NativeAttendanceRevisionPreview> PreviewAsync(NativeIdentity actor,DateOnly date,string site,IReadOnlyList<string> ids,string statusId,CancellationToken ct=default,NativeAttendanceDateRange? range=null)
            =>Preview(date,site,ids,statusId,false);
        public Task<NativeAttendanceRevisionPreview> PreviewInitialAsync(NativeIdentity actor,DateOnly date,string site,IReadOnlyList<string> ids,string statusId,CancellationToken ct=default,NativeAttendanceDateRange? range=null)
            =>Preview(date,site,ids,statusId,true);
        private Task<NativeAttendanceRevisionPreview> Preview(DateOnly date,string site,IReadOnlyList<string> ids,string statusId,bool initial)
        {
            Previews++;
            Assert.Equal(Current.SiteId,site);
            if(initial!=Initial)throw new InvalidOperationException("Canonical mode conflict.");
            if(PreviewPending is not null)return PreviewPending.Task;
            return Task.FromResult(new NativeAttendanceRevisionPreview(new string('a',64),date,statusId,"موجود",
                ids.Select(id=>new NativeAttendancePreviewPerson(id,"اسم الفرد","123","الموقع",initial?"":"غياب",initial?"":"STS-ABSENT")).ToArray()){IsInitial=initial});
        }
        public Task<NativeAttendanceCommitReceipt> CommitAsync(NativeIdentity actor,string token,string operationId,CancellationToken ct=default)
        {Commits++;throw new InvalidOperationException();}
        public Task<NativeAttendanceCommitReceipt> CommitInitialAsync(NativeIdentity actor,string token,string operationId,CancellationToken ct=default)
        {Commits++;throw new InvalidOperationException();}
    }
}
