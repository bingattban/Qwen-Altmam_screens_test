using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAccountLifecyclePhase11Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static readonly NativeAccountRow Row=new(1,"USR-2","supervisor",UserRole.Supervisor,true,7,
        "اسم المشرف","123","PER-2","owner","2026-01-01T10:00:00Z",true);
    private static NativeAccountLifecycleIntent Intent(NativeAccountAction action,NativeAccountRow? row=null,string reason="")=>
        new(row??Row,action,reason,Guid.NewGuid().ToString("N"));

    [Fact]
    public void Lifecycle_intent_and_receipt_enforce_scope_revision_and_action()
    {
        var change=Intent(NativeAccountAction.ChangePassword);
        Assert.True(change.IsValid(Admin));
        Assert.True(change.IsValid(Admin with { Role=UserRole.OrdinaryAdministration }));
        Assert.False(change.IsValid(Admin with { Role=UserRole.Supervisor }));
        Assert.False((change with { Row=Row with { UserId=Admin.UserId,CanModify=false } }).IsValid(Admin));
        Assert.False((change with { Row=Row with { Role=UserRole.OrdinaryAdministration } }).IsValid(Admin with { Role=UserRole.OrdinaryAdministration }));
        Assert.False((change with { Row=Row with { Active=false } }).IsValid(Admin));
        Assert.False((change with { Row=Row with { Revision=long.MaxValue } }).IsValid(Admin));
        Assert.False((change with { Reason="extra" }).IsValid(Admin));
        var disable=Intent(NativeAccountAction.Deactivate);
        Assert.True(disable.IsValid(Admin));
        var enable=Intent(NativeAccountAction.Reactivate,Row with { Active=false },"عاد إلى القوة");
        Assert.True(enable.IsValid(Admin));
        Assert.False((enable with { Reason="قصير" }).IsValid(Admin));
        Assert.False((enable with { Row=Row }).IsValid(Admin));
        var receipt=new NativeAccountLifecycleReceipt(Row.UserId,false,8);
        Assert.True(receipt.Matches(disable));
        Assert.False(receipt.Matches(change));
        Assert.False((receipt with { Revision=7 }).Matches(disable));
        Assert.False((receipt with { UserId="USR-X" }).Matches(disable));
        Assert.True((receipt with { Active=true }).Matches(enable));
    }

    [Fact]
    public async Task Review_requires_exact_row_preview_and_password_confirmation()
    {
        var backend=new Backend();
        var vm=new NativeAccountLifecycleReview();
        Assert.False(vm.Open(backend,Admin,Row with { },NativeAccountAction.ChangePassword,()=>Admin,()=>Row));
        Assert.True(vm.Open(backend,Admin,Row,NativeAccountAction.ChangePassword,()=>Admin,()=>Row));
        await vm.ConfirmAsync("abc123","abc123");
        Assert.Empty(backend.Intents);
        vm.Preview();
        await vm.ConfirmAsync("abc123","abc124");
        Assert.Empty(backend.Intents);
        await vm.ConfirmAsync("abc123","abc123");
        Assert.Single(backend.Intents);
        Assert.NotNull(vm.Receipt);
        Assert.True(vm.Close());
    }

    [Theory]
    [InlineData(NativeAccountAction.Deactivate)]
    [InlineData(NativeAccountAction.Reactivate)]
    public async Task Activation_review_uses_exact_operation_on_retry(NativeAccountAction action)
    {
        var backend=new Backend { Fail=true };
        var vm=new NativeAccountLifecycleReview();
        var row=Row with { Active=action==NativeAccountAction.Deactivate };
        Assert.True(vm.Open(backend,Admin,row,action,()=>Admin,()=>row));
        if(action==NativeAccountAction.Reactivate) vm.Reason="عاد إلى القوة";
        vm.Preview();
        Assert.NotNull(vm.Prepared);
        await vm.ConfirmAsync("","");
        Assert.True(vm.HasUnverifiedCommit);
        Assert.False(vm.Close());
        var first=Assert.Single(backend.Intents);
        vm.Reason="سبب آخر"; vm.Preview();
        backend.Fail=false;
        await vm.ConfirmAsync("","");
        Assert.Equal(first,backend.Intents[1]);
        Assert.Equal(action==NativeAccountAction.Reactivate,vm.Receipt!.Active);
        Assert.True(vm.Close());
    }

    [Fact]
    public async Task Review_distinguishes_stale_rejection_and_ambiguous_reactivation()
    {
        var backend=new Backend { Rejected=true };
        var vm=new NativeAccountLifecycleReview();
        Assert.True(vm.Open(backend,Admin,Row,NativeAccountAction.Deactivate,()=>Admin,()=>Row));
        vm.Preview(); await vm.ConfirmAsync("","");
        Assert.True(vm.NeedsReload);
        Assert.False(vm.HasUnverifiedCommit);
        Assert.True(vm.Close());
        var inactive=Row with { Active=false };
        backend.Rejected=false; backend.Fail=true;
        Assert.True(vm.Open(backend,Admin,inactive,NativeAccountAction.Reactivate,()=>Admin,()=>inactive));
        vm.Reason="عاد إلى القوة"; vm.Preview(); await vm.ConfirmAsync("","");
        backend.Fail=false; backend.Rejected=true;
        await vm.ConfirmAsync("","");
        Assert.True(vm.HasUnverifiedCommit);
        Assert.Null(vm.Receipt);
        Assert.False(vm.Close());
    }

    [Fact]
    public async Task Lifecycle_session_teardown_discards_late_receipt()
    {
        var backend=new Backend();
        var vm=new NativeAccountLifecycleReview();
        NativeIdentity? actor=Admin;
        Assert.True(vm.Open(backend,Admin,Row,NativeAccountAction.Deactivate,()=>actor,()=>Row));
        vm.Preview();
        var pending=new TaskCompletionSource<NativeAccountLifecycleReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var task=vm.ConfirmAsync("","");
        actor=null; vm.ForceClear();
        pending.SetResult(new(Row.UserId,false,8));
        await task;
        Assert.False(vm.IsOpen);
        Assert.Null(vm.Receipt);
        Assert.Null(vm.Prepared);
    }

    private sealed class Backend : INativeAccountLifecycleBackend
    {
        public List<NativeAccountLifecycleIntent> Intents=[];
        public bool Fail,Rejected;
        public Task<NativeAccountLifecycleReceipt>? Pending;
        public Task<NativeAccountLifecycleReceipt> ApplyAccountActionAsync(NativeIdentity actor,
            NativeAccountLifecycleIntent intent,string password,CancellationToken ct=default)
        {
            Intents.Add(intent);
            if(Fail) throw new IOException("response lost");
            if(Rejected) throw new NativeAccountLifecycleRejectedException();
            return Pending??Task.FromResult(new NativeAccountLifecycleReceipt(intent.Row.UserId,
                intent.Action!=NativeAccountAction.Deactivate,intent.Row.Revision+1));
        }
    }
}
