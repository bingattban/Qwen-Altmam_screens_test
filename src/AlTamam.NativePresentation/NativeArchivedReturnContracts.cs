namespace AlTamam.NativePresentation;

/// <summary>
/// Native archived-person return MUST delegate to original ArchivedReturnService.
/// The selected historical PersonId never changes; there is no "new person" shortcut.
/// </summary>
public interface INativeArchivedReturnBackend
{
    Task<NativeArchivedReturnPreview> PreviewReturnAsync(
        NativeIdentity actor, NativeArchivedReturnDraft draft,
        CancellationToken ct = default);

    Task<NativeArchivedReturnReceipt> CommitReturnAsync(
        NativeIdentity actor, NativeArchivedReturnPreview preview,
        string operationId, CancellationToken ct = default);
}

public sealed record NativeArchivedReturnDraft(
    string PersonId, string OriginalName, string ServiceNumberA,
    long Revision, int OrganizationalStatus,
    string DestinationSiteId, string DestinationSiteName);

public sealed record NativeArchivedReturnCandidate(
    string PersonId, string Name, string ServiceNumberA, long Revision,
    int OrganizationalStatus, string ExitDate, string PreviousSiteName,
    string ExitType, bool NumberMatch, bool NameMatch);

public sealed record NativeArchivedReturnPreview(
    NativeArchivedReturnDraft Draft, string BusinessDate, string PreviewToken,
    bool NumberContested, IReadOnlyList<NativeArchivedReturnCandidate> Candidates);

public sealed record NativeArchivedReturnReceipt(
    string PersonId, string MovementId, string SiteId, string BusinessDate,
    long Revision, bool Returned);
