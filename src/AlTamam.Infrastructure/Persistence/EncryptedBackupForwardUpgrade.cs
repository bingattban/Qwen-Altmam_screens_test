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
                throw new InvalidDataException("The source backup has SQLite sidecars; a standalone snapshot is required.");
        }
        RequireStandalone(source);
        string folder=Path.Combine(Path.GetDirectoryName(active)!,"backup-forward-upgrades");
        Directory.CreateDirectory(folder);
        if(File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Forward-upgrade destination is redirected.");
        string id=Guid.NewGuid().ToString("N");
        string scratch=Path.Combine(folder,".pending-"+id);
        Directory.CreateDirectory(scratch);
        string copy=Path.Combine(scratch,"source-copy.db");
        string candidate=Path.Combine(scratch,"verified.dbbackup");
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
                // InitializeAsync creates a verified encrypted pre-migration
                // rescue and applies the canonical hash-pinned migration only.
                await db.InitializeAsync(ct).ConfigureAwait(false);
                using(var c=db.CreateOpenConnection())
                {
                    if(SchemaMigrationRunner.GetCurrentVersion(c)!=AlTamamDbContext.CurrentSchemaVersion)
                        throw new InvalidDataException("Unexpected migrated schema.");
                    runner.VerifyRecordedHashes(c);
                }
                await db.CreateVerifiedEncryptedBackupAsync(candidate,ct).ConfigureAwait(false);
            }
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
}
