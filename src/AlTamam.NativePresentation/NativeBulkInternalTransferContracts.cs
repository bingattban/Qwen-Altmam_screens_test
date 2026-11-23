namespace AlTamam.NativePresentation;

/// <summary>
/// Internal bulk transfer uses ONLY the original PersonnelMovementService
/// actor-bound preview and atomic commit. Selection is explicit from one
/// CURRENT directory page, never from all filtered or hidden personnel.
/// </summary>
public interface INativeBulkInternalTransferBackend
{
    Task<NativeBulkInternalTransferPreview> PreviewInternalBatchAsync(
        NativeIdentity actor, NativeBulkInternalTransferDraft draft,
        CancellationToken ct = default);

    Task<NativeBulkInternalTransferReceipt> CommitInternalBatchAsync(
        NativeIdentity actor, NativeBulkInternalTransferPreview preview,
        string operationId, CancellationToken ct = default);
}

public sealed record NativeBulkInternalPerson(
    string PersonId, string Name, string ServiceNumberA, long Revision);

public sealed record NativeBulkInternalTransferDraft(
    string SourceSiteId, string SourceSiteName, string DestinationSiteId,
    IReadOnlyList<NativeBulkInternalPerson> Persons);

public sealed record NativeBulkInternalTransferPreview(
    NativeBulkInternalTransferDraft Draft,
    string BusinessDate, string PreviewToken, string DestinationName,
    IReadOnlyList<NativePersonnelMovementPerson> Persons);

public sealed record NativeBulkInternalTransferReceipt(
    string BusinessDate, int Count, string DestinationName);
