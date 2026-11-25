using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeHistoricalRangeReviewPhase10Tests
{
    private static readonly NativeIdentity Owner =
        new("USR-1","admin",UserRole.GeneralAdministration,"RANGE-NONCE");
    private static DateOnly Day => DateOnly.FromDateTime(DateTime.Now).AddDays(-2);
    private static NativeMonthlyCellDetails Detail => new("P-1",Day,"أحمد","123",
        "المركز","","","المركز","","","","",[]);

    private static async Task<NativeHistoricalRangeReview> Open(Fake fake,
        NativeMonthlyCellDetails detail, Func<NativeIdentity?> actor,
        Func<NativeMonthlyCellDetails?> selected)
    {
        var vm = new NativeHistoricalRangeReview();
        Assert.True(vm.Open(fake, fake, actor, selected, detail));
        await vm.LoadChoicesAsync();
        vm.SelectedStatusId = "STS-ABSENT";
        vm.Reason = "تصحيح معتمد للسجل التاريخي";
        vm.ToText = detail.BusinessDate.AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return vm;
    }

    [Fact]
    public async Task Range_requires_explicit_approval_and_draft_edits_revoke_preview()
    {
        var detail=Detail;
        var fake=new Fake();
        var vm=await Open(fake,detail,()=>Owner,()=>detail);
        await vm.ConfirmAsync();
        Assert.Empty(fake.Commits);
        await vm.PreviewAsync();
        Assert.NotNull(vm.Preview);
        Assert.Equal(1,vm.Preview!.SkippedDays);
        Assert.Empty(fake.Commits);
        vm.Reason="سبب آخر معتمد للسجل التاريخي";
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Empty(fake.Commits);
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.Single(fake.Commits);
        Assert.NotNull(vm.Receipt);
        Assert.Equal(8,vm.Receipt!.FinalRevisionNumber);
        Assert.False(vm.CanEdit);
        Assert.True(vm.Close());
    }

    [Fact]
    public async Task Range_rejects_wrong_role_invalid_dates_and_forged_preview()
    {
        var detail=Detail;
        var fake=new Fake();
        var denied=new NativeHistoricalRangeReview();
        Assert.False(denied.Open(fake,fake,()=>Owner with { Role=UserRole.Supervisor },()=>detail,detail));
        var vm=await Open(fake,detail,()=>Owner,()=>detail);
        string to=vm.ToText;
        vm.ToText="invalid";
        await vm.PreviewAsync();
        Assert.Equal(0,fake.Previews);
        vm.ToText=to;
        fake.Forge=true;
        await vm.PreviewAsync();
        Assert.Null(vm.Preview);
        await vm.ConfirmAsync();
        Assert.Empty(fake.Commits);
    }

    [Fact]
    public async Task Range_uncertain_commit_retries_only_identical_approved_operation()
    {
        var detail=Detail;
        var fake=new Fake { Lose=true };
        var vm=await Open(fake,detail,()=>Owner,()=>detail);
        await vm.PreviewAsync();
        await vm.ConfirmAsync();
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.CanEdit);
        Assert.False(vm.Close());
        vm.Reason="لا يسمح بتغيير سبب الاعتماد";
        await vm.PreviewAsync();
        Assert.Equal(1,fake.Previews);
        fake.Lose=false;
        await vm.ConfirmAsync();
        Assert.Equal(2,fake.Commits.Count);
        Assert.Equal(fake.Commits[0],fake.Commits[1]);
        Assert.NotNull(vm.Receipt);
        Assert.False(vm.HasUnverifiedCommit);
    }

    [Fact]
    public async Task Range_discards_late_preview_after_session_clear_and_rejects_changed_person()
    {
        var detail=Detail;
        NativeMonthlyCellDetails? current=detail;
        var fake=new Fake();
        var vm=await Open(fake,detail,()=>Owner,()=>current);
        await vm.PreviewAsync();
        current=detail with { };
        await vm.ConfirmAsync();
        Assert.Empty(fake.Commits);
        Assert.Null(vm.Preview);
        current=detail;
        vm=await Open(fake,detail,()=>Owner,()=>current);
        fake.Pending=new TaskCompletionSource<NativeHistoricalRangePreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=vm.PreviewAsync();
        vm.ForceClear();
        fake.Pending.SetResult(fake.LastPreview!);
        await pending;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Preview);
        Assert.Empty(fake.Commits);
    }

    private sealed class Fake : INativeHistoricalRangeBackend, INativeClosedDayCorrectionBackend
    {
        public int Previews;
        public bool Forge,Lose;
        public NativeHistoricalRangePreview? LastPreview;
        public TaskCompletionSource<NativeHistoricalRangePreview>? Pending;
        public List<(string Token,string Operation)> Commits=[];
        public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadCorrectionChoicesAsync(NativeIdentity actor,CancellationToken ct=default) =>
            Task.FromResult<IReadOnlyList<NativeAttendanceStatusChoice>>([new("STS-ABSENT","غياب")]);
        public Task<NativeHistoricalRangePreview> PreviewRangeAsync(NativeIdentity actor,NativeHistoricalRangeRequest request,CancellationToken ct=default)
        {
            Previews++;
            LastPreview=new(Forge?request with { PersonId="OTHER" }:request,new string('a',64),"أحمد","123",
                [new(request.From,false,0,"A-1","SITE-1","المركز","__UNPREPARED__","غير محضر","غياب","غ","#FF0000"),
                 new(request.To,true,7,"A-2","SITE-2","الموقع الثاني","STS-PRESENT","موجود","غياب","غ","#FF0000")],true);
            return Pending?.Task ?? Task.FromResult(LastPreview!);
        }
        public Task<NativeHistoricalRangeReceipt> CommitRangeAsync(NativeIdentity actor,NativeHistoricalRangePreview approved,string operationId,CancellationToken ct=default)
        {
            Commits.Add((approved.Token,operationId));
            if(Lose) throw new IOException("Lost reply");
            return Task.FromResult(new NativeHistoricalRangeReceipt(approved.Request.PersonId,approved.Request.From,8));
        }
        public Task<NativeClosedDayCorrectionPreview> PreviewAsync(NativeIdentity actor,NativeClosedDayCorrectionRequest request,CancellationToken ct=default) => throw new NotSupportedException();
        public Task<NativeClosedDayCorrectionReceipt> CommitAsync(NativeIdentity actor,string token,string operationId,CancellationToken ct=default) => throw new NotSupportedException();
    }
}
