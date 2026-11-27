using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAttendanceDraftSavePhase12Tests
{
    private static readonly NativeIdentity Actor=new("USR-1","owner",UserRole.Supervisor,"NONCE-A");
    private static DateOnly Today=>DateOnly.FromDateTime(DateTime.Now);
    private static NativeAttendanceDraftIntent Intent()=>new("DRF-"+new string('a',32),Today,"SITE-A","STS-PRESENT",["P-1","P-2"]);
    [Fact]
    public void Draft_intent_requires_today_unique_ids_and_no_unstored_range()
    {
        var i=Intent();
        Assert.True(i.IsValid(Today));
        Assert.False((i with {BusinessDate=Today.AddDays(-1)}).IsValid(Today));
        Assert.False((i with {TargetStatusId="STS-LEAVE"}).IsValid(Today));
        Assert.False((i with {TargetStatusId="STS-PERMISSION"}).IsValid(Today));
        Assert.False((i with {PersonIds=["P-1","P-1"]}).IsValid(Today));
        Assert.False((i with {DraftId="forged"}).IsValid(Today));
        Assert.True(i.Matches(new(i.DraftId,i.BusinessDate,i.SiteId,i.TargetStatusId,DateTimeOffset.UtcNow,["P-2","P-1"])));
        Assert.False(i.Matches(new(i.DraftId,i.BusinessDate,i.SiteId,i.TargetStatusId,DateTimeOffset.UtcNow,["P-1"])));
    }
    [Fact]
    public async Task Save_is_explicit_verified_once_and_never_previews_or_commits_attendance()
    {
        var b=new Probe();
        var vm=new NativeAttendanceReviewViewModel();
        vm.AttachBackend(b,()=>Actor);
        vm.AttachDraftWriter(b);
        await vm.OpenPersonsAsync(Today,"SITE-A",["P-2","P-1"]);
        Assert.False(vm.SaveDraftCommand.CanExecute(null));
        vm.ChooseStatus("STS-LEAVE");
        Assert.False(vm.SaveDraftCommand.CanExecute(null));
        vm.ChooseStatus("STS-PRESENT");
        Assert.True(vm.SaveDraftCommand.CanExecute(null));
        Assert.Equal(0,b.Saves);
        await vm.SaveDraftAsync();
        Assert.Equal(1,b.Saves);
        Assert.NotEmpty(vm.SavedDraftId);
        Assert.False(vm.SaveDraftCommand.CanExecute(null));
        Assert.False(vm.HasPreview);
        Assert.False(vm.IsCompleted);
        Assert.Equal(0,b.Previews+b.Commits);
        await vm.SaveDraftAsync();
        Assert.Equal(1,b.Saves);
        vm.Cancel();
        Assert.Empty(vm.SavedDraftId);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Save_complete_range_preserves_exact_dates_and_mode(bool initial)
    {
        var b=new Probe();
        var vm=new NativeAttendanceReviewViewModel();
        vm.AttachBackend(b,()=>Actor);vm.AttachDraftWriter(b);
        if(initial)await vm.OpenInitialPersonsAsync(Today,"SITE-A",["P-1"]);
        else await vm.OpenPersonsAsync(Today,"SITE-A",["P-1"]);
        vm.ChooseStatus("STS-LEAVE");
        vm.RangeStartDate=Today.ToDateTime(TimeOnly.MinValue);
        Assert.False(vm.CanSaveDraft);
        vm.RangeEndDate=Today.AddDays(3).ToDateTime(TimeOnly.MinValue);
        Assert.True(vm.CanSaveDraft);
        await vm.SaveDraftAsync();
        Assert.Equal(1,b.Saves);
        Assert.NotEmpty(vm.SavedDraftId);
        Assert.Equal(initial,b.Last!.RangeEvidence!.IsInitial);
        Assert.Equal(Today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),b.Last.RangeEvidence.StartDate);
        Assert.Equal(Today.AddDays(3).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),b.Last.RangeEvidence.EndDate);
        Assert.Equal(0,b.Previews+b.Commits);
        Assert.False(vm.HasPreview);
        Assert.False(vm.CanSaveDraft);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_or_forged_receipt_never_claims_success_or_retries(bool lose)
    {
        var b=new Probe{Lose=lose,Forge=!lose};
        var vm=new NativeAttendanceReviewViewModel();
        vm.AttachBackend(b,()=>Actor);vm.AttachDraftWriter(b);
        await vm.OpenPersonsAsync(Today,"",["P-1"]);
        vm.ChooseStatus("STS-PRESENT");
        await vm.SaveDraftAsync();
        Assert.Empty(vm.SavedDraftId);
        Assert.NotEmpty(vm.DraftMessage);
        Assert.False(vm.SaveDraftCommand.CanExecute(null));
        await vm.SaveDraftAsync();
        Assert.Equal(1,b.Saves);
        Assert.Equal(0,b.Previews+b.Commits);
    }
    [Fact]
    public async Task Logout_discards_late_save_receipt_and_blocks_close_during_write()
    {
        NativeIdentity? live=Actor;
        var b=new Probe{Pending=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var vm=new NativeAttendanceReviewViewModel();
        vm.AttachBackend(b,()=>live);vm.AttachDraftWriter(b);
        await vm.OpenPersonsAsync(Today,"",["P-1"]);
        vm.ChooseStatus("STS-PRESENT");
        var work=vm.SaveDraftAsync();
        Assert.True(vm.IsSavingDraft);
        Assert.False(vm.CancelCommand.CanExecute(null));
        vm.Cancel();
        Assert.True(vm.IsOpen);
        live=null;vm.ForceCancel();
        b.Pending.SetResult(b.Receipt(b.Last!));
        await work;
        Assert.Empty(vm.SavedDraftId);
        Assert.False(vm.IsSavingDraft);
        Assert.False(vm.IsOpen);
    }
    private sealed class Probe:INativeAttendanceReviewBackend,INativeAttendanceDraftWriter
    {
        public int Saves,Previews,Commits;
        public bool Lose,Forge;
        public NativeAttendanceDraftIntent? Last;
        public TaskCompletionSource<NativeAttendanceDraft>? Pending;
        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(NativeIdentity actor,CancellationToken ct=default)
            =>Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>([new("STS-PRESENT","موجود"),new("STS-LEAVE","إجازة")]);
        public NativeAttendanceDraft Receipt(NativeAttendanceDraftIntent intent)=>new(intent.DraftId,intent.BusinessDate,intent.SiteId,
            intent.TargetStatusId,DateTimeOffset.UtcNow,Forge?["P-OTHER"]:intent.PersonIds){RangeEvidence=intent.RangeEvidence};
        public Task<NativeAttendanceDraft> SaveAttendanceDraftAsync(NativeIdentity actor,NativeAttendanceDraftIntent intent,CancellationToken ct=default)
        {
            Saves++;Last=intent;
            if(Lose)throw new InvalidOperationException("lost");
            return Pending?.Task??Task.FromResult(Receipt(intent));
        }
        public Task<NativeAttendanceRevisionPreview> PreviewAsync(NativeIdentity actor,DateOnly date,string site,IReadOnlyList<string> personIds,string statusId,CancellationToken ct=default,NativeAttendanceDateRange? range=null)
        {Previews++;throw new InvalidOperationException();}
        public Task<NativeAttendanceCommitReceipt> CommitAsync(NativeIdentity actor,string previewToken,string operationId,CancellationToken ct=default)
        {Commits++;throw new InvalidOperationException();}
    }
}
