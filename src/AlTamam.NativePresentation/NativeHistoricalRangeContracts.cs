namespace AlTamam.NativePresentation;

/// <summary>Independent range mode; do not reuse single closed-day assumptions.</summary>
public sealed record NativeHistoricalRangeRequest(
    string PersonId, DateOnly From, DateOnly To, string StatusId, string Reason)
{
    public int SpanDays => To.DayNumber - From.DayNumber + 1;

    public bool IsValid(DateOnly today) =>
        IsIdentifier(PersonId) && IsIdentifier(StatusId) &&
        SpanDays is >= 1 and <= 62 && To <= today &&
        Reason is { Length: >= 10 and <= 400 } &&
        Reason.Trim() == Reason && !Reason.Any(char.IsControl);

    internal static bool IsIdentifier(string? value) =>
        value is { Length: >= 1 and <= 128 } &&
        !string.IsNullOrWhiteSpace(value) && value.Trim() == value &&
        !value.Any(char.IsControl);
}

public sealed record NativeHistoricalRangeDay(
    DateOnly BusinessDate, bool HasExistingRecord, long PreviousRevision,
    string AssignmentId, string SiteId, string SiteName,
    string BeforeStatusId, string BeforeStatusName,
    string AfterStatusName, string AfterStatusSymbol, string AfterStatusColor);

public sealed record NativeHistoricalRangeReceipt(
    string PersonId, DateOnly From, long FinalRevisionNumber);

public sealed record NativeHistoricalRangePreview(
    NativeHistoricalRangeRequest Request, string Token, string PersonName,
    string ServiceNumberA, IReadOnlyList<NativeHistoricalRangeDay> Days,
    bool RequiresExplicitConfirmation)
{
    public int SkippedDays => Request.SpanDays - Days.Count;

    public bool Matches(NativeHistoricalRangeRequest expected, DateOnly today)
    {
        if (Request != expected || !expected.IsValid(today) ||
            Token is not { Length: 64 } || !Token.All(Uri.IsHexDigit) ||
            !RequiresExplicitConfirmation || string.IsNullOrWhiteSpace(PersonName) ||
            string.IsNullOrWhiteSpace(ServiceNumberA) || Days is null ||
            Days.Count < 1 || Days.Count > expected.SpanDays)
            return false;

        DateOnly? prior = null;
        foreach (var day in Days)
        {
            if (day is null || day.BusinessDate < expected.From ||
                day.BusinessDate > expected.To ||
                (prior is { } previous && day.BusinessDate <= previous) ||
                day.PreviousRevision is < 0 or long.MaxValue ||
                day.HasExistingRecord != (day.PreviousRevision > 0) ||
                !NativeHistoricalRangeRequest.IsIdentifier(day.AssignmentId) ||
                !NativeHistoricalRangeRequest.IsIdentifier(day.SiteId) ||
                string.IsNullOrWhiteSpace(day.SiteName) ||
                !NativeHistoricalRangeRequest.IsIdentifier(day.BeforeStatusId) ||
                day.BeforeStatusId == expected.StatusId ||
                day.HasExistingRecord == (day.BeforeStatusId == "__UNPREPARED__") ||
                string.IsNullOrWhiteSpace(day.BeforeStatusName) ||
                string.IsNullOrWhiteSpace(day.AfterStatusName) ||
                string.IsNullOrWhiteSpace(day.AfterStatusSymbol) ||
                string.IsNullOrWhiteSpace(day.AfterStatusColor))
                return false;
            prior = day.BusinessDate;
        }
        return true;
    }

    // The original receipt reports the start of the requested period and
    // the revision of the last actually changed day, NOT a count or sum.
    public bool MatchesReceipt(NativeHistoricalRangeReceipt receipt) =>
        Days is { Count: > 0 } &&
        Days[^1].PreviousRevision is >= 0 and < long.MaxValue &&
        receipt.PersonId == Request.PersonId && receipt.From == Request.From &&
        receipt.FinalRevisionNumber == Days[^1].PreviousRevision + 1;
}

public interface INativeHistoricalRangeBackend
{
    Task<NativeHistoricalRangePreview> PreviewRangeAsync(NativeIdentity actor,
        NativeHistoricalRangeRequest request, CancellationToken ct = default);

    Task<NativeHistoricalRangeReceipt> CommitRangeAsync(NativeIdentity actor,
        NativeHistoricalRangePreview approved, string operationId,
        CancellationToken ct = default);
}
