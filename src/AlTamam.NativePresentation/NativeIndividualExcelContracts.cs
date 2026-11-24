namespace AlTamam.NativePresentation;

/// <summary>
/// Phase 9 individual historical XLSX: separate approval, original immutable
/// PersonId source, exact applied site/status/date/movement filters.
/// </summary>
public interface INativePersonnelIndividualExcelBackend
{
    Task<NativeIndividualExcelPreview> PreviewAsync(
        NativeIdentity actor, NativeIndividualReportPages request,
        CancellationToken ct = default);
    Task<NativeIndividualExcelReceipt> ExportAsync(
        NativeIdentity actor, NativeIndividualReportPages request,
        string fingerprint, CancellationToken ct = default);
}

public sealed record NativeIndividualExcelPreview(
    string PersonId, string Name, string ServiceNumberA,
    string DateFrom, string DateTo, string SiteId, string StatusId,
    int MovementType,
    long AttendanceTotal, long MovementTotal, long IdentityTotal,
    long SiteTotal, long RevisionTotal, long AuditTotal,
    string Fingerprint, string SuggestedFileName);

public sealed record NativeIndividualExcelReceipt(
    string FilePath, string Fingerprint, long AttendanceRows,
    long MovementRows, long IdentityRows, long SiteRows,
    long RevisionRows, long AuditRows, int SheetCount);
