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
    int IdentityPage = 1, int SitePage = 1, int RevisionPage = 1,
    int AuditPage = 1, string DateFrom = "", string DateTo = "");

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


/// <summary>
/// Original effective-dated PersonAssignments and SiteNameHistory evidence;
/// SiteId remains stable even when a visible site name later changes.
/// </summary>
public sealed record NativePersonnelReportSite(
    string SiteId, string SiteName, string ValidFrom,
    string? ValidTo, string CreatedAtUtc);

/// <summary>
/// Original append-only AttendanceRevisions evidence, never a synthesized
/// interpretation of the current AttendanceRecords status.
/// </summary>
public sealed record NativePersonnelReportRevision(
    string Date, string Before, string After, string? Reason,
    string ExecutedAtUtc, string? AuthorizationMethod,
    string ExecutorSnapshot);

/// <summary>Source monthly status aggregates; not recalculated from a single UI page.</summary>
public sealed record NativePersonnelReportMonthSummary(
    string Period, string StatusId, string StatusName, long Count);

/// <summary>Saved original AuditEvents actor snapshots and immutable operation detail.</summary>
public sealed record NativePersonnelReportAuditRecord(
    string Operation, string UsernameSnapshot, string FullNameSnapshot,
    string? EffectiveDate, string ExecutedAtUtc,
    string Description, string? Reason);

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
    IReadOnlyList<NativePersonnelReportIdentity> Identities)
{
    // Additional read-only evidence from the SAME canonical report service.
    // Defaults preserve first-slice tests and older independent fakes.
    public long SiteTotal { get; init; }
    public int SitePage { get; init; } = 1;
    public bool SiteHasNext { get; init; }
    public IReadOnlyList<NativePersonnelReportSite> Sites { get; init; } = [];

    public long RevisionTotal { get; init; }
    public int RevisionPage { get; init; } = 1;
    public bool RevisionHasNext { get; init; }
    public IReadOnlyList<NativePersonnelReportRevision> Revisions { get; init; } = [];

    public IReadOnlyList<NativePersonnelReportMonthSummary> MonthlyTotals { get; init; } = [];
    public long AuditTotal { get; init; }
    public int AuditPage { get; init; } = 1;
    public bool AuditHasNext { get; init; }
    public IReadOnlyList<NativePersonnelReportAuditRecord> Audit { get; init; } = [];

    // Source-echoed inclusive ISO dates. Empty denotes unbounded search.
    // Older test fakes are intentionally unfiltered by default.
    public string DateFrom { get; init; } = "";
    public string DateTo { get; init; } = "";
}
