using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using AlTamam.Application.Attendance;
using AlTamam.Application.Bridge;
using AlTamam.Contracts.Bridge;
using AlTamam.Infrastructure.Persistence;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Dashboard;

/// <summary>
/// Reuses the canonical audited same-day revision service. The WPF shell
/// holds no DB write SQL, token validation, optimistic lock, date cutoff or
/// audit writer. Those belong to AttendanceCommitService exclusively.
/// </summary>
internal sealed class NativeSqlCipherAttendanceReviewBackend(
    BridgeSharedServices services, AlTamamDbContext database) : INativeAttendanceReviewBackend
{
    public async Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
        NativeIdentity identity, CancellationToken ct = default)
    {
        await ResolveAuthorizedAsync(identity, "GetActiveAttendanceStatuses", ct);
        BridgeResponse response = await services.ListActiveStatusesAsync(ct);
        await ResolveAuthorizedAsync(identity, "GetActiveAttendanceStatuses", ct);
        if (!response.Success || response.Data is not JsonArray array || array.Count > 300)
            throw new InvalidDataException("Canonical active attendance statuses unavailable.");
        var statuses = new List<NativeAttendanceStatusChoice>();
        foreach (JsonNode? node in array)
        {
            JsonObject item = RequiredObject(node);
            string id = RequiredText(item, "statusId");
            string name = RequiredText(item, "displayName");
            if (item["active"]?.GetValue<bool>() != true)
                continue;
            // Timed leave and permission need a separate start/end editor.
            // Do not silently assign indefinite leave in this native slice.
            if (AttendanceDateRangePolicy.RequiresRange(id)) continue;
            statuses.Add(new(id, name));
        }
        return statuses;
    }

    public async Task<NativeAttendanceRevisionPreview> PreviewAsync(
        NativeIdentity identity, DateOnly date, string site,
        IReadOnlyList<string> personIds, string statusId, CancellationToken ct = default)
    {
        string today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != today ||
            personIds.Count is < 1 or > 100 ||
            personIds.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128) ||
            personIds.Distinct(StringComparer.Ordinal).Count() != personIds.Count ||
            statusId.Length is < 1 or > 128 ||
            AttendanceDateRangePolicy.RequiresRange(statusId))
            throw new InvalidDataException("Invalid native same-day attendance revision.");

        BridgePrincipal principal = await ResolveAuthorizedAsync(identity, "PreviewAttendanceChange", ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        var payload = new JsonObject
        {
            ["businessDate"] = today,
            ["siteId"] = site ?? "",
            ["statusId"] = statusId,
            ["mode"] = "revise",
            ["personIds"] = new JsonArray(personIds.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
        };
        BridgeResponse response = await new AttendanceCommitService(database)
            .PreviewAsync(payload, principal, combined.Token);
        await ResolveAuthorizedAsync(identity, "PreviewAttendanceChange", combined.Token);
        if (!response.Success || response.Data is not JsonObject data)
            throw new InvalidDataException(response.MessageAr);
        string token = RequiredText(data, "previewToken");
        string actual = RequiredText(data, "statusId");
        string display = RequiredText(data, "statusName");
        if (token.Length < 32 || actual != statusId ||
            RequiredText(data, "businessDate") != today ||
            data["persons"] is not JsonArray selected ||
            selected.Count != personIds.Count)
            throw new InvalidDataException("SQLCipher attendance preview mismatch.");
        var people = selected.Select(node =>
        {
            JsonObject person = RequiredObject(node);
            return new NativeAttendancePreviewPerson(
                RequiredText(person, "personId"),
                RequiredText(person, "name"),
                RequiredText(person, "serviceNumberA"),
                RequiredText(person, "siteName"),
                RequiredText(person, "previousStatusName"),
                RequiredText(person, "previousStatusId"));
        }).ToArray();
        if (!people.Select(x => x.PersonId).Order(StringComparer.Ordinal)
                .SequenceEqual(personIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("SQLCipher returned unexpected attendance person IDs.");
        return new NativeAttendanceRevisionPreview(
            token, date, actual, display, people);
    }

    public async Task<NativeAttendanceCommitReceipt> CommitAsync(
        NativeIdentity identity, string previewToken, string operationId,
        CancellationToken ct = default)
    {
        if (previewToken.Length < 32 || operationId.Length is < 16 or > 128 ||
            operationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Invalid preview token or immutable operation ID.");

        BridgePrincipal principal = await ResolveAuthorizedAsync(identity, "ReviseTodayAttendance", ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        // One confirmed intent => the SAME previewToken + operationId even
        // after a lost response; do not mint another id or auto-resubmit.
        BridgeResponse response = await new AttendanceCommitService(database)
            .CommitAsync(new JsonObject
            {
                ["previewToken"] = previewToken,
                ["operationId"] = operationId
            }, principal, revise: true, combined.Token);
        await ResolveAuthorizedAsync(identity, "ReviseTodayAttendance", combined.Token);
        if (!response.Success || response.Data is not JsonObject data)
            throw new InvalidDataException(response.MessageAr);
        int count = data["count"]?.GetValue<int>() ?? 0;
        string audit = RequiredText(data, "auditEventId");
        if (count is < 1 or > 100 || !audit.StartsWith("AUD-", StringComparison.Ordinal) ||
            data["auditEventIds"] is not JsonArray audits || audits.Count != count)
            throw new InvalidDataException("Canonical SQLCipher attendance receipt incomplete.");
        return new NativeAttendanceCommitReceipt(count, audit);
    }

    private async Task<BridgePrincipal> ResolveAuthorizedAsync(
        NativeIdentity expected, string action, CancellationToken ct)
    {
        BridgePrincipal principal = await services.ResolveSessionAsync(expected.SessionNonce, ct)
            ?? throw new UnauthorizedAccessException("Native session has expired.");
        if (principal.UserId != expected.UserId ||
            principal.Username != expected.Username ||
            principal.Role != expected.Role ||
            principal.SessionNonce != expected.SessionNonce)
            throw new UnauthorizedAccessException("Account or nonce was replaced.");
        var allowed = ActionManifest.All.Single(x => x.Action == action);
        if (!allowed.AllowedRoles.Contains(principal.Role.ToString(), StringComparer.Ordinal))
            throw new UnauthorizedAccessException("This role cannot perform this attendance action.");
        return principal;
    }

    private static JsonObject RequiredObject(JsonNode? node) =>
        node as JsonObject ?? throw new InvalidDataException("Missing signed attendance preview row.");
    private static string RequiredText(JsonObject row, string property) =>
        row[property]?.GetValue<string>() ??
        throw new InvalidDataException($"Missing authoritative attendance field: {property}");
}
