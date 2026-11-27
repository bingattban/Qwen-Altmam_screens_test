namespace AlTamam.NativePresentation;

public sealed record NativeAttendanceDraftIntent(string DraftId,DateOnly BusinessDate,string SiteId,
    string TargetStatusId,IReadOnlyList<string> PersonIds)
{
    public NativeAttendanceDraftRangeEvidence? RangeEvidence {get;init;}
    public bool IsValid(DateOnly today)=>BusinessDate==today &&
        NativeAttendanceDateRange.RequiresRange(TargetStatusId)==(RangeEvidence is not null) &&
        new NativeAttendanceDraft(DraftId,BusinessDate,SiteId,TargetStatusId,
            DateTimeOffset.UnixEpoch,PersonIds){RangeEvidence=RangeEvidence}.IsValid;

    public bool Matches(NativeAttendanceDraft? receipt)=>receipt is not null && receipt.IsValid &&
        receipt.DraftId==DraftId && receipt.BusinessDate==BusinessDate &&
        receipt.SiteId==SiteId && receipt.TargetStatusId==TargetStatusId &&
        (RangeEvidence is null?receipt.RangeEvidence is null:RangeEvidence.Matches(receipt.RangeEvidence)) &&
        receipt.PersonIds.Order(StringComparer.Ordinal).SequenceEqual(PersonIds.Order(StringComparer.Ordinal),StringComparer.Ordinal);
}

public interface INativeAttendanceDraftWriter
{
    Task<NativeAttendanceDraft> SaveAttendanceDraftAsync(NativeIdentity actor,NativeAttendanceDraftIntent intent,CancellationToken ct=default);
}
