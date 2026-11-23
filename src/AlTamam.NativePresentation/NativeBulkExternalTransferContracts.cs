namespace AlTamam.NativePresentation;

/// <summary>
/// A destructive external exit is ONLY committed through original
/// PersonnelMovementService. External transfer also suspends linked
/// user accounts and revokes all linked active sessions atomically.
/// </summary>
public interface INativeBulkExternalTransferBackend
{
    Task<NativeBulkExternalTransferPreview> PreviewExternalBatchAsync(
        NativeIdentity actor, NativeBulkExternalTransferDraft draft,
        CancellationToken ct = default);

    Task<NativeBulkExternalTransferReceipt> CommitExternalBatchAsync(
        NativeIdentity actor, NativeBulkExternalTransferPreview preview,
        string operationId, CancellationToken ct = default);
}

public sealed record NativeBulkExternalTransferDraft(
    string SourceSiteId, string SourceSiteName,
    string ExternalDestinationName,
    IReadOnlyList<NativeBulkInternalPerson> Persons);

public sealed record NativeBulkExternalTransferPreview(
    NativeBulkExternalTransferDraft Draft,
    string BusinessDate, string PreviewToken,
    string DestinationName, bool IsExternal,
    IReadOnlyList<NativePersonnelMovementPerson> Persons);

public sealed record NativeBulkExternalTransferReceipt(
    string BusinessDate, int Count, string DestinationName, bool IsExternal);
