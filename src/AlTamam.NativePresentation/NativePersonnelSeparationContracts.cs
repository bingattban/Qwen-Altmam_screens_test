namespace AlTamam.NativePresentation;

/// <summary>
/// Canonical audited dismissal: PersonnelSeparationService PreviewAsync/CommitAsync only.
/// Native UI cannot update, delete or rewrite SQLCipher history directly.
/// </summary>
public interface INativePersonnelSeparationBackend
{
    Task<NativePersonnelSeparationPreview> PreviewSeparationAsync(
        NativeIdentity actor, NativePersonnelSeparationDraft draft,
        CancellationToken ct = default);
    Task<NativePersonnelSeparationReceipt> CommitSeparationAsync(
        NativeIdentity actor, NativePersonnelSeparationPreview preview,
        string operationId, CancellationToken ct = default);
}

public sealed record NativePersonnelSeparationDraft(
    string PersonId, string Name, string ServiceNumberA, long Revision,
    string SourceSiteId, string SourceSiteName, string Reason);

public sealed record NativePersonnelSeparationPreview(
    NativePersonnelSeparationDraft Draft, string BusinessDate,
    string PreviewToken, string SourceAssignmentId,
    int MinimumConfirmationSeconds);

public sealed record NativePersonnelSeparationReceipt(
    string PersonId, string MovementId, string BusinessDate, bool Separated);
