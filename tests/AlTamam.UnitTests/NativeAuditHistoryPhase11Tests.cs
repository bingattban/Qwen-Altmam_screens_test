using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAuditHistoryPhase11Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","admin",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeAuditHistoryRow Row(string id="AUD-1",int version=1,bool hide=false)=>
        new(id,"SITE_CREATE","الأصل","سبب موثق","USR-1","admin","2026-01-01T10:00:00Z",
            hide?null:"الوصف المعدل",new(version-1,false,"الأصل"),
            new(version,hide,hide?"الأصل":"الوصف المعدل"));

    [Fact]
    public void History_preserves_versions_and_rejects_duplicate_or_forged_states()
    {
        var request=new NativeAuditHistoryRequest("AUD-1",1);
        Assert.True(request.IsValid);
        Assert.True(request.IsAuthorized(Admin.Role));
        Assert.True(request.IsAuthorized(UserRole.OrdinaryAdministration));
        Assert.False(request.IsAuthorized(UserRole.Supervisor));
        Assert.False((request with { Page=0 }).IsValid);
        Assert.False((request with { AuditEventId="bad\nID" }).IsValid);
        var row=Row();
        var hidden=Row(version:2,hide:true) with { Before=new(1,false,"الوصف المعدل"),After=new(2,true,"الوصف المعدل") };
        var page=new NativeAuditHistoryPage(request,[hidden,row]);
        Assert.True(page.Matches(request));
        Assert.False((page with { Rows=[row,row] }).Matches(request));
        Assert.False((page with { Rows=[Row("AUD-2")] }).Matches(request));
        Assert.False((page with { Rows=[hidden with { After=hidden.After with { Description="تزوير" } }] }).Matches(request));
        Assert.False((page with { Rows=[row with { Before=row.Before with { Version=2 } }] }).Matches(request));
        Assert.False((page with { Rows=[row with { CorrectedDescription=null }] }).Matches(request));
        Assert.False(page.Matches(request with { Page=2 }));
    }

    [Fact]
    public async Task History_reads_selected_scope_and_pages_then_clears()
    {
        var backend=new Backend();
        var vm=new NativeAuditHistoryViewModel();
        vm.Attach(backend,()=>Admin);
        await vm.OpenAsync("AUD-1");
        Assert.Equal(50,vm.Rows.Count);
        Assert.True(vm.NextCommand.CanExecute(null));
        await vm.NextAsync();
        Assert.Equal(new NativeAuditHistoryRequest("AUD-1",2),backend.Requests[^1]);
        Assert.Single(vm.Rows);
        Assert.False(vm.NextCommand.CanExecute(null));
        await vm.PreviousAsync();
        Assert.Equal(1,backend.Requests[^1].Page);
        vm.Close();
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public async Task History_denies_supervisor_and_discards_wrong_scope()
    {
        var backend=new Backend();
        NativeIdentity actor=Admin with { Role=UserRole.Supervisor };
        var vm=new NativeAuditHistoryViewModel();
        vm.Attach(backend,()=>actor);
        await vm.OpenAsync("");
        Assert.Empty(backend.Requests);
        Assert.False(vm.IsOpen);
        actor=Admin;
        backend.Forged=true;
        await vm.OpenAsync("AUD-1");
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_cannot_repaint_after_close_or_session_change(bool changeSession)
    {
        var backend=new Backend();
        NativeIdentity? actor=Admin;
        var vm=new NativeAuditHistoryViewModel();
        vm.Attach(backend,()=>actor);
        var pending=new TaskCompletionSource<NativeAuditHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Pending=pending.Task;
        var task=vm.OpenAsync("AUD-1");
        Assert.True(vm.IsLoading);
        if(changeSession) actor=Admin with { SessionNonce="NONCE-B" };
        else vm.Close();
        pending.SetResult(new(new("AUD-1",1),[Row()]));
        await task;
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsOpen);
        Assert.False(vm.IsLoading);
    }

    private sealed class Backend : INativeAuditHistoryReader
    {
        public List<NativeAuditHistoryRequest> Requests=[];
        public bool Forged;
        public Task<NativeAuditHistoryPage>? Pending;
        public Task<NativeAuditHistoryPage> ReadAuditHistoryAsync(NativeIdentity actor,NativeAuditHistoryRequest request,CancellationToken ct=default)
        {
            Requests.Add(request);
            var id=request.AuditEventId.Length==0?"AUD-1":request.AuditEventId;
            var rows=request.Page==1?Enumerable.Range(2,50).Reverse().Select(v=>Row(id,v)).ToArray():[Row(id)];
            return Pending??Task.FromResult(new NativeAuditHistoryPage(Forged?request with { AuditEventId="AUD-2" }:request,rows));
        }
    }
}
