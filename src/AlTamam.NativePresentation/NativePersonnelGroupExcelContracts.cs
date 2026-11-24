namespace AlTamam.NativePresentation;

/// <summary>
/// An operator-approved, local XLSX export of the SAME canonical group report.
/// This is not the monthly-roster Excel exporter: group history has its own
/// person selection, effective-dated status evidence and daily summary pages.
/// </summary>
public interface INativePersonnelGroupExcelBackend
{
    Task<NativeGroupExcelPreview> PreviewGroupExcelAsync(
        NativeIdentity actor, NativePersonnelGroupReportRequest request,
        CancellationToken ct = default);
    Task<NativeGroupExcelReceipt> ExportGroupExcelAsync(
        NativeIdentity actor, NativePersonnelGroupReportRequest request,
        string fingerprint, CancellationToken ct = default);
}

public sealed record NativeGroupExcelPreview(
    IReadOnlyList<string> PersonIds, string DateFrom, string DateTo,
    string SiteId, string StatusId, int MovementType,
    int PersonCount, long AttendanceTotal, long MovementTotal,
    string Fingerprint, string SuggestedFileName);

public sealed record NativeGroupExcelReceipt(
    string FilePath, int PersonCount, long AttendanceTotal,
    long MovementTotal, string Fingerprint, int SheetCount,
    long DailyRows);
