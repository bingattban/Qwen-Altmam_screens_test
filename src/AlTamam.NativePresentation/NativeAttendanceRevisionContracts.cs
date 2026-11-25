namespace AlTamam.NativePresentation;

/// <summary>
/// Explicit user-authorized attendance revision. The native host delegates
/// every operation to the existing audited AttendanceCommitService and
/// receives the principal from live SQLCipher session verification.
/// </summary>
public interface INativeAttendanceReviewBackend
{
    Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
        NativeIdentity actor, CancellationToken ct = default);

    Task<NativeAttendanceRevisionPreview> PreviewAsync(
        NativeIdentity actor, DateOnly date, string site,
        IReadOnlyList<string> personIds, string statusId,
        CancellationToken ct = default,
        NativeAttendanceDateRange? range = null);

    Task<NativeAttendanceCommitReceipt> CommitAsync(
        NativeIdentity actor, string previewToken, string operationId,
        CancellationToken ct = default);

    // Existing revision-only backends fail closed on initial operations.
    Task<NativeAttendanceRevisionPreview> PreviewInitialAsync(
        NativeIdentity actor, DateOnly date, string site,
        IReadOnlyList<string> personIds, string statusId,
        CancellationToken ct = default, NativeAttendanceDateRange? range = null) =>
        Task.FromException<NativeAttendanceRevisionPreview>(
            new NotSupportedException("Initial attendance is not available."));

    Task<NativeAttendanceCommitReceipt> CommitInitialAsync(
        NativeIdentity actor, string previewToken, string operationId,
        CancellationToken ct = default) =>
        Task.FromException<NativeAttendanceCommitReceipt>(
            new NotSupportedException("Initial attendance is not available."));
}

public sealed record NativeAttendanceStatusChoice(string StatusId, string DisplayName);

public sealed record NativeAttendancePreviewPerson(
    string PersonId, string Name, string ServiceNumberA,
    string SiteName, string PreviousStatusName, string PreviousStatusId);

public sealed record NativeAttendanceRevisionPreview(
    string PreviewToken, DateOnly BusinessDate, string TargetStatusId,
    string TargetStatusName, IReadOnlyList<NativeAttendancePreviewPerson> Persons,
    string? RangeStartDate = null, string? RangeEndDate = null)
{
    public bool IsInitial { get; init; }
}

public sealed record NativeAttendanceCommitReceipt(int Count, string AuditEventId);
