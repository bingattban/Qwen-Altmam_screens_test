using System.Text.Json.Nodes;
using AlTamam.Domain.Enums;

namespace AlTamam.NativePresentation;

public sealed record NativeFullResetConsent(string Phrase,bool DeleteLiveData,bool DeleteOwnedBackups);

public sealed class NativeFullResetPreview
{
    public const string RequiredPhrase="تصفير جميع البيانات";
    public const int CountdownSeconds=5;
    private NativeFullResetPreview(NativeIdentity actor,string token,long revision,long[] counts,
        IReadOnlyList<string> names,bool truncated)
    {
        Actor=actor; Token=token; Revision=revision;
        People=counts[0]; Attendance=counts[1]; BackupEntries=counts[2]; Users=counts[3];
        Movements=counts[4]; Assignments=counts[5]; AuditEvents=counts[6];
        BackupFileNames=names; BackupFileNamesTruncated=truncated;
    }
    public NativeIdentity Actor { get; }
    public string Token { get; }
    public long Revision { get; }
    public long People { get; }
    public long Attendance { get; }
    public long BackupEntries { get; }
    public long Users { get; }
    public long Movements { get; }
    public long Assignments { get; }
    public long AuditEvents { get; }
    public IReadOnlyList<string> BackupFileNames { get; }
    public bool BackupFileNamesTruncated { get; }

    public bool Matches(NativeIdentity? liveActor)=>Actor==liveActor &&
        Actor.Role==UserRole.GeneralAdministration;
    public bool CanCommit(NativeIdentity? actor,NativeFullResetConsent consent,TimeSpan elapsed)
        =>Matches(actor) && consent is { DeleteLiveData:true,DeleteOwnedBackups:true } &&
            consent.Phrase==RequiredPhrase && elapsed>=TimeSpan.FromSeconds(CountdownSeconds);

    public static NativeFullResetPreview? Read(NativeIdentity actor,JsonObject? data)
    {
        if(actor.Role!=UserRole.GeneralAdministration || string.IsNullOrWhiteSpace(actor.SessionNonce) ||
            string.IsNullOrWhiteSpace(actor.UserId) || data is null) return null;
        try
        {
            string? token=data["previewToken"]?.GetValue<string>();
            long revision=data["expectedRevision"]?.GetValue<long>()??-1;
            if(token is not { Length:64 } || !token.All(Uri.IsHexDigit) ||
                revision<0 || revision>9007199254740991L ||
                data["countdownSeconds"]?.GetValue<int>()!=CountdownSeconds ||
                data["requiredConfirmation"]?.GetValue<string>()!=RequiredPhrase ||
                data["backupFileNames"] is not JsonArray names || names.Count>30 ||
                data["backupFileNamesTruncated"]?.GetValue<bool>() is not { } truncated) return null;
            string[] keys=["people","attendance","backupEntries","users","movements","assignments","auditEvents"];
            long[] counts=keys.Select(k=>data[k]?.GetValue<long>()??-1).ToArray();
            if(counts.Any(n=>n<0) || names.Count>counts[2] || truncated!=(counts[2]>30)) return null;
            var safeNames=new List<string>();
            foreach(var item in names)
            {
                string? name=item?.GetValue<string>();
                if(string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) ||
                    name.Contains('/') || name.Contains('\\') ||
                    !name.StartsWith("AlTamam-",StringComparison.Ordinal) ||
                    !name.EndsWith(".dbbackup",StringComparison.OrdinalIgnoreCase)) return null;
                safeNames.Add(name);
            }
            return new(actor,token,revision,counts,safeNames.AsReadOnly(),truncated);
        }
        catch(InvalidOperationException) { return null; }
        catch(FormatException) { return null; }
        catch(OverflowException) { return null; }
    }
}
