using System.Globalization;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

/// <summary>Read-only audit query; no overlay mutation or independent SQL writer.</summary>
public sealed record NativeAuditFilter(string Term="",string Actor="",string Target="",
    string Operation="",string FromDate="",string ToDate="",bool IncludeTombstoned=false,int Page=1)
{
    public const int PageSize=50;
    private static bool ValidText(string? value) => value is { Length: <= 200 } &&
        value.Trim()==value && !value.Any(char.IsControl);
    private static bool ValidDate(string? value) => value is not null &&
        (value.Length==0 || DateOnly.TryParseExact(value,"yyyy-MM-dd",
            CultureInfo.InvariantCulture,DateTimeStyles.None,out _));

    public bool IsValid => Page is >= 1 and <= 100000 &&
        ValidText(Term) && ValidText(Actor) && ValidText(Target) && ValidText(Operation) &&
        ValidDate(FromDate) && ValidDate(ToDate) &&
        (FromDate.Length==0 || ToDate.Length==0 || string.CompareOrdinal(FromDate,ToDate)<=0);

    public bool IsAuthorized(UserRole role) => RoleAuthorizationPolicy.CanPerformStandardOperations(role) &&
        (!IncludeTombstoned || RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(role));
}

public sealed record NativeAuditRow(string AuditEventId,long Sequence,string OperationCode,
    string OperationName,string ActorDisplay,string TargetDisplay,string Description,
    string RawDescription,string Reason,string EffectiveLocalDate,string ExecutedAtUtc,
    string Outcome,string EventHash,bool IsTombstoned,int OverlayVersion)
{
    public bool IsValid => AuditEventId is { Length: >= 1 and <= 128 } &&
        !AuditEventId.Any(char.IsControl) && !string.IsNullOrWhiteSpace(AuditEventId) &&
        Sequence>0 && !string.IsNullOrWhiteSpace(OperationCode) &&
        !string.IsNullOrWhiteSpace(OperationName) &&
        ActorDisplay is not null && TargetDisplay is not null &&
        Description is not null && RawDescription is not null && Reason is not null &&
        !string.IsNullOrWhiteSpace(EffectiveLocalDate) && !string.IsNullOrWhiteSpace(ExecutedAtUtc) &&
        !string.IsNullOrWhiteSpace(Outcome) && EventHash is { Length: 64 } &&
        EventHash.All(Uri.IsHexDigit) && OverlayVersion>=0 &&
        (!IsTombstoned || OverlayVersion>0);
}

public sealed record NativeAuditSnapshot(NativeAuditFilter Filter,long Total,bool HasNext,
    IReadOnlyList<NativeAuditRow> Rows)
{
    public bool Matches(NativeAuditFilter expected)
    {
        if (Filter!=expected || !expected.IsValid || Total<0 || Rows is null ||
            Rows.Count>NativeAuditFilter.PageSize) return false;
        long offset=(long)(expected.Page-1)*NativeAuditFilter.PageSize;
        if (Rows.Count!=Math.Clamp(Total-offset,0L,NativeAuditFilter.PageSize) ||
            HasNext!=(offset+NativeAuditFilter.PageSize<Total)) return false;
        var ids=new HashSet<string>(StringComparer.Ordinal);
        for (int i=0;i<Rows.Count;i++)
        {
            var row=Rows[i];
            if (row is null || !row.IsValid || row.Sequence!=offset+i+1 ||
                (!expected.IncludeTombstoned && row.IsTombstoned) ||
                !ids.Add(row.AuditEventId)) return false;
        }
        return true;
    }
}

public interface INativeAuditReader
{
    Task<NativeAuditSnapshot> ReadAuditAsync(NativeIdentity actor,NativeAuditFilter filter,
        CancellationToken ct=default);
}
