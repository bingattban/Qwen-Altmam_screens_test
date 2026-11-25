namespace AlTamam.NativePresentation;

/// <summary>
/// Native read-only interface into the SAME canonical SQLCipher
/// PersonnelReportService.ReportAsync used by individual history.
/// A group is explicitly selected by immutable PersonIds (1..100).
/// </summary>
public interface INativePersonnelGroupReportReader
{
    Task<NativePersonnelGroupReportSnapshot> ReadGroupAsync(
        NativeIdentity actor, NativePersonnelGroupReportRequest request,
        CancellationToken ct = default);
}

/// <summary>No auto-select-all, no current-name or service-number identity.</summary>
public sealed record NativePersonnelGroupReportRequest(
    IReadOnlyList<string> PersonIds, string DateFrom = "", string DateTo = "",
    int MovementType = 0, int SummaryPage = 1,
    string SiteId = "", string StatusId = "");

/// <summary>
/// The original group SQLCipher query supplies lifetime current-visible
/// identity separately from committed historical attendance/status totals.
/// </summary>
public sealed record NativePersonnelGroupPerson(
    string PersonId, string Name, string ServiceNumberA,
    string FirstRecordedDate, int OrganizationalStatus, bool Active,
    long AttendanceTotal, long MovementTotal,
    IReadOnlyList<NativePersonnelReportStatusTotal> StatusTotals)
{
    public IReadOnlyList<NativeGroupCustomField> CustomFields { get; init; } = [];
    public string Rank => CustomFields.FirstOrDefault(field =>
        field.Caption.Trim() is "الرتبة" or "رتبة")?.Value ?? "";
}

// Current visible values, never reconstructed historical values.
public sealed record NativeGroupCustomField(string ColumnId, string Caption, string Value);

public sealed record NativePersonnelGroupDailyStatus(
    string Date, string StatusId, string StatusName, long Count,
    long PersonCount);

public sealed record NativePersonnelGroupReportSnapshot(
    IReadOnlyList<string> PersonIds, int PersonCount,
    long AttendanceTotal, long MovementTotal,
    string DateFrom, string DateTo, string SiteId, string StatusId,
    int MovementType, int DailyStatusPage, bool DailyStatusHasNext,
    IReadOnlyList<NativePersonnelGroupPerson> Persons,
    IReadOnlyList<NativePersonnelReportStatusTotal> StatusTotals,
    IReadOnlyList<NativePersonnelGroupDailyStatus> DailyStatuses)
{
    // Full historical choices for the selected immutable group, not the
    // current attendance page or the active filtered result.
    public IReadOnlyList<NativeDashboardColumn> CustomColumns { get; init; } = [];
    public IReadOnlyList<NativeReportFilterOption> AvailableSites { get; init; } = [];
    public IReadOnlyList<NativeReportFilterOption> AvailableStatuses { get; init; } = [];
}
