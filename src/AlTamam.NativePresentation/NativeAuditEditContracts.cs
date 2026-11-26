using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeAuditEditRequest(NativeAuditRow Row,NativeAuditDetail Detail,
    string Description,string Reason,string OperationId,bool Hide=false)
{
    public static bool CanUse(NativeAuditRow row,NativeAuditDetail detail,UserRole role)=>
        RoleAuthorizationPolicy.CanEditOrDeleteVisibleAuditLog(role) &&
        row is not null && detail is not null && !row.IsTombstoned &&
        row.OverlayVersion<int.MaxValue && detail.ChainValid && detail.Matches(row,role);

    private static bool ValidText(string? value,int max)=>value is { Length:>0 } &&
        value.Length<=max && value==value.Trim() && !value.Any(char.IsControl);
    public bool IsValid(UserRole role)=>CanUse(Row,Detail,role) &&
        (Hide ? Description==Row.Description : ValidText(Description,2000) && Description!=Row.Description) &&
        ValidText(Reason,400) &&
        Guid.TryParseExact(OperationId,"N",out var operation) && operation!=Guid.Empty;
}

public sealed record NativeAuditEditState(long Version,bool IsTombstoned,string Description);
public sealed record NativeAuditEditReceipt(string AuditEventId,string OperationAuditEventId,
    NativeAuditEditState Before,NativeAuditEditState After,bool RawEvidenceUntouched)
{
    public bool Matches(NativeAuditEditRequest request)=>
        AuditEventId==request.Row.AuditEventId && RawEvidenceUntouched &&
        OperationAuditEventId is { Length:>=5 and <=128 } &&
        OperationAuditEventId.StartsWith("AUD-",StringComparison.Ordinal) &&
        !OperationAuditEventId.Any(char.IsControl) && OperationAuditEventId!=AuditEventId &&
        Before==new NativeAuditEditState(request.Row.OverlayVersion,false,request.Row.Description) &&
        After==new NativeAuditEditState((long)request.Row.OverlayVersion+1,request.Hide,request.Description);
}

/// <summary>Only a canonical, exact stale-version rejection is definitive.</summary>
public sealed class NativeAuditEditStaleException : Exception
{
    public NativeAuditEditStaleException() : base("Audit overlay revision changed.") { }
}

public interface INativeAuditEditBackend
{
    Task<NativeAuditEditReceipt> EditAuditDescriptionAsync(NativeIdentity actor,
        NativeAuditEditRequest request,CancellationToken ct=default);
    Task<NativeAuditEditReceipt> HideAuditEntryAsync(NativeIdentity actor,
        NativeAuditEditRequest request,CancellationToken ct=default);
}
