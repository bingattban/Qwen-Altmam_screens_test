using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeAccountPerson(string PersonId,string FullName,string ServiceNumberA,
    string SiteName,int OrganizationalStatus,long Revision)
{
    public bool IsValid=>PersonId is { Length:>0 and <=128 } && !string.IsNullOrWhiteSpace(PersonId) &&
        !PersonId.Any(char.IsControl) && !string.IsNullOrWhiteSpace(FullName) &&
        !string.IsNullOrWhiteSpace(ServiceNumberA) && SiteName is not null &&
        OrganizationalStatus==1 && Revision>=1;
    public string Label=>$"{ServiceNumberA} — {FullName} — {SiteName}";
}

public sealed record NativeAccountPersonSearch(string Term,IReadOnlyList<NativeAccountPerson> People)
{
    public static bool ValidTerm(string? term)=>term is { Length:>0 and <=100 } &&
        term==term.Trim() && !string.IsNullOrWhiteSpace(term) && !term.Any(char.IsControl);
    public bool Matches(string term)=>Term==term && ValidTerm(term) && People is not null &&
        People.Count<=50 && People.All(person=>person is not null && person.IsValid) &&
        People.Select(person=>person.PersonId).Distinct(StringComparer.Ordinal).Count()==People.Count;
}

public sealed record NativeAccountRoleChoice(UserRole Value,string Label);

/// <summary>Reviewed metadata only. Passwords are never stored in this intent.</summary>
public sealed record NativeAccountCreateIntent(NativeAccountPerson Person,string Username,UserRole Role,string OperationId)
{
    public string RoleLabel=>Role.ToArabicLabel();
    public bool IsValid(UserRole actorRole)=>Person is not null && Person.IsValid &&
        RoleAuthorizationPolicy.CanCreateAccountWithRole(actorRole,Role) &&
        Username is { Length:>=3 and <=64 } && Username==Username.Trim() &&
        !Username.Any(char.IsWhiteSpace) && !Username.Any(char.IsControl) &&
        Guid.TryParseExact(OperationId,"N",out var id) && id!=Guid.Empty;
    public static bool ValidPassword(string password,string confirmation)=>
        string.Equals(password,confirmation,StringComparison.Ordinal) &&
        PasswordPolicyValidator.Validate(password).IsValid;
}

public sealed record NativeAccountCreateReceipt(string UserId,string Username,UserRole Role,string PersonId)
{
    public bool Matches(NativeAccountCreateIntent intent)=>UserId is { Length:>=5 and <=128 } &&
        UserId.StartsWith("USR-",StringComparison.Ordinal) && !UserId.Any(char.IsControl) &&
        Username==intent.Username && Role==intent.Role && PersonId==intent.Person.PersonId;
}

/// <summary>Only a known canonical rejection with no successful receipt.</summary>
public sealed class NativeAccountCreateRejectedException(string message):Exception(message);

public interface INativeAccountCreateBackend
{
    Task<NativeAccountPersonSearch> SearchAccountPeopleAsync(NativeIdentity actor,string term,
        CancellationToken ct=default);
    Task<NativeAccountCreateReceipt> CreateAccountAsync(NativeIdentity actor,NativeAccountCreateIntent intent,
        string password,string confirmation,CancellationToken ct=default);
}
