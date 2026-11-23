namespace AlTamam.NativePresentation;

/// <summary>
/// Native WPF selects and reviews a local XLSX, but all parsing, deduplication,
/// historical identity checks and actual writes remain in the ORIGINAL
/// ExcelPersonnelImportService under the live authenticated SQLCipher user.
/// </summary>
public interface INativePersonnelExcelImportBackend
{
    Task<NativePersonnelExcelWorkbookSelection> StageWorkbookAsync(
        NativeIdentity actor, string filename, byte[] bytes, bool allSheets,
        CancellationToken ct = default);

    Task<NativePersonnelExcelImportPreview> PreviewWorkbookAsync(
        NativeIdentity actor, NativePersonnelExcelWorkbookSelection selection,
        CancellationToken ct = default);

    Task<NativePersonnelExcelImportReceipt> CommitWorkbookAsync(
        NativeIdentity actor, NativePersonnelExcelImportPreview preview,
        string operationId, CancellationToken ct = default);
}

public sealed record NativePersonnelExcelWorkbookSelection(
    string FileName, string WorkbookToken, int RowCount,
    IReadOnlyList<string> SelectedWorksheets);

public sealed record NativePersonnelExcelCandidate(
    string Worksheet, int Row, string Name, string ServiceNumberA,
    string SiteName);

public sealed record NativePersonnelExcelIssue(
    string Worksheet, int Row, string ServiceNumberA, string Name,
    string SiteName, string Reason);

public sealed record NativePersonnelExcelSite(string Name, int PersonCount);

public sealed record NativePersonnelExcelImportPreview(
    string FileName, string WorkbookToken, string PreviewToken,
    int TotalCount, int NewCount, int IssueCount, int CustomUpdateCount,
    int SimilarNameCount, IReadOnlyList<NativePersonnelExcelCandidate> AcceptedRows,
    IReadOnlyList<NativePersonnelExcelIssue> Issues,
    IReadOnlyList<NativePersonnelExcelSite> NewSites,
    bool RequiresExplicitConfirmation);

public sealed record NativePersonnelExcelImportReceipt(
    string ImportBatchId, int CreatedCount, int RejectedCount,
    int CreatedSitesCount, int CustomUpdatedCount, string CommittedAtUtc);
