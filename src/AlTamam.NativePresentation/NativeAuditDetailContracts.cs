using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>Canonical immutable event details, not a claim that a false chain result is valid.</summary>
public sealed record NativeAuditDetail(string AuditEventId,string OperationCode,string OperationName,
    string ActorUserId,string ActorUsername,string ActorRosterFullName,string TargetEntityType,
    string TargetEntityId,string TargetDisplay,string RawDescription,string Reason,
    string EffectiveLocalDate,string ExecutedAtUtc,string CorrelationId,string EventHash,
    string PriorEventHash,string? OldValueJson,string? NewValueJson,bool IsTombstoned,
    int OverlayVersion,bool ChainValid)
{
    private static bool ValidHash(string? value)=>value is { Length:64 } && value.All(Uri.IsHexDigit);
    public string ChainStatus=>ChainValid ? "نجح تحقق الخدمة الأصلية من سلسلة القيد" :
        "تحذير: فشل تحقق الخدمة الأصلية من سلسلة القيد";
    public string OldValueDisplay=>OldValueJson??"لا توجد قيمة سابقة";
    public string NewValueDisplay=>NewValueJson??"لا توجد قيمة لاحقة";

    public static bool MatchesPresentation(NativeAuditRow row,NativeAuditEditState state)=>
        row is not null && state is not null && row.IsValid &&
        state.Version==row.OverlayVersion && state.IsTombstoned==row.IsTombstoned &&
        state.Description==row.Description;

    public bool Matches(NativeAuditRow row,UserRole role)=>
        row is not null && row.IsValid &&
        RoleAuthorizationPolicy.CanPerformStandardOperations(role) &&
        (!IsTombstoned || RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(role)) &&
        AuditEventId==row.AuditEventId && OperationCode==row.OperationCode &&
        OperationName==row.OperationName && TargetDisplay==row.TargetDisplay &&
        RawDescription==row.RawDescription && Reason==row.Reason &&
        EffectiveLocalDate==row.EffectiveLocalDate && ExecutedAtUtc==row.ExecutedAtUtc &&
        ValidHash(EventHash) && EventHash==row.EventHash &&
        (PriorEventHash=="GENESIS" || ValidHash(PriorEventHash)) &&
        OverlayVersion==row.OverlayVersion && IsTombstoned==row.IsTombstoned &&
        !string.IsNullOrWhiteSpace(ActorUserId) && ActorUsername is not null &&
        ActorRosterFullName is not null && TargetEntityType is not null &&
        TargetEntityId is not null && CorrelationId is not null;
}

public interface INativeAuditDetailReader
{
    Task<NativeAuditDetail> ReadAuditDetailAsync(NativeIdentity actor,NativeAuditRow row,
        CancellationToken ct=default);
}
