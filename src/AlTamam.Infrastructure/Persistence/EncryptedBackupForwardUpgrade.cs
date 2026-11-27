using System.Security.Cryptography;
using AlTamam.Infrastructure.Persistence.Migrations;

namespace AlTamam.Infrastructure.Persistence;

/// <summary>A new encrypted backup, not a restore approval or activation marker.</summary>
public sealed record ForwardUpgradedBackup(int SourceSchemaVersion,string SourceSha256,
    EncryptedBackupInspection Upgraded);

internal static class EncryptedBackupForwardUpgrade
{
    internal static async Task<ForwardUpgradedBackup> PrepareAsync(string sourcePath,string activePath,
        string keyHex,string expectedSha256,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if(!Path.IsPathFullyQualified(sourcePath) || sourcePath.StartsWith(@"\\",StringComparison.Ordinal) ||
           sourcePath.StartsWith("//",StringComparison.Ordinal) || sourcePath.Any(char.IsControl))
            throw new ArgumentException("Invalid local backup path.",nameof(sourcePath));
        if(expectedSha256 is not {Length:64} || !expectedSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("An independently inspected source SHA256 is required.",nameof(expectedSha256));
        string source=Path.GetFullPath(sourcePath);
        string active=Path.GetFullPath(activePath);
        if(string.Equals(source,active,StringComparison.OrdinalIgnoreCase) ||
           Path.GetExtension(source) is not ".db" and not ".dbbackup")
            throw new ArgumentException("The source must be a separate encrypted backup.",nameof(sourcePath));
        var info=new FileInfo(source);
        if(!info.Exists || info.Length<1024 || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Missing or redirected backup.");

        static void RequireStandalone(string path)
        {
            if(new[]{"-wal","-shm","-journal"}.Any(suffix=>File.Exists(path+suffix)))
                throw new InvalidDataException("Backup has SQLite sidecars; a standalone snapshot is required.");
        }
        RequireStandalone(source);
        string folder=Path.Combine(Path.GetDirectoryName(active)!,"backup-forward-upgrades");
        Directory.CreateDirectory(folder);
        if(File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Forward-upgrade destination is redirected.");
        string id=Guid.NewGuid().ToString("N");
        // Keep the working paths short even under a nested Windows profile.
        // The final backup stays in the designated output directory; the scratch
        // directory is a GUID-owned sibling on the same volume.
        string scratch=Path.Combine(Path.GetDirectoryName(active)!,".bu-"+id);
        Directory.CreateDirectory(scratch);
        string copy=Path.Combine(scratch,"s.db");
        string rescue=Path.Combine(scratch,"r.db");
        string candidate=Path.Combine(scratch,"v.dbbackup");
        string output=Path.Combine(folder,"forward-upgraded-"+id+".dbbackup");
        try
        {
            // Never open the selected original through SQLite. Even a read-only
            // WAL-mode connection can create sidecars. Hash and copy under a
            // shared read handle; the private copy is the only migration target.
            await using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,
                81920,FileOptions.SequentialScan))
            {
                RequireStandalone(source);
                string actual=Convert.ToHexString(await SHA256.HashDataAsync(input,ct).ConfigureAwait(false));
                if(!string.Equals(actual,expectedSha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Source backup SHA256 changed.");
                input.Position=0;
                await using(var destination=new FileStream(copy,FileMode.CreateNew,FileAccess.Write,FileShare.None,
                    81920,FileOptions.WriteThrough))
                {
                    await input.CopyToAsync(destination,ct).ConfigureAwait(false);
                    destination.Flush(flushToDisk:true);
                }
                RequireStandalone(source);
            }
            var before=await EncryptedBackupInspector.VerifyAsync(copy,active,keyHex,ct).ConfigureAwait(false);
            if(!string.Equals(before.Sha256,expectedSha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source copy SHA256 mismatch.");
            // Only the immediately preceding version is accepted. Older or future
            // versions and already-current files require a separate decision.
            if(before.SchemaVersion!=AlTamamDbContext.CurrentSchemaVersion-1)
                throw new InvalidDataException("Only the immediately preceding schema can be upgraded.");
            var runner=new SchemaMigrationRunner(AlTamamDbContext.CanonicalMigrations);
            using(var db=new AlTamamDbContext(copy,keyHex,useConnectionPooling:false))
            {
                // This is a private disposable copy, never the active database.
                // Validate historical pins BEFORE creating a rescue or applying DDL.
                using(var c=db.CreateOpenConnection())
                {
                    if(SchemaMigrationRunner.GetCurrentVersion(c)!=before.SchemaVersion)
                        throw new InvalidDataException("Unexpected source schema.");
                    runner.VerifyRecordedHashes(c);
                }
                // Reuse the verified encrypted backup writer but avoid the nested
                // InitializeAsync rescue filename, which exceeded Windows SQLite
                // path limits. A failed rescue still prevents every migration.
                await db.CreateVerifiedEncryptedBackupAsync(rescue,ct).ConfigureAwait(false);
                var rescued=await EncryptedBackupInspector.VerifyAsync(rescue,active,keyHex,ct).ConfigureAwait(false);
                if(rescued.SchemaVersion!=before.SchemaVersion ||
                   rescued.PeopleCount!=before.PeopleCount || rescued.AttendanceCount!=before.AttendanceCount ||
                   rescued.AuditCount!=before.AuditCount || rescued.UserCount!=before.UserCount)
                    throw new InvalidDataException("Pre-migration rescue verification failed.");
                using(var c=db.CreateOpenConnection())
                {
                    runner.VerifyRecordedHashes(c);
                    await runner.UpgradeAsync(c,ct).ConfigureAwait(false);
                    if(SchemaMigrationRunner.GetCurrentVersion(c)!=AlTamamDbContext.CurrentSchemaVersion)
                        throw new InvalidDataException("Unexpected migrated schema.");
                    runner.VerifyRecordedHashes(c);
                }
                await db.CreateVerifiedEncryptedBackupAsync(candidate,ct).ConfigureAwait(false);
            }
            // SQLite online backup can retain the source's persistent WAL mode.
            // Normalize ONLY the private output, before hashing/inspection, so
            // a read-only verifier cannot recreate WAL/SHM alongside the artifact.
            await MakeStandaloneAsync(candidate,keyHex,ct).ConfigureAwait(false);
            RequireStandalone(candidate);
            var after=await EncryptedBackupInspector.VerifyAsync(candidate,active,keyHex,ct).ConfigureAwait(false);
            if(after.SchemaVersion!=AlTamamDbContext.CurrentSchemaVersion ||
               after.PeopleCount!=before.PeopleCount || after.AttendanceCount!=before.AttendanceCount ||
               after.AuditCount!=before.AuditCount || after.UserCount!=before.UserCount)
                throw new InvalidDataException("Forward-upgrade changed protected record counts.");
            // Recheck the final portable snapshot's migration pins, not only the
            // working copy. No activation or restore journal is ever created.
            using(var snapshot=new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{
                    DataSource=candidate,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                    Password=keyHex,Pooling=false,ForeignKeys=true}.ConnectionString))
            {
                await snapshot.OpenAsync(ct).ConfigureAwait(false);
                runner.VerifyRecordedHashes(snapshot);
            }
            await using(var finalRead=new FileStream(candidate,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                string digest=Convert.ToHexString(await SHA256.HashDataAsync(finalRead,ct).ConfigureAwait(false));
                if(!string.Equals(digest,after.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Verified output SHA256 changed.");
            }
            RequireStandalone(candidate);
            ct.ThrowIfCancellationRequested();
            File.Move(candidate,output,overwrite:false);
            return new ForwardUpgradedBackup(before.SchemaVersion,before.Sha256,after with {FullPath=output});
        }
        finally
        {
            // Only this GUID-owned scratch folder can be removed. Original
            // backup, live DB and any previously published copies stay untouched.
            try{Directory.Delete(scratch,recursive:true);}
            catch(IOException){}
            catch(UnauthorizedAccessException){}
        }
    }

    private static async Task MakeStandaloneAsync(string privateBackup,string keyHex,CancellationToken ct)
    {
        // ReadWrite (not ReadWriteCreate), no pooling: SQLite owns checkpoint
        // and journal cleanup. Never manually remove WAL/SHM files.
        using var connection=new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{
                DataSource=privateBackup,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
                Password=keyHex,Pooling=false,ForeignKeys=true,DefaultTimeout=15
            }.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        using var command=connection.CreateCommand();
        command.CommandText="PRAGMA wal_checkpoint(TRUNCATE);";
        using(var checkpoint=await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if(!await checkpoint.ReadAsync(ct).ConfigureAwait(false) ||
                checkpoint.GetInt64(0)!=0 || checkpoint.GetInt64(1)!=checkpoint.GetInt64(2))
                throw new InvalidDataException("Private backup WAL checkpoint did not complete.");
        }
        command.CommandText="PRAGMA journal_mode=DELETE;";
        if(!string.Equals((await command.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString(),
            "delete",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Private backup could not enter standalone DELETE journal mode.");
    }
}
