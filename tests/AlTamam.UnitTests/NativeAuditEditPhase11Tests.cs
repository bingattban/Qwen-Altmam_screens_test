using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAuditEditPhase11Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","admin",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly NativeAuditRow Row=new("AUD-1",1,"SITE_CREATE","إضافة موقع","admin","المركز",
        "الوصف","الوصف","", "2026-01-01","2026-01-01T10:00:00Z","تم الاعتماد",new string('a',64),false,0);
    private static readonly NativeAuditDetail Detail=new(Row.AuditEventId,Row.OperationCode,Row.OperationName,
        "USR-1","admin","","Site","SITE-1",Row.TargetDisplay,Row.RawDescription,Row.Reason,
        Row.EffectiveLocalDate,Row.ExecutedAtUtc,"COR-1",Row.EventHash,"GENESIS",null,null,false,0,true);
    private static NativeAuditEditRequest Request()=>new(Row,Detail,"وصف جديد","توضيح الوصف",Guid.NewGuid().ToString("N"));
    private static NativeAuditEditReceipt Receipt(NativeAuditEditRequest request)=>new(
        request.Row.AuditEventId,"AUD-2",new(0,false,Row.Description),new(1,false,request.Description),true);

    [Fact]
    public void Edit_request_and_receipt_enforce_identity_role_revision_and_raw_evidence()
    {
        var request=Request();
        Assert.True(request.IsValid(Admin.Role));
        Assert.True(request.IsValid(UserRole.OrdinaryAdministration));
        Assert.False(request.IsValid(UserRole.Supervisor));
        Assert.False((request with { Detail=Detail with { ChainValid=false } }).IsValid(Admin.Role));
        Assert.False((request with { Description=Row.Description }).IsValid(Admin.Role));
        Assert.False((request with { Reason=" " }).IsValid(Admin.Role));
        Assert.False((request with { OperationId="short" }).IsValid(Admin.Role));
        var receipt=Receipt(request);
        Assert.True(receipt.Matches(request));
        Assert.False((receipt with { OperationAuditEventId=Row.AuditEventId }).Matches(request));
        Assert.False((receipt with { RawEvidenceUntouched=false }).Matches(request));
        Assert.False((receipt with { Before=receipt.Before with { Description="مزور" } }).Matches(request));
        Assert.False((receipt with { After=receipt.After with { Version=2 } }).Matches(request));
        Assert.False((receipt with { After=receipt.After with { IsTombstoned=true } }).Matches(request));
    }

    [Fact]
    public async Task Edit_review_requires_explicit_preview_and_retries_identical_operation()
    {
        var backend=new Backend();
        var review=new NativeAuditEditReview();
        Assert.True(review.Open(backend,Admin,Row,Detail,()=>Admin,()=>Row,()=>Detail));
        review.Description="وصف جديد";
        review.Reason="توضيح الوصف";
        await review.ConfirmAsync();
        Assert.Empty(backend.Requests);
        review.Preview();
        Assert.NotNull(review.Prepared);
        review.Reason="سبب آخر";
        Assert.Null(review.Prepared);
        review.Preview();
        backend.Fail=true;
        await review.ConfirmAsync();
        Assert.True(review.HasUnverifiedCommit);
        Assert.False(review.CanEdit);
        Assert.False(review.Close());
        var first=Assert.Single(backend.Requests);
        review.Description="محاولة تغيير";
        backend.Fail=false;
        await review.ConfirmAsync();
        Assert.Equal(first,backend.Requests[1]);
        Assert.NotNull(review.Receipt);
        Assert.False(review.HasUnverifiedCommit);
        Assert.True(review.Close());
    }

    [Fact]
    public async Task Edit_review_rejects_forged_receipt_and_definitive_stale_version()
    {
        var backend=new Backend { Forged=true };
        var review=new NativeAuditEditReview();
        Assert.True(review.Open(backend,Admin,Row,Detail,()=>Admin,()=>Row,()=>Detail));
        review.Description="وصف جديد"; review.Reason="توضيح الوصف"; review.Preview();
        await review.ConfirmAsync();
        Assert.Null(review.Receipt);
        Assert.True(review.HasUnverifiedCommit);
        backend.Forged=false; backend.Stale=true;
        await review.ConfirmAsync();
        Assert.True(review.NeedsReload);
        Assert.False(review.HasUnverifiedCommit);
        Assert.True(review.Close());
    }

    [Fact]
    public async Task Edit_review_scrubs_pending_result_on_session_teardown()
    {
        var backend=new Backend();
        var review=new NativeAuditEditReview();
        NativeIdentity? actor=Admin;
        Assert.True(review.Open(backend,Admin,Row,Detail,()=>actor,()=>Row,()=>Detail));
        review.Description="وصف جديد"; review.Reason="توضيح الوصف"; review.Preview();
        var pending=new TaskCompletionSource<NativeAuditEditReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var task=review.ConfirmAsync();
        Assert.True(review.IsBusy);
        actor=null;
        review.ForceClear();
        pending.SetResult(Receipt(Assert.Single(backend.Requests)));
        await task;
        Assert.False(review.IsOpen);
        Assert.False(review.IsBusy);
        Assert.Null(review.Receipt);
        Assert.Null(review.Prepared);
    }

    private sealed class Backend : INativeAuditEditBackend
    {
        public List<NativeAuditEditRequest> Requests=[];
        public bool Fail,Forged,Stale;
        public Task<NativeAuditEditReceipt>? Pending;
        public Task<NativeAuditEditReceipt> EditAuditDescriptionAsync(NativeIdentity actor,NativeAuditEditRequest request,CancellationToken ct=default)
        {
            Requests.Add(request);
            if(Fail) throw new IOException("response lost");
            if(Stale) throw new NativeAuditEditStaleException();
            return Pending??Task.FromResult(Forged?Receipt(request) with { RawEvidenceUntouched=false }:Receipt(request));
        }
    }
}
