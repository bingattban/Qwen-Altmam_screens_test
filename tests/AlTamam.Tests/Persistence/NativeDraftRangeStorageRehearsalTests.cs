using AlTamam.Infrastructure.Persistence;
using AlTamam.Infrastructure.Persistence.Migrations;
using AlTamam.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AlTamam.Tests.Persistence;

/// <summary>
/// Verify the registered V017 migration on disposable real SQLCipher databases.
/// Exercise fresh initialization and migration of actual V016 encrypted copies.
/// </summary>
public sealed class NativeDraftRangeStorageRehearsalTests : IDisposable
{
    private readonly string _path = SyntheticFixtures.CreateTempDatabasePath();
    private const string DraftId = "TST-LEGACY-DRAFT";
    private const string Owner = "TST-DRAFT-OWNER";
    private const string Day = "2026-11-27";

    // Historical source is created from the released canonical V001–V016,
    // not from the current database and not by downgrading a newer file.
    private static async Task InitializeV016Async(AlTamamDbContext db)
    {
        using var c=db.CreateOpenConnection();
        await new SchemaMigrationRunner(AlTamamDbContext.CanonicalMigrations.Take(16).ToArray())
            .UpgradeAsync(c);
        Assert.Equal(16,SchemaMigrationRunner.GetCurrentVersion(c));
    }

    private static async Task InsertAsync(AlTamamDbContext db, string id, string status,
        string? start, string? end, int? initial)
    {
        using var c = db.CreateOpenConnection();
        using var q = c.CreateCommand();
        q.CommandText = """
            INSERT INTO AttendanceDrafts(DraftId,BusinessDate,SiteId,TargetStatusId,
                SelectedPersonIdsJson,CreatedByUserId,UpdatedAtUtc,
                RangeStartDate,RangeEndDate,RangeIsInitial)
            VALUES($id,$day,NULL,$status,'["TST-P1"]',$owner,
                '2026-11-27T08:00:00.0000000+00:00',$start,$end,$initial);
            """;
        q.Parameters.AddWithValue("$id",id);
        q.Parameters.AddWithValue("$day",Day);
        q.Parameters.AddWithValue("$status",status);
        q.Parameters.AddWithValue("$owner",Owner);
        q.Parameters.AddWithValue("$start",(object?)start ?? DBNull.Value);
        q.Parameters.AddWithValue("$end",(object?)end ?? DBNull.Value);
        q.Parameters.AddWithValue("$initial",(object?)initial ?? DBNull.Value);
        await q.ExecuteNonQueryAsync();
    }

    private static long Count(AlTamamDbContext db,string sql)
    {
        using var c=db.CreateOpenConnection();
        using var q=c.CreateCommand();
        q.CommandText=sql;
        return Convert.ToInt64(q.ExecuteScalar());
    }

    [Fact]
    public async Task EncryptedCopy_UpgradePreservesLegacyAndLeavesOriginalSchemaUntouched()
    {
        using var original = new AlTamamDbContext(_path,SyntheticFixtures.TestKeyHex,false);
        await InitializeV016Async(original);
        using(var c=original.CreateOpenConnection())
        using(var q=c.CreateCommand())
        {
            q.CommandText="""
                INSERT INTO AttendanceDrafts(DraftId,BusinessDate,SiteId,TargetStatusId,
                    SelectedPersonIdsJson,CreatedByUserId,UpdatedAtUtc)
                VALUES($id,$day,NULL,'STS-LEAVE','["TST-P1","TST-P2"]',$owner,
                    '2026-11-27T08:00:00.0000000+00:00');
                """;
            q.Parameters.AddWithValue("$id",DraftId);
            q.Parameters.AddWithValue("$day",Day);
            q.Parameters.AddWithValue("$owner",Owner);
            await q.ExecuteNonQueryAsync();
            Assert.Equal(16,SchemaMigrationRunner.GetCurrentVersion(c));
        }
        string backup=Path.Combine(Path.GetDirectoryName(_path)!,"v016-original.dbbackup");
        await original.CreateVerifiedEncryptedBackupAsync(backup);
        var before=await original.InspectEncryptedBackupAsync(backup);
        string copy=Path.Combine(Path.GetDirectoryName(_path)!,"v017-rehearsal.db");
        File.Copy(backup,copy);
        using var staged=new AlTamamDbContext(copy,SyntheticFixtures.TestKeyHex,false);
        await staged.InitializeAsync();
        Assert.Equal(17,AlTamamDbContext.CurrentSchemaVersion);
        using(var migrated=staged.CreateOpenConnection())
        {
            Assert.Equal(17,SchemaMigrationRunner.GetCurrentVersion(migrated));
            new SchemaMigrationRunner(AlTamamDbContext.CanonicalMigrations).VerifyRecordedHashes(migrated);
        }
        string rescueFolder=copy+".migration-backups";
        string rescue=Assert.Single(Directory.GetFiles(rescueFolder,"pre-migration-v016-to-v017-*.db"));
        Assert.Equal(16,(await staged.InspectEncryptedBackupAsync(rescue)).SchemaVersion);
        Assert.Equal(1L,Count(staged,"""
            SELECT COUNT(*) FROM AttendanceDrafts WHERE DraftId='TST-LEGACY-DRAFT'
              AND CreatedByUserId='TST-DRAFT-OWNER' AND BusinessDate='2026-11-27'
              AND TargetStatusId='STS-LEAVE' AND SiteId IS NULL
              AND SelectedPersonIdsJson='["TST-P1","TST-P2"]'
              AND UpdatedAtUtc='2026-11-27T08:00:00.0000000+00:00'
              AND RangeStartDate IS NULL AND RangeEndDate IS NULL AND RangeIsInitial IS NULL;
            """));
        Assert.Equal(0L,Count(original,"SELECT COUNT(*) FROM pragma_table_info('AttendanceDrafts') WHERE name LIKE 'Range%';"));
        Assert.Equal(3L,Count(staged,"SELECT COUNT(*) FROM pragma_table_info('AttendanceDrafts') WHERE name LIKE 'Range%';"));
        foreach(string table in new[]{"Persons","AttendanceRecords","AuditEvents","Users","AttendanceDateRanges"})
            Assert.Equal(Count(original,$"SELECT COUNT(*) FROM {table};"),Count(staged,$"SELECT COUNT(*) FROM {table};"));
        Assert.True(staged.HasValidIntegrity());
        Assert.True(original.HasValidIntegrity());
        var after=await original.InspectEncryptedBackupAsync(backup);
        Assert.Equal(before.Sha256,after.Sha256);
        Assert.Equal(16,after.SchemaVersion);
    }

    [Theory]
    [InlineData("STS-LEAVE","2026-11-27",null,1)]
    [InlineData("STS-LEAVE",null,"2026-12-01",1)]
    [InlineData("STS-LEAVE","2026-11-27","2026-12-01",null)]
    [InlineData("STS-LEAVE","2026-11-27","2026-11-26",1)]
    [InlineData("STS-LEAVE","2026-11-26","2026-12-01",1)]
    [InlineData("STS-LEAVE","2026-11-27","2027-11-28",1)]
    [InlineData("STS-LEAVE","2026-11-27","2027-02-30",1)]
    [InlineData("STS-LEAVE","0000-11-27","0000-12-01",1)]
    [InlineData("STS-PERMISSION","2026-11-27","2026-12-01",2)]
    [InlineData("STS-PRESENT","2026-11-27","2026-12-01",1)]
    public async Task IncompleteOrInvalidRange_IsRejected(string status,string? start,string? end,int? initial)
    {
        using var db=new AlTamamDbContext(_path,SyntheticFixtures.TestKeyHex,false);
        await db.InitializeAsync();
        await Assert.ThrowsAsync<SqliteException>(()=>InsertAsync(db,DraftId,status,start,end,initial));
        Assert.Equal(0L,Count(db,"SELECT COUNT(*) FROM AttendanceDrafts;"));
    }

    [Theory]
    [InlineData("STS-LEAVE",1,"2027-11-27")]
    [InlineData("STS-PERMISSION",0,"2026-11-27")]
    public async Task CompleteEvidence_IsStoredExactlyAndCannotBeOverwritten(string status,int initial,string end)
    {
        using var db=new AlTamamDbContext(_path,SyntheticFixtures.TestKeyHex,false);
        await db.InitializeAsync();
        await InsertAsync(db,DraftId,status,Day,end,initial);
        using var c=db.CreateOpenConnection();
        using var q=c.CreateCommand();
        q.CommandText="SELECT RangeStartDate,RangeEndDate,RangeIsInitial FROM AttendanceDrafts WHERE DraftId=$id;";
        q.Parameters.AddWithValue("$id",DraftId);
        using(var row=await q.ExecuteReaderAsync())
        {
            Assert.True(await row.ReadAsync());
            Assert.Equal(Day,row.GetString(0));
            Assert.Equal(end,row.GetString(1));
            Assert.Equal(initial,row.GetInt32(2));
        }
        q.CommandText="UPDATE AttendanceDrafts SET SelectedPersonIdsJson='[]' WHERE DraftId=$id;";
        await Assert.ThrowsAsync<SqliteException>(()=>q.ExecuteNonQueryAsync());
        q.CommandText="UPDATE AttendanceDrafts SET RangeEndDate='2026-11-28' WHERE DraftId=$id;";
        await Assert.ThrowsAsync<SqliteException>(()=>q.ExecuteNonQueryAsync());
        await InsertAsync(db,"TST-LEGACY-NEW","STS-PRESENT",null,null,null);
        Assert.Equal(2L,Count(db,"SELECT COUNT(*) FROM AttendanceDrafts;"));
        Assert.Equal(0L,Count(db,"SELECT COUNT(*) FROM AttendanceDateRanges;"));
    }

    [Fact]
    public async Task YearZeroWithMatchingBusinessDate_IsRejected()
    {
        using var db=new AlTamamDbContext(_path,SyntheticFixtures.TestKeyHex,false);
        await db.InitializeAsync();
        using var c=db.CreateOpenConnection();
        using var q=c.CreateCommand();
        q.CommandText="""
            INSERT INTO AttendanceDrafts(DraftId,BusinessDate,SiteId,TargetStatusId,SelectedPersonIdsJson,
              CreatedByUserId,UpdatedAtUtc,RangeStartDate,RangeEndDate,RangeIsInitial)
            VALUES('TST-ZERO','0000-01-01',NULL,'STS-LEAVE','["TST-P1"]','TST-DRAFT-OWNER',
              '2026-11-27T08:00:00Z','0000-01-01','0000-01-02',1);
            """;
        await Assert.ThrowsAsync<SqliteException>(()=>q.ExecuteNonQueryAsync());
        Assert.Equal(0L,Count(db,"SELECT COUNT(*) FROM AttendanceDrafts;"));
    }

    public void Dispose()=>SyntheticFixtures.DeleteTempDatabase(_path);
}
