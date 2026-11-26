using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeAccountRequest(string Term="",int Page=1)
{
    public const int PageSize=50;
    public bool IsValid=>Page is >=1 and <=100000 && Term is { Length:<=200 } &&
        Term==Term.Trim() && !Term.Any(char.IsControl);
    public bool IsAuthorized(UserRole role)=>RoleAuthorizationPolicy.CanAccessUsersManagement(role);
    public static IReadOnlyList<UserRole> AllowedCreationRoles(UserRole actorRole)=>
        Array.AsReadOnly(new[]{UserRole.OrdinaryAdministration,UserRole.Supervisor}
            .Where(role=>RoleAuthorizationPolicy.CanCreateAccountWithRole(actorRole,role)).ToArray());
}

public sealed record NativeAccountRow(long Index,string UserId,string Username,UserRole Role,
    bool Active,long Revision,string FullName,string ServiceNumberA,string? PersonId,
    string CreatedBy,string CreatedAtUtc,bool CanModify)
{
    public string RoleLabel=>Role.ToArabicLabel();
    public string ActiveLabel=>Active?"نشط":"معطل";
    public string ModifyLabel=>CanModify?"مسموح":"غير مسموح";
    public bool IsValidFor(NativeIdentity actor)=>
        Index>0 && UserId is { Length:>0 and <=128 } && !string.IsNullOrWhiteSpace(UserId) &&
        !UserId.Any(char.IsControl) && !string.IsNullOrWhiteSpace(Username) &&
        RoleAuthorizationPolicy.CanPerformStandardOperations(Role) &&
        (actor.Role!=UserRole.OrdinaryAdministration || Role==UserRole.Supervisor) &&
        (Role==UserRole.GeneralAdministration ? PersonId is null : !string.IsNullOrWhiteSpace(PersonId)) &&
        Revision>=1 && FullName is not null && ServiceNumberA is not null &&
        CreatedBy is not null && !string.IsNullOrWhiteSpace(CreatedAtUtc) &&
        CanModify==(actor.UserId!=UserId && RoleAuthorizationPolicy.CanModifyAccount(actor.Role,Role));
}

public sealed record NativeAccountPage(NativeAccountRequest Request,long Total,bool HasNext,
    IReadOnlyList<NativeAccountRow> Rows)
{
    public bool Matches(NativeAccountRequest expected,NativeIdentity actor)
    {
        if(Request!=expected || !expected.IsValid || !expected.IsAuthorized(actor.Role) ||
            Total<0 || Rows is null || Rows.Count>NativeAccountRequest.PageSize) return false;
        long offset=(long)(expected.Page-1)*NativeAccountRequest.PageSize;
        if(Rows.Count!=Math.Clamp(Total-offset,0L,NativeAccountRequest.PageSize) ||
            HasNext!=(offset+NativeAccountRequest.PageSize<Total)) return false;
        var ids=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<Rows.Count;i++)
        {
            var row=Rows[i];
            if(row is null || !row.IsValidFor(actor) || row.Index!=offset+i+1 ||
                !ids.Add(row.UserId)) return false;
        }
        return true;
    }
}

public interface INativeAccountReader
{
    Task<NativeAccountPage> ReadAccountsAsync(NativeIdentity actor,NativeAccountRequest request,
        CancellationToken ct=default);
}
