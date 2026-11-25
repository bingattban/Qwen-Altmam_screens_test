using System.Security.Cryptography;
using AlTamam.Domain.Policies;

namespace AlTamam.NativePresentation;

public sealed record NativeIndividualExcelRequest(
    string PersonId, string DateFrom, string DateTo, int MovementType,
    string SiteId, string StatusId);

public sealed record NativeIndividualExcelPreview(
    NativeIndividualExcelRequest Request, string Name, string ServiceNumberA,
    string Directory, long AttendanceTotal, long IdentityTotal,
    long MovementTotal, long RevisionTotal, long AuditTotal, long SiteTotal,
    string Fingerprint, string ApprovalToken);

public sealed record NativeIndividualExcelReceipt(
    NativeIndividualExcelRequest Request, string FilePath, string Fingerprint,
    int SheetCount);

public interface INativeIndividualExcelBackend
{
    Task<NativeIndividualExcelPreview> PreviewIndividualExcelAsync(
        NativeIdentity actor, NativeIndividualExcelRequest request,
        CancellationToken ct = default);

    Task<NativeIndividualExcelReceipt> ExportIndividualExcelAsync(
        NativeIdentity actor, NativeIndividualExcelRequest request,
        string approvalToken, CancellationToken ct = default);

    void InvalidateIndividualExcelApproval();
}

/// <summary>
/// A single in-memory approval per native adapter, bound to the exact actor,
/// session and immutable request. No database or file write is performed here.
/// The adapter must independently resolve the live session before any source
/// read or publication. A consumed token cannot be recovered or replayed.
/// </summary>
public sealed class NativeIndividualExcelApprovalGate
{
    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private long _generation;
    private bool _published;
    private Approval? _approval;
    private sealed record Approval(NativeIdentity Actor,
        NativeIndividualExcelRequest Request, string Fingerprint,
        string Token, DateTimeOffset Expires);

    public NativeIndividualExcelApprovalGate(TimeProvider? clock = null)
        => _clock = clock ?? TimeProvider.System;

    public long Begin()
    {
        lock (_sync)
        {
            _approval = null;
            _published = false;
            return ++_generation;
        }
    }

    public void Invalidate() => Begin();

    public string Publish(long generation, NativeIdentity actor,
        NativeIndividualExcelRequest request, string fingerprint)
    {
        lock (_sync)
        {
            if (generation != _generation || generation == 0 || _published ||
                !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
                string.IsNullOrWhiteSpace(actor.SessionNonce) ||
                string.IsNullOrWhiteSpace(request.PersonId) ||
                fingerprint is not { Length: 64 } ||
                fingerprint.Any(c => !char.IsAsciiHexDigit(c)))
                throw new UnauthorizedAccessException("Invalid or stale Excel approval.");

            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _approval = new(actor, request, fingerprint, token,
                _clock.GetUtcNow().AddMinutes(2));
            _published = true;
            return token;
        }
    }

    public string Consume(NativeIdentity actor,
        NativeIndividualExcelRequest request, string token)
    {
        lock (_sync)
        {
            var current = _approval;
            _approval = null;
            if (current is null ||
                !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) ||
                current.Actor != actor || current.Request != request ||
                !string.Equals(current.Token, token, StringComparison.Ordinal) ||
                _clock.GetUtcNow() >= current.Expires)
                throw new UnauthorizedAccessException("Excel approval is invalid or expired.");
            return current.Fingerprint;
        }
    }
}
