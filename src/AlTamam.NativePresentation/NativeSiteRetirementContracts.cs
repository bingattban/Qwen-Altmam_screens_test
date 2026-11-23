namespace AlTamam.NativePresentation;

/// <summary>
/// No site deletion in native WPF: original AdminConfigurationService owns
/// read-only retirement impact and audited atomic soft-retire/reassignment.
/// The destination is ALWAYS canonical SITE-UNSPECIFIED, not user-supplied.
/// </summary>
public interface INativeSiteRetirementBackend
{
    Task<NativeSiteRetirementPreview> PreviewRetirementAsync(
        NativeIdentity actor, string siteId, long revision,
        CancellationToken ct = default);

    Task<NativeSiteRetirementReceipt> CommitRetirementAsync(
        NativeIdentity actor, NativeSiteRetirementPreview preview,
        string operationId, CancellationToken ct = default);
}

public sealed record NativeSiteRetirementPreview(
    string SiteId, string SiteName, long Revision, long ActiveCount,
    string EffectiveDate, string DestinationSiteId,
    string DestinationSiteName, bool Eligible, string BlockReason);

public sealed record NativeSiteRetirementReceipt(
    string SiteId, string SiteName, long Revision, long ReassignedCount,
    string EffectiveDate, string DestinationSiteId, bool Active);
