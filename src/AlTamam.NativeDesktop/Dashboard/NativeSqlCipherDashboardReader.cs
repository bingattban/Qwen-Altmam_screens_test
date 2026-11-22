using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using AlTamam.Application.Bridge;
using AlTamam.Contracts.Bridge;
using AlTamam.Domain.Policies;
using AlTamam.NativePresentation;

namespace AlTamam.NativeDesktop.Dashboard;

/// <summary>
/// Read-only native adapter over exactly the existing production SQLCipher
/// dashboard/roster/site services. No duplicate SQL, private session shortcut,
/// fake UI counts, background login, custom-field omissions or write operations.
/// </summary>
public sealed class NativeSqlCipherDashboardReader(
    BridgeSharedServices services, AlTamam.Infrastructure.Persistence.AlTamamDbContext database)
    : INativeDashboardReader, INativeDashboardContinuationReader, INativeAttendanceReviewBackend
{
    private readonly NativeSqlCipherAttendanceReviewBackend _review =
        new(services, database);

    public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
        NativeIdentity actor, CancellationToken ct = default) =>
        _review.ReadChoicesAsync(actor, ct);

    public Task<NativeAttendanceRevisionPreview> PreviewAsync(
        NativeIdentity actor, DateOnly date, string site,
        IReadOnlyList<string> people, string statusId, CancellationToken ct = default,
        NativeAttendanceDateRange? range = null) =>
        _review.PreviewAsync(actor, date, site, people, statusId, ct, range);

    public Task<NativeAttendanceCommitReceipt> CommitAsync(
        NativeIdentity actor, string previewToken, string operationId,
        CancellationToken ct = default) =>
        _review.CommitAsync(actor, previewToken, operationId, ct);

    public async Task<NativeDashboardSnapshot> ReadAsync(
        NativeIdentity identity, NativeDashboardFilter filters, CancellationToken ct = default)
    {
        if (filters.Page < 1 || filters.PageSize != 100 ||
            filters.SearchTerm.Length > 100 || filters.SearchTerm.Any(char.IsControl))
            throw new InvalidDataException("Invalid read-only dashboard request.");
        if (!RoleAuthorizationPolicy.CanPerformStandardOperations(identity.Role))
            throw new UnauthorizedAccessException("The role cannot read the native dashboard.");

        async Task VerifyLiveSessionAsync()
        {
            var server = await services.ResolveSessionAsync(identity.SessionNonce, ct)
                .ConfigureAwait(false);
            if (server is null || server.UserId != identity.UserId ||
                server.Username != identity.Username || server.Role != identity.Role ||
                server.SessionNonce != identity.SessionNonce)
                throw new UnauthorizedAccessException("Native SQLCipher session expired or changed.");
        }
        // The UI cannot choose another account or change its role. Check on
        // both ends to avoid publishing a read after a concurrent native logout.
        await VerifyLiveSessionAsync().ConfigureAwait(false);
        string today = filters.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var summaryQuery = new JsonObject {
            ["businessDate"] = today, ["siteId"] = filters.SiteId
        };
        var rosterQuery = new JsonObject {
            ["businessDate"] = today, ["siteId"] = filters.SiteId,
            ["statusId"] = filters.StatusId, ["term"] = filters.SearchTerm,
            ["page"] = filters.Page, ["pageSize"] = filters.PageSize
        };
        var canonicalRead = new DailyMonthlyReadService(database);
        // These three canonical sources preserve the same business semantics
        // as functional-screens.js renderDashboard(), without making UI rows
        // the source of truth for summary counts.
        BridgeResponse totalsResponse = await canonicalRead
            .DashboardSummaryAsync(summaryQuery, ct).ConfigureAwait(false);
        BridgeResponse sitesResponse = await services
            .ListSitesAsync(includeInactive: false, ct).ConfigureAwait(false);
        BridgeResponse rosterResponse = await canonicalRead
            .DailyRosterAsync(rosterQuery, ct).ConfigureAwait(false);
        await VerifyLiveSessionAsync().ConfigureAwait(false);

        JsonObject totals = RequiredObject(totalsResponse, "GetDashboardSummary");
        JsonArray sitesArray = RequiredArray(sitesResponse, "QuerySites");
        JsonObject roster = RequiredObject(rosterResponse, "QueryDailyRoster");
        if (RequiredText(totals, "businessDate") != today ||
            RequiredText(totals, "siteId") != filters.SiteId ||
            RequiredNumber(roster, "page") != filters.Page ||
            RequiredNumber(roster, "pageSize") != filters.PageSize)
            throw new InvalidDataException("Native dashboard response filter mismatch.");

        var statuses = RequiredArray(totals, "statusCounts").Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativeDashboardStatus(
                RequiredText(item, "statusId"), RequiredText(item, "name"),
                RequiredNumber(item, "count"), RequiredText(item, "color"));
        }).ToArray();

        var sites = sitesArray.Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new { Data = item, Active = RequiredBoolean(item, "active") };
        }).Where(site => site.Active).Select(site =>
            new NativeDashboardSite(
                RequiredText(site.Data, "siteId"), RequiredText(site.Data, "name")))
            .ToArray();

        var columns = RequiredArray(roster, "customColumns").Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativeDashboardColumn(
                RequiredText(item, "columnId"), RequiredText(item, "caption"));
        }).ToArray();

        var rows = RequiredArray(roster, "items").Select((node, index) =>
        {
            JsonObject item = RequireObject(node);
            JsonObject custom = RequireObject(item["customFields"]);
            var cells = columns.Select(col =>
                custom[col.ColumnId]?.GetValue<string>() ?? "").ToArray();
            return new NativeDashboardRow(
                RequiredText(item, "personId"), RequiredText(item, "serviceNumberA"),
                RequiredText(item, "name"), RequiredText(item, "siteName"),
                RequiredText(item, "statusName"),
                item["statusColor"]?.GetValue<string>(), cells)
            {
                RowNumber = checked((filters.Page - 1) * filters.PageSize + index + 1)
            };
        }).ToArray();

        return new NativeDashboardSnapshot(
            filters.BusinessDate, filters.SiteId,
            RequiredNumber(totals, "totalForce"),
            RequiredNumber(totals, "automaticallyPrepared"),
            RequiredNumber(totals, "internalTransferDay"),
            statuses, sites, rows, columns,
            RequiredNumber(roster, "total"), filters.Page,
            RequiredBoolean(roster, "hasNext"));
    }


    /// <summary>
    /// Read only the canonical persisted attendance due time. No RunPendingAsync,
    /// timer writes, policy updates or client-supplied role/permission.
    /// </summary>
    public async Task<NativeAutoAttendancePolicy> ReadPolicyAsync(
        NativeIdentity identity, CancellationToken ct = default)
    {
        await VerifyContinuationIdentityAsync(identity, ct).ConfigureAwait(false);
        BridgeResponse response = await new AutoAttendancePolicyService(database)
            .GetAsync(ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(identity, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "GetAutoAttendancePolicy");
        string due = RequiredText(data, "dueTimeLocal");
        if (!AutoAttendancePolicyService.IsValidClock(due))
            throw new InvalidDataException("Canonical SQLCipher due time invalid.");
        return new NativeAutoAttendancePolicy(due, RequiredBoolean(data, "configured"));
    }

    /// <summary>
    /// Same QueryDailyRoster(" __AUTO_PREPARED__ ") as the shipped HTML dialog.
    /// Always 100-row server pages, active-site filtered, exact-date-scoped,
    /// with saved auto-source UTC receipt and all visible custom fields.
    /// </summary>
    public async Task<NativeAutoRegisterPage> ReadAutoRegisterAsync(
        NativeIdentity identity, NativeAutoRegisterFilter filter,
        CancellationToken ct = default)
    {
        if (filter.Page < 1 || filter.PageSize != 100 ||
            filter.SearchTerm.Length > 100 || filter.SearchTerm.Any(char.IsControl))
            throw new InvalidDataException("Invalid auto-prepared register filter.");
        await VerifyContinuationIdentityAsync(identity, ct).ConfigureAwait(false);
        BridgeResponse response = await new DailyMonthlyReadService(database)
            .DailyRosterAsync(new JsonObject
            {
                ["businessDate"] = filter.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["siteId"] = filter.SiteId,
                ["statusId"] = "__AUTO_PREPARED__",
                ["term"] = filter.SearchTerm,
                ["page"] = filter.Page,
                ["pageSize"] = filter.PageSize
            }, ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(identity, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "QueryDailyRoster(__AUTO_PREPARED__)");
        if (RequiredNumber(data, "page") != filter.Page ||
            RequiredNumber(data, "pageSize") != filter.PageSize)
            throw new InvalidDataException("Canonical auto-prepared register page mismatch.");

        var customColumns = RequiredArray(data, "customColumns").Select(node =>
        {
            var item = RequireObject(node);
            return new NativeDashboardColumn(
                RequiredText(item, "columnId"), RequiredText(item, "caption"));
        }).ToArray();

        var rows = RequiredArray(data, "items").Select((node, index) =>
        {
            JsonObject item = RequireObject(node);
            JsonObject custom = RequireObject(item["customFields"]);
            string date = RequiredText(item, "savedBusinessDate");
            if (date != filter.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                throw new InvalidDataException("Auto-prepared register contains a different business date.");
            return new NativeAutoRegisterRow(
                RequiredText(item, "personId"),
                RequiredText(item, "serviceNumberA"),
                RequiredText(item, "name"),
                RequiredText(item, "siteName"),
                date,
                RequiredText(item, "autoSource"),
                RequiredText(item, "committedAtUtc"),
                RequiredText(item, "statusName"),
                customColumns.Select(col => custom[col.ColumnId]?.GetValue<string>() ?? "").ToArray())
            {
                RowNumber = checked((filter.Page - 1) * filter.PageSize + index + 1),
                SavedStatusId = RequiredText(item, "statusId")
            };
        }).ToArray();
        return new NativeAutoRegisterPage(filter.BusinessDate, filter.SiteId,
            RequiredNumber(data, "total"), filter.Page, RequiredBoolean(data, "hasNext"),
            customColumns, rows);
    }

    private async Task VerifyContinuationIdentityAsync(NativeIdentity identity, CancellationToken ct)
    {
        if (!RoleAuthorizationPolicy.CanPerformStandardOperations(identity.Role))
            throw new UnauthorizedAccessException("The role cannot access native attendance.");
        var session = await services.ResolveSessionAsync(identity.SessionNonce, ct)
            .ConfigureAwait(false);
        if (session is null || session.UserId != identity.UserId ||
            session.Username != identity.Username || session.Role != identity.Role ||
            session.SessionNonce != identity.SessionNonce)
            throw new UnauthorizedAccessException("SQLCipher session changed or expired.");
    }

    private static JsonObject RequiredObject(BridgeResponse response, string action) =>
        response.Success && response.Data is JsonObject json
            ? json : throw new InvalidDataException($"Canonical {action} read failed.");

    private static JsonArray RequiredArray(BridgeResponse response, string action) =>
        response.Success && response.Data is JsonArray json
            ? json : throw new InvalidDataException($"Canonical {action} read failed.");

    private static JsonObject RequireObject(JsonNode? node) =>
        node as JsonObject ?? throw new InvalidDataException("Invalid canonical dashboard row.");

    private static JsonArray RequiredArray(JsonObject parent, string key) =>
        parent[key] as JsonArray ?? throw new InvalidDataException($"Missing dashboard array {key}.");

    private static string RequiredText(JsonObject parent, string key) =>
        parent[key]?.GetValue<string>() ??
        throw new InvalidDataException($"Missing dashboard field {key}.");

    private static long RequiredNumber(JsonObject parent, string key)
    {
        if (parent[key] is not JsonValue value)
            throw new InvalidDataException($"Missing dashboard numeric field {key}.");
        // Actual canonical JSON is a mixture of Int32 request echo and Int64
        // SQLite COUNTs; never assume all JsonValues have the same backing type.
        if (value.TryGetValue<long>(out long wide)) return wide;
        if (value.TryGetValue<int>(out int narrow)) return narrow;
        throw new InvalidDataException($"Invalid dashboard numeric field {key}.");
    }

    private static bool RequiredBoolean(JsonObject parent, string key) =>
        parent[key]?.GetValue<bool>() ??
        throw new InvalidDataException($"Missing dashboard flag {key}.");
}
