using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AlTamam.Application.Bridge;
using AlTamam.Application.Scheduling;
using AlTamam.Domain.Enums;
using AlTamam.Infrastructure.Persistence;
using AlTamam.Tests.Fixtures;
using Xunit;

namespace AlTamam.Tests.Bridge;

/// <summary>Phase 12: actual encrypted backup files and disconnected-folder recovery,
/// without changing the user's machine, E: drive, or production database.</summary>
public sealed class Phase12BackupDiskRecoveryTests : IAsyncLifetime, IDisposable
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 24, 15, 59, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }

    private readonly string _database = SyntheticFixtures.CreateTempDatabasePath();
    private readonly string _folder = Path.Combine(Path.GetTempPath(),
        "altamam-phase12-disk-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new();
    private AlTamamDbContext _db = null!;
    private static readonly BridgePrincipal Admin = new(
        "TST-PHASE12-GA", "phase12-admin", UserRole.GeneralAdministration,
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        _db = new AlTamamDbContext(_database, SyntheticFixtures.TestKeyHex);
        await _db.InitializeAsync();
    }

    [Fact]
    public async Task RemovedSelectedDisk_FailsVisibly_PreservesBackup_AndCatchesUpOnceWhenRestored()
    {
        var folderSettings = new OutputDirectorySettings(_db);
        var selected = await folderSettings.SaveAsync(_folder, Admin, CancellationToken.None);
        Assert.True(selected.Success, selected.UserMessage);
        var scheduler = new AutomaticBackupService(_db, _clock);
        Assert.True((await scheduler.ConfigureAsync(new JsonObject
        {
            ["enabled"] = true, ["backupTime"] = "16:00"
        }, Admin, CancellationToken.None)).Success);

        Assert.False(await scheduler.RunIfDueAsync());
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await scheduler.RunIfDueAsync());
        Assert.False(await scheduler.RunIfDueAsync());

        string firstPath;
        using (var connection = _db.CreateOpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT BackupFilePath FROM BackupHistory WHERE BackupId='AUTO-20261024';";
            firstPath = Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        Assert.True(File.Exists(firstPath));
        byte[] firstDigest = SHA256.HashData(await File.ReadAllBytesAsync(firstPath));

        _clock.Advance(TimeSpan.FromDays(1));
        // Simulate an ejected removable drive without depending on runner hardware.
        string temporarilyRemoved = _folder + "-disconnected";
        Directory.Move(_folder, temporarilyRemoved);
        try
        {
            var failed = await Assert.ThrowsAsync<IOException>(
                () => scheduler.RunIfDueAsync());
            Assert.Contains("مجلد النسخ", failed.Message);
            var rejectedFolder = await folderSettings.SaveAsync(_folder, Admin, CancellationToken.None);
            Assert.False(rejectedFolder.Success);
            var manual = await new SettingsReadBackupService(_db, _clock)
                .BackupAsync(Admin, CancellationToken.None);
            Assert.False(manual.Success);
            Assert.Contains("هذا المسار غير صالح أو غير متاح", manual.UserMessage);
            Assert.Equal(_folder, await folderSettings.GetAsync());
            using var connection = _db.CreateOpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM BackupHistory WHERE BackupId='AUTO-20261025';";
            Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally
        {
            Directory.Move(temporarilyRemoved, _folder);
        }

        Assert.True(await scheduler.RunIfDueAsync());
        Assert.False(await scheduler.RunIfDueAsync());
        Assert.Equal("2026-10-25", await scheduler.LastSuccessfulDateAsync());
        Assert.Equal(firstDigest, SHA256.HashData(await File.ReadAllBytesAsync(firstPath)));
        using var copy = new AlTamamDbContext(firstPath, SyntheticFixtures.TestKeyHex);
        Assert.True(copy.HasValidIntegrity());
        using (var connection = _db.CreateOpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT COUNT(*) FROM BackupHistory
                WHERE IsAutomatic=1 AND VerificationPassed=1;
                """;
            Assert.Equal(2L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task InvalidPathCannotReplacePersistedPreferenceOrTriggerHiddenFallback()
    {
        var settings = new OutputDirectorySettings(_db);
        Assert.True((await settings.SaveAsync(_folder, Admin, CancellationToken.None)).Success);
        var missing = Path.Combine(_folder, "missing");
        var denied = await settings.SaveAsync(missing, Admin, CancellationToken.None);
        Assert.False(denied.Success);
        Assert.Contains("هذا المسار غير صالح أو غير متاح", denied.UserMessage);
        Assert.Equal(Path.GetFullPath(_folder), await settings.GetAsync());
        Assert.False(Directory.Exists(missing));
        Assert.True(_db.HasValidIntegrity());
    }

    [Fact]
    public async Task ReviewedManualBackupRejectsChangedDirectoryAndVerifiesRegisteredEncryptedFile()
    {
        var settings=new OutputDirectorySettings(_db);
        Assert.True((await settings.SaveAsync(_folder,Admin,default)).Success);
        var backup=new SettingsReadBackupService(_db,_clock);
        var stale=await backup.BackupAsync(Admin,default,Path.Combine(_folder,"old"));
        Assert.False(stale.Success);
        Assert.Equal(AlTamam.Contracts.Bridge.BridgeErrorCodes.ConflictStale,stale.Code);
        Assert.Empty(Directory.EnumerateFiles(_folder,"*.dbbackup"));
        var created=await backup.BackupAsync(Admin,default,await settings.GetAsync());
        Assert.True(created.Success,created.UserMessage);
        var data=Assert.IsType<JsonObject>(created.Data);
        var path=data["path"]!.GetValue<string>();
        Assert.True(File.Exists(path));
        Assert.True(data["verificationPassed"]!.GetValue<bool>());
        var verified=await new BackupInspectionService(_db).VerifyAsync(path,default);
        Assert.True(verified.Success,verified.UserMessage);
        var details=Assert.IsType<JsonObject>(verified.Data);
        Assert.True(details["verified"]!.GetValue<bool>());
        Assert.True(details["registeredInCurrentCatalog"]!.GetValue<bool>());
        Assert.Equal(data["sha256"]!.GetValue<string>(),details["sha256"]!.GetValue<string>(),
            ignoreCase:true);
        Assert.Single(Directory.EnumerateFiles(_folder,"*.dbbackup"));
    }

    [Fact]
    public async Task ScheduleReceiptReplaysAfterDiskRemovalButNewEnableIsRejected()
    {
        var settings=new OutputDirectorySettings(_db);
        Assert.True((await settings.SaveAsync(_folder,Admin,default)).Success);
        var scheduler=new AutomaticBackupService(_db,_clock);
        var intent=new JsonObject
        {
            ["enabled"]=true,["backupTime"]="17:30",["retentionCount"]=3,
            ["operationId"]="phase12-schedule-original"
        };
        var first=await scheduler.ConfigureAsync(intent,Admin,default);
        Assert.True(first.Success,first.UserMessage);
        string disconnected=_folder+"-disconnected";
        Directory.Move(_folder,disconnected);
        try
        {
            var replay=await scheduler.ConfigureAsync(intent,Admin,default);
            Assert.True(replay.Success,replay.UserMessage);
            Assert.Equal(first.Data!.ToJsonString(),replay.Data!.ToJsonString());

            var changed=(JsonObject)intent.DeepClone();
            changed["backupTime"]="18:30";
            var conflict=await scheduler.ConfigureAsync(changed,Admin,default);
            Assert.False(conflict.Success);
            Assert.Equal(AlTamam.Contracts.Bridge.BridgeErrorCodes.ConflictStale,conflict.Code);
            changed["operationId"]="phase12-schedule-new-intent";
            var blocked=await scheduler.ConfigureAsync(changed,Admin,default);
            Assert.False(blocked.Success);
            Assert.Equal(AlTamam.Contracts.Bridge.BridgeErrorCodes.ScheduleUnavailable,blocked.Code);
            Assert.Equal((true,"17:30"),await scheduler.ReadSettingsAsync());

            var pause=new JsonObject
            {
                ["enabled"]=false,["backupTime"]="17:30",["retentionCount"]=3,
                ["operationId"]="phase12-schedule-pause"
            };
            Assert.True((await scheduler.ConfigureAsync(pause,Admin,default)).Success);
            Assert.Equal((false,"17:30"),await scheduler.ReadSettingsAsync());
            // An old receipt is historical evidence, not the current schedule.
            Assert.True((await scheduler.ConfigureAsync(intent,Admin,default)).Success);
            Assert.Equal((false,"17:30"),await scheduler.ReadSettingsAsync());
            using var connection=_db.CreateOpenConnection();
            using var command=connection.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM MutationReceipts WHERE ActionName='UpdateBackupSchedule';";
            Assert.Equal(2L,Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally { Directory.Move(disconnected,_folder); }
    }

    public Task DisposeAsync() => Task.CompletedTask;
    public void Dispose()
    {
        _db?.Dispose();
        SyntheticFixtures.DeleteTempDatabase(_database);
        try { Directory.Delete(_folder, true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
