namespace AlTamam.NativePresentation;

/// <summary>
/// Native WPF status catalogue. Reads only canonical active definitions and
/// delegates create/update/disable to original AdminConfigurationService.
/// No second SQL writer or deletion of historical attendance snapshots.
/// </summary>
public interface INativeStatusCatalogBackend
{
    Task<IReadOnlyList<NativeStatusRow>> ReadActiveStatusesAsync(
        NativeIdentity actor, CancellationToken ct = default);

    Task<NativeStatusMutationReceipt> CreateStatusAsync(
        NativeIdentity actor, string name, string symbol, string color,
        string operationId, CancellationToken ct = default);

    Task<NativeStatusMutationReceipt> UpdateStatusAsync(
        NativeIdentity actor, string statusId, long revision,
        string name, string symbol, string color, string operationId,
        CancellationToken ct = default);

    Task<NativeStatusMutationReceipt> DeactivateStatusAsync(
        NativeIdentity actor, string statusId, long revision,
        string operationId, CancellationToken ct = default);
}

public sealed record NativeStatusRow(
    string StatusId, string Name, string Symbol, string Color,
    long Revision, long TodayCount);

public sealed record NativeStatusMutationReceipt(
    string StatusId, string Name, string Symbol, string Color,
    long Revision, bool Active, long TodayCount);
