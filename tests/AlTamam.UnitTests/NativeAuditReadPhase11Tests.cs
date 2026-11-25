using AlTamam.Domain.Enums;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeAuditReadPhase11Tests
{
    [Theory]
    [InlineData(UserRole.GeneralAdministration,true)]
    [InlineData(UserRole.OrdinaryAdministration,true)]
    [InlineData(UserRole.Supervisor,false)]
    public void Hidden_audit_entries_require_administration(UserRole role,bool canSeeHidden)
    {
        Assert.True(new NativeAuditFilter().IsAuthorized(role));
        Assert.Equal(canSeeHidden,new NativeAuditFilter(IncludeTombstoned:true).IsAuthorized(role));
    }

    [Fact]
    public void Audit_filter_preserves_bounds_and_rejects_invalid_dates()
    {
        Assert.True(new NativeAuditFilter(Term:"أحمد",FromDate:"2026-01-01",ToDate:"2026-02-28").IsValid);
        Assert.False(new NativeAuditFilter(FromDate:"2026-02-30").IsValid);
        Assert.False(new NativeAuditFilter(FromDate:"2026-03-01",ToDate:"2026-02-28").IsValid);
        Assert.False(new NativeAuditFilter(Page:0).IsValid);
        Assert.False(new NativeAuditFilter(Page:100001).IsValid);
        Assert.False(new NativeAuditFilter(Term:new string('a',201)).IsValid);
        Assert.False(new NativeAuditFilter(Actor:"bad\nactor").IsValid);
    }

    private static NativeAuditRow Row(string id,long seq,bool hidden=false,int version=0) =>
        new(id,seq,"SITE_CREATE","إضافة موقع","admin","المركز","وصف العرض","الوصف الأصلي","",
            "2026-01-01","2026-01-01T10:00:00Z","تم الاعتماد",new string('a',64),hidden,version);

    [Fact]
    public void Audit_snapshot_rejects_duplicate_rows_wrong_sequence_and_hidden_leaks()
    {
        var filter=new NativeAuditFilter();
        var page=new NativeAuditSnapshot(filter,2,false,[Row("AUD-1",1),Row("AUD-2",2)]);
        Assert.True(page.Matches(filter));
        Assert.False((page with { Rows=[Row("AUD-1",1),Row("AUD-1",2)] }).Matches(filter));
        Assert.False((page with { Rows=[Row("AUD-1",2),Row("AUD-2",3)] }).Matches(filter));
        Assert.False((page with { Rows=[Row("AUD-1",1,true,1),Row("AUD-2",2)] }).Matches(filter));
        Assert.False((page with { Rows=[Row("AUD-1",1,false,-1),Row("AUD-2",2)] }).Matches(filter));
        Assert.False((page with { HasNext=true }).Matches(filter));
        Assert.False(page.Matches(filter with { Term="مختلف" }));
        var privileged=filter with { IncludeTombstoned=true };
        Assert.True((page with { Filter=privileged, Rows=[Row("AUD-1",1,true,1),Row("AUD-2",2)] }).Matches(privileged));
    }

    [Fact]
    public void Audit_snapshot_checks_actual_page_size_and_canonical_offset()
    {
        var filter=new NativeAuditFilter(Page:2);
        var page=new NativeAuditSnapshot(filter,51,false,[Row("AUD-51",51)]);
        Assert.True(page.Matches(filter));
        Assert.False((page with { Total=52 }).Matches(filter));
        Assert.False((page with { Rows=[] }).Matches(filter));
        Assert.True(new NativeAuditSnapshot(filter,0,false,[]).Matches(filter));
    }
}
