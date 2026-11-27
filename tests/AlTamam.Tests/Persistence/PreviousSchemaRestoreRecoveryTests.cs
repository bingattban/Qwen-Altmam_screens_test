using System.Security.Cryptography;
using System.Text.Json;
using AlTamam.Infrastructure.Persistence;
using AlTamam.Infrastructure.Persistence.Migrations;
using AlTamam.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AlTamam.Tests.Persistence;

[Collection("SQLCipherGlobalPool")]
public sealed class PreviousSchemaRestoreRecoveryTests : IDisposable
{
    private readonly string _live=SyntheticFixtures.CreateTempDatabasePath();
    private string Root=>Path.GetDirectoryName(_live)!;
    private static string Key=>SyntheticFixtures.TestKeyHex;
    private static int Current=>AlTamamDbContext.CurrentSchemaVersion;
    private const string Id="1234567890abcdef1234567890abcdef";
    private string Stage=>Path.Combine(Root,".altamam-restore-stage-"+Id+".dbbackup");
    private string Rollback=>Path.Combine(Root,".altamam-restore-rollback-"+Id+".dbbackup");
    private string Rescue=>Path.Combine(Root,"backups","rescue-at-activation-"+Id+".dbbackup");
    private string Journal=>Path.Combine(Root,".altamam-restore-activation.pending.json");

    private static string Digest(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private async Task MakeBackupAsync(string path,int version,string marker,bool tamper=false)
    {
        string seed=Path.Combine(Root,Guid.NewGuid().ToString("N")+".db");
        using var db=new AlTamamDbContext(seed,Key,false);
        using(var c=db.CreateOpenConnection())
        {
            await new SchemaMigrationRunner(AlTamamDbContext.CanonicalMigrations.Take(version).ToArray()).UpgradeAsync(c);
            using var q=c.CreateCommand();
            q.CommandText="INSERT INTO AppSettings(SettingKey,SettingValue,UpdatedAtUtc) VALUES('LegacyRecoveryMarker',$marker,'2026-11-27T00:00:00Z');";
            q.Parameters.AddWithValue("$marker",marker);
            await q.ExecuteNonQueryAsync();
            if(tamper)
            {
                q.CommandText="UPDATE SchemaVersions SET Sha256='tampered' WHERE SchemaVersion=2;";
                await q.ExecuteNonQueryAsync();
            }
        }
        await db.CreateVerifiedEncryptedBackupAsync(path);
    }

    private async Task WriteLegacyJournalAsync(int version,bool tamper=false)
    {
        await MakeBackupAsync(Rollback,version,"old",tamper);
        await MakeBackupAsync(Stage,version,"new",tamper);
        Directory.CreateDirectory(Path.GetDirectoryName(Rescue)!);
        File.Copy(Rollback,Rescue);
        // Match the existing on-disk legacy intent shape: no version field.
        await File.WriteAllTextAsync(Journal,JsonSerializer.Serialize(new
        {
            Live=_live,Stage,Rollback,Rescue,OldDigest=Digest(Rollback),NewDigest=Digest(Stage)
        }));
    }

    [Theory]
    [InlineData("before",OfflineRestoreSwitch.RecoveryOutcome.OriginalRetained)]
    [InlineData("after",OfflineRestoreSwitch.RecoveryOutcome.ReplacementVerified)]
    [InlineData("missing",OfflineRestoreSwitch.RecoveryOutcome.OriginalRecovered)]
    public async Task PendingPreviousSchema_ReconcilesExactBytesBeforeMigration(string state,OfflineRestoreSwitch.RecoveryOutcome expected)
    {
        await WriteLegacyJournalAsync(Current-1);
        if(state!="missing") File.Copy(state=="before"?Rollback:Stage,_live);
        string old=Digest(Rollback),next=Digest(Stage),rescue=Digest(Rescue);
        var service=new OfflineRestoreSwitch();
        Assert.Equal(expected,await service.ReconcileAtStartupAsync(_live,Key));
        Assert.Equal(state=="after"?next:old,Digest(_live));
        Assert.Equal(old,Digest(Rollback));
        Assert.Equal(rescue,Digest(Rescue));
        Assert.False(File.Exists(Journal));
        foreach(string suffix in new[]{"-wal","-shm","-journal"})
            Assert.False(File.Exists(_live+suffix));
        Assert.Empty(Directory.GetFiles(Root,".altamam-restore-reconcile-*.dbbackup"));
        // Only after successful journal reconciliation may normal startup
        // migrate the recovered live database, using its usual encrypted rescue.
        using var db=new AlTamamDbContext(_live,Key,false);
        using(var c=db.CreateOpenConnection())
            Assert.Equal(Current-1,SchemaMigrationRunner.GetCurrentVersion(c));
        await db.InitializeAsync();
        using(var c=db.CreateOpenConnection())
        using(var q=c.CreateCommand())
        {
            Assert.Equal(Current,SchemaMigrationRunner.GetCurrentVersion(c));
            q.CommandText="SELECT SettingValue FROM AppSettings WHERE SettingKey='LegacyRecoveryMarker';";
            Assert.Equal(state=="after"?"new":"old",q.ExecuteScalar()?.ToString());
        }
        Assert.Equal(old,Digest(Rollback));
        Assert.Equal(rescue,Digest(Rescue));
    }

    [Theory]
    [InlineData(1,true)]
    [InlineData(2,false)]
    public async Task UntrustedOrTooOldRecovery_LeavesJournalAndFilesUntouched(int age,bool tamper)
    {
        await WriteLegacyJournalAsync(Current-age,tamper);
        File.Copy(Stage,_live);
        string live=Digest(_live),old=Digest(Rollback),rescue=Digest(Rescue);
        await Assert.ThrowsAsync<InvalidDataException>(()=>new OfflineRestoreSwitch().ReconcileAtStartupAsync(_live,Key));
        Assert.True(File.Exists(Journal));
        Assert.Equal(live,Digest(_live));
        Assert.Equal(old,Digest(Rollback));
        Assert.Equal(rescue,Digest(Rescue));
    }

    [Fact]
    public async Task PreviousSchemaCannotEnterNewActivation()
    {
        await MakeBackupAsync(Stage,Current-1,"old");
        using var db=new AlTamamDbContext(_live,Key,false);
        await db.InitializeAsync();
        var prepared=new StagedRestorePreparation(Stage,Digest(Stage),Stage,Digest(Stage),Current-1,0,0);
        Assert.False(await OfflineRestoreSwitch.CanUsePreparedKeyAsync(_live,Key,prepared));
        await Assert.ThrowsAsync<InvalidDataException>(()=>new OfflineRestoreSwitch().ActivateOfflineAsync(_live,Key,prepared));
        Assert.False(File.Exists(Journal));
        Assert.False(Directory.Exists(Path.Combine(Root,"backups")));
        Assert.True(db.HasValidIntegrity());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        SyntheticFixtures.DeleteTempDatabase(_live);
    }
}
