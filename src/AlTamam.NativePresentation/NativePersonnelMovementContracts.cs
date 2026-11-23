namespace AlTamam.NativePresentation;

/// <summary>
/// The only movement boundary is the original PersonnelMovementService:
/// actor-bound one-use SQLCipher preview followed by an explicit commit.
/// </summary>
public interface INativePersonnelMovementBackend
{
    Task<NativePersonnelMovementPreview> PreviewMovementAsync(
        NativeIdentity actor, NativePersonnelMovementDraft draft,
        CancellationToken ct = default);

    Task<NativePersonnelMovementReceipt> CommitMovementAsync(
        NativeIdentity actor, NativePersonnelMovementPreview preview,
        string operationId, CancellationToken ct = default);
}

public sealed record NativePersonnelMovementDraft(
    bool IsExternal, string PersonId, string PersonName,
    string ServiceNumberA, long Revision, string SourceSiteId,
    string SourceSiteName, string DestinationSiteId,
    string ExternalDestinationName);

public sealed record NativePersonnelMovementPerson(
    string PersonId, string Name, string ServiceNumberA,
    long Revision, string SourceSiteId, string SourceSiteName,
    string SourceAssignmentId);

public sealed record NativePersonnelMovementPreview(
    NativePersonnelMovementDraft Draft, string BusinessDate,
    string PreviewToken, string DestinationName,
    IReadOnlyList<NativePersonnelMovementPerson> Persons);

public sealed record NativePersonnelMovementReceipt(
    bool IsExternal, string BusinessDate, int Count,
    string DestinationName);
