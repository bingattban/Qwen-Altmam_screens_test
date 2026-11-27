using System.IO;
using AlTamam.NativePresentation;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeMonthlyExportReceiptPhase12Tests
{
    private static string Folder=>Path.GetFullPath(Path.Combine(Path.GetTempPath(),"altamam-export-receipt"));
    private static string Primary=>Path.Combine(Folder,"combined.xlsx");
    private static string Site=>Path.Combine(Folder,"site.xlsx");
    private static NativeMonthlyExcelFilter Filter=>new("2026-11","",""){OutputDirectory=Folder,OutputFileName="combined.xlsx"};
    private static NativeMonthlyExcelReceipt Receipt()=>new("2026-11","","",Primary,2,5)
    {
        Files=[new("","جميع المواقع",Primary,5),new("SITE-A","المركز",Site,3)]
    };

    [Fact]
    public void Complete_set_preserves_order_and_counts_without_summing_overlapping_memberships()
    {
        var files=NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(Receipt(),Filter);
        Assert.Equal(2,files.Count);
        Assert.Equal("",files[0].SiteId);
        Assert.Equal(5,files[0].PersonCount);
        Assert.Equal("SITE-A",files[1].SiteId);
        Assert.Equal(3,files[1].PersonCount);
        Assert.Equal(Site,files[1].FilePath);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("count")]
    [InlineData("duplicate-site")]
    [InlineData("duplicate-path")]
    [InlineData("outside")]
    [InlineData("relative")]
    [InlineData("wrong-primary")]
    [InlineData("wrong-total")]
    [InlineData("empty-site")]
    [InlineData("zero")]
    [InlineData("bad-name")]
    [InlineData("wrong-extension")]
    public void Inconsistent_set_fails_closed(string reason)
    {
        var receipt=Receipt();
        var rows=receipt.Files!.ToArray();
        switch(reason)
        {
            case "missing":receipt=receipt with{Files=null};break;
            case "count":receipt=receipt with{FileCount=3};break;
            case "duplicate-site":rows[1]=rows[1] with{SiteId=""};break;
            case "duplicate-path":rows[1]=rows[1] with{FilePath=Primary};break;
            case "outside":rows[1]=rows[1] with{FilePath=Path.Combine(Path.GetTempPath(),"foreign.xlsx")};break;
            case "relative":rows[1]=rows[1] with{FilePath="site.xlsx"};break;
            case "wrong-primary":rows[0]=rows[0] with{FilePath=Path.Combine(Folder,"other.xlsx")};break;
            case "wrong-total":rows[0]=rows[0] with{PersonCount=4};break;
            case "empty-site":rows[1]=rows[1] with{SiteId=" "};break;
            case "zero":rows[1]=rows[1] with{PersonCount=0};break;
            case "bad-name":rows[1]=rows[1] with{SiteName=""};break;
            case "wrong-extension":rows[1]=rows[1] with{FilePath=Path.Combine(Folder,"site.pdf")};break;
        }
        if(reason!="missing")receipt=receipt with{Files=rows};
        Assert.Throws<InvalidDataException>(()=>NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(receipt,Filter));
    }

    [Fact]
    public void Single_site_receipt_requires_exact_site_and_one_file()
    {
        var filter=Filter with{SiteId="SITE-A"};
        var receipt=new NativeMonthlyExcelReceipt(filter.Period,filter.SiteId,"",Primary,1,3)
        {Files=[new("SITE-A","المركز",Primary,3)]};
        Assert.Single(NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(receipt,filter));
        Assert.Throws<InvalidDataException>(()=>NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(
            receipt with{Files=[new("SITE-B","موقع آخر",Primary,3)]},filter));
    }

    [Fact]
    public void Frozen_receipt_does_not_alias_mutable_backend_list()
    {
        var source=Receipt().Files!.ToList();
        var result=NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(Receipt() with{Files=source},Filter);
        source.Clear();
        Assert.Equal(2,result.Count);
    }
}
