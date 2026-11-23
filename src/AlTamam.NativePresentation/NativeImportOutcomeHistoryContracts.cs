namespace AlTamam.NativePresentation;

/// <summary>
/// Read-only projection of the original ExcelImportOutcomeService.QueryAsync.
/// The canonical service, not the native client, enforces CreatedByUserId ownership.
/// </summary>
public interface INativeImportOutcomeHistoryBackend
{
    Task<IReadOnlyList<NativeImportBatchRow>> ReadRecentAsync(
        NativeIdentity actor, CancellationToken ct = default);
    Task<NativeImportBatchDetail> ReadBatchAsync(
        NativeIdentity actor, string batchId, CancellationToken ct = default);
    // Optional backward-compatible native export. Existing read-only test
    // backends and callers must fail closed instead of silently creating files.
    Task<NativeImportIssuesExportReceipt> ExportIssuesAsync(
        NativeIdentity actor, string batchId, CancellationToken ct = default) =>
        throw new NotSupportedException("Original saved import issue export unavailable.");
}

public sealed record NativeImportBatchRow(
    string ImportBatchId, string FileName, int TotalRows,
    int AcceptedRows, int RejectedRows, string Status, string CommittedAtUtc);

public sealed record NativeImportBatchIssue(
    int Row, string ServiceNumberA, string Name, string SiteName, string Reason);

public sealed record NativeImportBatchDetail(
    NativeImportBatchRow Batch, IReadOnlyList<NativeImportBatchIssue> Issues);

/// <summary>Original verified XLSX receipt. File paths are backend output only.</summary>
public sealed record NativeImportIssuesExportReceipt(
    string ImportBatchId, string FileName, string FilePath,
    int IssueCount, string Format);
