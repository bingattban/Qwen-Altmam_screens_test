using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeAuditHistoryRequest(string AuditEventId="",int Page=1)
{
    public const int PageSize=50;
    public bool IsValid=>Page is >=1 and <=100000 && AuditEventId is { Length:<=128 } &&
        AuditEventId==AuditEventId.Trim() && !AuditEventId.Any(char.IsControl);
    public bool IsAuthorized(UserRole role)=>RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(role);
}

public sealed record NativeAuditHistoryRow(string AuditEventId,string OperationCode,string RawDescription,
    string Reason,string EditedByUserId,string EditedByDisplay,string EditedAtUtc,
    string? CorrectedDescription,NativeAuditEditState Before,NativeAuditEditState After)
{
    public string ActionLabel=>After.IsTombstoned?"إخفاء":"تعديل الوصف";
    public string BeforeVisibility=>Before.IsTombstoned?"مخفي":"ظاهر";
    public string AfterVisibility=>After.IsTombstoned?"مخفي":"ظاهر";
    public bool IsValid=>AuditEventId is { Length:>0 and <=128 } &&
        !string.IsNullOrWhiteSpace(AuditEventId) && !AuditEventId.Any(char.IsControl) &&
        !string.IsNullOrWhiteSpace(OperationCode) && RawDescription is not null &&
        !string.IsNullOrWhiteSpace(Reason) && !string.IsNullOrWhiteSpace(EditedByUserId) &&
        !string.IsNullOrWhiteSpace(EditedByDisplay) && !string.IsNullOrWhiteSpace(EditedAtUtc) &&
        Before is not null && After is not null &&
        Before.Version>=0 && After.Version is >0 and <=int.MaxValue && After.Version==Before.Version+1 &&
        Before.Description is not null && After.Description is not null &&
        (Before.Version!=0 || (!Before.IsTombstoned && Before.Description==RawDescription)) &&
        (After.IsTombstoned
            ? CorrectedDescription is null && After.Description==Before.Description
            : !string.IsNullOrWhiteSpace(CorrectedDescription) && After.Description==CorrectedDescription);
}

public sealed record NativeAuditHistoryPage(NativeAuditHistoryRequest Request,IReadOnlyList<NativeAuditHistoryRow> Rows)
{
    // The original history API returns count and page, not total/hasNext.
    // A full page permits probing the next page; it may be empty.
    public bool MayHaveNext=>Rows.Count==NativeAuditHistoryRequest.PageSize && Request.Page<100000;
    public bool Matches(NativeAuditHistoryRequest expected)
    {
        if(Request!=expected || !expected.IsValid || Rows is null ||
            Rows.Count>NativeAuditHistoryRequest.PageSize) return false;
        var keys=new HashSet<(string,long)>();
        foreach(var row in Rows)
            if(row is null || !row.IsValid ||
                (expected.AuditEventId.Length!=0 && row.AuditEventId!=expected.AuditEventId) ||
                !keys.Add((row.AuditEventId,row.After.Version))) return false;
        return true;
    }
}

public interface INativeAuditHistoryReader
{
    Task<NativeAuditHistoryPage> ReadAuditHistoryAsync(NativeIdentity actor,
        NativeAuditHistoryRequest request,CancellationToken ct=default);
}
