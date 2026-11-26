using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public enum NativeAccountAction { ChangePassword, Deactivate, Reactivate }

public sealed record NativeAccountLifecycleIntent(NativeAccountRow Row,NativeAccountAction Action,
    string Reason,string OperationId)
{
    public static bool CanUse(NativeIdentity actor,NativeAccountRow row,NativeAccountAction action)=>
        RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) && row is not null &&
        row.IsValidFor(actor) && row.CanModify && row.UserId!=actor.UserId && row.Revision<long.MaxValue &&
        (action switch
        {
            NativeAccountAction.ChangePassword=>row.Active,
            NativeAccountAction.Deactivate=>row.Active && row.Role!=UserRole.GeneralAdministration,
            NativeAccountAction.Reactivate=>!row.Active && row.Role!=UserRole.GeneralAdministration &&
                !string.IsNullOrWhiteSpace(row.PersonId),
            _=>false
        });
    public bool IsValid(NativeIdentity actor)=>CanUse(actor,Row,Action) &&
        (Action==NativeAccountAction.Reactivate
            ? Reason is { Length:>=5 and <=400 } && Reason==Reason.Trim() && !Reason.Any(char.IsControl)
            : Reason=="") &&
        Guid.TryParseExact(OperationId,"N",out var id) && id!=Guid.Empty;
}

public sealed record NativeAccountLifecycleReceipt(string UserId,bool Active,long Revision)
{
    public bool Matches(NativeAccountLifecycleIntent intent)=>UserId==intent.Row.UserId &&
        intent.Row.Revision<long.MaxValue && Revision==intent.Row.Revision+1 &&
        Active==(intent.Action!=NativeAccountAction.Deactivate);
}

/// <summary>Canonical rejection; not proof of a prior attempt's outcome.</summary>
public sealed class NativeAccountLifecycleRejectedException:Exception
{
    public NativeAccountLifecycleRejectedException():base("Canonical account action rejected.") { }
}

public interface INativeAccountLifecycleBackend
{
    Task<NativeAccountLifecycleReceipt> ApplyAccountActionAsync(NativeIdentity actor,
        NativeAccountLifecycleIntent intent,string password,CancellationToken ct=default);
}
