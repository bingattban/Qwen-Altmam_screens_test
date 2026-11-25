using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAuditListPhase11Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","admin",UserRole.GeneralAdministration,"NONCE-A");

    [Fact]
    public async Task Audit_list_reads_exact_filters_and_pages_only_on_explicit_request()
    {
        var reader=new Reader();
        var vm=new NativeAuditPageViewModel();
        vm.AttachReader(reader,()=>Admin);
        await vm.RefreshAsync();
        Assert.True(vm.IsDataConnected);
        Assert.Equal(50,vm.Rows.Count);
        vm.Term=" أحمد ";
        vm.Actor="admin";
        vm.Target="المركز";
        vm.Operation="SITE_CREATE";
        vm.FromDate="2026-01-01";
        vm.ToDate="2026-01-31";
        vm.IncludeTombstoned=true;
        Assert.Empty(vm.Rows);
        Assert.Single(reader.Requests);
        Assert.False(vm.NextCommand.CanExecute(null));
        await vm.SearchAsync();
        Assert.Equal(new NativeAuditFilter("أحمد","admin","المركز","SITE_CREATE","2026-01-01","2026-01-31",true),reader.Requests[^1]);
        Assert.True(vm.NextCommand.CanExecute(null));
        await vm.NextAsync();
        Assert.Equal(2,reader.Requests[^1].Page);
        Assert.Equal(51,Assert.Single(vm.Rows).Sequence);
        Assert.False(vm.NextCommand.CanExecute(null));
        await vm.PreviousAsync();
        Assert.Equal(1,reader.Requests[^1].Page);
    }

    [Fact]
    public async Task Audit_list_denies_hidden_scope_to_supervisor_and_rejects_bad_echo()
    {
        var reader=new Reader();
        var supervisor=Admin with { Role=UserRole.Supervisor };
        var vm=new NativeAuditPageViewModel();
        vm.AttachReader(reader,()=>supervisor);
        Assert.False(vm.CanIncludeTombstoned);
        vm.IncludeTombstoned=true;
        Assert.False(vm.IncludeTombstoned);
        await vm.SearchAsync();
        Assert.False(Assert.Single(reader.Requests).IncludeTombstoned);
        vm.FromDate="2026-02-30";
        await vm.SearchAsync();
        Assert.Single(reader.Requests);
        Assert.Empty(vm.Rows);
        Assert.NotEmpty(vm.Error);
        vm.FromDate="";
        reader.Forged=true;
        await vm.SearchAsync();
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);
        Assert.NotEmpty(vm.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audit_list_discards_pending_results_after_clear_or_identity_change(bool changeIdentity)
    {
        NativeIdentity? live=Admin;
        var reader=new Reader();
        var vm=new NativeAuditPageViewModel();
        vm.AttachReader(reader,()=>live);
        var pending=new TaskCompletionSource<NativeAuditSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending=pending.Task;
        var task=vm.RefreshAsync();
        Assert.True(vm.IsLoading);
        if(changeIdentity) live=Admin with { SessionNonce="NONCE-B" };
        else vm.Clear();
        pending.SetResult(reader.Build(reader.Requests[^1]));
        await task;
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task Audit_list_cannot_repaint_old_results_after_filter_edit()
    {
        var reader=new Reader();
        var vm=new NativeAuditPageViewModel();
        vm.AttachReader(reader,()=>Admin);
        var pending=new TaskCompletionSource<NativeAuditSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending=pending.Task;
        var task=vm.RefreshAsync();
        vm.Term="جديد";
        pending.SetResult(reader.Build(reader.Requests[0]));
        await task;
        Assert.Empty(vm.Rows);
        Assert.False(vm.IsDataConnected);
        Assert.False(vm.IsLoading);
        reader.Pending=null;
        await vm.SearchAsync();
        Assert.Equal("جديد",reader.Requests[^1].Term);
        Assert.NotEmpty(vm.Rows);
    }

    private sealed class Reader : INativeAuditReader
    {
        public List<NativeAuditFilter> Requests=[];
        public bool Forged;
        public Task<NativeAuditSnapshot>? Pending;
        public NativeAuditSnapshot Build(NativeAuditFilter filter)
        {
            long offset=(long)(filter.Page-1)*50;
            var rows=Enumerable.Range(0,(int)Math.Clamp(51-offset,0,50))
                .Select(i=>new NativeAuditRow("AUD-"+(offset+i+1),offset+i+1,"SITE_CREATE",
                    "إضافة موقع","admin","المركز","الوصف","الأصل","","2026-01-01",
                    "2026-01-01T10:00:00Z","تم الاعتماد",new string('a',64),false,0)).ToArray();
            return new(Forged?filter with { Term="forged" }:filter,51,offset+50<51,rows);
        }
        public Task<NativeAuditSnapshot> ReadAuditAsync(NativeIdentity actor,NativeAuditFilter filter,CancellationToken ct=default)
        {
            Requests.Add(filter);
            return Pending??Task.FromResult(Build(filter));
        }
    }
}
