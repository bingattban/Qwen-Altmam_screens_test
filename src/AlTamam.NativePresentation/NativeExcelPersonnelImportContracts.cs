namespace AlTamam.NativePresentation;

/// <summary>
/// Original ExcelPersonnelImportService is the ONLY workbook parser,
/// duplicate-name/number analyzer and audited SQLCipher writer.
/// This pilot is strictly additive: existing persons/custom fields are NEVER updated.
/// </summary>
public interface INativeExcelPersonnelImportBackend
{
    Task<NativeExcelImportReview> StageAndPreviewAsync(
        NativeIdentity actor, string fileName, byte[] workbook,
        CancellationToken ct = default);

    Task<NativeExcelImportReceipt> CommitAsync(
        NativeIdentity actor, NativeExcelImportReview reviewed,
        string operationId, CancellationToken ct = default);
}

public sealed record NativeExcelImportPerson(
    string Worksheet, int Row, string Name,
    string ServiceNumberA, string SiteName);

public sealed record NativeExcelImportIssue(
    string Worksheet, int Row, string ServiceNumberA,
    string Name, string SiteName, string Reason);

public sealed record NativeExcelImportSite(string Name, int PersonCount);

public sealed record NativeExcelImportReview(
    string FileName, string Sha256, string PreviewToken,
    int TotalRows, int NewCount, int RejectedCount, int CustomUpdateCount,
    IReadOnlyList<NativeExcelImportSite> NewSites, int SimilarNameCount,
    IReadOnlyList<NativeExcelImportPerson> NewPeople,
    IReadOnlyList<NativeExcelImportIssue> Issues);

public sealed record NativeExcelImportReceipt(
    string ImportBatchId, int CreatedCount, int RejectedCount,
    int CreatedSitesCount, int CustomUpdatedCount, string CommittedAtUtc);
