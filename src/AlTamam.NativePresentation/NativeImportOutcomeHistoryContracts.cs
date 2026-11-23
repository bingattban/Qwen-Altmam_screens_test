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
}

public sealed record NativeImportBatchRow(
    string ImportBatchId, string FileName, int TotalRows,
    int AcceptedRows, int RejectedRows, string Status, string CommittedAtUtc);

public sealed record NativeImportBatchIssue(
    int Row, string ServiceNumberA, string Name, string SiteName, string Reason);

public sealed record NativeImportBatchDetail(
    NativeImportBatchRow Batch, IReadOnlyList<NativeImportBatchIssue> Issues);
