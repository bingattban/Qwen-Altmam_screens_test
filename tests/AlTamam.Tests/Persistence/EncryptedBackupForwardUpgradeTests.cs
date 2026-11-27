using AlTamam.Infrastructure.Persistence;
using AlTamam.Infrastructure.Persistence.Migrations;
using AlTamam.Tests.Fixtures;
using System.Security.Cryptography;
using Xunit;

namespace AlTamam.Tests.Persistence;

public sealed class EncryptedBackupForwardUpgradeTests : IDisposable
{
    private readonly string _path=SyntheticFixtures.CreateTempDatabasePath();
    private string Folder=>Path.GetDirectoryName(_path)!;
    private string Source=>Path.Combine(Folder,"source.dbbackup");
    private string OutputFolder=>Path.Combine(Folder,"backup-forward-upgrades");
    private static string Key=>SyntheticFixtures.TestKeyHex;
    private static int Current=>AlTamamDbContext.CurrentSchemaVersion;

    private async Task CreateSourceAsync(int version,bool tamper=false)
    {
        using var db=new AlTamamDbContext(Path.Combine(Folder,"source-seed.db"),Key,false);
        using(var c=db.CreateOpenConnection())
        {
            await new SchemaMigrationRunner(AlTamamDbContext.CanonicalMigrations.Take(version).ToArray()).UpgradeAsync(c);
            using var q=c.CreateCommand();
            q.CommandText="""
                INSERT INTO AttendanceDrafts(DraftId,BusinessDate,SiteId,TargetStatusId,
                    SelectedPersonIdsJson,CreatedByUserId,UpdatedAtUtc)
                VALUES('TST-RANGE','2026-11-27',NULL,'STS-LEAVE','["TST-P1"]','TST-OWNER','2026-11-27T08:00:00+00:00');
                """;
            await q.ExecuteNonQueryAsync();
            if(tamper)
            {
                q.CommandText="UPDATE SchemaVersions SET Sha256='tampered' WHERE SchemaVersion=2;";
                await q.ExecuteNonQueryAsync();
            }
        }
        await db.CreateVerifiedEncryptedBackupAsync(Source);
    }
    private static string Digest(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private void NoOutput()
    {
        if(Directory.Exists(OutputFolder))
            Assert.Empty(Directory.EnumerateFileSystemEntries(OutputFolder));
    }

    [Fact]
    public async Task PreviousVersion_CopyUpgradesAndRetainsOriginalAndLiveData()
    {
        await CreateSourceAsync(Current-1);
        using var live=new AlTamamDbContext(_path,Key,false);
        await live.InitializeAsync();
        string original=Digest(Source);
        var result=await live.PrepareForwardUpgradedBackupAsync(Source,original);
        Assert.Equal(Current-1,result.SourceSchemaVersion);
        Assert.Equal(original,result.SourceSha256);
        Assert.Equal(Current,result.Upgraded.SchemaVersion);
        Assert.Equal(result.Upgraded.Sha256,Digest(result.Upgraded.FullPath));
        Assert.Equal(original,Digest(Source));
        Assert.NotEqual(Source,result.Upgraded.FullPath);
        Assert.Equal(OutputFolder,Path.GetDirectoryName(result.Upgraded.FullPath));
        using(var upgraded=new AlTamamDbContext(result.Upgraded.FullPath,Key,false))
        using(var c=upgraded.CreateOpenConnection())
        using(var q=c.CreateCommand())
        {
            new SchemaMigrationRunner(AlTamamDbContext.CanonicalMigrations).VerifyRecordedHashes(c);
            q.CommandText="SELECT COUNT(*) FROM AttendanceDrafts WHERE DraftId='TST-RANGE' AND CreatedByUserId='TST-OWNER' AND TargetStatusId='STS-LEAVE';";
            Assert.Equal(1L,Convert.ToInt64(q.ExecuteScalar()));
        }
        using(var c=live.CreateOpenConnection())
        using(var q=c.CreateCommand())
        {
            q.CommandText="SELECT COUNT(*) FROM AttendanceDrafts;";
            Assert.Equal(0L,Convert.ToInt64(q.ExecuteScalar()));
        }
        Assert.True(live.HasValidIntegrity());
        Assert.Single(Directory.GetFiles(OutputFolder,"*.dbbackup"));
    }

    [Fact]
    public async Task WrongHash_RefusedWithoutPublication()
    {
        await CreateSourceAsync(Current-1);
        using var live=new AlTamamDbContext(_path,Key,false);
        string original=Digest(Source);
        await Assert.ThrowsAsync<InvalidDataException>(()=>live.PrepareForwardUpgradedBackupAsync(Source,new string('0',64)));
        Assert.Equal(original,Digest(Source));
        Assert.False(File.Exists(_path));
        NoOutput();
    }

    [Fact]
    public async Task TamperedMigrationHistory_RefusedWithoutPublication()
    {
        await CreateSourceAsync(Current-1,true);
        using var live=new AlTamamDbContext(_path,Key,false);
        string original=Digest(Source);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>live.PrepareForwardUpgradedBackupAsync(Source,original));
        Assert.Contains("hash mismatch",error.Message,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original,Digest(Source));
        Assert.False(File.Exists(_path));
        NoOutput();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task NonAdjacentVersion_RefusedWithoutPublication(int delta)
    {
        await CreateSourceAsync(Current+delta);
        using var live=new AlTamamDbContext(_path,Key,false);
        string original=Digest(Source);
        await Assert.ThrowsAsync<InvalidDataException>(()=>live.PrepareForwardUpgradedBackupAsync(Source,original));
        Assert.Equal(original,Digest(Source));
        NoOutput();
    }

    [Fact]
    public async Task SidecarOrCancellation_RefusedWithoutPublication()
    {
        await CreateSourceAsync(Current-1);
        using var live=new AlTamamDbContext(_path,Key,false);
        string original=Digest(Source);
        File.WriteAllText(Source+"-wal","synthetic");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(()=>live.PrepareForwardUpgradedBackupAsync(Source,original));
        }
        finally{File.Delete(Source+"-wal");}
        using var cancellation=new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>live.PrepareForwardUpgradedBackupAsync(Source,original,cancellation.Token));
        Assert.Equal(original,Digest(Source));
        NoOutput();
    }

    [Fact]
    public async Task NestedWindowsPath_UsesShortRescueAndLeavesNoScratch()
    {
        await CreateSourceAsync(Current-1);
        // Model a nested Windows profile without requiring long-path support.
        string nested=Path.Combine(Folder,new string('n',Math.Max(12,130-Folder.Length-1)));
        Directory.CreateDirectory(nested);
        string active=Path.Combine(nested,"live.db");
        using var live=new AlTamamDbContext(active,Key,false);
        string original=Digest(Source);
        var result=await live.PrepareForwardUpgradedBackupAsync(Source,original);
        Assert.Equal(Current,result.Upgraded.SchemaVersion);
        Assert.Equal(original,Digest(Source));
        Assert.False(File.Exists(active));
        Assert.Empty(Directory.GetDirectories(nested,".bu-*"));
        Assert.Single(Directory.GetFiles(Path.Combine(nested,"backup-forward-upgrades"),"*.dbbackup"));
    }

    public void Dispose()=>SyntheticFixtures.DeleteTempDatabase(_path);
}
