using System.Text.Json.Nodes;
using AlTamam.Application.Bridge;
using AlTamam.Contracts.Bridge;
using Xunit;

namespace AlTamam.UnitTests;

public sealed class NativeIndividualExcelSourcePhase9Tests
{
    private static readonly (string Rows, string Prefix, int Total)[] Streams =
    [
        ("attendance", "attendance", 201), ("identityHistory", "identity", 0),
        ("movements", "movement", 101), ("revisions", "revision", 1),
        ("audit", "audit", 100), ("siteHistory", "site", 2)
    ];

    private static JsonObject Request() => new()
    {
        ["personId"]="PER-001", ["dateFrom"]="2026-01-01",
        ["dateTo"]="2026-12-31", ["siteId"]="SITE-1",
        ["statusId"]="STS-1", ["movementType"]=2
    };

    private static JsonObject Page(JsonObject request)
    {
        var result = new JsonObject
        {
            ["person"]=new JsonObject { ["personId"]="PER-001", ["name"]="اسم" },
            ["dateFrom"]=request["dateFrom"]!.DeepClone(),
            ["dateTo"]=request["dateTo"]!.DeepClone(),
            ["siteId"]=request["siteId"]!.DeepClone(),
            ["statusId"]=request["statusId"]!.DeepClone(),
            ["movementType"]=request["movementType"]!.DeepClone(),
            ["pageSize"]=100,
            ["statusTotals"]=new JsonArray(new JsonObject { ["count"]=201 }),
            ["monthlyTotals"]=new JsonArray(new JsonObject { ["count"]=201 })
        };
        foreach (var (rows, prefix, total) in Streams)
        {
            int p = request[prefix+"Page"]!.GetValue<int>();
            int start = (p-1)*100;
            var array = new JsonArray();
            for (int i = start; i < Math.Min(start+100,total); ++i)
                array.Add(new JsonObject { ["fixture"]=i, ["value"]="evidence" });
            result[rows]=array;
            result[prefix+"Total"]=total;
            result[prefix+"Page"]=p;
            result[prefix+"HasNext"]=p*100<total;
        }
        return result;
    }

    private static BridgeResponse Ok(JsonObject value) => new("",true,value,null,null);

    [Fact]
    public async Task Collects_every_stream_and_checks_full_second_read()
    {
        int reads=0, authorizations=0;
        var source = await PersonnelIndividualExcelSource.CollectAsync(Request(),
            (request, ct) => { reads++; return Task.FromResult(Ok(Page(request))); },
            ct => { authorizations++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(6, reads);
        Assert.True(authorizations >= reads*2);
        foreach (var (rows, _, total) in Streams)
        {
            var array=source[rows]!.AsArray();
            Assert.Equal(total,array.Count);
            Assert.Equal(Enumerable.Range(0,total),array.Select(r=>r!["fixture"]!.GetValue<int>()));
        }
        Assert.Equal(64,PersonnelIndividualExcelSource.Fingerprint(source).Length);
    }

    [Fact]
    public async Task Collects_current_custom_metadata_and_binds_it_to_fingerprint()
    {
        JsonObject WithCustom(JsonObject request)
        {
            var data = Page(request);
            data["customColumns"] = new JsonArray(
                new JsonObject { ["columnId"]="FIELD-RANK", ["caption"]="الرتبة" });
            data["person"]!["customFields"] = new JsonObject { ["FIELD-RANK"]="رقيب" };
            return data;
        }
        var source = await PersonnelIndividualExcelSource.CollectAsync(Request(),
            (request,ct) => Task.FromResult(Ok(WithCustom(request))),
            _=>Task.CompletedTask,CancellationToken.None);
        Assert.Equal("FIELD-RANK", source["customColumns"]![0]!["columnId"]!.GetValue<string>());
        string fingerprint = PersonnelIndividualExcelSource.Fingerprint(source);
        source["customColumns"]![0]!["caption"] = "رتبة جديدة";
        Assert.NotEqual(fingerprint, PersonnelIndividualExcelSource.Fingerprint(source));
        int reads = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PersonnelIndividualExcelSource.CollectAsync(Request(), (request,ct) =>
            {
                var data = WithCustom(request);
                if (++reads == 5) data["customColumns"]![0]!["caption"] = "رتبة جديدة";
                return Task.FromResult(Ok(data));
            }, _=>Task.CompletedTask,CancellationToken.None));
    }

    [Theory]
    [InlineData("person")]
    [InlineData("filter")]
    [InlineData("count")]
    [InlineData("page")]
    [InlineData("flag")]
    [InlineData("partial")]
    [InlineData("late-drift")]
    [InlineData("summary")]
    public async Task Rejects_inconsistent_canonical_evidence(string fault)
    {
        int reads=0;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PersonnelIndividualExcelSource.CollectAsync(Request(),(request,ct) =>
            {
                var data=Page(request);
                reads++;
                if (fault=="person") data["person"]!["personId"]="OTHER";
                if (fault=="filter") data["siteId"]="OTHER";
                if (fault=="count" && reads==2) data["movementTotal"]=102;
                if (fault=="page") data["attendancePage"]=99;
                if (fault=="flag") data["attendanceHasNext"]=false;
                if (fault=="partial") data["attendance"]!.AsArray().RemoveAt(0);
                if (fault=="late-drift" && reads==5) data["attendance"]![0]!["value"]="changed";
                if (fault=="summary") data["monthlyTotals"]![0]!["count"]=200;
                return Task.FromResult(Ok(data));
            },_=>Task.CompletedTask,CancellationToken.None));
    }

    [Fact]
    public async Task Denial_after_first_source_read_prevents_any_more_reads()
    {
        int reads=0, checks=0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PersonnelIndividualExcelSource.CollectAsync(Request(),
                (request,ct) => { reads++; return Task.FromResult(Ok(Page(request))); },
                ct => ++checks == 2 ? Task.FromException(new UnauthorizedAccessException()) : Task.CompletedTask,
                CancellationToken.None));
        Assert.Equal(1,reads);
    }

    [Fact]
    public async Task Invalid_request_or_cancelled_token_never_reads_source()
    {
        int reads=0;
        Task<BridgeResponse> Read(JsonObject request,CancellationToken ct)
        { reads++; return Task.FromResult(Ok(Page(request))); }
        var invalid=Request();
        invalid["dateFrom"]="2026-02-30";
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PersonnelIndividualExcelSource.CollectAsync(invalid,Read,_=>Task.CompletedTask,CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PersonnelIndividualExcelSource.CollectAsync(Request(),Read,_=>Task.CompletedTask,new CancellationToken(true)));
        Assert.Equal(0,reads);
    }
}
