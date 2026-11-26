using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAccountListPhase11Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","owner",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeAccountRow Row(string id="USR-2",long index=1,UserRole role=UserRole.Supervisor,
        bool canModify=true)=>new(index,id,"user"+id,role,true,1,"اسم المستخدم","123",
            role==UserRole.GeneralAdministration?null:"PER-"+id,"owner","2026-01-01T10:00:00Z",canModify);
    [Fact]
    public void Account_page_rejects_scope_leaks_forged_permissions_and_duplicate_identity()
    {
        var request=new NativeAccountRequest();
        var ordinary=Admin with { Role=UserRole.OrdinaryAdministration };
        var page=new NativeAccountPage(request,1,false,[Row()]);
        Assert.True(page.Matches(request,ordinary));
        Assert.True(page.Matches(request,Admin));
        Assert.False(page.Matches(request,Admin with { Role=UserRole.Supervisor }));
        Assert.False((page with { Rows=[Row(role:UserRole.GeneralAdministration,canModify:false)] }).Matches(request,ordinary));
        Assert.False((page with { Rows=[Row(role:UserRole.OrdinaryAdministration)] }).Matches(request,ordinary));
        Assert.False((page with { Rows=[Row(canModify:false)] }).Matches(request,Admin));
        Assert.False((page with { Rows=[Row("USR-1")] }).Matches(request,Admin));
        Assert.False((page with { Total=2,Rows=[Row(),Row(index:2)] }).Matches(request,Admin));
        Assert.False((page with { HasNext=true }).Matches(request,Admin));
        Assert.False(page.Matches(request with { Term="غير" },Admin));
    }

    [Fact]
    public void Account_creation_roles_exclude_founder_and_follow_canonical_policy()
    {
        Assert.Equal(new[]{UserRole.OrdinaryAdministration,UserRole.Supervisor},NativeAccountRequest.AllowedCreationRoles(Admin.Role));
        Assert.Equal(new[]{UserRole.Supervisor},NativeAccountRequest.AllowedCreationRoles(UserRole.OrdinaryAdministration));
        Assert.Empty(NativeAccountRequest.AllowedCreationRoles(UserRole.Supervisor));
        Assert.False(new NativeAccountRequest(Page:0).IsValid);
        Assert.False(new NativeAccountRequest("bad\nterm").IsValid);
    }

    [Fact]
    public async Task Account_search_and_paging_require_explicit_requests()
    {
        var reader=new Reader();
        var vm=new NativeAccountsPageViewModel();
        vm.AttachReader(reader,()=>Admin);
        await vm.SearchAsync();
        Assert.Equal(50,vm.Rows.Count);
        Assert.True(vm.IsDataConnected);
        vm.Term=" أحمد ";
        Assert.Empty(vm.Rows);
        Assert.Single(reader.Requests);
        await vm.SearchAsync();
        Assert.Equal("أحمد",reader.Requests[^1].Term);
        await vm.NextAsync();
        Assert.Equal(2,vm.Page);
        Assert.Single(vm.Rows);
        Assert.False(vm.NextCommand.CanExecute(null));
        await vm.PreviousAsync();
        Assert.Equal(1,vm.Page);
    }

    [Fact]
    public async Task Account_page_denies_supervisor_and_fails_closed_on_forged_response()
    {
        var reader=new Reader();
        NativeIdentity? actor=Admin with { Role=UserRole.Supervisor };
        var vm=new NativeAccountsPageViewModel();
        vm.AttachReader(reader,()=>actor);
        await vm.SearchAsync();
        Assert.Empty(reader.Requests);
        Assert.Empty(vm.Rows);
        actor=Admin;
        reader.Forged=true;
        await vm.SearchAsync();
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);
        Assert.NotEmpty(vm.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Account_list_cannot_repaint_after_clear_or_session_change(bool sessionChange)
    {
        var reader=new Reader();
        NativeIdentity? actor=Admin;
        var vm=new NativeAccountsPageViewModel();
        vm.AttachReader(reader,()=>actor);
        var pending=new TaskCompletionSource<NativeAccountPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending=pending.Task;
        var task=vm.SearchAsync();
        Assert.True(vm.IsLoading);
        if(sessionChange) actor=Admin with { SessionNonce="NONCE-B" }; else vm.Clear();
        pending.SetResult(reader.Build(reader.Requests[0]));
        await task;
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);
        Assert.False(vm.IsLoading);
    }

    private sealed class Reader : INativeAccountReader
    {
        public List<NativeAccountRequest> Requests=[];
        public bool Forged;
        public Task<NativeAccountPage>? Pending;
        public NativeAccountPage Build(NativeAccountRequest request)
        {
            long offset=(long)(request.Page-1)*50;
            var rows=Enumerable.Range(0,(int)Math.Clamp(51-offset,0L,50L))
                .Select(i=>Row("USR-X"+(offset+i+1),offset+i+1)).ToArray();
            return new(Forged?request with { Term="forged" }:request,51,offset+50<51,rows);
        }
        public Task<NativeAccountPage> ReadAccountsAsync(NativeIdentity actor,NativeAccountRequest request,CancellationToken ct=default)
        {
            Requests.Add(request);
            return Pending??Task.FromResult(Build(request));
        }
    }
}
