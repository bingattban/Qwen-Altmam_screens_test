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
        var receipt = Assert.IsType<NativeAdministrativeCatchUpReceipt>(review.Receipt);
        Assert.Equal(2,receipt.AppliedCount);
        Assert.Equal(5,receipt.PendingBefore);
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

    [Fact]
    public void Settings_status_and_connection_follow_live_authorized_session()
    {
        var page = new SettingsPageViewModel();
        Assert.False(page.IsDataConnected);
        Assert.Contains("لم يتم",page.StatusMessage);
        NativeIdentity? live = null;
        var fake = new Fake();
        var notifications = new List<string?>();
        page.PropertyChanged += (_,e) => notifications.Add(e.PropertyName);
        page.AttachCatchUp(fake,()=>live);
        Assert.False(page.IsDataConnected);
        Assert.Contains("لم يتم",page.StatusMessage);

        live = Admin with { Role=UserRole.Supervisor };
        notifications.Clear();
        page.NotifySessionChanged();
        Assert.False(page.IsDataConnected);
        Assert.False(page.OpenCatchUpCommand.CanExecute(null));
        Assert.Contains("لا تملك صلاحية",page.StatusMessage);
        Assert.Contains(nameof(SettingsPageViewModel.StatusMessage),notifications);

        live = Admin;
        notifications.Clear();
        page.NotifySessionChanged();
        Assert.True(page.IsDataConnected);
        Assert.True(page.CanOpenCatchUp);
        Assert.Contains("متاح للإدارة",page.StatusMessage);
        Assert.Contains(nameof(SettingsPageViewModel.StatusMessage),notifications);
        page.OpenCatchUpCommand.Execute(null);
        Assert.True(page.IsDataConnected);
        Assert.False(page.CanOpenCatchUp);

        live = null;
        notifications.Clear();
        page.Clear();
        Assert.False(page.IsDataConnected);
        Assert.False(page.CatchUp.IsOpen);
        Assert.Contains("لم يتم",page.StatusMessage);
        Assert.Contains(nameof(SettingsPageViewModel.StatusMessage),notifications);
        Assert.Equal(0,fake.Previews);
        Assert.Empty(fake.Commits);
    }

    [Fact]
    public async Task Settings_policy_and_catch_up_reviews_are_mutually_exclusive()
    {
        var fake = new Fake();
        var page = new SettingsPageViewModel();
        NativeIdentity? live = Admin with { Role=UserRole.Supervisor };
        page.AttachCatchUp(fake,()=>live);
        page.AttachClosedDayPolicy(fake,()=>live);
        Assert.False(page.CanOpenPolicy);
        Assert.False(page.EnablePolicyCommand.CanExecute(null));
        Assert.False(page.DisablePolicyCommand.CanExecute(null));
        live=Admin;
        page.NotifySessionChanged();
        Assert.True(page.CanOpenPolicy);
        page.OpenCatchUpCommand.Execute(null);
        Assert.True(page.CatchUp.IsOpen);
        Assert.True(page.IsReviewOpen);
        Assert.False(page.CanOpenPolicy);
        page.EnablePolicyCommand.Execute(null);
        Assert.False(page.Policy.IsOpen);
        Assert.True(page.CatchUp.Close());
        page.DisablePolicyCommand.Execute(null);
        Assert.True(page.Policy.IsOpen);
        Assert.True(page.IsReviewOpen);
        Assert.False(page.CanOpenCatchUp);
        page.OpenCatchUpCommand.Execute(null);
        Assert.False(page.CatchUp.IsOpen);
        await page.Policy.ConfirmAsync();
        Assert.Empty(fake.PolicyCalls);
        page.Policy.Prepare();
        Assert.Empty(fake.PolicyCalls);
        await page.Policy.ConfirmAsync();
        Assert.False(Assert.IsType<NativeClosedDayPolicyReceipt>(page.Policy.Receipt).Enabled);
        Assert.Single(fake.PolicyCalls);
        Assert.True(page.Policy.Close());
        Assert.False(page.IsReviewOpen);
        Assert.True(page.CanOpenCatchUp);
        Assert.True(page.CanOpenPolicy);
    }

    [Fact]
    public async Task Settings_policy_uncertain_commit_blocks_other_review_and_logout_clears_late_result()
    {
        var fake = new Fake { WrongPolicyReceipt=true };
        NativeIdentity? live=Admin;
        var page = new SettingsPageViewModel();
        page.AttachCatchUp(fake,()=>live);
        page.AttachClosedDayPolicy(fake,()=>live);
        page.EnablePolicyCommand.Execute(null);
        page.Policy.Prepare();
        await page.Policy.ConfirmAsync();
        Assert.True(page.Policy.HasUnverifiedCommit);
        Assert.True(page.IsReviewOpen);
        Assert.False(page.CanOpenCatchUp);
        Assert.False(page.CanOpenPolicy);
        Assert.False(page.Policy.Close());
        fake.WrongPolicyReceipt=false;
        fake.PendingPolicy=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=page.Policy.ConfirmAsync();
        live=null;
        page.Clear();
        fake.PendingPolicy.SetResult(new(true));
        await pending;
        Assert.False(page.IsReviewOpen);
        Assert.Null(page.Policy.Receipt);
        Assert.False(page.IsDataConnected);
        Assert.Contains("لم يتم",page.StatusMessage);
        Assert.Equal(2,fake.PolicyCalls.Count);
        Assert.Equal(fake.PolicyCalls[0],fake.PolicyCalls[1]);
    }

    private sealed class Fake : INativeAdministrativeCatchUpBackend, INativeClosedDayPolicyBackend
    {
        public bool WrongPolicyReceipt;
        public TaskCompletionSource<NativeClosedDayPolicyReceipt>? PendingPolicy;
        public List<(NativeClosedDayPolicyRequest Request,string Operation)> PolicyCalls=[];
        public Task<NativeClosedDayPolicyReceipt> SetClosedDayPolicyAsync(NativeIdentity actor,
            NativeClosedDayPolicyRequest approved,string operationId,CancellationToken ct=default)
        {
            PolicyCalls.Add((approved,operationId));
            return PendingPolicy?.Task ?? Task.FromResult(new NativeClosedDayPolicyReceipt(
                WrongPolicyReceipt ? !approved.Enabled : approved.Enabled));
        }
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
