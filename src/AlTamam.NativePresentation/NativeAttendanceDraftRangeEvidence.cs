namespace AlTamam.NativePresentation;

/// <summary>
/// Exact persisted evidence required before range drafts may be restored.
/// Missing legacy evidence is not inferred from the business date or status.
/// </summary>
public sealed record NativeAttendanceDraftRangeEvidence(string StartDate,string EndDate,bool IsInitial)
{
    public bool IsValid(string statusId,DateOnly businessDate)=>
        NativeAttendanceDateRange.RequiresRange(statusId) &&
        NativeAttendanceDateRange.TryCreate(statusId,businessDate,StartDate,EndDate,
            out var range,out _) && range is not null &&
        range.StartDate==StartDate && range.EndDate==EndDate;

    public bool Matches(NativeAttendanceDraftRangeEvidence? other)=>
        other is not null && StartDate==other.StartDate && EndDate==other.EndDate &&
        IsInitial==other.IsInitial;
}
