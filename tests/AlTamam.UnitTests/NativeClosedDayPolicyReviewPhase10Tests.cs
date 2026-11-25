using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeClosedDayPolicyReviewPhase10Tests
{
    private static readonly NativeIdentity Admin = new("USR-1","admin",UserRole.OrdinaryAdministration,"POLICY-NONCE");

    [Fact]
    public void Policy_reason_is_optional_but_nonempty_reason_must_be_valid()
    {
        Assert.True(new NativeClosedDayPolicyRequest(true,"").IsValid);
        Assert.True(new NativeClosedDayPolicyRequest(false,"إيقاف الإذن بعد المراجعة").IsValid);
        Assert.False(new NativeClosedDayPolicyRequest(true,"قصير").IsValid);
        Assert.False(new NativeClosedDayPolicyRequest(true,"   ").IsValid);
        Assert.False(new NativeClosedDayPolicyRequest(true,new string('a',401)).IsValid);
        Assert.False(new NativeClosedDayPolicyRequest(true,"ملاحظة\nغير صالحة").IsValid);
    }

    [Fact]
    public async Task Policy_requires_authorized_explicit_confirmation_and_invalidates_edited_intent()
    {
        var fake = new Fake();
        var review = new NativeClosedDayPolicyReview();
        Assert.False(review.Open(fake,()=>Admin with { Role=UserRole.Supervisor },true));
        Assert.True(review.Open(fake,()=>Admin,true));
        await review.ConfirmAsync();
        Assert.Empty(fake.Calls);
        review.Reason="قصير";
        review.Prepare();
        Assert.Null(review.Approved);
        review.Reason="";
        review.Prepare();
        Assert.NotNull(review.Approved);
        Assert.Empty(fake.Calls);
        review.Reason="تفعيل الإذن بعد المراجعة";
        Assert.Null(review.Approved);
        await review.ConfirmAsync();
        Assert.Empty(fake.Calls);
        review.Prepare();
        await review.ConfirmAsync();
        Assert.Single(fake.Calls);
        Assert.True(Assert.IsType<NativeClosedDayPolicyReceipt>(review.Receipt).Enabled);
        Assert.False(review.CanEdit);
        Assert.True(review.Close());
    }

    [Fact]
    public async Task Policy_wrong_receipt_freezes_intent_and_retries_same_operation()
    {
        var fake = new Fake { WrongReceipt=true };
        var review = new NativeClosedDayPolicyReview();
        Assert.True(review.Open(fake,()=>Admin,false));
        review.Prepare();
        await review.ConfirmAsync();
        Assert.True(review.HasUnverifiedCommit);
        Assert.False(review.Close());
        Assert.False(review.CanEdit);
        review.Reason="محاولة تغيير السبب بعد الاعتماد";
        review.Prepare();
        fake.WrongReceipt=false;
        await review.ConfirmAsync();
        Assert.Equal(2,fake.Calls.Count);
        Assert.Equal(fake.Calls[0],fake.Calls[1]);
        Assert.False(Assert.IsType<NativeClosedDayPolicyReceipt>(review.Receipt).Enabled);
        Assert.False(review.HasUnverifiedCommit);
    }

    [Fact]
    public async Task Policy_session_replacement_and_late_reply_never_publish_another_users_state()
    {
        var fake = new Fake();
        NativeIdentity? live=Admin;
        var review = new NativeClosedDayPolicyReview();
        Assert.True(review.Open(fake,()=>live,true));
        review.Prepare();
        live=Admin with { SessionNonce="OTHER" };
        await review.ConfirmAsync();
        Assert.Empty(fake.Calls);
        Assert.False(review.IsOpen);
        live=Admin;
        Assert.True(review.Open(fake,()=>live,false));
        review.Prepare();
        fake.Pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=review.ConfirmAsync();
        live=null;
        review.ForceClear();
        fake.Pending.SetResult(new(false));
        await pending;
        Assert.False(review.IsOpen);
        Assert.Null(review.Receipt);
        Assert.Null(review.Approved);
    }

    private sealed class Fake : INativeClosedDayPolicyBackend
    {
        public bool WrongReceipt;
        public TaskCompletionSource<NativeClosedDayPolicyReceipt>? Pending;
        public List<(NativeClosedDayPolicyRequest Request,string Operation)> Calls=[];
        public Task<NativeClosedDayPolicyReceipt> SetClosedDayPolicyAsync(NativeIdentity actor,
            NativeClosedDayPolicyRequest approved,string operationId,CancellationToken ct=default)
        {
            Calls.Add((approved,operationId));
            return Pending?.Task ?? Task.FromResult(new NativeClosedDayPolicyReceipt(
                WrongReceipt ? !approved.Enabled : approved.Enabled));
        }
    }
}
