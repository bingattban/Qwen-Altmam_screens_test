namespace AlTamam.NativePresentation;

/// <summary>
/// Read-only boundary to the original PersonnelMovementService.ArchiveAsync
/// and DetailsAsync. No native movement SQL or mutation is permitted.
/// </summary>
public interface INativeMovementArchiveBackend
{
    Task<NativeMovementArchivePage> ReadMovementsAsync(
        NativeIdentity actor, NativeMovementArchiveFilter filter,
        CancellationToken ct = default);

    Task<NativeMovementArchiveDetail> ReadMovementDetailsAsync(
        NativeIdentity actor, string movementId, CancellationToken ct = default);
}

public sealed record NativeMovementArchiveFilter(
    string SearchTerm, string Type, int Page, int PageSize = 50);

public sealed record NativeMovementArchivePage(
    NativeMovementArchiveFilter Filter, long Total, bool HasNext,
    IReadOnlyList<NativeMovementArchiveRow> Rows);

public sealed record NativeMovementArchiveRow(
    string MovementId, string PersonId, string Name, string ServiceNumberA,
    string Type, string SourceSite, string Destination,
    string EffectiveDate, string CommittedAtUtc);

public sealed record NativeMovementArchiveDetail(
    string MovementId, string PersonId, string Name, string ServiceNumberA,
    string Type, string SourceSite, string Destination,
    string EffectiveDate, string CommittedAtUtc, string ActorUsername);

public sealed record NativeMovementArchiveTypeOption(string Value, string Label);
