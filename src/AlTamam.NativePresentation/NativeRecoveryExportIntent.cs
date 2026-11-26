using AlTamam.Domain.Enums;

namespace AlTamam.NativePresentation;

/// <summary>Native file-picker intent only. No passwords, key bytes or file writes.</summary>
public sealed record NativeRecoveryExportIntent(NativeIdentity Actor,string Destination)
{
    public bool IsValid=>Actor is { Role:UserRole.GeneralAdministration } &&
        !string.IsNullOrWhiteSpace(Actor.UserId) && !string.IsNullOrWhiteSpace(Actor.Username) &&
        !string.IsNullOrWhiteSpace(Actor.SessionNonce) && ValidDestination(Destination);

    public bool Matches(NativeIdentity? liveActor)=>IsValid && Actor==liveActor;

    public static bool ValidDestination(string? path)
    {
        if(path is not { Length:>0 and <=1024 } || path.Any(char.IsControl) ||
            !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\",StringComparison.Ordinal) ||
            path.StartsWith("//",StringComparison.Ordinal)) return false;
        try
        {
            return string.Equals(Path.GetExtension(path),".altrecovery",StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(path)==path;
        }
        catch(ArgumentException) { return false; }
        catch(NotSupportedException) { return false; }
        catch(IOException) { return false; }
    }
}
