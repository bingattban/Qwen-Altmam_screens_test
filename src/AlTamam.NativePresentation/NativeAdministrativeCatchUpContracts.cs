namespace AlTamam.NativePresentation;

/// <summary>All eligible missing attendance for one previous day, not a person correction.</summary>
public sealed record NativeAdministrativeCatchUpRequest(DateOnly BusinessDate, string Reason)
{
    internal bool IsWellFormed => Reason is { Length: >= 10 and <= 400 } &&
        Reason.Trim() == Reason && !Reason.Any(char.IsControl);

    public bool IsValid(DateOnly today) => IsWellFormed &&
        today.DayNumber - BusinessDate.DayNumber is >= 1 and <= 7;
}

public sealed record NativeAdministrativeCatchUpReceipt(DateOnly BusinessDate,
    int PendingBefore, int AppliedCount, string Summary, bool IsCatchUpRun);

public sealed record NativeAdministrativeCatchUpPreview(NativeAdministrativeCatchUpRequest Request,
    string Token, string Fingerprint, int PendingCount, int EligibleCount,
    bool RequiresExplicitConfirmation)
{
    private static bool IsHexDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public bool IsWellFormed => Request is not null && Request.IsWellFormed &&
        IsHexDigest(Token) && IsHexDigest(Fingerprint) &&
        PendingCount > 0 && EligibleCount >= PendingCount && RequiresExplicitConfirmation;

    public bool Matches(NativeAdministrativeCatchUpRequest expected, DateOnly today) =>
        IsWellFormed && Request == expected && expected.IsValid(today);

    // Canonical automatic attendance may skip some missing records when no
    // usable previous/default status exists. Never infer applied = pending.
    public bool MatchesReceipt(NativeAdministrativeCatchUpReceipt receipt) =>
        IsWellFormed && receipt is not null && receipt.BusinessDate == Request.BusinessDate &&
        receipt.PendingBefore == PendingCount && receipt.AppliedCount >= 0 &&
        receipt.AppliedCount <= PendingCount && receipt.IsCatchUpRun &&
        !string.IsNullOrWhiteSpace(receipt.Summary);
}

public interface INativeAdministrativeCatchUpBackend
{
    Task<NativeAdministrativeCatchUpPreview> PreviewCatchUpAsync(NativeIdentity actor,
        NativeAdministrativeCatchUpRequest request, CancellationToken ct = default);

    Task<NativeAdministrativeCatchUpReceipt> CommitCatchUpAsync(NativeIdentity actor,
        NativeAdministrativeCatchUpPreview approved, string operationId, CancellationToken ct = default);
}
