namespace AlTamam.NativePresentation;

/// <summary>
/// Read-only boundary to original PersonnelSeparationService.ArchiveAsync/DetailsAsync.
/// No native personnel writes, changes of identity or restoration are exposed.
/// </summary>
public interface INativeSeparationArchiveBackend
{
    Task<NativeSeparationArchivePage> ReadSeparationsAsync(
        NativeIdentity actor, NativeSeparationArchiveFilter filter,
        CancellationToken ct = default);

    Task<NativeSeparationArchiveDetail> ReadSeparationDetailsAsync(
        NativeIdentity actor, string movementId, CancellationToken ct = default);
}

public sealed record NativeSeparationArchiveFilter(
    string SearchTerm, string SiteId, int Page, int PageSize = 50);

public sealed record NativeSeparationArchivePage(
    NativeSeparationArchiveFilter Filter, long Total, bool HasNext,
    IReadOnlyList<NativeSeparationArchiveRow> Rows);

public sealed record NativeSeparationArchiveRow(
    string MovementId, string PersonId, string Name, string ServiceNumberA,
    string SiteName, string Reason, string EffectiveDate, string CommittedAtUtc,
    string ActorUserId, string ActorDisplay);

public sealed record NativeSeparationArchiveDetail(
    string MovementId, string PersonId, string Name, string ServiceNumberA,
    string SiteName, string Reason, string EffectiveDate, string CommittedAtUtc,
    string ActorUserId);
