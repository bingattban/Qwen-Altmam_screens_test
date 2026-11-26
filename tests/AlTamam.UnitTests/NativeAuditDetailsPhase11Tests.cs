using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAuditDetailsPhase11Tests
{
    private static readonly NativeIdentity Admin=new("USR-1","admin",UserRole.GeneralAdministration,"NONCE-A");
    private static NativeAuditRow Row(bool hidden=false,int version=0) =>
        new("AUD-1",1,"SITE_CREATE","إضافة موقع","admin","المركز","الوصف","الأصل","",
            "2026-01-01","2026-01-01T10:00:00Z","تم الاعتماد",new string('a',64),hidden,version);
    private static NativeAuditDetail Detail(NativeAuditRow row,bool chain=true) =>
        new(row.AuditEventId,row.OperationCode,row.OperationName,"USR-1","admin","",
            "Site","SITE-1",row.TargetDisplay,row.RawDescription,row.Reason,
            row.EffectiveLocalDate,row.ExecutedAtUtc,"COR-1",row.EventHash,"GENESIS",
            null,"{\"name\":\"المركز\"}",row.IsTombstoned,row.OverlayVersion,chain);

    [Fact]
    public void Detail_matches_only_exact_immutable_evidence_and_overlay_revision()
    {
        var row=Row();
        var detail=Detail(row,false);
        Assert.True(detail.Matches(row,Admin.Role));
        Assert.False(detail.ChainValid);
        Assert.False((detail with { AuditEventId="AUD-2" }).Matches(row,Admin.Role));
        Assert.False((detail with { EventHash=new string('b',64) }).Matches(row,Admin.Role));
        Assert.False((detail with { OverlayVersion=1 }).Matches(row,Admin.Role));
        Assert.False((detail with { RawDescription="مزور" }).Matches(row,Admin.Role));
        Assert.False((detail with { PriorEventHash="invalid" }).Matches(row,Admin.Role));
        var hidden=Row(true,1);
        Assert.True(Detail(hidden).Matches(hidden,Admin.Role));
        Assert.False(Detail(hidden).Matches(hidden,UserRole.Supervisor));
    }

    [Fact]
    public void Presentation_evidence_must_match_exact_list_description_revision_and_visibility()
    {
        var row=Row(true,2);
        var finalState=new NativeAuditEditState(2,true,row.Description);
        Assert.True(NativeAuditDetail.MatchesPresentation(row,finalState));
        Assert.False(NativeAuditDetail.MatchesPresentation(row,finalState with { Description=row.RawDescription }));
        Assert.False(NativeAuditDetail.MatchesPresentation(row,finalState with { Version=1 }));
        Assert.False(NativeAuditDetail.MatchesPresentation(row,finalState with { IsTombstoned=false }));
        var original=Row() with { Description="الأصل" };
        Assert.True(NativeAuditDetail.MatchesPresentation(original,new(0,false,original.RawDescription)));
    }

    [Fact]
    public async Task Detail_requires_reference_bound_row_and_preserves_list()
    {
        var reader=new Reader();
        var vm=new NativeAuditPageViewModel();
        vm.AttachReader(reader,()=>Admin);
        await vm.SearchAsync();
        var row=Assert.Single(vm.Rows);
        await vm.OpenDetailAsync(row with { });
        Assert.Equal(0,reader.Reads);
        await vm.OpenDetailAsync(row);
        Assert.Equal(1,reader.Reads);
        Assert.True(vm.IsDetailOpen);
        Assert.NotNull(vm.Detail);
        Assert.False(vm.Detail!.ChainValid);
        Assert.Same(row,Assert.Single(vm.Rows));
        vm.CloseDetail();
        Assert.Null(vm.Detail);
        Assert.False(vm.IsDetailOpen);
        Assert.Same(row,Assert.Single(vm.Rows));
        reader.Forged=true;
        await vm.OpenDetailAsync(row);
        Assert.Null(vm.Detail);
        Assert.NotEmpty(vm.DetailError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detail_discards_late_response_on_filter_edit_or_session_change(bool sessionChange)
    {
        NativeIdentity? actor=Admin;
        var reader=new Reader();
        var vm=new NativeAuditPageViewModel();
        vm.AttachReader(reader,()=>actor);
        await vm.SearchAsync();
        var row=Assert.Single(vm.Rows);
        var pending=new TaskCompletionSource<NativeAuditDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending=pending.Task;
        var task=vm.OpenDetailAsync(row);
        Assert.True(vm.IsDetailLoading);
        if(sessionChange) actor=Admin with { SessionNonce="NONCE-B" };
        else vm.Term="غير البحث";
        pending.SetResult(Detail(row));
        await task;
        Assert.Null(vm.Detail);
        Assert.False(vm.IsDetailLoading);
        Assert.False(vm.IsDetailOpen);
    }

    [Fact]
    public async Task Detail_clear_during_read_scrubs_all_evidence()
    {
        var reader=new Reader();
        var vm=new NativeAuditPageViewModel();
        vm.AttachReader(reader,()=>Admin);
        await vm.SearchAsync();
        var row=Assert.Single(vm.Rows);
        var pending=new TaskCompletionSource<NativeAuditDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        reader.Pending=pending.Task;
        var task=vm.OpenDetailAsync(row);
        vm.Clear();
        pending.SetResult(Detail(row));
        await task;
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Detail);
        Assert.False(vm.IsDetailOpen);
        Assert.False(vm.IsDetailLoading);
    }

    private sealed class Reader : INativeAuditReader, INativeAuditDetailReader
    {
        public int Reads;
        public bool Forged;
        public Task<NativeAuditDetail>? Pending;
        public Task<NativeAuditSnapshot> ReadAuditAsync(NativeIdentity actor,NativeAuditFilter filter,CancellationToken ct=default)
            =>Task.FromResult(new NativeAuditSnapshot(filter,1,false,[Row()]));
        public Task<NativeAuditDetail> ReadAuditDetailAsync(NativeIdentity actor,NativeAuditRow row,CancellationToken ct=default)
        {
            Reads++;
            return Pending??Task.FromResult(Forged?Detail(row) with { OverlayVersion=99 }:Detail(row,false));
        }
    }
}
