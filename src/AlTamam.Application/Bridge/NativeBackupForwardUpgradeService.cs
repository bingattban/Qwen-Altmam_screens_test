using System.Security.Cryptography;
using AlTamam.Contracts.Bridge;
using AlTamam.Domain.Enums;
using AlTamam.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace AlTamam.Application.Bridge;

/// <summary>
/// Native-only preparation of a separate encrypted backup; never a restore
/// grant, activation, or WebView2 action. Reuses the current administrator
/// password lockout policy and independently validates any registered SHA pin.
/// </summary>
public sealed class NativeBackupForwardUpgradeService
{
    private readonly AlTamamDbContext _db;
    private readonly BridgeSharedServices _sessions;

    public NativeBackupForwardUpgradeService(AlTamamDbContext db,BridgeSharedServices sessions)
    {
        _db=db ?? throw new ArgumentNullException(nameof(db));
        _sessions=sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async Task<ForwardUpgradedBackup?> PrepareAsync(BridgePrincipal? actor,string? password,
        string? sourcePath,string? expectedSha256,CancellationToken ct=default)
    {
        if(actor is null || actor.Role!=UserRole.GeneralAdministration ||
           string.IsNullOrWhiteSpace(sourcePath) || sourcePath.Length>1024 ||
           expectedSha256 is not {Length:64} || !expectedSha256.All(Uri.IsHexDigit))
            return null;
        try
        {
            if(!Path.IsPathFullyQualified(sourcePath)) return null;
            string path=Path.GetFullPath(sourcePath);
            if(!await new NativeRestoreReauthenticationGate(_db,_sessions)
                .VerifyCurrentGeneralAdministratorPasswordAsync(actor,password,ct).ConfigureAwait(false))
                return null;
            if(!await CanPublishAsync(actor,path,expectedSha256,ct).ConfigureAwait(false))
                return null;
            // Do not open the selected backup in SQLite here. Infrastructure
            // hashes and verifies a private copy, preserving the selected bytes.
            return await _db.PrepareForwardUpgradedBackupAsync(path,expectedSha256,
                token=>CanPublishAsync(actor,path,expectedSha256,token),ct).ConfigureAwait(false);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or
            InvalidDataException or ArgumentException or CryptographicException or
            SqliteException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<bool> CanPublishAsync(BridgePrincipal actor,string path,string sha,
        CancellationToken ct)
    {
        var live=await _sessions.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(live is null || live.Role!=UserRole.GeneralAdministration ||
           live.UserId!=actor.UserId || live.Username!=actor.Username) return false;
        using var connection=_db.CreateOpenConnection();
        using var q=connection.CreateCommand();
        q.CommandText="""
            SELECT Sha256Checksum FROM BackupHistory
            WHERE BackupFilePath=$path COLLATE NOCASE AND VerificationPassed=1
            ORDER BY CreatedAtUtc DESC LIMIT 1;
            """;
        q.Parameters.AddWithValue("$path",path);
        string? registered=await q.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return registered is null || string.Equals(registered,sha,StringComparison.OrdinalIgnoreCase);
    }
}
