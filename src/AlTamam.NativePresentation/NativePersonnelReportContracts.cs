namespace AlTamam.NativePresentation;

/// <summary>
/// Phase 9: a strictly read-only first native slice of screen_025.
/// The canonical SQLCipher PersonnelReportService supplies all historical facts
/// by immutable PersonId; no parallel queries or report export are introduced.
/// </summary>
public interface INativePersonnelReportReader
{
    Task<NativePersonnelReportSnapshot> ReadIndividualAsync(
        NativeIdentity actor, NativeIndividualReportPages pages,
        CancellationToken ct = default);
}

public sealed record NativeIndividualReportPages(
    string PersonId, int AttendancePage = 1, int MovementPage = 1,
    int IdentityPage = 1);

public sealed record NativePersonnelReportStatusTotal(
    string StatusId, string StatusName, long Count);

public sealed record NativePersonnelReportAttendance(
    string Date, string StatusName, string SiteName, string Source,
    string? NameSnapshot, string? NumberSnapshot, long Revision)
{
    // Missing original historical identity MUST NOT be replaced by the
    // current name or number; show absence of source evidence explicitly.
    public string HistoricalNameDisplay => NameSnapshot ?? "غير موثق تاريخيا";
    public string HistoricalNumberDisplay => NumberSnapshot ?? "غير موثق تاريخيا";
}

public sealed record NativePersonnelReportMovement(
    string Type, string Source, string Destination, string Date,
    string NameSnapshot, string NumberSnapshot, string Executor);

public sealed record NativePersonnelReportIdentity(
    string Name, string ServiceNumberA, string ValidFrom,
    string? ValidTo, string Reason);

public sealed record NativePersonnelReportSnapshot(
    string PersonId, string Name, string ServiceNumberA,
    string FirstRecordedDate, bool Active,
    long AttendanceTotal, long MovementTotal, long IdentityTotal, int PageSize,
    int AttendancePage, bool AttendanceHasNext,
    int MovementPage, bool MovementHasNext,
    int IdentityPage, bool IdentityHasNext,
    IReadOnlyList<NativePersonnelReportStatusTotal> StatusTotals,
    IReadOnlyList<NativePersonnelReportAttendance> Attendance,
    IReadOnlyList<NativePersonnelReportMovement> Movements,
    IReadOnlyList<NativePersonnelReportIdentity> Identities);
