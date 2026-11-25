using System.IO;
using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAdministrativeCatchUpReviewPhase10Tests
{
    private static readonly NativeIdentity Admin = new("USR-1","admin",UserRole.OrdinaryAdministration,"CATCHUP-NONCE");
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private static NativeAdministrativeCatchUpReview Open(Fake fake, Func<NativeIdentity?> actor)
    {
        var review = new NativeAdministrativeCatchUpReview();
        Assert.True(review.Open(fake,actor));
        review.Reason = "استدراك موثق للتحضير الناقص";
        return review;
    }

    [Fact]
    public async Task Catch_up_requires_admin_valid_date_reason_and_separate_confirmation()
    {
        var fake = new Fake();
        var denied = new NativeAdministrativeCatchUpReview();
        Assert.False(denied.Open(fake,()=>Admin with { Role=UserRole.Supervisor }));
        var review = Open(fake,()=>Admin);
        await review.ConfirmAsync();
        Assert.Empty(fake.Commits);
        string validDate = review.DateText;
        review.DateText = Today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        await review.PreviewAsync();
        Assert.Equal(0,fake.Previews);
        review.DateText = validDate;
        await review.PreviewAsync();
        Assert.NotNull(review.Preview);
        Assert.Empty(fake.Commits);
        review.Reason = "سبب آخر معتمد للاستدراك";
        Assert.Null(review.Preview);
        await review.PreviewAsync();
        await review.ConfirmAsync();
        Assert.Single(fake.Commits);
        Assert.Equal(2,review.Receipt!.AppliedCount);
        Assert.Equal(5,review.Receipt.PendingBefore);
        Assert.False(review.CanEdit);
        Assert.True(review.Close());
    }

    [Fact]
    public async Task Catch_up_rejects_mismatched_preview_and_retries_exact_uncertain_operation()
    {
        var fake = new Fake { Forge=true };
        var review = Open(fake,()=>Admin);
        await review.PreviewAsync();
        Assert.Null(review.Preview);
        await review.ConfirmAsync();
        Assert.Empty(fake.Commits);
        fake.Forge=false;
        fake.BadReceipt=true;
        await review.PreviewAsync();
        await review.ConfirmAsync();
        Assert.True(review.HasUnverifiedCommit);
        Assert.False(review.Close());
        Assert.False(review.CanEdit);
        review.Reason = "سبب غير مسموح بعد الاعتماد";
        await review.PreviewAsync();
        Assert.Equal(2,fake.Previews);
        fake.BadReceipt=false;
        await review.ConfirmAsync();
        Assert.Equal(2,fake.Commits.Count);
        Assert.Equal(fake.Commits[0],fake.Commits[1]);
        Assert.NotNull(review.Receipt);
    }

    [Fact]
    public async Task Catch_up_discards_late_preview_on_logout_and_never_commits_as_replaced_actor()
    {
        var fake = new Fake();
        NativeIdentity? live = Admin;
        var review = Open(fake,()=>live);
        await review.PreviewAsync();
        live = Admin with { SessionNonce="OTHER" };
        await review.ConfirmAsync();
        Assert.Empty(fake.Commits);
        Assert.Null(review.Preview);
        live=Admin;
        review=Open(fake,()=>live);
        fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=review.PreviewAsync();
        live=null;
        review.ForceClear();
        fake.Pending.SetResult(fake.Last!);
        await pending;
        Assert.False(review.IsOpen);
        Assert.Null(review.Preview);
    }

    [Fact]
    public async Task Settings_catch_up_is_disabled_for_supervisor_and_cleared_on_session_reset()
    {
        var fake = new Fake();
        NativeIdentity? live = Admin with { Role=UserRole.Supervisor };
        var page = new SettingsPageViewModel();
        page.AttachCatchUp(fake,()=>live);
        Assert.False(page.CanOpenCatchUp);
        page.OpenCatchUpCommand.Execute(null);
        Assert.False(page.CatchUp.IsOpen);
        live=Admin;
        page.NotifySessionChanged();
        Assert.True(page.CanOpenCatchUp);
        page.OpenCatchUpCommand.Execute(null);
        Assert.True(page.CatchUp.IsOpen);
        Assert.False(page.CanOpenCatchUp);
        page.CatchUp.Reason="استدراك موثق للتحضير الناقص";
        await page.CatchUp.PreviewAsync();
        Assert.NotNull(page.CatchUp.Preview);
        live=null;
        page.Clear();
        Assert.False(page.CatchUp.IsOpen);
        Assert.Null(page.CatchUp.Preview);
    }

    private sealed class Fake : INativeAdministrativeCatchUpBackend
    {
        public int Previews;
        public bool Forge,BadReceipt;
        public NativeAdministrativeCatchUpPreview? Last;
        public TaskCompletionSource<NativeAdministrativeCatchUpPreview>? Pending;
        public List<(string Token,string Operation)> Commits=[];
        public Task<NativeAdministrativeCatchUpPreview> PreviewCatchUpAsync(NativeIdentity actor,NativeAdministrativeCatchUpRequest request,CancellationToken ct=default)
        {
            Previews++;
            Last=new(Forge?request with { BusinessDate=request.BusinessDate.AddDays(-1) }:request,
                new string('a',64),new string('B',64),5,12,true);
            return Pending?.Task ?? Task.FromResult(Last!);
        }
        public Task<NativeAdministrativeCatchUpReceipt> CommitCatchUpAsync(NativeIdentity actor,NativeAdministrativeCatchUpPreview approved,string operationId,CancellationToken ct=default)
        {
            Commits.Add((approved.Token,operationId));
            return Task.FromResult(new NativeAdministrativeCatchUpReceipt(approved.Request.BusinessDate,
                approved.PendingCount,BadReceipt?6:2,"تم استدراك حالتين",true));
        }
    }
}
