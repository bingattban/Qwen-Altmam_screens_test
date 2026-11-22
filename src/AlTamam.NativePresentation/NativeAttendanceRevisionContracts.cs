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
        CancellationToken ct = default, NativeAttendanceDateRange? range = null);

    Task<NativeAttendanceCommitReceipt> CommitAsync(
        NativeIdentity actor, string previewToken, string operationId,
        CancellationToken ct = default);
}

/// <summary>Only the canonical backend can mark a persisted system status as requiring a range.</summary>
public sealed record NativeAttendanceStatusChoice(string StatusId, string DisplayName,
    bool RequiresDateRange = false);

/// <summary>Inclusive local business dates, never an unscheduled future-only change.</summary>
public sealed record NativeAttendanceDateRange(DateOnly Start, DateOnly End);

public sealed record NativeAttendancePreviewPerson(
    string PersonId, string Name, string ServiceNumberA,
    string SiteName, string PreviousStatusName, string PreviousStatusId);

public sealed record NativeAttendanceRevisionPreview(
    string PreviewToken, DateOnly BusinessDate, string TargetStatusId,
    string TargetStatusName, IReadOnlyList<NativeAttendancePreviewPerson> Persons,
    NativeAttendanceDateRange? DateRange = null);

public sealed record NativeAttendanceCommitReceipt(int Count, string AuditEventId);
