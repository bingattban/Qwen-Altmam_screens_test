namespace AlTamam.NativePresentation;

/// <summary>
/// This is only a typed adapter for canonical PersonRosterService identity
/// mutation and ArchivedReturnService identity-collision preview. The WPF
/// presentation layer may never access SQLite or invent a PersonId.
/// </summary>
public interface INativePersonnelMutationBackend
{
    Task<NativePersonnelMutationPreview> PreviewAsync(
        NativeIdentity actor, NativePersonnelMutationDraft draft,
        CancellationToken ct = default);

    Task<NativePersonnelMutationReceipt> CommitAsync(
        NativeIdentity actor, NativePersonnelMutationPreview preview,
        string operationId, CancellationToken ct = default);
}

public sealed record NativePersonnelMutationDraft(
    bool IsCreate, string PersonId, long BaseRevision,
    string OriginalName, string OriginalNumber,
    string Name, string Number, string SiteId, string Reason);

public sealed record NativePersonnelMutationPreview(
    NativePersonnelMutationDraft Draft,
    string CanonicalNumber, string EffectiveLocalDate, string PreviewToken,
    bool RequiresIdentityResolution, int CandidateCount,
    string OldName, string OldNumber);

public sealed record NativePersonnelMutationReceipt(
    bool IsCreate, string PersonId, long Revision);
