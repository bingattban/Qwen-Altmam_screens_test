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
    : INativeDashboardReader, INativeDashboardContinuationReader, INativeAttendanceReviewBackend,
      INativeMonthlyAttendanceReader, INativeMonthlyCellDetailsReader,
      INativeMonthlyExcelExportBackend, INativePersonnelDirectoryReader,
      INativePersonnelMutationBackend, INativePersonnelMovementBackend
{
    private readonly NativeSqlCipherAttendanceReviewBackend _review =
        new(services, database);




    // Phase 8: source-of-truth movement preview and commit are ONLY
    // PersonnelMovementService (SQLCipher, one-use preview, audit,
    // immutable assignment/movement history, same-day move guard).
    private static void ValidateMovementDraft(NativePersonnelMovementDraft draft)
    {
        if (draft is null ||
            string.IsNullOrWhiteSpace(draft.PersonId) ||
            draft.PersonId.Length > 128 || draft.PersonId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(draft.PersonName) ||
            draft.PersonName.Length > 200 || draft.PersonName.Any(char.IsControl) ||
            draft.ServiceNumberA is null || draft.Revision < 1 ||
            string.IsNullOrWhiteSpace(draft.SourceSiteId) ||
            draft.SourceSiteId.Length > 128 || draft.SourceSiteId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(draft.SourceSiteName) ||
            draft.SourceSiteName.Length > 150 ||
            draft.IsExternal
                ? draft.ExternalDestinationName is null ||
                  draft.ExternalDestinationName.Length is < 2 or > 150 ||
                  draft.ExternalDestinationName.Any(char.IsControl) ||
                  draft.DestinationSiteId.Length != 0
                : string.IsNullOrWhiteSpace(draft.DestinationSiteId) ||
                  draft.DestinationSiteId.Length > 128 ||
                  draft.DestinationSiteId.Any(char.IsControl) ||
                  draft.DestinationSiteId == draft.SourceSiteId ||
                  draft.ExternalDestinationName.Length != 0)
            throw new InvalidDataException("Invalid transfer intent.");
        _ = CanonicalNumber(draft.ServiceNumberA);
    }

    public async Task<NativePersonnelMovementPreview> PreviewMovementAsync(
        NativeIdentity actor, NativePersonnelMovementDraft draft,
        CancellationToken ct = default)
    {
        ValidateMovementDraft(draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await services.ResolveSessionAsync(actor.SessionNonce, ct)
            .ConfigureAwait(false) ??
            throw new UnauthorizedAccessException("Movement session expired.");
        if (principal.UserId != actor.UserId || principal.Username != actor.Username ||
            principal.Role != actor.Role || principal.SessionNonce != actor.SessionNonce)
            throw new UnauthorizedAccessException("Movement principal changed.");

        string today = LocalPersonDay();
        BridgeResponse response = await new PersonnelMovementService(database)
            .PreviewAsync(new JsonObject
            {
                ["businessDate"] = today,
                ["sourceSiteId"] = draft.SourceSiteId,
                ["destinationSiteId"] = draft.IsExternal ? null : draft.DestinationSiteId,
                ["destinationExternalName"] = draft.IsExternal ? draft.ExternalDestinationName : null,
                ["personIds"] = new JsonArray(JsonValue.Create(draft.PersonId))
            }, principal, draft.IsExternal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "PersonnelMovementPreview");
        string date = RequiredText(data, "businessDate");
        string destination = RequiredText(data, "destinationName");
        string token = RequiredText(data, "previewToken");
        var personArray = RequiredArray(data, "persons");
        if (date != today || RequiredText(data, "sourceSiteId") != draft.SourceSiteId ||
            RequiredBoolean(data, "external") != draft.IsExternal ||
            (!draft.IsExternal &&
                RequiredText(data, "destinationSiteId") != draft.DestinationSiteId) ||
            (draft.IsExternal && data["destinationSiteId"] is not null) ||
            RequiredNumber(data, "count") != 1 ||
            personArray.Count != 1 || token.Length != 64 ||
            token.Any(c => !char.IsAsciiHexDigit(c)) ||
            (draft.IsExternal && destination != draft.ExternalDestinationName) ||
            string.IsNullOrWhiteSpace(destination))
            throw new InvalidDataException("Canonical transfer preview mismatch.");
        JsonObject person = RequireObject(personArray[0]);
        NativePersonnelMovementPerson mapped = new(
            RequiredText(person, "personId"),
            RequiredText(person, "name"),
            RequiredText(person, "serviceNumberA"),
            RequiredNumber(person, "rowVersion"),
            RequiredText(person, "sourceSiteId"),
            RequiredText(person, "sourceSiteName"),
            RequiredText(person, "sourceAssignmentId"));
        if (mapped.PersonId != draft.PersonId || mapped.Name != draft.PersonName ||
            mapped.ServiceNumberA != draft.ServiceNumberA ||
            mapped.Revision != draft.Revision ||
            mapped.SourceSiteId != draft.SourceSiteId ||
            mapped.SourceSiteName != draft.SourceSiteName ||
            mapped.SourceAssignmentId.Length is < 3 or > 128)
            throw new InvalidDataException("Person moved since the selected roster.");
        return new NativePersonnelMovementPreview(draft, date, token, destination, [mapped]);
    }

    public async Task<NativePersonnelMovementReceipt> CommitMovementAsync(
        NativeIdentity actor, NativePersonnelMovementPreview preview,
        string operationId, CancellationToken ct = default)
    {
        if (preview is null || !Guid.TryParseExact(operationId, "N", out _) ||
            preview.BusinessDate != LocalPersonDay() ||
            preview.PreviewToken.Length != 64 ||
            preview.PreviewToken.Any(c => !char.IsAsciiHexDigit(c)) ||
            preview.Persons.Count != 1 ||
            (preview.Draft.IsExternal &&
                preview.DestinationName != preview.Draft.ExternalDestinationName))
            throw new InvalidDataException("Movement approval expired or invalid.");
        ValidateMovementDraft(preview.Draft);
        var person = preview.Persons[0];
        if (person.PersonId != preview.Draft.PersonId ||
            person.Name != preview.Draft.PersonName ||
            person.ServiceNumberA != preview.Draft.ServiceNumberA ||
            person.Revision != preview.Draft.Revision ||
            person.SourceSiteId != preview.Draft.SourceSiteId ||
            person.SourceSiteName != preview.Draft.SourceSiteName ||
            string.IsNullOrWhiteSpace(person.SourceAssignmentId))
            throw new InvalidDataException("Movement identity preview changed.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await services.ResolveSessionAsync(actor.SessionNonce, ct)
            .ConfigureAwait(false) ??
            throw new UnauthorizedAccessException("Movement session expired.");
        if (principal.UserId != actor.UserId || principal.Username != actor.Username ||
            principal.Role != actor.Role || principal.SessionNonce != actor.SessionNonce)
            throw new UnauthorizedAccessException("Movement principal changed.");
        BridgeResponse result = await new PersonnelMovementService(database)
            .CommitAsync(new JsonObject
            {
                ["previewToken"] = preview.PreviewToken,
                ["operationId"] = operationId
            }, principal, preview.Draft.IsExternal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject receipt = RequiredObject(result, "PersonnelMovementCommit");
        if (RequiredNumber(receipt, "count") != 1 ||
            RequiredText(receipt, "businessDate") != preview.BusinessDate ||
            RequiredBoolean(receipt, "external") != preview.Draft.IsExternal ||
            RequiredText(receipt, "destinationName") != preview.DestinationName)
            throw new InvalidDataException("Canonical transfer receipt mismatch.");
        return new NativePersonnelMovementReceipt(
            preview.Draft.IsExternal, preview.BusinessDate, 1, preview.DestinationName);
    }

    // Phase 8: all mutations reuse existing audited canonical domain services.
    // The adapter is the only route from native WPF to SQLCipher writes.
    private static string CanonicalNumber(string input)
    {
        if (!ServiceNumberPolicy.TryCanonicalize(input, out string canonical))
            throw new InvalidDataException("Invalid service number A (4–70 digits).");
        return canonical;
    }

    private static string LocalPersonDay() =>
        DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void ValidatePersonDraft(NativePersonnelMutationDraft draft)
    {
        if (draft.Name is null || draft.Number is null ||
            draft.SiteId is null || draft.Reason is null ||
            draft.Name.Length is < 3 or > 200 ||
            draft.Name != draft.Name.Trim() ||
            draft.Name.Any(char.IsControl) ||
            draft.SiteId.Length > 128 || draft.SiteId.Any(char.IsControl) ||
            (!draft.IsCreate &&
                (draft.Reason.Length is < 3 or > 500 ||
                 draft.Reason.Any(char.IsControl))) ||
            (draft.IsCreate &&
                (draft.PersonId.Length != 0 || draft.BaseRevision != 0 ||
                 draft.OriginalName.Length != 0 || draft.OriginalNumber.Length != 0)) ||
            (!draft.IsCreate &&
                (string.IsNullOrWhiteSpace(draft.PersonId) ||
                 draft.PersonId.Length > 128 || draft.PersonId.Any(char.IsControl) ||
                 draft.BaseRevision < 1 ||
                 draft.OriginalName.Length < 3 ||
                 draft.OriginalNumber.Length < 4)))
            throw new InvalidDataException("Invalid person identity mutation draft.");
        _ = CanonicalNumber(draft.Number);
    }

    public async Task<NativePersonnelMutationPreview> PreviewAsync(
        NativeIdentity actor, NativePersonnelMutationDraft draft,
        CancellationToken ct = default)
    {
        ValidatePersonDraft(draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        string number = CanonicalNumber(draft.Number);
        string day = LocalPersonDay();
        NativePersonnelMutationPreview result;

        if (draft.IsCreate)
        {
            var principal = await services.ResolveSessionAsync(actor.SessionNonce, ct)
                .ConfigureAwait(false) ??
                throw new UnauthorizedAccessException("Personnel session not found.");
            if (principal.UserId != actor.UserId || principal.Username != actor.Username ||
                principal.Role != actor.Role || principal.SessionNonce != actor.SessionNonce)
                throw new UnauthorizedAccessException("Personnel principal changed.");
            BridgeResponse preview = await new ArchivedReturnService(database)
                .PreviewAsync(new JsonObject
                {
                    ["visibleName"] = draft.Name,
                    ["serviceNumberA"] = number,
                    ["siteId"] = draft.SiteId
                }, principal, ct).ConfigureAwait(false);
            JsonObject data = RequiredObject(preview, "ResolveArchivedIdentity");
            JsonArray candidates = RequiredArray(data, "candidates");
            bool conflict = RequiredBoolean(data, "requiresResolution");
            if (conflict != (candidates.Count > 0) || candidates.Count > 50 ||
                RequiredText(data, "businessDate") != day)
                throw new InvalidDataException("Archived identity preview mismatch.");
            result = new NativePersonnelMutationPreview(
                draft, number, day, "", conflict, candidates.Count, "", "");
        }
        else
        {
            BridgeResponse preview = await new PersonRosterService(database)
                .PreviewPersonIdentityUpdateAsync(new JsonObject
                {
                    ["personId"] = draft.PersonId,
                    ["baseRevision"] = draft.BaseRevision,
                    ["visibleName"] = draft.Name,
                    ["serviceNumberA"] = number,
                    ["effectiveLocalDate"] = day,
                    ["actorHint"] = actor.UserId
                }, ct).ConfigureAwait(false);
            JsonObject data = RequiredObject(preview, "PreviewPersonIdentityUpdate");
            string token = RequiredText(data, "previewToken");
            string oldName = RequiredText(data, "oldName");
            string oldNumber = RequiredText(data, "oldServiceNumberA");
            if (RequiredText(data, "personId") != draft.PersonId ||
                RequiredNumber(data, "baseRevision") != draft.BaseRevision ||
                oldName != draft.OriginalName ||
                oldNumber != draft.OriginalNumber ||
                RequiredText(data, "newName") != draft.Name ||
                RequiredText(data, "newServiceNumberA") != number ||
                RequiredText(data, "effectiveLocalDate") != day ||
                !RequiredBoolean(data, "willCloseOldSnapshot") ||
                token.Length != 64)
                throw new InvalidDataException("Canonical identity update preview mismatch.");
            result = new NativePersonnelMutationPreview(
                draft, number, day, token, false, 0, oldName, oldNumber);
        }
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<NativePersonnelMutationReceipt> CommitAsync(
        NativeIdentity actor, NativePersonnelMutationPreview preview,
        string operationId, CancellationToken ct = default)
    {
        if (preview is null || preview.RequiresIdentityResolution ||
            preview.CandidateCount != 0 ||
            preview.EffectiveLocalDate != LocalPersonDay() ||
            !Guid.TryParseExact(operationId, "N", out _) ||
            preview.OldName != preview.Draft.OriginalName ||
            preview.OldNumber != preview.Draft.OriginalNumber ||
            preview.CanonicalNumber != CanonicalNumber(preview.Draft.Number) ||
            (!preview.Draft.IsCreate && preview.PreviewToken.Length != 64) ||
            (preview.Draft.IsCreate && preview.PreviewToken.Length != 0))
            throw new InvalidDataException("Invalid or expired identity approval.");
        ValidatePersonDraft(preview.Draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response;
        if (preview.Draft.IsCreate)
        {
            // All archived-name/number collisions are blocked by the native
            // preview. The canonical transaction re-checks them, along with
            // active uniqueness and existing active-site assignment.
            response = await new PersonRosterService(database).CreatePersonAsync(
                actor.UserId, actor.Username, new JsonObject
                {
                    ["visibleName"] = preview.Draft.Name,
                    ["serviceNumberA"] = preview.CanonicalNumber,
                    ["siteId"] = preview.Draft.SiteId,
                    ["firstRecordedLocalDate"] = preview.EffectiveLocalDate,
                    ["operationId"] = operationId
                }, ct).ConfigureAwait(false);
        }
        else
        {
            response = await new PersonRosterService(database).UpdatePersonIdentityAsync(
                actor.UserId, actor.Username, new JsonObject
                {
                    ["personId"] = preview.Draft.PersonId,
                    ["expectedRevision"] = preview.Draft.BaseRevision,
                    ["visibleName"] = preview.Draft.Name,
                    ["serviceNumberA"] = preview.CanonicalNumber,
                    ["effectiveLocalDate"] = preview.EffectiveLocalDate,
                    ["reason"] = preview.Draft.Reason,
                    ["previewToken"] = preview.PreviewToken,
                    ["operationId"] = operationId
                }, ct).ConfigureAwait(false);
        }
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject result = RequiredObject(response,
            preview.Draft.IsCreate ? "CreatePerson" : "UpdatePersonIdentity");
        string id = RequiredText(result, "personId");
        long revision = RequiredNumber(result, "revision");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
            (!preview.Draft.IsCreate && id != preview.Draft.PersonId) ||
            revision != (preview.Draft.IsCreate ? 1 : preview.Draft.BaseRevision + 1))
            throw new InvalidDataException("Identity commit receipt mismatch.");
        return new NativePersonnelMutationReceipt(preview.Draft.IsCreate, id, revision);
    }

    // Phase 8: personnel administration reads only the original roster
    // service, with the same permanent PersonId and current custom fields.
    // Both the verified session and manager-only authorization are required.
    public async Task<NativePersonnelCatalogue> ReadPersonnelCatalogueAsync(
        NativeIdentity actor, CancellationToken ct = default)
    {
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await services.ListSitesAsync(
            includeInactive: true, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var sites = RequiredArray(response, "NativePersonnelSites")
            .Select(node =>
            {
                JsonObject site = RequireObject(node);
                return new NativeDashboardSite(
                    RequiredText(site, "siteId"), RequiredText(site, "name"));
            }).ToArray();
        return new NativePersonnelCatalogue(sites);
    }

    public async Task<NativePersonnelSnapshot> ReadPageAsync(
        NativeIdentity actor, NativePersonnelFilter filter,
        CancellationToken ct = default)
    {
        if (filter.SiteId is null || filter.SiteId.Length > 128 ||
            filter.SiteId.Any(char.IsControl) ||
            filter.SearchTerm is null || filter.SearchTerm.Length > 100 ||
            filter.SearchTerm.Any(char.IsControl) ||
            filter.OrganizationalStatus is < 0 or > 4 ||
            filter.Page is < 1 or > 100000 || filter.PageSize != 100)
            throw new InvalidDataException("Invalid personnel directory filter.");

        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new PersonRosterService(database)
            .QueryPersonsAsync(new JsonObject
            {
                ["siteId"] = filter.SiteId,
                ["term"] = filter.SearchTerm,
                ["organizationalStatus"] = filter.OrganizationalStatus,
                ["includeArchived"] = filter.OrganizationalStatus == 0,
                ["page"] = filter.Page,
                ["pageSize"] = filter.PageSize
            }, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "QueryPersons");
        if (RequiredNumber(data, "page") != filter.Page ||
            RequiredNumber(data, "pageSize") != filter.PageSize)
            throw new InvalidDataException("Personnel directory page mismatch.");
        var cols = RequiredArray(data, "customColumns").Select(node =>
        {
            JsonObject col = RequireObject(node);
            return new NativeDashboardColumn(
                RequiredText(col, "columnId"), RequiredText(col, "caption"));
        }).ToArray();
        var rows = RequiredArray(data, "items").Select((node, index) =>
        {
            JsonObject row = RequireObject(node);
            JsonObject fields = RequireObject(row["customFields"]);
            return new NativePersonnelRow(
                RequiredText(row, "personId"),
                RequiredText(row, "serviceNumberA"),
                RequiredText(row, "displayName"),
                RequiredText(row, "siteName"),
                checked((int)RequiredNumber(row, "organizationalStatus")),
                RequiredBoolean(row, "active"), RequiredNumber(row, "revision"),
                cols.Select(col => fields[col.ColumnId]?.GetValue<string>() ?? "").ToArray())
            {
                RowNumber = checked((filter.Page - 1) * filter.PageSize + index + 1)
            };
        }).ToArray();
        if (RequiredNumber(data, "count") != rows.Length)
            throw new InvalidDataException("Canonical person count mismatch.");
        return new NativePersonnelSnapshot(
            filter.SiteId, filter.SearchTerm, filter.OrganizationalStatus,
            filter.Page, RequiredNumber(data, "total"),
            RequiredBoolean(data, "hasNext"), cols, rows);
    }

    public async Task<NativePersonnelProfile> ReadProfileAsync(
        NativeIdentity actor, string personId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(personId) || personId.Length > 128 ||
            personId.Any(char.IsControl))
            throw new InvalidDataException("Invalid stable person identifier.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new PersonRosterService(database)
            .GetPersonSummaryAsync(new JsonObject { ["personId"] = personId }, ct)
            .ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "GetPersonSummary");
        if (RequiredText(data, "personId") != personId)
            throw new InvalidDataException("Canonical person profile identity mismatch.");
        var history = RequiredArray(data, "identityHistory").Select(node =>
        {
            JsonObject entry = RequireObject(node);
            return new NativePersonnelIdentityHistory(
                RequiredText(entry, "name"),
                RequiredText(entry, "serviceNumberA"),
                RequiredText(entry, "validFrom"),
                entry["validTo"]?.GetValue<string>() ?? "",
                RequiredText(entry, "reason"),
                RequiredText(entry, "recordedAtUtc"));
        }).ToArray();
        if (history.Length > 50)
            throw new InvalidDataException("Excessive person identity history.");
        return new NativePersonnelProfile(
            personId, RequiredText(data, "displayName"),
            RequiredText(data, "serviceNumberA"),
            checked((int)RequiredNumber(data, "organizationalStatus")),
            RequiredBoolean(data, "active"), RequiredNumber(data, "revision"),
            RequiredText(data, "firstRecordedLocalDate"), history);
    }

    private async Task VerifyPersonnelManagerAsync(
        NativeIdentity actor, CancellationToken ct)
    {
        if (!RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))
            throw new UnauthorizedAccessException("Personnel administration requires a manager.");
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Monthly WPF catalogue: original archive periods and ALL canonical sites,
    /// including inactive sites needed by historical site filtering.
    /// </summary>
    public async Task<NativeMonthlyCatalogue> ReadCatalogueAsync(
        NativeIdentity actor, CancellationToken ct = default)
    {
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
        var canonical = new DailyMonthlyReadService(database);
        BridgeResponse periodsResponse = await canonical.ArchivePeriodsAsync(ct)
            .ConfigureAwait(false);
        BridgeResponse sitesResponse = await services.ListSitesAsync(
            includeInactive: true, ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);

        var periodArray = RequiredArray(periodsResponse, "GetMonthlyArchivePeriods");
        if (periodArray.Count > 5000)
            throw new InvalidDataException("Canonical monthly archive is too large.");
        var periods = periodArray.Select(x => x?.GetValue<string>() ??
            throw new InvalidDataException("Invalid month in archive index.")).ToArray();

        var sites = RequiredArray(sitesResponse, "QuerySites(historical)")
            .Select(x =>
            {
                var site = RequireObject(x);
                return new NativeDashboardSite(
                    RequiredText(site, "siteId"), RequiredText(site, "name"));
            }).ToArray();
        return new NativeMonthlyCatalogue(periods, sites);
    }

    /// <summary>
    /// Read-only canonical historical month including *dated* attendance
    /// snapshot symbols and movement annotations; never infer prior statuses
    /// from the current mutable person record or status catalogue.
    /// </summary>
    public async Task<NativeMonthlySnapshot> ReadMonthAsync(
        NativeIdentity actor, NativeMonthlyFilter filter,
        CancellationToken ct = default)
    {
        if (filter.Period is not { Length: 7 } ||
            !DateOnly.TryParseExact(filter.Period + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly monthStart) ||
            monthStart.Year is < 1900 or > 2200 ||
            monthStart > new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1) ||
            filter.Page is < 1 or > 100000 || filter.PageSize != 100 ||
            filter.SiteId is null || filter.SearchTerm is null ||
            filter.SearchTerm.Length > 100 || filter.SearchTerm.Any(char.IsControl))
            throw new InvalidDataException("Invalid native monthly archive request.");

        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
        bool historical = monthStart.Year != DateTime.Now.Year ||
                          monthStart.Month != DateTime.Now.Month;
        BridgeResponse response = await new DailyMonthlyReadService(database)
            .MonthlyGridAsync(new JsonObject
            {
                ["year"] = monthStart.Year,
                ["month"] = monthStart.Month,
                ["siteId"] = filter.SiteId,
                ["term"] = filter.SearchTerm,
                ["page"] = filter.Page,
                ["pageSize"] = 100,
                ["archiveMode"] = historical,
                ["dayFrom"] = 1,
                ["dayTo"] = DateTime.DaysInMonth(monthStart.Year, monthStart.Month)
            }, ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);

        JsonObject data = RequiredObject(response, "QueryMonthlyGrid");
        int days = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        if (RequiredNumber(data, "year") != monthStart.Year ||
            RequiredNumber(data, "month") != monthStart.Month ||
            RequiredNumber(data, "daysInMonth") != days ||
            RequiredNumber(data, "dayFrom") != 1 ||
            RequiredNumber(data, "dayTo") != days ||
            RequiredNumber(data, "page") != filter.Page)
            throw new InvalidDataException("Canonical monthly archive response mismatch.");

        var columns = RequiredArray(data, "customColumns").Select(column =>
        {
            var field = RequireObject(column);
            return new NativeDashboardColumn(
                RequiredText(field, "columnId"), RequiredText(field, "caption"));
        }).ToArray();
        var rows = RequiredArray(data, "items").Select((node, index) =>
        {
            var row = RequireObject(node);
            var fields = RequireObject(row["customFields"]);
            var savedDays = RequireObject(row["days"]);
            var monthly = new NativeMonthlyCell?[days];
            foreach (var saved in savedDays)
            {
                if (!DateOnly.TryParseExact(saved.Key, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                    date.Year != monthStart.Year || date.Month != monthStart.Month)
                    throw new InvalidDataException("Invalid historical attendance date.");
                var info = RequireObject(saved.Value);
                string description = RequiredText(info, "name");
                string movement = info["movementNote"]?.GetValue<string>() ?? "";
                if (movement.Length > 0) description += " — " + movement;
                monthly[date.Day - 1] = new NativeMonthlyCell(
                    RequiredText(info, "symbol"), description,
                    info["color"]?.GetValue<string>(), movement);
            }
            return new NativeMonthlyRow(
                RequiredText(row, "personId"),
                RequiredText(row, "serviceNumberA"),
                RequiredText(row, "name"),
                RequiredText(row, "siteName"),
                columns.Select(col => fields[col.ColumnId]?.GetValue<string>() ?? "").ToArray(),
                monthly)
            {
                RowNumber = checked((filter.Page - 1) * filter.PageSize + index + 1)
            };
        }).ToArray();
        if (rows.Length > filter.PageSize)
            throw new InvalidDataException("Monthly page exceeds server browse limit.");
        return new NativeMonthlySnapshot(
            filter.Period, filter.SiteId, filter.Page, days,
            RequiredNumber(data, "total"), RequiredBoolean(data, "hasNext"),
            columns, rows);
    }


    /// <summary>
    /// Native monthly cell details use the ORIGINAL historical SQLCipher
    /// service. No independent historical SQL or guessed attendance, and
    /// every response is bound to the verified session, PersonId and date.
    /// </summary>
    public async Task<NativeMonthlyCellDetails> ReadCellDetailsAsync(
        NativeIdentity actor, NativeMonthlyCellRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.PersonId) ||
            request.PersonId.Length > 128 || request.SiteId is null ||
            request.BusinessDate.Year is < 1900 or > 2200 ||
            request.BusinessDate > DateOnly.FromDateTime(DateTime.Now))
            throw new InvalidDataException("Invalid native monthly cell request.");

        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new DailyMonthlyReadService(database)
            .MonthlyCellDetailsAsync(new JsonObject
            {
                ["personId"] = request.PersonId,
                ["businessDate"] = request.BusinessDate.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
                ["siteId"] = request.SiteId
            }, ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);

        JsonObject data = RequiredObject(response, "GetMonthlyCellDetails");
        if (RequiredText(data, "personId") != request.PersonId ||
            RequiredText(data, "businessDate") != request.BusinessDate.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture))
            throw new InvalidDataException("Canonical historical detail identity/date mismatch.");

        JsonObject? attendance = data["attendance"] is null ? null :
            RequireObject(data["attendance"]);
        JsonObject? movement = data["movement"] is null ? null :
            RequireObject(data["movement"]);
        JsonArray revisions = RequiredArray(data, "revisions");
        if (revisions.Count > 100)
            throw new InvalidDataException("Excessive historical attendance revisions.");
        string status = attendance is null ? "لا توجد حالة حضور مسجلة" :
            RequiredText(attendance, "statusName");
        string symbol = attendance is null ? "" : RequiredText(attendance, "symbol");
        string source = attendance is null ? "" : RequiredText(attendance, "source");
        string attendanceSite = attendance is null ? "" : RequiredText(attendance, "siteName");
        string committed = attendance is null ? "" : RequiredText(attendance, "committedAtUtc");
        string movementSummary = "لا توجد حركة مسجلة في هذا اليوم";
        string movementReason = "";
        if (movement is not null)
        {
            int kind = checked((int)RequiredNumber(movement, "type"));
            string label = kind switch
            {
                1 => "نقل داخلي", 2 => "نقل خارجي",
                3 => "فصل", 4 => "عودة للقوة",
                _ => throw new InvalidDataException("Unknown canonical movement type.")
            };
            string from = RequiredText(movement, "sourceSiteName");
            string to = movement["destinationSiteName"]?.GetValue<string>() ??
                        movement["destinationExternalName"]?.GetValue<string>() ?? "";
            movementSummary = to.Length > 0 ? $"{label} من {from} إلى {to}" :
                $"{label} من {from}";
            movementReason = movement["separationReason"]?.GetValue<string>() ?? "";
        }
        var history = revisions.Select(node =>
        {
            JsonObject revision = RequireObject(node);
            return new NativeMonthlyRevisionDetail(
                RequiredText(revision, "beforeStatus"),
                RequiredText(revision, "afterStatus"),
                RequiredText(revision, "reason"),
                RequiredText(revision, "actorUserId"),
                RequiredText(revision, "executedAtUtc"),
                RequiredText(revision, "authorizationMethod"));
        }).ToArray();

        return new NativeMonthlyCellDetails(
            RequiredText(data, "personId"), request.BusinessDate,
            RequiredText(data, "personName"), RequiredText(data, "serviceNumberA"),
            RequiredText(data, "siteName"), status, symbol, attendanceSite,
            source, committed, movementSummary, movementReason, history);
    }


    // Only this existing canonical service chooses the local output directory,
    // writes .xlsx and enforces non-overwrite/atomic completion. Presentation
    // layers never supply a path or clone any Excel writer.
    private static (int Year, int Month) ValidateMonthlyExcelFilter(
        NativeMonthlyExcelFilter filter)
    {
        if (filter.Period is not { Length: 7 } ||
            !DateOnly.TryParseExact(filter.Period + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var period) ||
            period.Year is < 1900 or > 2199 ||
            period > new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1) ||
            filter.SiteId is null || filter.SiteId.Length > 128 ||
            filter.SiteId.Any(char.IsControl) ||
            filter.SearchTerm is null || filter.SearchTerm.Length > 100 ||
            filter.SearchTerm.Any(char.IsControl))
            throw new InvalidDataException("Invalid native monthly XLSX filter.");
        return (period.Year, period.Month);
    }

    private static JsonObject MonthlyExcelPayload(
        NativeMonthlyExcelFilter filter, int year, int month) => new()
        {
            ["year"] = year, ["month"] = month,
            ["siteId"] = filter.SiteId, ["term"] = filter.SearchTerm
        };

    public async Task<NativeMonthlyExcelPreview> PreviewExcelAsync(
        NativeIdentity actor, NativeMonthlyExcelFilter filter,
        CancellationToken ct = default)
    {
        var (year, month) = ValidateMonthlyExcelFilter(filter);
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new MonthlyExcelExportService(database)
            .PreviewAsync(MonthlyExcelPayload(filter, year, month), ct)
            .ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "PreviewMonthlyExcelExport");
        long total = RequiredNumber(data, "personCount");
        if (RequiredNumber(data, "year") != year ||
            RequiredNumber(data, "month") != month ||
            RequiredNumber(data, "daysInMonth") != DateTime.DaysInMonth(year, month) ||
            RequiredText(data, "siteId") != filter.SiteId ||
            RequiredText(data, "term") != filter.SearchTerm ||
            RequiredText(data, "format") != "xlsx" ||
            total is < 0 or > 100000)
            throw new InvalidDataException("Monthly Excel preview mismatch.");
        string name = RequiredText(data, "suggestedFileName");
        if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            name.Contains('/') || name.Contains('\\'))
            throw new InvalidDataException("Unsafe monthly Excel filename.");
        return new NativeMonthlyExcelPreview(
            filter.Period, filter.SiteId, filter.SearchTerm,
            RequiredText(data, "siteName"), name, total,
            DateTime.DaysInMonth(year, month));
    }

    public async Task<NativeMonthlyExcelReceipt> ExportExcelAsync(
        NativeIdentity actor, NativeMonthlyExcelFilter filter,
        CancellationToken ct = default)
    {
        var (year, month) = ValidateMonthlyExcelFilter(filter);
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new MonthlyExcelExportService(database)
            .ExportAsync(MonthlyExcelPayload(filter, year, month), ct)
            .ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "ExportMonthlyExcel");
        // The existing service exports combined + per-site workbooks when
        // siteId is empty; otherwise exactly one workbook. Its FileCount
        // only appears for the consolidated all-sites result.
        int fileCount = filter.SiteId.Length == 0
            ? checked((int)RequiredNumber(data, "fileCount")) : 1;
        string filePath = RequiredText(data, "filePath");
        long total = RequiredNumber(data, "personCount");
        if (RequiredText(data, "format") != "xlsx" ||
            fileCount is < 1 or > 10001 ||
            total is < 1 or > 100000 ||
            !filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            RequiredNumber(data, "year") != year ||
            RequiredNumber(data, "month") != month ||
            (filter.SiteId.Length != 0 &&
                (RequiredText(data, "siteId") != filter.SiteId ||
                 RequiredText(data, "term") != filter.SearchTerm)))
            throw new InvalidDataException("Monthly Excel export receipt mismatch.");
        return new NativeMonthlyExcelReceipt(
            filter.Period, filter.SiteId, filter.SearchTerm,
            filePath, fileCount, total);
    }

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
