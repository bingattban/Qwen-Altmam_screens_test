using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace AlTamam.Infrastructure.Persistence;

/// <summary>
/// Non-mutating inspection of an encrypted SQLCipher backup with THIS device's
/// existing key only. A different Windows installation needs an explicit
/// separately protected recovery key (not a new DPAPI key).
/// This inspector deliberately cannot switch, delete or overwrite the live DB.
/// </summary>
public sealed record EncryptedBackupInspection(
    string FullPath,string Sha256,long FileSize,int SchemaVersion,
    long PeopleCount,long AttendanceCount,long AuditCount,long UserCount);

internal static class EncryptedBackupInspector
{
    private static FileInfo ValidateBackupFile(string backupPath,string activePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        if(!Path.IsPathFullyQualified(backupPath) ||
           backupPath.StartsWith(@"\\",StringComparison.Ordinal) ||
           backupPath.StartsWith("//",StringComparison.Ordinal) ||
           backupPath.Any(char.IsControl))
            throw new ArgumentException("Invalid local backup path.",nameof(backupPath));
        string path=Path.GetFullPath(backupPath);
        if(string.Equals(path,Path.GetFullPath(activePath),StringComparison.OrdinalIgnoreCase) ||
           string.Equals(path,Path.GetFullPath(activePath)+"-wal",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The live database is never a restore candidate.",nameof(backupPath));
        if(Path.GetExtension(path) is not ".dbbackup" and not ".db")
            throw new ArgumentException("Only encrypted backup files can be inspected.",nameof(backupPath));
        var file=new FileInfo(path);
        if(!file.Exists || file.Length<1024 || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Backup missing, too small or redirected.");
        return file;
    }

    // A native upgrade inspection must not open the original backup through
    // SQLite. Read-only WAL databases may still create -wal and -shm files.
    internal static async Task<EncryptedBackupInspection> VerifyStandaloneCopyAsync(
        string backupPath,string activePath,string keyHex,CancellationToken ct)
    {
        var file=ValidateBackupFile(backupPath,activePath);
        string source=file.FullName;
        static void RequireStandalone(string path)
        {
            if(new[]{"-wal","-shm","-journal"}.Any(suffix=>File.Exists(path+suffix)))
                throw new InvalidDataException("A standalone encrypted backup is required; existing sidecars are preserved.");
        }
        RequireStandalone(source);
        string scratch=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(activePath))!,
            ".bi-"+Guid.NewGuid().ToString("N")+".db");
        try
        {
            await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,
                FileShare.Read,81920,FileOptions.SequentialScan);
            RequireStandalone(source);
            string digest=Convert.ToHexString(await SHA256.HashDataAsync(input,ct).ConfigureAwait(false));
            input.Position=0;
            await using(var output=new FileStream(scratch,FileMode.CreateNew,FileAccess.Write,
                FileShare.None,81920,FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output,ct).ConfigureAwait(false);
                output.Flush(flushToDisk:true);
            }
            RequireStandalone(source);
            var inspected=await VerifyAsync(scratch,activePath,keyHex,ct).ConfigureAwait(false);
            if(!string.Equals(inspected.Sha256,digest,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup inspection copy digest mismatch.");
            RequireStandalone(source);
            ct.ThrowIfCancellationRequested();
            return inspected with { FullPath=source };
        }
        finally
        {
            // Dispose of the GUID-owned private inspection copy only. Never
            // delete or checkpoint the source or its SQLite sidecars.
            foreach(string suffix in new[]{"-wal","-shm","-journal",""})
            {
                try{if(File.Exists(scratch+suffix))File.Delete(scratch+suffix);}
                catch(IOException){}
                catch(UnauthorizedAccessException){}
            }
        }
    }

    internal static async Task<EncryptedBackupInspection> VerifyAsync(
        string backupPath,string activePath,string keyHex,CancellationToken ct)
    {
        var file=ValidateBackupFile(backupPath,activePath);
        string path=file.FullName;
        // Reject the SQLite plaintext magic even before opening the cipher.
        await using(var raw=new FileStream(path,FileMode.Open,FileAccess.Read,
            FileShare.Read,bufferSize:4096,options:FileOptions.SequentialScan))
        {
            var prefix=new byte[16];
            if(await raw.ReadAsync(prefix,ct).ConfigureAwait(false)!=16 ||
               prefix.AsSpan().SequenceEqual("SQLite format 3\0"u8))
                throw new InvalidDataException("The selected file is not an encrypted backup.");
        }
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=path,Mode=SqliteOpenMode.ReadOnly,Password=keyHex,
            Pooling=false,ForeignKeys=true,DefaultTimeout=15
        }.ConnectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);
        async Task<string?> ScalarAsync(string sql)
        {
            using var q=c.CreateCommand();q.CommandText=sql;
            return (await q.ExecuteScalarAsync(ct).ConfigureAwait(false))?.ToString();
        }
        if(string.IsNullOrWhiteSpace(await ScalarAsync("PRAGMA cipher_version;")) ||
           !string.Equals(await ScalarAsync("PRAGMA integrity_check;"),
               "ok",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Encrypted backup integrity check failed.");
        using(var fk=c.CreateCommand())
        {
            fk.CommandText="PRAGMA foreign_key_check;";
            using var reader=await fk.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if(await reader.ReadAsync(ct).ConfigureAwait(false))
                throw new InvalidDataException("Backup contains broken foreign-key references.");
        }
        int schema=int.Parse(await ScalarAsync("SELECT MAX(SchemaVersion) FROM SchemaVersions;")
            ?? throw new InvalidDataException("Missing schema version."),
            System.Globalization.CultureInfo.InvariantCulture);
        if(schema<1)throw new InvalidDataException("Invalid backup schema version.");
        long people=long.Parse(await ScalarAsync("SELECT COUNT(*) FROM Persons;") ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        long attendance=long.Parse(await ScalarAsync("SELECT COUNT(*) FROM AttendanceRecords;") ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        long audit=long.Parse(await ScalarAsync("SELECT COUNT(*) FROM AuditEvents;") ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        long users=long.Parse(await ScalarAsync("SELECT COUNT(*) FROM Users;") ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        // Hash the unchanged file after closing SQLite, so the caller can compare
        // against the catalog or rehash at any future restore's actual commit.
        c.Close();
        await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,
            FileShare.Read,bufferSize:81920,options:FileOptions.SequentialScan);
        string sha=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)
            .ConfigureAwait(false)).ToLowerInvariant();
        return new EncryptedBackupInspection(path,sha,file.Length,schema,
            people,attendance,audit,users);
    }
}
