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
    BridgeSharedServices services, AlTamamDbContext database) : INativeAttendanceReviewBackend, INativeClosedDayCorrectionBackend, INativeHistoricalRangeBackend, INativeAdministrativeCatchUpBackend
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
            // Timed statuses are now available only with explicit bounded dates.
            // Canonical server validation remains authoritative.
            statuses.Add(new(id, name));
        }
        return statuses;
    }

    public Task<NativeAttendanceRevisionPreview> PreviewAsync(
        NativeIdentity identity, DateOnly date, string site,
        IReadOnlyList<string> personIds, string statusId, CancellationToken ct = default,
        NativeAttendanceDateRange? range = null) =>
        PreviewCoreAsync(identity, date, site, personIds, statusId, false, ct, range);

    public Task<NativeAttendanceRevisionPreview> PreviewInitialAsync(
        NativeIdentity identity, DateOnly date, string site, IReadOnlyList<string> personIds,
        string statusId, CancellationToken ct = default, NativeAttendanceDateRange? range = null) =>
        PreviewCoreAsync(identity, date, site, personIds, statusId, true, ct, range);

    private async Task<NativeAttendanceRevisionPreview> PreviewCoreAsync(
        NativeIdentity identity, DateOnly date, string site, IReadOnlyList<string> personIds,
        string statusId, bool initial, CancellationToken ct, NativeAttendanceDateRange? range)
    {
        string today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != today ||
            personIds.Count is < 1 or > 100 ||
            personIds.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128) ||
            personIds.Distinct(StringComparer.Ordinal).Count() != personIds.Count ||
            statusId.Length is < 1 or > 128 ||
            !NativeAttendanceDateRange.TryCreate(statusId, date,
                range?.StartDate, range?.EndDate, out var validatedRange, out _) ||
            validatedRange != range)
            throw new InvalidDataException("Invalid native same-day attendance revision or date range.");

        BridgePrincipal principal = await ResolveAuthorizedAsync(identity, "PreviewAttendanceChange", ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        var payload = new JsonObject
        {
            ["businessDate"] = today,
            ["siteId"] = site ?? "",
            ["statusId"] = statusId,
            ["mode"] = initial ? "initial" : "revise",
            ["personIds"] = new JsonArray(personIds.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray())
        };
        if (validatedRange is not null)
        {
            payload["rangeStartDate"] = validatedRange.StartDate;
            payload["rangeEndDate"] = validatedRange.EndDate;
        }
        if (!AttendanceDateRangePolicy.TryParse(payload, statusId, today,
                out _, out _, out _))
            throw new InvalidDataException("The date range was rejected by canonical attendance policy.");
        BridgeResponse response = await new AttendanceCommitService(database)
            .PreviewAsync(payload, principal, combined.Token);
        await ResolveAuthorizedAsync(identity, "PreviewAttendanceChange", combined.Token);
        if (!response.Success || response.Data is not JsonObject data)
            throw new InvalidDataException(response.MessageAr);
        string token = RequiredText(data, "previewToken");
        string actual = RequiredText(data, "statusId");
        string display = RequiredText(data, "statusName");
        string? actualStart = data["rangeStartDate"]?.GetValue<string>();
        string? actualEnd = data["rangeEndDate"]?.GetValue<string>();
        if (token.Length < 32 || actual != statusId ||
            RequiredText(data, "mode") != (initial ? "initial" : "revise") ||
            actualStart != validatedRange?.StartDate ||
            actualEnd != validatedRange?.EndDate ||
            RequiredText(data, "businessDate") != today ||
            data["persons"] is not JsonArray selected ||
            selected.Count != personIds.Count)
            throw new InvalidDataException("SQLCipher attendance preview mismatch.");
        var people = selected.Select(node =>
        {
            JsonObject person = RequiredObject(node);
            if (person["previousRevision"] is not JsonValue revisionValue ||
                !revisionValue.TryGetValue<long>(out long revision) ||
                (initial ? revision != 0 || !person.ContainsKey("previousStatusId") ||
                    person["previousStatusId"] is not null
                    : revision <= 0 || string.IsNullOrWhiteSpace(RequiredText(person, "previousStatusId"))))
                throw new InvalidDataException("Canonical preview previous attendance does not match mode.");
            return new NativeAttendancePreviewPerson(
                RequiredText(person, "personId"),
                RequiredText(person, "name"),
                RequiredText(person, "serviceNumberA"),
                RequiredText(person, "siteName"),
                RequiredText(person, "previousStatusName"),
                initial ? "" : RequiredText(person, "previousStatusId"));
        }).ToArray();
        if (!people.Select(x => x.PersonId).Order(StringComparer.Ordinal)
                .SequenceEqual(personIds.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("SQLCipher returned unexpected attendance person IDs.");
        return new NativeAttendanceRevisionPreview(
            token, date, actual, display, people, actualStart, actualEnd) { IsInitial = initial };
    }

    public Task<NativeAttendanceCommitReceipt> CommitAsync(
        NativeIdentity identity, string previewToken, string operationId,
        CancellationToken ct = default) =>
        CommitCoreAsync(identity, previewToken, operationId, true, ct);

    public Task<NativeAttendanceCommitReceipt> CommitInitialAsync(
        NativeIdentity identity, string previewToken, string operationId,
        CancellationToken ct = default) =>
        CommitCoreAsync(identity, previewToken, operationId, false, ct);

    private async Task<NativeAttendanceCommitReceipt> CommitCoreAsync(
        NativeIdentity identity, string previewToken, string operationId,
        bool revise, CancellationToken ct)
    {
        if (previewToken.Length < 32 || operationId.Length is < 16 or > 128 ||
            operationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Invalid preview token or immutable operation ID.");

        string action = revise ? "ReviseTodayAttendance" : "SubmitAttendance";
        BridgePrincipal principal = await ResolveAuthorizedAsync(identity, action, ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        // One confirmed intent => the SAME previewToken + operationId even
        // after a lost response; do not mint another id or auto-resubmit.
        BridgeResponse response = await new AttendanceCommitService(database)
            .CommitAsync(new JsonObject
            {
                ["previewToken"] = previewToken,
                ["operationId"] = operationId
            }, principal, revise: revise, combined.Token);
        await ResolveAuthorizedAsync(identity, action, combined.Token);
        if (!response.Success || response.Data is not JsonObject data)
            throw new InvalidDataException(response.MessageAr);
        int count = data["count"]?.GetValue<int>() ?? 0;
        string audit = RequiredText(data, "auditEventId");
        if (count is < 1 or > 100 || !audit.StartsWith("AUD-", StringComparison.Ordinal) ||
            data["revised"]?.GetValue<bool>() != revise ||
            data["auditEventIds"] is not JsonArray audits || audits.Count != count)
            throw new InvalidDataException("Canonical SQLCipher attendance receipt incomplete.");
        return new NativeAttendanceCommitReceipt(count, audit);
    }

    async Task<IReadOnlyList<NativeAttendanceStatusChoice>> INativeClosedDayCorrectionBackend.ReadCorrectionChoicesAsync(
        NativeIdentity identity, CancellationToken ct)
    {
        await ResolveAuthorizedAsync(identity, "PreviewClosedDayCorrection", ct);
        var choices = await ReadChoicesAsync(identity, ct);
        await ResolveAuthorizedAsync(identity, "PreviewClosedDayCorrection", ct);
        return choices;
    }

    async Task<NativeClosedDayCorrectionPreview> INativeClosedDayCorrectionBackend.PreviewAsync(
        NativeIdentity identity, NativeClosedDayCorrectionRequest request, CancellationToken ct)
    {
        string date = request.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!AlTamam.Application.Attendance.ClosedDayCorrections.ClosedDayCorrectionEligibilityPolicy
                .IsHistorical(date, DateTimeOffset.Now) ||
            request.PersonId.Length is < 1 or > 128 || request.StatusId.Length is < 1 or > 128 ||
            string.IsNullOrWhiteSpace(request.PersonId) || string.IsNullOrWhiteSpace(request.StatusId) ||
            request.PersonId.Any(char.IsControl) || request.StatusId.Any(char.IsControl) ||
            request.Reason.Length is < 10 or > 400 || request.Reason.Any(char.IsControl) ||
            request.Reason.Trim() != request.Reason)
            throw new InvalidDataException("Invalid historical correction request.");

        const string action = "PreviewClosedDayCorrection";
        var principal = await ResolveAuthorizedAsync(identity, action, ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        var response = await new ClosedDayCorrectionService(database).PreviewAsync(new JsonObject
        {
            ["personId"] = request.PersonId,
            ["businessDate"] = date,
            ["statusId"] = request.StatusId,
            ["reason"] = request.Reason
        }, principal, combined.Token);
        await ResolveAuthorizedAsync(identity, action, combined.Token);
        if (!response.Success || response.Data is not JsonObject data ||
            data.ContainsKey("days") || RequiredText(data, "personId") != request.PersonId ||
            RequiredText(data, "businessDate") != date ||
            RequiredText(data, "statusId") != request.StatusId ||
            RequiredText(data, "reason") != request.Reason ||
            data["requiresExplicitConfirmation"]?.GetValue<bool>() != true ||
            data["previousRevision"] is not JsonValue revisionValue ||
            !revisionValue.TryGetValue<long>(out long revision) || revision is < 1 or long.MaxValue)
            throw new InvalidDataException("Invalid canonical single-day correction preview.");
        string token = RequiredText(data, "previewToken");
        if (token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid correction token.");
        return new NativeClosedDayCorrectionPreview(request, token, revision,
            RequiredText(data, "personName"), RequiredText(data, "serviceNumberA"),
            RequiredText(data, "siteName"), RequiredText(data, "beforeStatusId"),
            RequiredText(data, "beforeStatusName"), RequiredText(data, "afterStatusName"), true);
    }

    async Task<NativeClosedDayCorrectionReceipt> INativeClosedDayCorrectionBackend.CommitAsync(
        NativeIdentity identity, string token, string operationId, CancellationToken ct)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit) ||
            operationId.Length is < 16 or > 128 ||
            operationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Invalid correction approval.");
        const string action = "CommitClosedDayCorrection";
        var principal = await ResolveAuthorizedAsync(identity, action, ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        var response = await new ClosedDayCorrectionService(database).CommitAsync(new JsonObject
        {
            ["previewToken"] = token,
            ["operationId"] = operationId
        }, principal, combined.Token);
        await ResolveAuthorizedAsync(identity, action, combined.Token);
        if (!response.Success || response.Data is not JsonObject data ||
            !DateOnly.TryParseExact(RequiredText(data, "businessDate"), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            date >= DateOnly.FromDateTime(DateTime.Now) ||
            data["revisionNumber"] is not JsonValue revisionValue ||
            !revisionValue.TryGetValue<long>(out long revision) || revision < 2)
            throw new InvalidDataException("Invalid canonical correction receipt.");
        return new NativeClosedDayCorrectionReceipt(RequiredText(data, "personId"), date, revision);
    }

    public async Task<NativeHistoricalRangePreview> PreviewRangeAsync(
        NativeIdentity identity, NativeHistoricalRangeRequest request, CancellationToken ct = default)
    {
        if (!request.IsValid(DateOnly.FromDateTime(DateTime.Now)))
            throw new InvalidDataException("Invalid historical range request.");
        const string action = "PreviewClosedDayCorrection";
        string from = request.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string to = request.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var principal = await ResolveAuthorizedAsync(identity, action, ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        var response = await new ClosedDayCorrectionService(database).PreviewAsync(new JsonObject
        {
            ["personId"] = request.PersonId,
            ["dateFrom"] = from,
            ["dateTo"] = to,
            ["statusId"] = request.StatusId,
            ["reason"] = request.Reason
        }, principal, combined.Token);
        await ResolveAuthorizedAsync(identity, action, combined.Token);
        if (!response.Success || response.Data is not JsonObject data ||
            RequiredText(data, "mode") != "range" ||
            RequiredText(data, "personId") != request.PersonId ||
            RequiredText(data, "businessDate") != from ||
            RequiredText(data, "dateFrom") != from || RequiredText(data, "dateTo") != to ||
            RequiredText(data, "statusId") != request.StatusId ||
            RequiredText(data, "reason") != request.Reason ||
            data["requiresExplicitConfirmation"]?.GetValue<bool>() != true ||
            data["days"] is not JsonArray rows || rows.Count < 1 || rows.Count > request.SpanDays ||
            data["previousRevision"] is not JsonValue aggregateValue ||
            !aggregateValue.TryGetValue<long>(out long aggregate))
            throw new InvalidDataException("Invalid canonical historical range preview.");

        var days = new List<NativeHistoricalRangeDay>(rows.Count);
        long revisionSum = 0;
        foreach (var node in rows)
        {
            var row = RequiredObject(node);
            if (!DateOnly.TryParseExact(RequiredText(row, "businessDate"), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
                row["previousRevision"] is not JsonValue revisionValue ||
                !revisionValue.TryGetValue<long>(out long revision) ||
                row["hasExistingRecord"] is not JsonValue existingValue ||
                !existingValue.TryGetValue<bool>(out bool existing))
                throw new InvalidDataException("Invalid canonical historical day.");
            days.Add(new(day, existing, revision,
                RequiredText(row, "assignmentId"), RequiredText(row, "siteId"),
                RequiredText(row, "siteName"), RequiredText(row, "beforeStatusId"),
                RequiredText(row, "beforeStatusName"), RequiredText(row, "afterStatusName"),
                RequiredText(row, "afterStatusSymbol"), RequiredText(row, "afterStatusColor")));
            revisionSum = unchecked(revisionSum * 31 + revision + 1);
        }
        var preview = new NativeHistoricalRangePreview(request, RequiredText(data, "previewToken"),
            RequiredText(data, "personName"), RequiredText(data, "serviceNumberA"),
            days.AsReadOnly(), true);
        if (revisionSum != aggregate || !preview.Matches(request, DateOnly.FromDateTime(DateTime.Now)))
            throw new InvalidDataException("Historical range day mismatch.");
        return preview;
    }

    public async Task<NativeHistoricalRangeReceipt> CommitRangeAsync(
        NativeIdentity identity, NativeHistoricalRangePreview approved, string operationId,
        CancellationToken ct = default)
    {
        if (!approved.Matches(approved.Request, DateOnly.FromDateTime(DateTime.Now)) ||
            operationId.Length is < 16 or > 128 ||
            operationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Invalid historical range approval.");
        const string action = "CommitClosedDayCorrection";
        var principal = await ResolveAuthorizedAsync(identity, action, ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        var response = await new ClosedDayCorrectionService(database).CommitAsync(new JsonObject
        {
            ["previewToken"] = approved.Token,
            ["operationId"] = operationId
        }, principal, combined.Token);
        await ResolveAuthorizedAsync(identity, action, combined.Token);
        if (!response.Success || response.Data is not JsonObject data ||
            !DateOnly.TryParseExact(RequiredText(data, "businessDate"), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            data["revisionNumber"] is not JsonValue revisionValue ||
            !revisionValue.TryGetValue<long>(out long revision))
            throw new InvalidDataException("Invalid canonical historical range receipt.");
        var receipt = new NativeHistoricalRangeReceipt(RequiredText(data, "personId"), date, revision);
        if (!approved.MatchesReceipt(receipt))
            throw new InvalidDataException("Historical range receipt mismatch.");
        return receipt;
    }

    public async Task<NativeAdministrativeCatchUpPreview> PreviewCatchUpAsync(
        NativeIdentity identity, NativeAdministrativeCatchUpRequest request, CancellationToken ct = default)
    {
        if (!request.IsValid(DateOnly.FromDateTime(DateTime.Now)))
            throw new InvalidDataException("Invalid administrative catch-up request.");
        const string action = "PreviewAdministrativeAttendanceCatchUp";
        string day = request.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var principal = await ResolveAuthorizedAsync(identity, action, ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        var response = await new AdministrativeAttendanceCatchUpService(database).PreviewAsync(new JsonObject
        {
            ["businessDate"] = day,
            ["reason"] = request.Reason
        }, principal, combined.Token);
        await ResolveAuthorizedAsync(identity, action, combined.Token);
        if (!response.Success || response.Data is not JsonObject data ||
            RequiredText(data, "businessDate") != day ||
            RequiredText(data, "reason") != request.Reason ||
            data["pendingCount"] is not JsonValue pendingValue ||
            !pendingValue.TryGetValue<int>(out int pending) ||
            data["eligibleCount"] is not JsonValue eligibleValue ||
            !eligibleValue.TryGetValue<int>(out int eligible) ||
            data["requiresExplicitConfirmation"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Invalid canonical catch-up preview.");
        var preview = new NativeAdministrativeCatchUpPreview(request,
            RequiredText(data, "previewToken"), RequiredText(data, "fingerprint"),
            pending, eligible, true);
        if (!preview.Matches(request, DateOnly.FromDateTime(DateTime.Now)))
            throw new InvalidDataException("Canonical catch-up preview mismatch.");
        return preview;
    }

    public async Task<NativeAdministrativeCatchUpReceipt> CommitCatchUpAsync(
        NativeIdentity identity, NativeAdministrativeCatchUpPreview approved,
        string operationId, CancellationToken ct = default)
    {
        // The canonical service checks date expiry AFTER exact idempotency replay.
        // Do not locally prevent retrieving a committed receipt after midnight.
        if (!approved.IsWellFormed || operationId.Length is < 16 or > 128 ||
            operationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Invalid administrative catch-up approval.");
        const string action = "CommitAdministrativeAttendanceCatchUp";
        var principal = await ResolveAuthorizedAsync(identity, action, ct);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, principal.SessionRevocation);
        var response = await new AdministrativeAttendanceCatchUpService(database).CommitAsync(new JsonObject
        {
            ["previewToken"] = approved.Token,
            ["operationId"] = operationId
        }, principal, combined.Token);
        await ResolveAuthorizedAsync(identity, action, combined.Token);
        if (!response.Success || response.Data is not JsonObject data ||
            !DateOnly.TryParseExact(RequiredText(data, "businessDate"), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            data["pendingBefore"] is not JsonValue pendingValue ||
            !pendingValue.TryGetValue<int>(out int pending) ||
            data["appliedCount"] is not JsonValue appliedValue ||
            !appliedValue.TryGetValue<int>(out int applied) ||
            data["isCatchUpRun"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Invalid canonical catch-up receipt.");
        var receipt = new NativeAdministrativeCatchUpReceipt(date, pending, applied,
            RequiredText(data, "summary"), true);
        if (!approved.MatchesReceipt(receipt))
            throw new InvalidDataException("Canonical catch-up receipt mismatch.");
        return receipt;
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
