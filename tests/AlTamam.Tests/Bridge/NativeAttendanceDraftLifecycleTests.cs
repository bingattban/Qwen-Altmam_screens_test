using System.Globalization;
using System.Text.Json.Nodes;
using AlTamam.Application.Bridge;
using AlTamam.Infrastructure.Persistence;
using AlTamam.Tests.Fixtures;
using Xunit;

namespace AlTamam.Tests.Bridge;

public sealed class NativeAttendanceDraftLifecycleTests : IAsyncLifetime, IDisposable
{
    private readonly string _path=SyntheticFixtures.CreateTempDatabasePath();
    private AlTamamDbContext _db=null!;
    private BridgeSharedServices _svc=null!;
    private const string Owner="TST-NATIVE-DRAFT-A";
    private const string Other="TST-NATIVE-DRAFT-B";
    private static string Today=>DateTime.Now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    private static string End=>DateTime.Now.AddDays(4).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);

    public async Task InitializeAsync()
    {
        _db=new AlTamamDbContext(_path,SyntheticFixtures.TestKeyHex,false);
        await _db.InitializeAsync();
        _svc=new BridgeSharedServices(_db);
    }
    private static JsonObject Payload(string status="STS-LEAVE",bool initial=true)=>new()
    {
        ["draftId"]="DRF-"+Guid.NewGuid().ToString("N"),["businessDate"]=Today,
        ["siteId"]="SITE-A",["targetStatusId"]=status,["selectedPersonIds"]=new JsonArray("TST-P1","TST-P2"),
        ["rangeStartDate"]=Today,["rangeEndDate"]=End,["rangeIsInitial"]=initial
    };
    private async Task<JsonArray> List(string owner)=>Assert.IsType<JsonArray>(
        (await _svc.ListNativeDraftsAsync(owner,CancellationToken.None)).Data);
    private long Count(string table)
    {
        using var c=_db.CreateOpenConnection();using var q=c.CreateCommand();
        q.CommandText="SELECT COUNT(*) FROM "+table;return Convert.ToInt64(q.ExecuteScalar());
    }

    [Theory]
    [InlineData("STS-LEAVE",true)]
    [InlineData("STS-PERMISSION",false)]
    public async Task NativeRange_ExactReceiptOwnerIsolationAndConditionalDelete(string status,bool initial)
    {
        var payload=Payload(status,initial);
        var counts=new[]{"AttendanceRecords","AttendanceDateRanges","AuditEvents"}.Select(Count).ToArray();
        var saved=await _svc.SaveNativeDraftAsync(Owner,payload,CancellationToken.None);
        Assert.True(saved.Success,saved.UserMessage);
        var row=Assert.IsType<JsonObject>(Assert.Single(await List(Owner)));
        Assert.Equal(payload["rangeStartDate"]!.GetValue<string>(),row["rangeStartDate"]!.GetValue<string>());
        Assert.Equal(payload["rangeEndDate"]!.GetValue<string>(),row["rangeEndDate"]!.GetValue<string>());
        Assert.Equal(initial,row["rangeIsInitial"]!.GetValue<bool>());
        Assert.Equal(Assert.IsType<JsonObject>(saved.Data)["savedAtUtc"]!.GetValue<string>(),row["updatedAtUtc"]!.GetValue<string>());
        Assert.Empty(await List(Other));
        Assert.Empty(Assert.IsType<JsonArray>((await _svc.ListDraftsAsync(Owner,CancellationToken.None)).Data));
        Assert.False((await _svc.SaveNativeDraftAsync(Owner,payload,CancellationToken.None)).Success);
        Assert.False((await _svc.SaveNativeDraftAsync(Other,payload,CancellationToken.None)).Success);
        Assert.False((await _svc.DeleteNativeReviewedDraftAsync(Other,row,CancellationToken.None)).Success);
        var stale=(JsonObject)row.DeepClone();stale["rangeIsInitial"]=!initial;
        Assert.False((await _svc.DeleteNativeReviewedDraftAsync(Owner,stale,CancellationToken.None)).Success);
        stale=(JsonObject)row.DeepClone();stale["rangeEndDate"]=Today;
        Assert.False((await _svc.DeleteNativeReviewedDraftAsync(Owner,stale,CancellationToken.None)).Success);
        Assert.Single(await List(Owner));
        Assert.True((await _svc.DeleteNativeReviewedDraftAsync(Owner,row,CancellationToken.None)).Success);
        Assert.Empty(await List(Owner));
        Assert.Equal(counts,new[]{"AttendanceRecords","AttendanceDateRanges","AuditEvents"}.Select(Count).ToArray());
    }

    [Theory]
    [InlineData("missing-mode")]
    [InlineData("missing-end")]
    [InlineData("old-date")]
    [InlineData("wrong-status")]
    [InlineData("whitespace")]
    [InlineData("duplicates")]
    public async Task InvalidNativeEvidence_CannotBeSaved(string reason)
    {
        var p=Payload();
        switch(reason)
        {
            case "missing-mode":p["rangeIsInitial"]=null;break;
            case "missing-end":p["rangeEndDate"]=null;break;
            case "old-date":p["businessDate"]="2020-01-01";break;
            case "wrong-status":p["targetStatusId"]="STS-PRESENT";break;
            case "whitespace":p["rangeStartDate"]=" "+Today;break;
            case "duplicates":p["selectedPersonIds"]=new JsonArray("TST-P1","TST-P1");break;
        }
        Assert.False((await _svc.SaveNativeDraftAsync(Owner,p,CancellationToken.None)).Success);
        Assert.Empty(await List(Owner));
    }

    [Fact]
    public async Task OrdinaryAndLegacyIncompleteDraftsRemainVisibleWithoutInventingRangeEvidence()
    {
        var p=Payload("STS-PRESENT");p["rangeStartDate"]=null;p["rangeEndDate"]=null;p["rangeIsInitial"]=null;
        Assert.True((await _svc.SaveNativeDraftAsync(Owner,p,CancellationToken.None)).Success);
        var legacy=Payload();legacy.Remove("rangeStartDate");legacy.Remove("rangeEndDate");legacy.Remove("rangeIsInitial");
        Assert.True((await _svc.SaveDraftAsync(Owner,legacy,CancellationToken.None)).Success);
        var rows=await List(Owner);
        Assert.Equal(2,rows.Count);
        foreach(var row in rows)
        {
            Assert.Null(row!["rangeStartDate"]);Assert.Null(row["rangeEndDate"]);Assert.Null(row["rangeIsInitial"]);
        }
        Assert.Equal(2,Assert.IsType<JsonArray>((await _svc.ListDraftsAsync(Owner,CancellationToken.None)).Data).Count);
    }

    [Fact]
    public async Task LegacyJsonFormatting_DoesNotPreventExactNativeDeletion()
    {
        var payload=Payload("STS-PRESENT");
        payload.Remove("rangeStartDate");payload.Remove("rangeEndDate");payload.Remove("rangeIsInitial");
        Assert.True((await _svc.SaveDraftAsync(Owner,payload,CancellationToken.None)).Success);
        using(var connection=_db.CreateOpenConnection())
        using(var cmd=connection.CreateCommand())
        {
            cmd.CommandText="UPDATE AttendanceDrafts SET SelectedPersonIdsJson=$json WHERE DraftId=$id;";
            cmd.Parameters.AddWithValue("$json","""[ "TST-P1", "TST-P2" ]""");
            cmd.Parameters.AddWithValue("$id",payload["draftId"]!.GetValue<string>());
            await cmd.ExecuteNonQueryAsync();
        }
        var row=Assert.IsType<JsonObject>(Assert.Single(await List(Owner)));
        var stale=(JsonObject)row.DeepClone();
        stale["selectedPersonIds"]=new JsonArray("TST-P2","TST-P1");
        Assert.False((await _svc.DeleteNativeReviewedDraftAsync(Owner,stale,CancellationToken.None)).Success);
        stale["selectedPersonIds"]=new JsonArray("TST-P1","TST-OTHER");
        Assert.False((await _svc.DeleteNativeReviewedDraftAsync(Owner,stale,CancellationToken.None)).Success);
        Assert.Single(await List(Owner));
        Assert.True((await _svc.DeleteNativeReviewedDraftAsync(Owner,row,CancellationToken.None)).Success);
        Assert.Empty(await List(Owner));
    }

    [Fact]
    public async Task OversizedNativeCatalogue_FailsClosedWithoutPartialRows()
    {
        using(var connection=_db.CreateOpenConnection())
        using(var cmd=connection.CreateCommand())
        {
            cmd.CommandText="""
                WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<10001)
                INSERT INTO AttendanceDrafts(DraftId,BusinessDate,TargetStatusId,
                    SelectedPersonIdsJson,CreatedByUserId,UpdatedAtUtc)
                SELECT 'DRF-'||printf('%032x',i),'2026-11-27','STS-PRESENT',
                    '["TST-P1"]',$owner,'2026-11-27T08:00:00.0000000+00:00' FROM n;
                """;
            cmd.Parameters.AddWithValue("$owner",Owner);
            await cmd.ExecuteNonQueryAsync();
        }
        var result=await _svc.ListNativeDraftsAsync(Owner,CancellationToken.None);
        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Empty(await List(Other));
        Assert.Equal(10001L,Count("AttendanceDrafts"));
    }

    public Task DisposeAsync()=>Task.CompletedTask;
    public void Dispose(){_db?.Dispose();SyntheticFixtures.DeleteTempDatabase(_path);}
}
