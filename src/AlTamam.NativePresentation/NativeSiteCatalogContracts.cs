namespace AlTamam.NativePresentation;

/// <summary>
/// Directly delegates to original BridgeSharedServices.ListSitesAsync and
/// AdminConfigurationService.CreateSiteAsync/RenameSiteAsync. No native SQL.
/// Site retirement/reassignment is deliberately NOT exposed in this slice.
/// </summary>
public interface INativeSiteCatalogBackend
{
    Task<IReadOnlyList<NativeSiteRow>> ReadSitesAsync(
        NativeIdentity actor, CancellationToken ct = default);

    Task<NativeSiteMutationReceipt> CreateSiteAsync(
        NativeIdentity actor, string name, string operationId,
        CancellationToken ct = default);

    Task<NativeSiteMutationReceipt> RenameSiteAsync(
        NativeIdentity actor, string siteId, long revision, string name,
        string operationId, CancellationToken ct = default);
}

public sealed record NativeSiteRow(
    string SiteId, string Name, bool Active, long Revision);

public sealed record NativeSiteMutationReceipt(
    string SiteId, string Name, long Revision);
