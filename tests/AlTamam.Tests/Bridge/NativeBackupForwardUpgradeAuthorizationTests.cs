using System.Security.Cryptography;
using AlTamam.Application.Bridge;
using AlTamam.Domain.Enums;
using AlTamam.Infrastructure.Persistence;
using AlTamam.Infrastructure.Persistence.Migrations;
using AlTamam.Tests.Fixtures;
using Xunit;

namespace AlTamam.Tests.Bridge;

public sealed class NativeBackupForwardUpgradeAuthorizationTests : IAsyncLifetime, IDisposable
{
    private const string Password="Synthetic#UpgradeStrong123";
    private readonly string _path=SyntheticFixtures.CreateTempDatabasePath();
    private string Folder=>Path.GetDirectoryName(_path)!;
    private string Source=>Path.Combine(Folder,"old.dbbackup");
    private string Output=>Path.Combine(Folder,"backup-forward-upgrades");
    private AlTamamDbContext _db=null!;
    private BridgeSharedServices _sessions=null!;
    private BridgePrincipal _actor=null!;
    private string _sha="";

    public async Task InitializeAsync()
    {
        _db=new AlTamamDbContext(_path,SyntheticFixtures.TestKeyHex,false);
        await _db.InitializeAsync();
        _sessions=new BridgeSharedServices(_db);
        var setup=await _sessions.ProvisionFirstAdministratorAsync("test-upgrade-ga",Password,CancellationToken.None);
        Assert.True(setup.Success,setup.UserMessage);
        var (principal,error)=await _sessions.AuthenticateAsync("test-upgrade-ga",Password,CancellationToken.None);
        Assert.Null(error);
        _actor=Assert.IsType<BridgePrincipal>(principal);
        using var seed=new AlTamamDbContext(Path.Combine(Folder,"seed.db"),SyntheticFixtures.TestKeyHex,false);
        using(var c=seed.CreateOpenConnection())
            await new SchemaMigrationRunner(AlTamamDbContext.CanonicalMigrations
                .Take(AlTamamDbContext.CurrentSchemaVersion-1).ToArray()).UpgradeAsync(c);
        await seed.CreateVerifiedEncryptedBackupAsync(Source);
        _sha=Digest(Source);
    }

    private static string Digest(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private void AssertNoPublication()
    {
        if(Directory.Exists(Output)) Assert.Empty(Directory.EnumerateFileSystemEntries(Output));
        Assert.Empty(Directory.GetDirectories(Folder,".bu-*"));
        Assert.Empty(Directory.GetFiles(Folder,".altamam-restore-*"));
        Assert.Equal(_sha,Digest(Source));
    }

    [Fact]
    public async Task FreshGeneralAdmin_PreparesNewBackupWithoutRestoreActivation()
    {
        var service=new NativeBackupForwardUpgradeService(_db,_sessions);
        var result=await service.PrepareAsync(_actor,Password,Source,_sha);
        Assert.NotNull(result);
        Assert.Equal(_sha,result!.SourceSha256);
        Assert.Equal(AlTamamDbContext.CurrentSchemaVersion,result.Upgraded.SchemaVersion);
        Assert.Equal(result.Upgraded.Sha256,Digest(result.Upgraded.FullPath));
        Assert.Equal(_sha,Digest(Source));
        Assert.Empty(Directory.GetFiles(Folder,".altamam-restore-*"));
        Assert.NotNull(await _sessions.ResolveSessionAsync(_actor.SessionNonce,CancellationToken.None));
        Assert.True(_db.HasValidIntegrity());
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("wrong-role")]
    [InlineData("wrong-digest")]
    [InlineData("revoked")]
    public async Task InvalidAuthorityOrEvidence_CannotPublish(string scenario)
    {
        var service=new NativeBackupForwardUpgradeService(_db,_sessions);
        var actor=scenario=="wrong-role"?new BridgePrincipal(_actor.UserId,_actor.Username,UserRole.Supervisor,_actor.SessionNonce):_actor;
        if(scenario=="revoked") await _sessions.RevokeSessionAsync(_actor.SessionNonce);
        var result=await service.PrepareAsync(actor,scenario=="wrong-password"?"Bad#Password123":Password,
            Source,scenario=="wrong-digest"?new string('0',64):_sha);
        Assert.Null(result);
        AssertNoPublication();
    }

    [Fact]
    public async Task CatalogDigestMismatch_RejectsEvenCorrectSourceHash()
    {
        using(var c=_db.CreateOpenConnection())
        using(var q=c.CreateCommand())
        {
            q.CommandText="""
                INSERT INTO BackupHistory(BackupId,BackupFilePath,Sha256Checksum,SchemaVersion,
                    IsAutomatic,VerificationPassed,KeyProtectionMode,CreatedAtUtc,CreatedByUserId)
                VALUES('TST-CATALOG-UPGRADE',$path,$sha,$version,0,1,'DPAPI','2026-11-27T00:00:00Z',$user);
                """;
            q.Parameters.AddWithValue("$path",Source);
            q.Parameters.AddWithValue("$sha",new string('a',64));
            q.Parameters.AddWithValue("$version",AlTamamDbContext.CurrentSchemaVersion-1);
            q.Parameters.AddWithValue("$user",_actor.UserId);
            await q.ExecuteNonQueryAsync();
        }
        Assert.Null(await new NativeBackupForwardUpgradeService(_db,_sessions).PrepareAsync(_actor,Password,Source,_sha));
        AssertNoPublication();
    }

    public Task DisposeAsync()=>Task.CompletedTask;
    public void Dispose(){_db?.Dispose();SyntheticFixtures.DeleteTempDatabase(_path);}
}
