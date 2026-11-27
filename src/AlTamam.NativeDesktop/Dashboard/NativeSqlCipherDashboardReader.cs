using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
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
    BridgeSharedServices services, AlTamam.Infrastructure.Persistence.AlTamamDbContext database,
    NativeOperationDrainGate operations)
    : INativeDashboardReader, INativeDashboardContinuationReader, INativeAttendanceReviewBackend,
      INativeMonthlyAttendanceReader, INativeMonthlyCellDetailsReader, INativeClosedDayCorrectionBackend, INativeHistoricalRangeBackend, INativeAdministrativeCatchUpBackend, INativeClosedDayPolicyBackend,
      INativeMonthlyExcelExportBackend, INativeMonthlyExcelProgressBackend, INativePersonnelDirectoryReader,
      INativePersonnelMutationBackend, INativePersonnelMovementBackend, INativeMovementArchiveBackend,
      INativeRosterRemovalPreviewBackend, INativePersonnelSeparationBackend, INativeSeparationArchiveBackend, INativeArchivedReturnBackend, INativeSiteCatalogBackend, INativeStatusCatalogBackend, INativeSiteRetirementBackend, INativeBulkInternalTransferBackend, INativeBulkExternalTransferBackend, INativeExcelPersonnelImportBackend,
      INativeImportOutcomeHistoryBackend, INativePersonnelReportReader, INativeHistoricalPeriodReader,
      INativePersonnelGroupReportReader, INativePersonnelGroupExcelBackend, INativeIndividualExcelBackend, INativeAuditReader, INativeAuditDetailReader, INativeAuditEditBackend, INativeAuditHistoryReader, INativeAccountReader, INativeAccountCreateBackend, INativeAccountLifecycleBackend, INativeSettingsOverviewReader, INativeManualBackupBackend, INativeBackupScheduleBackend, INativeBackupInspectionBackend, INativeProgramDiagnosticsBackend, INativeThemeBackend, INativeRosterLayoutReader, INativeRosterColumnMutationBackend, INativeAttendanceDraftReader, INativeAttendanceDraftWriter, INativeAttendanceDraftDeleter
{
    // Phase 11: canonical read-only audit query with the actual durable principal.
    public async Task<NativeAuditSnapshot> ReadAuditAsync(NativeIdentity actor,NativeAuditFilter filter,
        CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if (filter is null || !filter.IsValid)
            throw new InvalidDataException("Invalid native audit filter.");
        if (!filter.IsAuthorized(actor.Role))
            throw new UnauthorizedAccessException("Native audit scope denied.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if (principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce ||
            !filter.IsAuthorized(principal.Role))
            throw new UnauthorizedAccessException("Audit session changed.");
        // The canonical list is read-only and does not use the optional clock.
        var response=await new AuditOverlayService(database).QueryAuditLogAsync(new JsonObject
        {
            ["term"]=filter.Term,["actor"]=filter.Actor,["target"]=filter.Target,
            ["operation"]=filter.Operation,["fromDate"]=filter.FromDate,["toDate"]=filter.ToDate,
            ["includeTombstoned"]=filter.IncludeTombstoned,["page"]=filter.Page,
            ["pageSize"]=NativeAuditFilter.PageSize
        },ct,principal).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var data=RequiredObject(response,"QueryAuditLog");
        var items=RequiredArray(data,"items");
        if (RequiredNumber(data,"page")!=filter.Page ||
            RequiredNumber(data,"pageSize")!=NativeAuditFilter.PageSize ||
            RequiredNumber(data,"count")!=items.Count || items.Count>NativeAuditFilter.PageSize)
            throw new InvalidDataException("Canonical audit page mismatch.");
        var rows=items.Select(node =>
        {
            var row=RequireObject(node);
            return new NativeAuditRow(RequiredText(row,"auditEventId"),RequiredNumber(row,"sequence"),
                RequiredText(row,"operationCode"),RequiredText(row,"operationArabicName"),
                RequiredText(row,"actorDisplay"),RequiredText(row,"targetDisplay"),
                RequiredText(row,"description"),RequiredText(row,"rawDescription"),
                row["reason"]?.GetValue<string>()??"",RequiredText(row,"effectiveLocalDate"),
                RequiredText(row,"executedAtUtc"),RequiredText(row,"outcome"),RequiredText(row,"eventHash"),
                RequiredBoolean(row,"isTombstoned"),checked((int)RequiredNumber(row,"overlayVersion")));
        }).ToArray();
        var snapshot=new NativeAuditSnapshot(filter,RequiredNumber(data,"total"),
            RequiredBoolean(data,"hasNext"),Array.AsReadOnly(rows));
        if (!snapshot.Matches(filter))
            throw new InvalidDataException("Canonical audit rows invalid or changed during read.");
        return snapshot;
    }

    // Read the canonical immutable entry and verify that the presentation revision
    // still matches the selected list row. Do not expose the overlay history here.
    public async Task<NativeAuditDetail> ReadAuditDetailAsync(NativeIdentity actor,NativeAuditRow row,
        CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if (row is null || !row.IsValid)
            throw new InvalidDataException("Invalid audit selection.");
        if (row.IsTombstoned && !RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(actor.Role))
            throw new UnauthorizedAccessException("Hidden audit detail denied.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var response=await new AuditOverlayService(database).GetAuditEntryDetailsAsync(
            new JsonObject { ["auditEventId"]=row.AuditEventId },ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var data=RequiredObject(response,"GetAuditEntryDetails");
        int version=0;
        bool hidden=false;
        var state=new NativeAuditEditState(0,false,RequiredText(data,"rawDescription"));
        foreach (var node in RequiredArray(data,"overlays"))
        {
            var overlay=RequireObject(node);
            if (RequiredNumber(overlay,"overlayVersion")!=++version)
                throw new InvalidDataException("Noncontiguous canonical audit overlay history.");
            hidden=RequiredBoolean(overlay,"isTombstoned");
            NativeAuditEditState ReadState(string key)
            {
                var item=RequireObject(overlay[key]);
                return new(RequiredNumber(item,"overlayVersion"),RequiredBoolean(item,"isTombstoned"),
                    RequiredText(item,"displayDescription"));
            }
            var before=ReadState("beforeState");
            var after=ReadState("afterState");
            if(before!=state || after.Version!=version || after.IsTombstoned!=hidden ||
                !overlay.ContainsKey("correctedDescription"))
                throw new InvalidDataException("Discontinuous audit presentation state.");
            var corrected=overlay["correctedDescription"]?.GetValue<string>();
            if(hidden ? corrected is not null || after.Description!=before.Description
                : string.IsNullOrWhiteSpace(corrected) || after.Description!=corrected)
                throw new InvalidDataException("Invalid audit presentation transition.");
            state=after;
        }
        if(!NativeAuditDetail.MatchesPresentation(row,state))
            throw new InvalidDataException("Audit presentation changed since list read.");
        // A formerly visible row may have been hidden since the list was read.
        if (hidden && !RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(actor.Role))
            throw new UnauthorizedAccessException("Hidden audit detail denied.");
        var detail=new NativeAuditDetail(RequiredText(data,"auditEventId"),
            RequiredText(data,"operationCode"),RequiredText(data,"operationArabicName"),
            RequiredText(data,"actorUserId"),RequiredText(data,"actorUsername"),
            data["actorRosterFullName"]?.GetValue<string>()??"",
            RequiredText(data,"targetEntityType"),RequiredText(data,"targetEntityId"),
            RequiredText(data,"targetDisplay"),RequiredText(data,"rawDescription"),
            data["reason"]?.GetValue<string>()??"",RequiredText(data,"effectiveLocalDate"),
            RequiredText(data,"executedAtUtc"),RequiredText(data,"correlationId"),
            RequiredText(data,"eventHash"),RequiredText(data,"priorEventHash"),
            data["oldValue"]?.ToJsonString(),data["newValue"]?.ToJsonString(),
            hidden,version,RequiredBoolean(data,"chainValid"));
        if (!detail.Matches(row,actor.Role))
            throw new InvalidDataException("Audit detail or presentation revision changed.");
        return detail;
    }

    // Canonical append-only overlay mutation; no independent SQL or audit writer.
    public Task<NativeAuditEditReceipt> EditAuditDescriptionAsync(NativeIdentity actor,
        NativeAuditEditRequest request,CancellationToken ct=default)
        =>
        operations.RunAsync(() => ApplyAuditOverlayAsync(actor,request,hide:false,ct));

    public Task<NativeAuditEditReceipt> HideAuditEntryAsync(NativeIdentity actor,
        NativeAuditEditRequest request,CancellationToken ct=default)
        =>
        operations.RunAsync(() => ApplyAuditOverlayAsync(actor,request,hide:true,ct));

    private async Task<NativeAuditEditReceipt> ApplyAuditOverlayAsync(NativeIdentity actor,
        NativeAuditEditRequest request,bool hide,CancellationToken ct)
    {
        if (!RoleAuthorizationPolicy.CanEditOrDeleteVisibleAuditLog(actor.Role))
            throw new UnauthorizedAccessException("Audit edit denied.");
        if (request is null || request.Hide!=hide || !request.IsValid(actor.Role))
            throw new InvalidDataException("Invalid native audit edit intent.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if (principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce ||
            !RoleAuthorizationPolicy.CanEditOrDeleteVisibleAuditLog(principal.Role))
            throw new UnauthorizedAccessException("Audit edit session changed.");
        // Do not reread the old revision before a retry: canonical idempotency
        // must first resolve the same operation even if the first response was lost.
        var payload=new JsonObject
        {
            ["auditEventId"]=request.Row.AuditEventId,
            ["expectedOverlayVersion"]=request.Row.OverlayVersion,
            ["reason"]=request.Reason,["operationId"]=request.OperationId
        };
        if (!hide) payload["correctedDescription"]=request.Description;
        var response=hide
            ? await new AuditOverlayService(database).DeleteAuditPresentationAsync(payload,principal,ct).ConfigureAwait(false)
            : await new AuditOverlayService(database).UpdateAuditPresentationAsync(payload,principal,ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!response.Success && response.Code==BridgeErrorCodes.ConflictStale &&
            response.ExpectedVersion==request.Row.OverlayVersion &&
            response.ActualVersion is { } actual && actual!=request.Row.OverlayVersion &&
            response.ConflictInfo is JsonObject conflict &&
            conflict["reason"]?.GetValue<string>()=="OverlayVersionStale" &&
            conflict["auditEventId"]?.GetValue<string>()==request.Row.AuditEventId)
            throw new NativeAuditEditStaleException();
        var data=RequiredObject(response,hide?"DeleteAuditPresentation":"UpdateAuditPresentation");
        NativeAuditEditState ReadState(string key)
        {
            var state=RequireObject(data[key]);
            return new(RequiredNumber(state,"overlayVersion"),RequiredBoolean(state,"isTombstoned"),
                RequiredText(state,"displayDescription"));
        }
        var receipt=new NativeAuditEditReceipt(RequiredText(data,"auditEventId"),
            RequiredText(data,"operationAuditEventId"),ReadState("beforeState"),ReadState("afterState"),
            RequiredBoolean(data,"rawEvidenceUntouched"));
        if (response.AuditEventId!=receipt.OperationAuditEventId ||
            RequiredNumber(data,"overlayVersion")!=receipt.After.Version ||
            RequiredBoolean(data,"isTombstoned")!=receipt.After.IsTombstoned ||
            !receipt.Matches(request))
            throw new InvalidDataException("Canonical audit edit receipt mismatch.");
        return receipt;
    }

    // Canonical read-only presentation history, including all versions.
    public async Task<NativeAuditHistoryPage> ReadAuditHistoryAsync(NativeIdentity actor,
        NativeAuditHistoryRequest request,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(!RoleAuthorizationPolicy.CanReviewAuditOverlayHistory(actor.Role))
            throw new UnauthorizedAccessException("Audit history denied.");
        if(request is null || !request.IsValid)
            throw new InvalidDataException("Invalid audit history request.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce ||
            !request.IsAuthorized(principal.Role))
            throw new UnauthorizedAccessException("Audit history session changed.");
        var response=await new AuditOverlayService(database).QueryAuditPresentationChangesAsync(new JsonObject
        {
            ["auditEventId"]=request.AuditEventId,
            ["page"]=request.Page,
            ["pageSize"]=NativeAuditHistoryRequest.PageSize
        },ct,principal).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var data=RequiredObject(response,"QueryAuditPresentationChanges");
        var items=RequiredArray(data,"items");
        if(RequiredNumber(data,"page")!=request.Page || RequiredNumber(data,"count")!=items.Count ||
            items.Count>NativeAuditHistoryRequest.PageSize)
            throw new InvalidDataException("Canonical audit history page mismatch.");
        var rows=items.Select(node=>
        {
            var item=RequireObject(node);
            NativeAuditEditState ReadState(string key)
            {
                var state=RequireObject(item[key]);
                return new(RequiredNumber(state,"overlayVersion"),RequiredBoolean(state,"isTombstoned"),
                    RequiredText(state,"displayDescription"));
            }
            var after=ReadState("afterState");
            if(RequiredNumber(item,"overlayVersion")!=after.Version ||
                RequiredBoolean(item,"isTombstoned")!=after.IsTombstoned ||
                !item.ContainsKey("correctedDescription"))
                throw new InvalidDataException("Canonical audit history state mismatch.");
            return new NativeAuditHistoryRow(RequiredText(item,"auditEventId"),
                RequiredText(item,"operationCode"),RequiredText(item,"rawDescription"),
                RequiredText(item,"reason"),RequiredText(item,"editedByUserId"),
                RequiredText(item,"editedByDisplay"),RequiredText(item,"editedAtUtc"),
                item["correctedDescription"]?.GetValue<string>(),ReadState("beforeState"),after);
        }).ToArray();
        var page=new NativeAuditHistoryPage(request,Array.AsReadOnly(rows));
        if(!page.Matches(request))
            throw new InvalidDataException("Invalid canonical audit history rows.");
        return page;
    }

    // Native accounts are a read-only projection of the canonical management service.
    public async Task<NativeAccountPage> ReadAccountsAsync(NativeIdentity actor,
        NativeAccountRequest request,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(!RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))
            throw new UnauthorizedAccessException("Account management denied.");
        if(request is null || !request.IsValid)
            throw new InvalidDataException("Invalid account list request.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce ||
            !request.IsAuthorized(principal.Role))
            throw new UnauthorizedAccessException("Account list session changed.");
        var response=await new UserAccountManagementService(database).ListAsync(new JsonObject
        {
            ["term"]=request.Term,["page"]=request.Page,["pageSize"]=NativeAccountRequest.PageSize
        },principal,ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var data=RequiredObject(response,"ListUserAccounts");
        var items=RequiredArray(data,"items");
        if(RequiredNumber(data,"page")!=request.Page ||
            RequiredNumber(data,"pageSize")!=NativeAccountRequest.PageSize ||
            items.Count>NativeAccountRequest.PageSize)
            throw new InvalidDataException("Canonical account list page mismatch.");
        var rows=items.Select(node=>
        {
            var item=RequireObject(node);
            string roleText=RequiredText(item,"role");
            if(!Enum.TryParse<AlTamam.Domain.Enums.UserRole>(roleText,false,out var role) ||
                role.ToString()!=roleText || !item.ContainsKey("personId"))
                throw new InvalidDataException("Invalid canonical account role or identity.");
            return new NativeAccountRow(RequiredNumber(item,"index"),RequiredText(item,"userId"),
                RequiredText(item,"username"),role,RequiredBoolean(item,"active"),RequiredNumber(item,"rowVersion"),
                RequiredText(item,"fullName"),RequiredText(item,"serviceNumberA"),
                item["personId"]?.GetValue<string>(),RequiredText(item,"createdBy"),
                RequiredText(item,"createdAtUtc"),RequiredBoolean(item,"canModify"));
        }).ToArray();
        var page=new NativeAccountPage(request,RequiredNumber(data,"total"),RequiredBoolean(data,"hasNext"),
            Array.AsReadOnly(rows));
        if(!page.Matches(request,actor))
            throw new InvalidDataException("Canonical account list scope or permission mismatch.");
        return page;
    }

    public async Task<NativeAccountPersonSearch> SearchAccountPeopleAsync(NativeIdentity actor,
        string term,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(!RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))
            throw new UnauthorizedAccessException("Account roster search denied.");
        if(!NativeAccountPersonSearch.ValidTerm(term))
            throw new InvalidDataException("Invalid account roster search.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var response=await services.SearchPersonsAsync(term,50,ct,activeOnly:true).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var data=RequiredObject(response,"SearchActivePersonsForAccount");
        var items=RequiredArray(data,"items");
        if(items.Count>50 || RequiredNumber(data,"count")!=items.Count)
            throw new InvalidDataException("Invalid account roster search count.");
        var people=items.Select(node=>
        {
            var item=RequireObject(node);
            return new NativeAccountPerson(RequiredText(item,"personId"),RequiredText(item,"displayName"),
                RequiredText(item,"serviceNumberA"),RequiredText(item,"siteName"),
                checked((int)RequiredNumber(item,"organizationalStatus")),RequiredNumber(item,"revision"));
        }).ToArray();
        var result=new NativeAccountPersonSearch(term,Array.AsReadOnly(people));
        if(!result.Matches(term)) throw new InvalidDataException("Invalid active person evidence.");
        return result;
    }

    public async Task<NativeAccountCreateReceipt> CreateAccountAsync(NativeIdentity actor,
        NativeAccountCreateIntent intent,string password,string confirmation,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(intent is null || !intent.IsValid(actor.Role) ||
            !NativeAccountCreateIntent.ValidPassword(password,confirmation))
            throw new UnauthorizedAccessException("Invalid account creation intent.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce || !intent.IsValid(principal.Role))
            throw new UnauthorizedAccessException("Account creation session changed.");
        // The canonical service validates active roster linkage, uniqueness and role
        // atomically. Keep operationId stable on retry and never log the payload.
        var payload=new JsonObject
        {
            ["personId"]=intent.Person.PersonId,["username"]=intent.Username,["role"]=intent.Role.ToString(),
            ["password"]=password,["passwordConfirmation"]=confirmation,["operationId"]=intent.OperationId
        };
        BridgeResponse response;
        try
        {
            response=await new UserAccountManagementService(database).CreateAsync(payload,principal,ct).ConfigureAwait(false);
        }
        finally
        {
            payload.Remove("password"); payload.Remove("passwordConfirmation");
        }
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        // Do not classify operation-ID payload mismatch or transport errors as rejection.
        if(!response.Success && response.Code is BridgeErrorCodes.DuplicateUsername or BridgeErrorCodes.ValidationError)
            throw new NativeAccountCreateRejectedException("Canonical account creation rejected.");
        var data=RequiredObject(response,"CreateUserAccount");
        if(RequiredText(data,"role")!=intent.Role.ToString())
            throw new InvalidDataException("Account creation receipt role mismatch.");
        var receipt=new NativeAccountCreateReceipt(RequiredText(data,"userId"),RequiredText(data,"username"),
            intent.Role,RequiredText(data,"linkedPersonId"));
        if(receipt.UserId==actor.UserId || !receipt.Matches(intent))
            throw new InvalidDataException("Canonical account creation receipt mismatch.");
        return receipt;
    }

    public async Task<NativeAccountLifecycleReceipt> ApplyAccountActionAsync(NativeIdentity actor,
        NativeAccountLifecycleIntent intent,string password,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(intent is null || !intent.IsValid(actor) ||
            (intent.Action==NativeAccountAction.ChangePassword
                ? !PasswordPolicyValidator.Validate(password).IsValid : password!=""))
            throw new UnauthorizedAccessException("Invalid account lifecycle intent.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(principal.Role))
            throw new UnauthorizedAccessException("Account lifecycle session changed.");
        var payload=new JsonObject
        {
            ["userId"]=intent.Row.UserId,["expectedRevision"]=intent.Row.Revision,
            ["operationId"]=intent.OperationId
        };
        var service=new UserAccountManagementService(database);
        BridgeResponse response;
        try
        {
            switch(intent.Action)
            {
                case NativeAccountAction.ChangePassword:
                    payload["password"]=password;
                    response=await service.ChangePasswordAsync(payload,principal,ct).ConfigureAwait(false);
                    break;
                case NativeAccountAction.Deactivate:
                    response=await service.DeactivateAsync(payload,principal,ct).ConfigureAwait(false);
                    break;
                case NativeAccountAction.Reactivate:
                    payload["reason"]=intent.Reason;
                    response=await service.ReactivateAsync(payload,principal,ct).ConfigureAwait(false);
                    break;
                default: throw new InvalidDataException("Unknown account action.");
            }
        }
        finally { payload.Remove("password"); }
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        // The caller distinguishes first rejection from rejection following
        // an ambiguous attempt. A pre-replay eligibility failure is not proof
        // that an earlier reactivation did not commit.
        if(!response.Success && response.Code is BridgeErrorCodes.ConflictStale or
            BridgeErrorCodes.ValidationError or BridgeErrorCodes.Unauthorized)
            throw new NativeAccountLifecycleRejectedException();
        var data=RequiredObject(response,"AccountLifecycle");
        var receipt=new NativeAccountLifecycleReceipt(RequiredText(data,"userId"),
            RequiredBoolean(data,"active"),RequiredNumber(data,"rowVersion"));
        if(!receipt.Matches(intent))
            throw new InvalidDataException("Canonical account lifecycle receipt mismatch.");
        return receipt;
    }

    public async Task<NativeProgramInformation> ReadProgramInformationAsync(NativeIdentity actor,
        CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        await ResolveBackupPrincipalAsync(actor,ct).ConfigureAwait(false);
        var info=new NativeProgramInformation(
            typeof(NativeSqlCipherDashboardReader).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0]??"unknown",
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.Version.ToString(),true,
            AlTamam.Infrastructure.Persistence.AlTamamDbContext.CurrentSchemaVersion);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if(!info.IsValid) throw new InvalidDataException("Invalid program metadata.");
        return info;
    }

    public async Task<NativeIntegrityStatus> CheckDatabaseIntegrityAsync(NativeIdentity actor,
        CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        await ResolveBackupPrincipalAsync(actor,ct).ConfigureAwait(false);
        // The synchronous canonical SQLCipher check is kept off the STA.
        var response=await Task.Run(()=>new SettingsReadBackupService(database).Integrity(),ct)
            .ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if(!response.Success)
        {
            if(response.Code==BridgeErrorCodes.BackupIntegrityFailed) return NativeIntegrityStatus.Failed;
            throw new InvalidDataException("Integrity result unavailable.");
        }
        var data=RequiredObject(response,"RunDatabaseIntegrityCheck");
        if(RequiredText(data,"integrity")!="ok" || !RequiredBoolean(data,"encrypted"))
            throw new InvalidDataException("Invalid canonical integrity receipt.");
        return NativeIntegrityStatus.Passed;
    }

    public async Task<NativeSettingsOverview> ReadSettingsOverviewAsync(NativeIdentity actor,
        CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        return await ReadSettingsOverviewCoreAsync(actor,ct).ConfigureAwait(false);
    }

    public async Task<NativeSettingsOverview> SaveThemeAsync(NativeIdentity actor,string theme,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(theme is not ("light" or "dark")) throw new ArgumentException("Invalid theme.",nameof(theme));
        var principal=await ResolveBackupPrincipalAsync(actor,ct).ConfigureAwait(false);
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation);
        lifetime.Token.ThrowIfCancellationRequested();
        var response=await new SettingsReadBackupService(database)
            .SetThemeAsync(new JsonObject{["theme"]=theme},lifetime.Token,principal).ConfigureAwait(false);
        var receipt=RequiredObject(response,"SetTheme");
        if(RequiredText(receipt,"theme")!=theme) throw new InvalidDataException("Invalid theme receipt.");
        var saved=await ReadSettingsOverviewCoreAsync(actor,lifetime.Token).ConfigureAwait(false);
        if(saved.Theme!=theme) throw new InvalidDataException("Stored theme differs from receipt.");
        return saved;
    }

    private async Task<NativeSettingsOverview> ReadSettingsOverviewCoreAsync(NativeIdentity actor,CancellationToken ct)
    {
        if(!RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role))
            throw new UnauthorizedAccessException("Settings overview denied.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce)
            throw new UnauthorizedAccessException("Settings session changed.");
        var response=await new SettingsReadBackupService(database).OverviewAsync(principal,ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var data=RequiredObject(response,"GetSettingsOverview");
        if(RequiredText(data,"role")!=actor.Role.ToString() || !data.ContainsKey("lastAutoBackupDate"))
            throw new InvalidDataException("Invalid settings identity or backup evidence.");
        var overview=new NativeSettingsOverview(RequiredText(data,"username"),actor.Role,
            RequiredText(data,"backupDirectory"),RequiredBoolean(data,"defaultDriveAvailable"),
            RequiredText(data,"backupTime"),RequiredBoolean(data,"automaticBackupEnabled"),
            checked((int)RequiredNumber(data,"automaticBackupRetentionCount")),
            data["lastAutoBackupDate"]?.GetValue<string>(),RequiredText(data,"lastAutoBackupFailure"),
            RequiredText(data,"theme"));
        if(!overview.Matches(actor))
            throw new InvalidDataException("Invalid canonical settings overview.");
        return overview;
    }

    private async Task<BridgePrincipal> ResolveBackupPrincipalAsync(NativeIdentity actor,CancellationToken ct)
    {
        if(!RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role))
            throw new UnauthorizedAccessException("Backup denied.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce)
            throw new UnauthorizedAccessException("Backup session changed.");
        return principal;
    }

    public async Task<NativeManualBackupReceipt> CreateManualBackupAsync(NativeIdentity actor,string directory,
        CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(!NativeManualBackupReceipt.ValidDirectory(directory)) throw new NativeBackupRejectedException();
        var principal=await ResolveBackupPrincipalAsync(actor,ct).ConfigureAwait(false);
        var response=await new SettingsReadBackupService(database).BackupAsync(principal,ct,directory).ConfigureAwait(false);
        if(!response.Success && response.Code is BridgeErrorCodes.ConflictStale or BridgeErrorCodes.ValidationError)
            throw new NativeBackupRejectedException();
        var data=RequiredObject(response,"CreateVerifiedBackup");
        string path=RequiredText(data,"path"),hash=RequiredText(data,"sha256"),id=RequiredText(data,"backupId");
        if(!RequiredBoolean(data,"verificationPassed") ||
            !new NativeManualBackupReceipt(id,path,hash,1,true).Matches(directory))
            throw new InvalidDataException("Invalid backup creation receipt.");
        var verification=await new BackupInspectionService(database).VerifyAsync(path,ct).ConfigureAwait(false);
        var verified=RequiredObject(verification,"VerifyBackup");
        var result=new NativeManualBackupReceipt(id,path,hash,RequiredNumber(verified,"fileSizeBytes"),
            RequiredBoolean(verified,"verified") && RequiredBoolean(verified,"schemaCompatible") &&
            RequiredBoolean(verified,"registeredInCurrentCatalog") &&
            string.Equals(RequiredText(verified,"path"),path,StringComparison.OrdinalIgnoreCase) &&
            string.Equals(RequiredText(verified,"sha256"),hash,StringComparison.OrdinalIgnoreCase));
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if(!result.Matches(directory)) throw new InvalidDataException("Unverified manual backup.");
        return result;
    }

    public async Task<string> SaveBackupDirectoryAsync(NativeIdentity actor,string directory,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(!NativeManualBackupReceipt.ValidDirectory(directory)) throw new NativeBackupRejectedException();
        var principal=await ResolveBackupPrincipalAsync(actor,ct).ConfigureAwait(false);
        var settings=new OutputDirectorySettings(database);
        var response=await settings.SaveAsync(directory,principal,ct).ConfigureAwait(false);
        if(!response.Success && response.Code==BridgeErrorCodes.ValidationError)
            throw new NativeBackupRejectedException();
        string saved=RequiredText(RequiredObject(response,"SetOutputDirectory"),"directory");
        var persisted=await settings.GetAsync(ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if(!NativeManualBackupReceipt.SameDirectory(saved,directory) ||
            !NativeManualBackupReceipt.SameDirectory(persisted,saved))
            throw new InvalidDataException("Unverified output directory.");
        return saved;
    }

    public async Task<NativeBackupScheduleReceipt> SaveBackupScheduleAsync(NativeIdentity actor,
        NativeBackupScheduleIntent intent,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(intent is null || !intent.IsValid(actor))
            throw new UnauthorizedAccessException("Invalid backup schedule intent.");
        var principal=await ResolveBackupPrincipalAsync(actor,ct).ConfigureAwait(false);
        if(!RoleAuthorizationPolicy.CanAccessUsersManagement(principal.Role))
            throw new UnauthorizedAccessException("Backup schedule administration required.");
        var directory=await new OutputDirectorySettings(database).GetAsync(ct).ConfigureAwait(false);
        if(!NativeManualBackupReceipt.SameDirectory(directory,intent.Before.BackupDirectory))
            throw new NativeBackupScheduleRejectedException();
        var payload=new JsonObject
        {
            ["enabled"]=intent.Enabled,["backupTime"]=intent.Time,
            ["retentionCount"]=intent.RetentionCount,["operationId"]=intent.OperationId
        };
        var response=await new SettingsReadBackupService(database)
            .ConfigureBackupScheduleAsync(payload,principal,ct).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if(!response.Success && response.Code is BridgeErrorCodes.ScheduleUnavailable or
            BridgeErrorCodes.ValidationError or BridgeErrorCodes.Unauthorized)
            throw new NativeBackupScheduleRejectedException();
        var data=RequiredObject(response,"UpdateBackupSchedule");
        var receipt=new NativeBackupScheduleReceipt(RequiredBoolean(data,"enabled"),
            RequiredText(data,"backupTime"),checked((int)RequiredNumber(data,"retentionCount")));
        if(!receipt.Matches(intent))
            throw new InvalidDataException("Canonical backup schedule receipt mismatch.");
        return receipt;
    }

    private static NativeBackupCounts ReadBackupCounts(JsonObject data)=>new(
        RequiredNumber(data,"people"),RequiredNumber(data,"attendance"),
        RequiredNumber(data,"auditEvents"),RequiredNumber(data,"users"));

    public async Task<NativeBackupInspection> InspectBackupAsync(NativeIdentity actor,string path,
        CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(!NativeBackupInspection.ValidPath(path))
            throw new InvalidDataException("Invalid backup path.");
        await ResolveBackupPrincipalAsync(actor,ct).ConfigureAwait(false);
        var data=RequiredObject(await new BackupInspectionService(database)
            .VerifyNativeAsync(path,ct).ConfigureAwait(false),"VerifyBackup");
        var result=new NativeBackupInspection(RequiredText(data,"path"),RequiredText(data,"sha256"),
            RequiredNumber(data,"fileSizeBytes"),checked((int)RequiredNumber(data,"schemaVersion")),
            RequiredBoolean(data,"schemaCompatible"),RequiredBoolean(data,"registeredInCurrentCatalog"),
            ReadBackupCounts(data))
        { CurrentSchemaVersion=AlTamam.Infrastructure.Persistence.AlTamamDbContext.CurrentSchemaVersion };
        if(!RequiredBoolean(data,"verified") || RequiredBoolean(data,"restoreEnabled") ||
            !result.Matches(path) || RequiredText(data,"fileName")!=Path.GetFileName(result.Path) ||
            result.Compatible!=(result.SchemaVersion==AlTamam.Infrastructure.Persistence.AlTamamDbContext.CurrentSchemaVersion))
            throw new InvalidDataException("Invalid backup inspection evidence.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<NativeRestoreComparison> PreviewBackupRestoreAsync(NativeIdentity actor,
        NativeBackupInspection evidence,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(evidence is null || !evidence.Matches(evidence.Path) || !evidence.Compatible ||
            !RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role))
            throw new UnauthorizedAccessException("Backup restore preview denied.");
        var principal=await ResolveBackupPrincipalAsync(actor,ct).ConfigureAwait(false);
        if(!RoleAuthorizationPolicy.CanAccessUsersManagement(principal.Role))
            throw new UnauthorizedAccessException("Backup restore preview denied.");
        var data=RequiredObject(await new BackupInspectionService(database)
            .PreviewRestoreAsync(evidence.Path,evidence.Sha256,ct).ConfigureAwait(false),"PreviewRestore");
        var result=new NativeRestoreComparison(RequiredText(data,"path"),RequiredText(data,"sha256"),
            checked((int)RequiredNumber(data,"schemaVersion")),ReadBackupCounts(RequireObject(data["incoming"])),
            ReadBackupCounts(RequireObject(data["current"])));
        if(!RequiredBoolean(data,"verified") || !RequiredBoolean(data,"previewOnly") ||
            RequiredBoolean(data,"restoreEnabled") || !result.Matches(evidence) ||
            RequiredText(data,"fileName")!=Path.GetFileName(result.Path))
            throw new InvalidDataException("Invalid read-only restore comparison.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return result;
    }

    private readonly NativeSqlCipherAttendanceReviewBackend _review =
        new(services, database);




    private readonly NativeIndividualExcelApprovalGate _individualExcelApproval = new();

    public void InvalidateIndividualExcelApproval() => _individualExcelApproval.Invalidate();

    private static JsonObject IndividualExcelPayload(NativeIndividualExcelRequest request) => new()
    {
        ["personId"] = request.PersonId, ["dateFrom"] = request.DateFrom,
        ["dateTo"] = request.DateTo, ["movementType"] = request.MovementType,
        ["siteId"] = request.SiteId, ["statusId"] = request.StatusId
    };

    private static void VerifyIndividualExcelScope(JsonObject result,
        NativeIndividualExcelRequest request)
    {
        if (RequiredText(result, "personId") != request.PersonId ||
            RequiredText(result, "dateFrom") != request.DateFrom ||
            RequiredText(result, "dateTo") != request.DateTo ||
            RequiredText(result, "siteId") != request.SiteId ||
            RequiredText(result, "statusId") != request.StatusId ||
            RequiredNumber(result, "movementType") != request.MovementType ||
            RequiredNumber(result, "sheetCount") != 9 ||
            RequiredText(result, "format") != "xlsx")
            throw new InvalidDataException("Individual Excel scope changed.");
    }

    public async Task<NativeIndividualExcelPreview> PreviewIndividualExcelAsync(
        NativeIdentity actor, NativeIndividualExcelRequest request,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        var generation = _individualExcelApproval.Begin();
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var service = new PersonnelIndividualExcelExportService(database);
        var result = RequiredObject(await service.PreviewAsync(IndividualExcelPayload(request),
            token => VerifyPersonnelManagerAsync(actor, token), ct).ConfigureAwait(false),
            "PreviewIndividualExcel");
        VerifyIndividualExcelScope(result, request);
        var name = RequiredText(result, "name");
        var number = RequiredText(result, "serviceNumberA");
        var folder = RequiredText(result, "directory");
        var attendance = RequiredNumber(result, "attendanceTotal");
        var identities = RequiredNumber(result, "identityTotal");
        var movements = RequiredNumber(result, "movementTotal");
        var revisions = RequiredNumber(result, "revisionTotal");
        var audits = RequiredNumber(result, "auditTotal");
        var sites = RequiredNumber(result, "siteTotal");
        var fingerprint = RequiredText(result, "fingerprint");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        string approval = _individualExcelApproval.Publish(generation, actor, request, fingerprint);
        return new NativeIndividualExcelPreview(request, name, number, folder,
            attendance, identities, movements, revisions, audits, sites, fingerprint, approval);
    }

    public async Task<NativeIndividualExcelReceipt> ExportIndividualExcelAsync(
        NativeIdentity actor, NativeIndividualExcelRequest request, string approvalToken,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        // Consume before awaiting: even a failed or cancelled attempt needs a fresh preview.
        string fingerprint = _individualExcelApproval.Consume(actor, request, approvalToken);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var service = new PersonnelIndividualExcelExportService(database);
        var response = await service.ExportAuditedAsync(IndividualExcelPayload(request), fingerprint,
            async token =>
            {
                await VerifyPersonnelManagerAsync(actor, token).ConfigureAwait(false);
                var principal = await services.ResolveSessionAsync(actor.SessionNonce, token)
                    .ConfigureAwait(false);
                if (principal is null || principal.UserId != actor.UserId ||
                    principal.Username != actor.Username || principal.Role != actor.Role ||
                    principal.SessionNonce != actor.SessionNonce)
                    throw new UnauthorizedAccessException("Individual Excel session changed.");
                return principal;
            }, ct).ConfigureAwait(false);
        var result = RequiredObject(response, "ExportIndividualExcel");
        return new NativeIndividualExcelReceipt(request, RequiredText(result, "filePath"),
            RequiredText(result, "fingerprint"), checked((int)RequiredNumber(result, "sheetCount")));
    }

    // Native Phase 9 group Excel: preview and explicit confirm are backed by
    // the original PersonId historical source and approved monthly-export
    // output-directory policy; the UI never supplies filesystem paths.
    private static JsonObject GroupExcelPayload(NativePersonnelGroupReportRequest request)
    {
        if (request is null || request.PersonIds is not { Count: >= 1 and <= 100 } ||
            request.PersonIds.Any(id => id is not { Length: >= 3 and <= 128 } ||
                id.Any(char.IsControl)) ||
            request.PersonIds.Distinct(StringComparer.Ordinal).Count() != request.PersonIds.Count ||
            request.MovementType is < 0 or > 4 || request.SummaryPage != 1 ||
            request.DateFrom is null || request.DateTo is null ||
            request.SiteId is null || request.StatusId is null ||
            request.SiteId.Length > 128 || request.StatusId.Length > 128 ||
            request.SiteId.Any(char.IsControl) || request.StatusId.Any(char.IsControl))
            throw new InvalidDataException("Invalid native group Excel request.");
        return new JsonObject
        {
            ["personIds"] = new JsonArray(request.PersonIds
                .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
            ["dateFrom"] = request.DateFrom,
            ["dateTo"] = request.DateTo,
            ["siteId"] = request.SiteId,
            ["statusId"] = request.StatusId,
            ["movementType"] = request.MovementType
        };
    }

    public async Task<NativeGroupExcelPreview> PreviewGroupExcelAsync(
        NativeIdentity actor, NativePersonnelGroupReportRequest request,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        JsonObject payload = GroupExcelPayload(request);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new PersonnelGroupExcelExportService(database)
            .PreviewAsync(payload, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject source = RequiredObject(response, "PreviewPersonnelGroupExcel");
        JsonArray ids = RequiredArray(source, "personIds");
        if (ids.Count != request.PersonIds.Count ||
            !ids.Select(node => node?.GetValue<string>() ?? "")
                .SequenceEqual(request.PersonIds, StringComparer.Ordinal) ||
            RequiredText(source, "dateFrom") != request.DateFrom ||
            RequiredText(source, "dateTo") != request.DateTo ||
            RequiredText(source, "siteId") != request.SiteId ||
            RequiredText(source, "statusId") != request.StatusId ||
            RequiredNumber(source, "movementType") != request.MovementType ||
            RequiredText(source, "format") != "xlsx" ||
            RequiredNumber(source, "personCount") != request.PersonIds.Count)
            throw new InvalidDataException("Canonical group Excel preview identity/filter mismatch.");
        string hash = RequiredText(source, "fingerprint");
        if (hash.Length != 64 || hash.Any(ch => !char.IsAsciiHexDigit(ch)))
            throw new InvalidDataException("Invalid group Excel source evidence fingerprint.");
        return new NativeGroupExcelPreview(
            request.PersonIds.ToArray(), request.DateFrom, request.DateTo,
            request.SiteId, request.StatusId, request.MovementType,
            checked((int)RequiredNumber(source, "personCount")),
            RequiredNumber(source, "attendanceTotal"),
            RequiredNumber(source, "movementTotal"),
            hash, RequiredText(source, "suggestedFileName"));
    }

    public async Task<NativeGroupExcelReceipt> ExportGroupExcelAsync(
        NativeIdentity actor, NativePersonnelGroupReportRequest request,
        string fingerprint, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        JsonObject payload = GroupExcelPayload(request);
        if (fingerprint is not { Length: 64 } ||
            fingerprint.Any(ch => !char.IsAsciiHexDigit(ch)))
            throw new InvalidDataException("Invalid group Excel approval fingerprint.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new PersonnelGroupExcelExportService(database)
            .ExportAsync(payload, fingerprint,
                token => VerifyPersonnelManagerAsync(actor, token), ct)
            .ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject source = RequiredObject(response, "ExportPersonnelGroupExcel");
        if (RequiredText(source, "format") != "xlsx" ||
            RequiredText(source, "fingerprint") != fingerprint.ToLowerInvariant() ||
            RequiredNumber(source, "personCount") != request.PersonIds.Count ||
            RequiredNumber(source, "sheetCount") != 4 ||
            RequiredNumber(source, "dailyRows") is < 0 or > 20000)
            throw new InvalidDataException("Canonical group Excel export receipt mismatch.");
        return new NativeGroupExcelReceipt(
            RequiredText(source, "filePath"),
            checked((int)RequiredNumber(source, "personCount")),
            RequiredNumber(source, "attendanceTotal"),
            RequiredNumber(source, "movementTotal"),
            RequiredText(source, "fingerprint"),
            checked((int)RequiredNumber(source, "sheetCount")),
            RequiredNumber(source, "dailyRows"));
    }

    // Phase 9 group slice: same canonical PersonnelReportService.ReportAsync,
    // not another SQL query engine, report writer or invented day counts.
    // Verify a real live manager session on both sides of the database read.
    private static IReadOnlyList<NativeReportFilterOption> ParseChoices(
        JsonArray source, string scope)
    {
        if (source.Count > 500)
            throw new InvalidDataException("Unbounded canonical " + scope + " history selectors.");
        var choices = source.Select(node =>
        {
            JsonObject item = RequireObject(node);
            string id = RequiredText(item, "id"), title = RequiredText(item, "title");
            if (id.Length is < 1 or > 128 || id.Any(char.IsControl) ||
                title.Length is < 1 or > 240 || title.Any(char.IsControl))
                throw new InvalidDataException("Invalid canonical " + scope + " history selector.");
            return new NativeReportFilterOption(id, title);
        }).ToArray();
        if (choices.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != choices.Length)
            throw new InvalidDataException("Duplicate canonical " + scope + " ID.");
        return choices;
    }

    public async Task<NativePersonnelGroupReportSnapshot> ReadGroupAsync(
        NativeIdentity actor, NativePersonnelGroupReportRequest request,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        static bool IsIsoDate(string date) => date.Length == 0 ||
            DateOnly.TryParseExact(date, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        if (request is null || request.PersonIds is not { Count: >= 1 and <= 100 } ||
            request.PersonIds.Any(id => id is not { Length: >= 3 and <= 128 } ||
                id.Any(char.IsControl)) ||
            request.PersonIds.Distinct(StringComparer.Ordinal).Count() !=
                request.PersonIds.Count ||
            request.SummaryPage is < 1 or > 100000 ||
            request.MovementType is < 0 or > 4 ||
            request.DateFrom is null || request.DateTo is null ||
            request.SiteId is null || request.StatusId is null ||
            !IsIsoDate(request.DateFrom) || !IsIsoDate(request.DateTo) ||
            (request.DateFrom.Length > 0 && request.DateTo.Length > 0 &&
                string.CompareOrdinal(request.DateFrom, request.DateTo) > 0) ||
            request.SiteId.Length > 128 || request.StatusId.Length > 128 ||
            request.SiteId.Any(char.IsControl) || request.StatusId.Any(char.IsControl))
            throw new InvalidDataException("Invalid original PersonId group report request.");

        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse original = await new PersonnelReportService(database)
            .ReportAsync(new JsonObject
            {
                ["personIds"] = new JsonArray(request.PersonIds
                    .Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
                ["dateFrom"] = request.DateFrom,
                ["dateTo"] = request.DateTo,
                ["siteId"] = request.SiteId,
                ["statusId"] = request.StatusId,
                ["movementType"] = request.MovementType,
                ["summaryPage"] = request.SummaryPage
            }, ct, includeGroupFilterChoices: true).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);

        JsonObject data = RequiredObject(original, "PersonnelReport(native-group)");
        if (RequiredText(data, "mode") != "group" ||
            RequiredNumber(data, "personCount") != request.PersonIds.Count ||
            RequiredText(data, "dateFrom") != request.DateFrom ||
            RequiredText(data, "dateTo") != request.DateTo ||
            RequiredText(data, "siteId") != request.SiteId ||
            RequiredText(data, "statusId") != request.StatusId ||
            RequiredNumber(data, "movementType") != request.MovementType ||
            RequiredNumber(data, "dailyStatusPage") != request.SummaryPage)
            throw new InvalidDataException("Canonical group PersonIds, filter or page echo mismatch.");

        JsonArray sourcePersons = RequiredArray(data, "persons");
        JsonArray sourceTotals = RequiredArray(data, "statusTotals");
        JsonArray sourceDaily = RequiredArray(data, "dailyStatusTotals");
        if (sourcePersons.Count != request.PersonIds.Count ||
            sourceTotals.Count > 5000 || sourceDaily.Count > 100)
            throw new InvalidDataException("Canonical group source was truncated or unbounded.");

        var customSource = RequiredArray(data, "customColumns");
        if (customSource.Count > 500)
            throw new InvalidDataException("Canonical group custom column limit exceeded.");
        var customColumns = customSource.Select(node =>
        {
            JsonObject column = RequireObject(node);
            return new NativeDashboardColumn(
                RequiredText(column, "columnId"), RequiredText(column, "caption"));
        }).ToArray();
        if (customColumns.Any(column =>
                column.ColumnId is not { Length: >= 1 and <= 128 } ||
                column.ColumnId.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(column.Caption) ||
                column.Caption.Length > 240 || column.Caption.Any(char.IsControl)) ||
            customColumns.Select(column => column.ColumnId)
                .Distinct(StringComparer.Ordinal).Count() != customColumns.Length)
            throw new InvalidDataException("Invalid canonical group custom columns.");
        var customIds = customColumns.Select(column => column.ColumnId)
            .ToHashSet(StringComparer.Ordinal);

        var persons = sourcePersons.Select((node, index) =>
        {
            JsonObject item = RequireObject(node);
            string id = RequiredText(item, "personId");
            if (id != request.PersonIds[index])
                throw new InvalidDataException("Canonical group returned wrong roster or order.");
            var statusTotals = RequiredArray(item, "statusTotals");
            if (statusTotals.Count > 5000)
                throw new InvalidDataException("Canonical person historical statuses exceeded limit.");
            var custom = RequireObject(item["customFields"]);
            if (custom.Any(field => !customIds.Contains(field.Key)))
                throw new InvalidDataException("Unrecognized canonical group custom field.");
            return new NativePersonnelGroupPerson(
                id, RequiredText(item, "name"),
                RequiredText(item, "serviceNumberA"),
                RequiredText(item, "firstRecordedDate"),
                checked((int)RequiredNumber(item, "organizationalStatus")),
                RequiredBoolean(item, "active"),
                RequiredNumber(item, "attendanceTotal"),
                RequiredNumber(item, "movementTotal"),
                statusTotals.Select(status =>
                {
                    JsonObject s = RequireObject(status);
                    return new NativePersonnelReportStatusTotal(
                        RequiredText(s, "statusId"),
                        RequiredText(s, "statusName"),
                        RequiredNumber(s, "count"));
                }).ToArray())
            {
                CustomFields = customColumns.Select(column => new NativeGroupCustomField(
                    column.ColumnId, column.Caption,
                    custom[column.ColumnId]?.GetValue<string>() ?? "")).ToArray()
            };
        }).ToArray();

        var totals = sourceTotals.Select(node =>
        {
            JsonObject s = RequireObject(node);
            return new NativePersonnelReportStatusTotal(
                RequiredText(s, "statusId"),
                RequiredText(s, "statusName"),
                RequiredNumber(s, "count"));
        }).ToArray();
        var daily = sourceDaily.Select(node =>
        {
            JsonObject s = RequireObject(node);
            return new NativePersonnelGroupDailyStatus(
                RequiredText(s, "date"),
                RequiredText(s, "statusId"),
                RequiredText(s, "statusName"),
                RequiredNumber(s, "count"),
                RequiredNumber(s, "personCount"));
        }).ToArray();

        var report = new NativePersonnelGroupReportSnapshot(
            persons.Select(x => x.PersonId).ToArray(),
            checked((int)RequiredNumber(data, "personCount")),
            RequiredNumber(data, "attendanceTotal"),
            RequiredNumber(data, "movementTotal"),
            RequiredText(data, "dateFrom"),
            RequiredText(data, "dateTo"),
            RequiredText(data, "siteId"),
            RequiredText(data, "statusId"),
            checked((int)RequiredNumber(data, "movementType")),
            checked((int)RequiredNumber(data, "dailyStatusPage")),
            RequiredBoolean(data, "dailyStatusHasNext"),
            persons, totals, daily)
        {
            CustomColumns = customColumns,
            AvailableSites = ParseChoices(RequiredArray(data, "filterSites"), "group site"),
            AvailableStatuses = ParseChoices(RequiredArray(data, "filterStatuses"), "group status")
        };

        if (report.AttendanceTotal < 0 || report.MovementTotal < 0 ||
            report.Persons.Any(x => x.AttendanceTotal < 0 || x.MovementTotal < 0 ||
                x.StatusTotals.Any(s => s.Count < 0) ||
                x.StatusTotals.Sum(s => s.Count) != x.AttendanceTotal) ||
            report.Persons.Sum(x => x.AttendanceTotal) != report.AttendanceTotal ||
            report.Persons.Sum(x => x.MovementTotal) != report.MovementTotal ||
            report.StatusTotals.Any(x => x.Count < 0) ||
            report.StatusTotals.Sum(x => x.Count) != report.AttendanceTotal ||
            (report.DailyStatusHasNext && report.DailyStatuses.Count != 100) ||
            report.DailyStatuses.Any(x => x.Count < 0 || x.PersonCount < 0 ||
                x.PersonCount > report.PersonCount || x.PersonCount > x.Count))
            throw new InvalidDataException("Canonical group totals, PersonId or daily pagination mismatch.");
        return report;
    }

    // Phase 9: the ORIGINAL immutable PersonId-keyed historical report.
    // WPF presents selected 100-row pages only; the backend never clones SQL
    // or writes/exports a second independent personnel report.
    public async Task<IReadOnlyList<NativeHistoricalPersonCandidate>> SearchHistoricalPeopleAsync(
        NativeIdentity actor,string term,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if (!NativeHistoricalPersonCandidate.IsValidTerm(term))
            throw new InvalidDataException("Invalid historical person search.");
        if (!RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role))
            throw new UnauthorizedAccessException("Historical correction requires administration.");
        await VerifyPersonnelManagerAsync(actor,ct).ConfigureAwait(false);
        var response=await services.SearchPersonsAsync(term,50,ct,activeOnly:false).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor,ct).ConfigureAwait(false);
        var data=RequiredObject(response,"SearchPersons(historical)");
        var rows=RequiredArray(data,"items");
        if (rows.Count > 50 || RequiredNumber(data,"count") != rows.Count)
            throw new InvalidDataException("Invalid historical person search count.");
        var result=rows.Select(node =>
        {
            var item=RequireObject(node);
            return new NativeHistoricalPersonCandidate(RequiredText(item,"personId"),
                RequiredText(item,"displayName"),RequiredText(item,"serviceNumberA"),
                RequiredText(item,"siteName"),checked((int)RequiredNumber(item,"organizationalStatus")));
        }).ToArray();
        if (result.Any(x=>!x.IsValid) ||
            result.Select(x=>x.PersonId).Distinct(StringComparer.Ordinal).Count()!=result.Length)
            throw new InvalidDataException("Invalid historical person search response.");
        return Array.AsReadOnly(result);
    }

    public async Task<NativeHistoricalPeriodEvidence> ReadHistoricalPeriodAsync(
        NativeIdentity actor,NativeHistoricalPeriodRequest request,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if (!request.IsValid(DateOnly.FromDateTime(DateTime.Now)))
            throw new InvalidDataException("Invalid historical period.");
        if (!RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role))
            throw new UnauthorizedAccessException("Historical correction requires administration.");
        var report=await ReadIndividualAsync(actor,new NativeIndividualReportPages(request.PersonId,
            DateFrom:request.From.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            DateTo:request.To.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)),ct).ConfigureAwait(false);
        return NativeHistoricalPeriodEvidence.FromReport(request,report,DateOnly.FromDateTime(DateTime.Now));
    }

    public async Task<NativePersonnelReportSnapshot> ReadIndividualAsync(
        NativeIdentity actor, NativeIndividualReportPages pages,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        static bool IsCanonicalDate(string value) =>
            value.Length == 0 || DateOnly.TryParseExact(
                value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _);
        if (pages is null || pages.PersonId is not { Length: >= 3 and <= 128 } ||
            pages.PersonId.Any(char.IsControl) ||
            pages.AttendancePage is < 1 or > 100000 ||
            pages.MovementPage is < 1 or > 100000 ||
            pages.IdentityPage is < 1 or > 100000 ||
            pages.SitePage is < 1 or > 100000 ||
            pages.RevisionPage is < 1 or > 100000 ||
            pages.AuditPage is < 1 or > 100000 ||
            pages.MovementType is < 0 or > 4 ||
            pages.SiteId is null || pages.StatusId is null ||
            pages.SiteId.Length > 128 || pages.StatusId.Length > 128 ||
            pages.SiteId.Any(char.IsControl) ||
            pages.StatusId.Any(char.IsControl) ||
            pages.DateFrom is null || pages.DateTo is null ||
            !IsCanonicalDate(pages.DateFrom) || !IsCanonicalDate(pages.DateTo) ||
            (pages.DateFrom.Length > 0 && pages.DateTo.Length > 0 &&
             string.CompareOrdinal(pages.DateFrom, pages.DateTo) > 0))
            throw new InvalidDataException("Invalid native individual report request.");

        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse original = await new PersonnelReportService(database)
            .ReportAsync(new JsonObject
            {
                ["personId"] = pages.PersonId,
                ["attendancePage"] = pages.AttendancePage,
                ["movementPage"] = pages.MovementPage,
                ["identityPage"] = pages.IdentityPage,
                ["sitePage"] = pages.SitePage,
                ["revisionPage"] = pages.RevisionPage,
                ["auditPage"] = pages.AuditPage,
                ["dateFrom"] = pages.DateFrom,
                ["dateTo"] = pages.DateTo,
                ["movementType"] = pages.MovementType,
                ["siteId"] = pages.SiteId,
                ["statusId"] = pages.StatusId
            }, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);

        JsonObject data = RequiredObject(original, "PersonnelReport(native)");
        JsonObject person = RequireObject(data["person"]);
        string returnedId = RequiredText(person, "personId");
        if (returnedId != pages.PersonId ||
            RequiredNumber(data, "pageSize") != 100 ||
            RequiredNumber(data, "attendancePage") != pages.AttendancePage ||
            RequiredNumber(data, "movementPage") != pages.MovementPage ||
            RequiredNumber(data, "identityPage") != pages.IdentityPage ||
            RequiredNumber(data, "sitePage") != pages.SitePage ||
            RequiredNumber(data, "revisionPage") != pages.RevisionPage ||
            RequiredNumber(data, "auditPage") != pages.AuditPage ||
            RequiredText(data, "dateFrom") != pages.DateFrom ||
            RequiredText(data, "dateTo") != pages.DateTo ||
            RequiredNumber(data, "movementType") != pages.MovementType ||
            RequiredText(data, "siteId") != pages.SiteId ||
            RequiredText(data, "statusId") != pages.StatusId)
            throw new InvalidDataException("Original individual report identity, filter or page mismatch.");

        // Selectors originate from the SAME original encrypted individual
        // history response. No native SQL, and never infer complete lists
        // from the current 100-row attendance or assignment page.
        var filterSites = ParseChoices(RequiredArray(data, "filterSites"), "site");
        var filterStatuses = ParseChoices(RequiredArray(data, "filterStatuses"), "status");
        if ((pages.SiteId.Length > 0 && !filterSites.Any(x => x.Id == pages.SiteId)) ||
            (pages.StatusId.Length > 0 && !filterStatuses.Any(x => x.Id == pages.StatusId)))
            throw new InvalidDataException("Requested historical filter is not owned by this PersonId.");

        var totals = RequiredArray(data, "statusTotals").Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativePersonnelReportStatusTotal(
                RequiredText(item, "statusId"),
                RequiredText(item, "statusName"),
                RequiredNumber(item, "count"));
        }).ToArray();
        var attendance = RequiredArray(data, "attendance").Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativePersonnelReportAttendance(
                RequiredText(item, "date"), RequiredText(item, "statusName"),
                RequiredText(item, "site"), RequiredText(item, "source"),
                item["name"] is null ? null : RequiredText(item, "name"),
                item["serviceNumberA"] is null ? null : RequiredText(item, "serviceNumberA"),
                RequiredNumber(item, "revision"));
        }).ToArray();
        var moves = RequiredArray(data, "movements").Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativePersonnelReportMovement(
                RequiredText(item, "type"), RequiredText(item, "source"),
                RequiredText(item, "destination"), RequiredText(item, "date"),
                RequiredText(item, "name"), RequiredText(item, "serviceNumberA"),
                RequiredText(item, "executor"));
        }).ToArray();
        var identities = RequiredArray(data, "identityHistory").Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativePersonnelReportIdentity(
                RequiredText(item, "name"), RequiredText(item, "serviceNumberA"),
                RequiredText(item, "from"),
                item["to"] is null ? null : RequiredText(item, "to"),
                RequiredText(item, "reason"));
        }).ToArray();

        // These projections MUST come from the same canonical report read;
        // no current-site lookup is allowed to reconstruct historical site names.
        var sites = RequiredArray(data, "siteHistory").Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativePersonnelReportSite(
                RequiredText(item, "siteId"), RequiredText(item, "siteName"),
                RequiredText(item, "from"),
                item["to"] is null ? null : RequiredText(item, "to"),
                RequiredText(item, "createdAtUtc"));
        }).ToArray();
        var revisions = RequiredArray(data, "revisions").Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativePersonnelReportRevision(
                RequiredText(item, "date"), RequiredText(item, "before"),
                RequiredText(item, "after"),
                item["reason"] is null ? null : RequiredText(item, "reason"),
                RequiredText(item, "executedAtUtc"),
                item["authorizationMethod"] is null
                    ? null : RequiredText(item, "authorizationMethod"),
                RequiredText(item, "executor"));
        }).ToArray();

        // Complete monthly aggregates are sourced from the canonical
        // attendance GROUP BY month/status, not from any currently displayed page.
        JsonArray monthSource = RequiredArray(data, "monthlyTotals");
        if (monthSource.Count > 5000)
            throw new InvalidDataException("Original monthly report exceeds native safety bound.");
        var months = monthSource.Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativePersonnelReportMonthSummary(
                RequiredText(item, "period"), RequiredText(item, "statusId"),
                RequiredText(item, "statusName"), RequiredNumber(item, "count"));
        }).ToArray();

        JsonArray auditSource = RequiredArray(data, "audit");
        if (auditSource.Count > 100)
            throw new InvalidDataException("Original historical audit page exceeds 100 records.");
        var audits = auditSource.Select(node =>
        {
            JsonObject item = RequireObject(node);
            return new NativePersonnelReportAuditRecord(
                RequiredText(item, "operation"),
                RequiredText(item, "username"), RequiredText(item, "fullName"),
                item["effectiveDate"] is null
                    ? null : RequiredText(item, "effectiveDate"),
                RequiredText(item, "executedAtUtc"),
                RequiredText(item, "description"),
                item["reason"] is null ? null : RequiredText(item, "reason"));
        }).ToArray();

        var report = new NativePersonnelReportSnapshot(
            returnedId, RequiredText(person, "name"),
            RequiredText(person, "serviceNumberA"),
            RequiredText(person, "firstRecordedDate"),
            RequiredBoolean(person, "active"),
            RequiredNumber(data, "attendanceTotal"),
            RequiredNumber(data, "movementTotal"),
            RequiredNumber(data, "identityTotal"), 100,
            pages.AttendancePage, RequiredBoolean(data, "attendanceHasNext"),
            pages.MovementPage, RequiredBoolean(data, "movementHasNext"),
            pages.IdentityPage, RequiredBoolean(data, "identityHasNext"),
            totals, attendance, moves, identities)
        {
            SitePage = pages.SitePage,
            SiteTotal = RequiredNumber(data, "siteTotal"),
            SiteHasNext = RequiredBoolean(data, "siteHasNext"),
            Sites = sites,
            RevisionPage = pages.RevisionPage,
            RevisionTotal = RequiredNumber(data, "revisionTotal"),
            RevisionHasNext = RequiredBoolean(data, "revisionHasNext"),
            Revisions = revisions,
            MonthlyTotals = months,
            AuditTotal = RequiredNumber(data, "auditTotal"),
            AuditPage = pages.AuditPage,
            AuditHasNext = RequiredBoolean(data, "auditHasNext"),
            Audit = audits,
            DateFrom = RequiredText(data, "dateFrom"),
            DateTo = RequiredText(data, "dateTo"),
            MovementType = checked((int)RequiredNumber(data, "movementType")),
            SiteId = RequiredText(data, "siteId"),
            StatusId = RequiredText(data, "statusId"),
            AvailableSites = filterSites,
            AvailableStatuses = filterStatuses
        };
        if (report.StatusTotals.Count > 500 || report.Attendance.Count > 100 ||
            report.Movements.Count > 100 || report.Identities.Count > 100 ||
            report.Sites.Count > 100 || report.Revisions.Count > 100 ||
            report.MonthlyTotals.Count > 5000 || report.Audit.Count > 100 ||
            report.PersonId != pages.PersonId)
            throw new InvalidDataException("Original personnel report was truncated or forged.");
        return report;
    }

    // Phase 8 slice 15: exact owner-scoped, read-only outcome service.
    // No native SQL, export, or import-plan replay. The original service
    // filters CreatedByUserId=$actor AND Status='COMMITTED'.
    public async Task<IReadOnlyList<NativeImportBatchRow>> ReadRecentAsync(
        NativeIdentity actor, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new ExcelImportOutcomeService(database)
            .QueryAsync(null, actor.UserId, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "ExcelImportOutcomeList(native)");
        JsonArray batches = RequiredArray(data, "batches");
        if (batches.Count > 30)
            throw new InvalidDataException("Original import history exceeded 30 rows.");
        NativeImportBatchRow[] items = batches.Select(node =>
            ParseImportBatch(RequireObject(node))).ToArray();
        if (items.Select(x => x.ImportBatchId)
                .Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new InvalidDataException("Duplicate import batch identities.");
        return items;
    }

    public async Task<NativeImportBatchDetail> ReadBatchAsync(
        NativeIdentity actor, string batchId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (batchId is not { Length: >= 5 and <= 128 } ||
            !batchId.StartsWith("IMP-", StringComparison.Ordinal) ||
            batchId.Any(char.IsControl))
            throw new InvalidDataException("Invalid import batch key.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new ExcelImportOutcomeService(database)
            .QueryAsync(new JsonObject { ["importBatchId"] = batchId },
                actor.UserId, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "ExcelImportOutcomeDetails(native)");
        NativeImportBatchRow batch = ParseImportBatch(
            RequireObject(data["batch"]));
        JsonArray rejected = RequiredArray(data, "issues");
        if (batch.ImportBatchId != batchId || rejected.Count > 500 ||
            rejected.Count != batch.RejectedRows)
            throw new InvalidDataException("Incomplete or mismatched original import batch.");
        var issues = rejected.Select(node =>
        {
            JsonObject row = RequireObject(node);
            var issue = new NativeImportBatchIssue(
                checked((int)RequiredNumber(row, "row")),
                RequiredText(row, "serviceNumberA"),
                RequiredText(row, "name"),
                RequiredText(row, "siteName"),
                RequiredText(row, "reason"));
            if (issue.Row < 1 || issue.Reason.Length is < 1 or > 500 ||
                issue.Name.Length > 180 || issue.SiteName.Length > 100 ||
                issue.ServiceNumberA.Length > 70)
                throw new InvalidDataException("Malformed saved import issue.");
            return issue;
        }).ToArray();
        return new NativeImportBatchDetail(batch, issues);
    }

    // Phase 8 slice 16: ONLY the original owner-scoped, non-overwriting
    // rejected-row XLSX writer with preconfigured and validated output folder.
    // The UI never supplies any filesystem path or report filename.
    public async Task<NativeImportIssuesExportReceipt> ExportIssuesAsync(
        NativeIdentity actor, string batchId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (batchId is not { Length: >= 5 and <= 128 } ||
            !batchId.StartsWith("IMP-", StringComparison.Ordinal) ||
            batchId.Any(char.IsControl))
            throw new InvalidDataException("Invalid import issue export batch.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new ExcelImportOutcomeService(database)
            .ExportIssuesAsync(new JsonObject { ["importBatchId"] = batchId },
                actor.UserId, ct).ConfigureAwait(false);
        // If the session expired after the file was written, the client receives
        // no success receipt and must treat the result as uncertain.
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject original = RequiredObject(response, "ExcelImportIssuesExport(native)");
        var receipt = new NativeImportIssuesExportReceipt(
            RequiredText(original, "importBatchId"),
            RequiredText(original, "fileName"),
            RequiredText(original, "filePath"),
            checked((int)RequiredNumber(original, "issueCount")),
            RequiredText(original, "format"));
        if (receipt.ImportBatchId != batchId ||
            receipt.IssueCount is < 1 or > 500 || receipt.Format != "xlsx" ||
            receipt.FileName is not { Length: >= 6 and <= 180 } ||
            !receipt.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            receipt.FileName.Any(char.IsControl) ||
            receipt.FilePath is not { Length: >= 8 and <= 2048 } ||
            !Path.IsPathFullyQualified(receipt.FilePath) ||
            !string.Equals(Path.GetFileName(receipt.FilePath), receipt.FileName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Canonical issues export returned forged path or count.");
        return receipt;
    }

    private static NativeImportBatchRow ParseImportBatch(JsonObject data)
    {
        var row = new NativeImportBatchRow(
            RequiredText(data, "importBatchId"),
            RequiredText(data, "fileName"),
            checked((int)RequiredNumber(data, "totalRows")),
            checked((int)RequiredNumber(data, "acceptedRows")),
            checked((int)RequiredNumber(data, "rejectedRows")),
            RequiredText(data, "status"),
            RequiredText(data, "committedAtUtc"));
        if (row.ImportBatchId is not { Length: >= 5 and <= 128 } ||
            !row.ImportBatchId.StartsWith("IMP-", StringComparison.Ordinal) ||
            row.FileName.Length is < 1 or > 180 ||
            row.Status != "COMMITTED" ||
            row.TotalRows is < 1 or > 500 ||
            row.AcceptedRows is < 0 or > 500 ||
            row.RejectedRows is < 0 or > 500 ||
            row.AcceptedRows + row.RejectedRows != row.TotalRows ||
            !DateTimeOffset.TryParse(row.CommittedAtUtc, out _))
            throw new InvalidDataException("Malformed original import batch.");
        return row;
    }

    // Phase 8: canonical XLSX preview and additive-only, one-use import commit.
    // File bytes stay local and bounded; no workbook path ever reaches SQLCipher.
    public Task<NativeExcelImportReview> StageAndPreviewAsync(
        NativeIdentity actor, string fileName, byte[] workbook,
        CancellationToken ct = default) =>
        operations.RunAsync(() => StageAndPreviewAsync(actor, fileName, workbook, false, ct));

    public async Task<NativeExcelImportReview> StageAndPreviewAsync(
        NativeIdentity actor, string fileName, byte[] workbook,
        bool allSheets, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (fileName is not { Length: >= 6 and <= 180 } ||
            !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            fileName.Any(char.IsControl) ||
            workbook is not { Length: >= 1 and <= 160 * 1024 })
            throw new InvalidDataException("Unsafe local XLSX import file.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        string sha = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(workbook));
        string base64 = Convert.ToBase64String(workbook);
        var original = new ExcelPersonnelImportService(database);
        // Forbid updates to any current PersonId or existing custom field.
        BridgeResponse selected = await original.SelectAsync(new JsonObject
        {
            ["fileName"] = fileName,
            ["base64"] = base64,
            ["allSheets"] = allSheets,
            ["updateExistingCustomFields"] = false
        }, actor.UserId, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject source = RequiredObject(selected, "ExcelImportSelect(native)");
        string workbookToken = RequiredText(source, "workbookToken");
        long sourceRows = RequiredNumber(source, "rowCount");
        JsonArray selectedWorksheets = RequiredArray(source, "selectedWorksheets");
        var permittedSheets = selectedWorksheets.Select(node =>
            node?.GetValue<string>() ?? "").ToArray();
        if (permittedSheets.Length is < 1 or > 50 ||
            (!allSheets && permittedSheets.Length != 1) ||
            permittedSheets.Any(x => x.Length is < 1 or > 100 ||
                x.Any(char.IsControl)) ||
            permittedSheets.Distinct(StringComparer.Ordinal).Count() !=
                permittedSheets.Length)
            throw new InvalidDataException("Untrusted workbook worksheet selection.");
        if (workbookToken is not { Length: 64 } ||
            workbookToken.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            RequiredText(source, "fileName") != fileName ||
            sourceRows is < 1 or > 500)
            throw new InvalidDataException("Unsafe original staged workbook.");

        BridgeResponse analysisResponse = await original.AnalyzeAsync(
            new JsonObject { ["workbookToken"] = workbookToken },
            actor.UserId, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject analysis = RequiredObject(
            analysisResponse, "ExcelImportAnalyze(native)");
        BridgeResponse previewResponse = await original.PreviewAsync(
            new JsonObject { ["workbookToken"] = workbookToken },
            actor.UserId, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject preview = RequiredObject(
            previewResponse, "ExcelImportPreview(native)");
        string token = RequiredText(preview, "previewToken");
        if (token is not { Length: 64 } ||
            token.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            !RequiredBoolean(preview, "requiresExplicitConfirmation"))
            throw new InvalidDataException("Missing canonical one-use import approval.");

        foreach (string field in new[]
        {
            "totalRows", "newCount", "issueCount", "customUpdateCount",
            "acceptedPreview", "issues", "newSites", "similarNames",
            "similarNameCount", "customUpdatesPreview"
        })
            if (!JsonNode.DeepEquals(analysis[field], preview[field]))
                throw new InvalidDataException("Workbook changed during review: " + field);

        int total = checked((int)RequiredNumber(analysis, "totalRows"));
        int fresh = checked((int)RequiredNumber(preview, "newCount"));
        int issues = checked((int)RequiredNumber(preview, "issueCount"));
        int customUpdated = checked((int)RequiredNumber(preview, "customUpdateCount"));
        int similarCount = checked((int)RequiredNumber(preview, "similarNameCount"));
        JsonArray accepted = RequiredArray(preview, "acceptedPreview");
        JsonArray rejected = RequiredArray(preview, "issues");
        JsonArray newSites = RequiredArray(preview, "newSites");
        JsonArray collisions = RequiredArray(preview, "similarNames");
        if (total != sourceRows || total is < 1 or > 500 ||
            fresh is < 0 or > 500 || customUpdated != 0 ||
            fresh + issues != total || accepted.Count != fresh ||
            rejected.Count != issues || newSites.Count > 500 ||
            similarCount != collisions.Count || collisions.Count > 40 ||
            RequiredArray(preview, "customUpdatesPreview").Count != 0)
            throw new InvalidDataException(
                "Native pilot requires ALL accepted and same-name rows visible; " +
                "split files above 500 rows or when >40 name advisories cannot all be reviewed.");

        var people = accepted.Select(n =>
        {
            var x = RequireObject(n);
            return new NativeExcelImportPerson(
                RequiredText(x, "worksheet"),
                checked((int)RequiredNumber(x, "row")),
                RequiredText(x, "name"),
                RequiredText(x, "serviceNumberA"),
                RequiredText(x, "siteName"));
        }).ToArray();
        var errors = rejected.Select(n =>
        {
            var x = RequireObject(n);
            return new NativeExcelImportIssue(
                RequiredText(x, "worksheet"),
                checked((int)RequiredNumber(x, "row")),
                RequiredText(x, "serviceNumberA"),
                RequiredText(x, "name"), RequiredText(x, "siteName"),
                RequiredText(x, "reason"));
        }).ToArray();
        var sites = newSites.Select(n =>
        {
            var x = RequireObject(n);
            return new NativeExcelImportSite(
                RequiredText(x, "name"),
                checked((int)RequiredNumber(x, "personCount")));
        }).ToArray();
        var hints = collisions.Select(n =>
        {
            var x = RequireObject(n);
            if (!RequiredBoolean(x, "advisoryOnly"))
                throw new InvalidDataException("Non-advisory name-collision hint.");
            var matching = RequiredArray(x, "matches").Select(y =>
            {
                var m = RequireObject(y);
                return new NativeExcelImportMatch(
                    RequiredText(m, "personId"), RequiredText(m, "name"),
                    RequiredText(m, "serviceNumberA"),
                    RequiredBoolean(m, "active"));
            }).ToArray();
            if (matching.Length is < 1 or > 4)
                throw new InvalidDataException("Incomplete name collision hint.");
            return new NativeExcelImportCollision(
                RequiredText(x, "worksheet"),
                checked((int)RequiredNumber(x, "row")),
                RequiredText(x, "name"),
                RequiredText(x, "serviceNumberA"), matching);
        }).ToArray();

        // The signed canonical staging token must refer only to the
        // explicitly selected worksheets, including rejected rows and
        // same-name advisories. A truncated member list is never approved.
        var allowed = permittedSheets.ToHashSet(StringComparer.Ordinal);
        if (people.Any(p => !allowed.Contains(p.Worksheet)) ||
            errors.Any(e => !allowed.Contains(e.Worksheet)) ||
            hints.Any(h => !allowed.Contains(h.Worksheet)))
            throw new InvalidDataException("Unexpected worksheet in canonical import.");
        if (people.Select(p => p.ServiceNumberA)
                .Distinct(StringComparer.Ordinal).Count() != people.Length ||
            sites.Select(s => s.Name)
                .Distinct(StringComparer.Ordinal).Count() != sites.Length ||
            people.Any(p => p.Row < 2 || p.Name.Length is < 3 or > 180 ||
                p.ServiceNumberA.Length is < 4 or > 70) ||
            sites.Any(s => s.PersonCount is < 1 or > 500))
            throw new InvalidDataException("Duplicated or malformed canonical import.");
        return new NativeExcelImportReview(
            fileName, sha, token, total, fresh, issues, 0, sites,
            similarCount, people, errors, hints);
    }

    public async Task<NativeExcelImportReceipt> CommitAsync(
        NativeIdentity actor, NativeExcelImportReview reviewed,
        string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (reviewed is null || reviewed.CustomUpdateCount != 0 ||
            reviewed.NewCount is < 1 or > 500 ||
            reviewed.TotalRows is < 1 or > 500 ||
            reviewed.NewPeople.Count != reviewed.NewCount ||
            reviewed.Issues.Count != reviewed.RejectedCount ||
            reviewed.NewCount + reviewed.RejectedCount != reviewed.TotalRows ||
            reviewed.Collisions is null ||
            reviewed.Collisions.Count != reviewed.SimilarNameCount ||
            reviewed.PreviewToken is not { Length: 64 } ||
            reviewed.PreviewToken.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            !Guid.TryParseExact(operationId, "N", out _))
            throw new InvalidDataException("Original XLSX import approval invalid.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        // ONLY the original SQLCipher additive import writer. Explicit false
        // means existing peoples' custom fields cannot be silently updated.
        BridgeResponse committed = await new ExcelPersonnelImportService(database)
            .CommitAsync(new JsonObject
            {
                ["previewToken"] = reviewed.PreviewToken,
                ["operationId"] = operationId,
                ["approveCustomUpdates"] = false
            }, actor.UserId, actor.Username, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject result = RequiredObject(committed, "ExcelImportCommit(native)");
        var receipt = new NativeExcelImportReceipt(
            RequiredText(result, "importBatchId"),
            checked((int)RequiredNumber(result, "createdCount")),
            checked((int)RequiredNumber(result, "rejectedCount")),
            checked((int)RequiredNumber(result, "createdSitesCount")),
            checked((int)RequiredNumber(result, "customUpdatedCount")),
            RequiredText(result, "committedAtUtc"));
        if (receipt.CreatedCount != reviewed.NewCount ||
            receipt.RejectedCount != reviewed.RejectedCount ||
            receipt.CreatedSitesCount != reviewed.NewSites.Count ||
            receipt.CustomUpdatedCount != 0 ||
            receipt.ImportBatchId is not { Length: >= 5 and <= 128 } ||
            !receipt.ImportBatchId.StartsWith("IMP-", StringComparison.Ordinal))
            throw new InvalidDataException("Original XLSX receipt inconsistent.");
        return receipt;
    }

    // Phase 8 status definitions: original SQLCipher read and audited
    // AdminConfigurationService create/update/deactivate; never write SQL here.
    public async Task<IReadOnlyList<NativeAttendanceDraft>> DeleteAttendanceDraftAsync(
        NativeIdentity actor,NativeAttendanceDraft expected,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(expected is null || !expected.IsValid)
            throw new InvalidDataException("Invalid reviewed draft evidence.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce ||
            !RoleAuthorizationPolicy.CanPerformStandardOperations(principal.Role))
            throw new UnauthorizedAccessException("Draft deletion session changed.");
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation);
        await VerifyContinuationIdentityAsync(actor,lifetime.Token).ConfigureAwait(false);
        lifetime.Token.ThrowIfCancellationRequested();
        var response=await services.DeleteNativeReviewedDraftAsync(principal.UserId,new JsonObject
        {
            ["draftId"]=expected.DraftId,
            ["businessDate"]=expected.BusinessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            ["siteId"]=expected.SiteId==""?null:JsonValue.Create(expected.SiteId),
            ["targetStatusId"]=expected.TargetStatusId,
            ["rangeStartDate"]=expected.RangeEvidence?.StartDate,
            ["rangeEndDate"]=expected.RangeEvidence?.EndDate,
            ["rangeIsInitial"]=expected.RangeEvidence is { } range?JsonValue.Create(range.IsInitial):null,
            ["updatedAtUtc"]=expected.UpdatedAtUtc.ToString("O",CultureInfo.InvariantCulture),
            ["selectedPersonIds"]=new JsonArray(expected.PersonIds.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray())
        },lifetime.Token).ConfigureAwait(false);
        var receipt=RequiredObject(response,"DeleteAttendanceDraft(native)");
        if(RequiredText(receipt,"draftId")!=expected.DraftId)
            throw new InvalidDataException("Draft deletion receipt mismatch.");
        var after=await services.ListNativeDraftsAsync(principal.UserId,lifetime.Token).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,lifetime.Token).ConfigureAwait(false);
        lifetime.Token.ThrowIfCancellationRequested();
        var remaining=ParseNativeDraftCatalogue(after);
        if(remaining.Any(row=>row.DraftId==expected.DraftId))
            throw new InvalidDataException("Deleted draft remains in owner catalogue.");
        return remaining;
    }

    public async Task<NativeAttendanceDraft> SaveAttendanceDraftAsync(
        NativeIdentity actor,NativeAttendanceDraftIntent intent,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(intent is null || !intent.IsValid(DateOnly.FromDateTime(DateTime.Now)))
            throw new InvalidDataException("Invalid native draft intent.");
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce ||
            !RoleAuthorizationPolicy.CanPerformStandardOperations(principal.Role))
            throw new UnauthorizedAccessException("Draft save session changed.");
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation);
        var before=RequiredArray(await services.ListNativeDraftsAsync(principal.UserId,lifetime.Token).ConfigureAwait(false),"ListAttendanceDrafts(before native save)");
        if(before.Count>=10000 || before.Any(n=>RequiredText(RequireObject(n),"draftId")==intent.DraftId))
            throw new InvalidDataException("Draft identity already exists or catalogue full.");
        await VerifyContinuationIdentityAsync(actor,lifetime.Token).ConfigureAwait(false);
        lifetime.Token.ThrowIfCancellationRequested();
        if(!intent.IsValid(DateOnly.FromDateTime(DateTime.Now)))
            throw new InvalidDataException("Draft date changed.");
        var response=await services.SaveNativeDraftAsync(principal.UserId,new JsonObject
        {
            ["draftId"]=intent.DraftId,
            ["businessDate"]=intent.BusinessDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            ["siteId"]=intent.SiteId==""?null:JsonValue.Create(intent.SiteId),
            ["targetStatusId"]=intent.TargetStatusId,
            ["rangeStartDate"]=intent.RangeEvidence?.StartDate,
            ["rangeEndDate"]=intent.RangeEvidence?.EndDate,
            ["rangeIsInitial"]=intent.RangeEvidence is { } range?JsonValue.Create(range.IsInitial):null,
            ["selectedPersonIds"]=new JsonArray(intent.PersonIds.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray())
        },lifetime.Token).ConfigureAwait(false);
        var receipt=RequiredObject(response,"SaveAttendanceDraft(native)");
        if(RequiredText(receipt,"draftId")!=intent.DraftId ||
            !DateTimeOffset.TryParseExact(RequiredText(receipt,"savedAtUtc"),"O",CultureInfo.InvariantCulture,
                DateTimeStyles.None,out var saved) || saved.Offset!=TimeSpan.Zero)
            throw new InvalidDataException("Invalid canonical draft save receipt.");
        var after=RequiredArray(await services.ListNativeDraftsAsync(principal.UserId,lifetime.Token).ConfigureAwait(false),"ListAttendanceDrafts(after native save)");
        await VerifyContinuationIdentityAsync(actor,lifetime.Token).ConfigureAwait(false);
        lifetime.Token.ThrowIfCancellationRequested();
        var result=ParseNativeDraftRows(after).Single(row=>row.DraftId==intent.DraftId);
        if(!intent.Matches(result) || result.UpdatedAtUtc!=saved)
            throw new InvalidDataException("Draft read-back mismatch.");
        return result;
    }

    public async Task<IReadOnlyList<NativeAttendanceDraft>> ReadAttendanceDraftsAsync(
        NativeIdentity actor,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce ||
            !RoleAuthorizationPolicy.CanPerformStandardOperations(principal.Role))
            throw new UnauthorizedAccessException("Draft session changed.");
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation);
        var response=await services.ListNativeDraftsAsync(principal.UserId,lifetime.Token).ConfigureAwait(false);
        await VerifyContinuationIdentityAsync(actor,lifetime.Token).ConfigureAwait(false);
        lifetime.Token.ThrowIfCancellationRequested();
        return ParseNativeDraftCatalogue(response);
    }

    private static IReadOnlyList<NativeAttendanceDraft> ParseNativeDraftCatalogue(BridgeResponse response)
    {
        return ParseNativeDraftRows(RequiredArray(response,"ListAttendanceDrafts(native)"));
    }

    private static IReadOnlyList<NativeAttendanceDraft> ParseNativeDraftRows(JsonArray items)
    {
        if(items.Count>10000)throw new InvalidDataException("Draft catalogue exceeds native bound.");
        var rows=items.Select(node=>
        {
            var row=RequireObject(node);
            if(!DateOnly.TryParseExact(RequiredText(row,"businessDate"),"yyyy-MM-dd",CultureInfo.InvariantCulture,
                    DateTimeStyles.None,out var date) ||
                !DateTimeOffset.TryParseExact(RequiredText(row,"updatedAtUtc"),"O",CultureInfo.InvariantCulture,
                    DateTimeStyles.None,out var updated))
                throw new InvalidDataException("Malformed canonical draft date.");
            var ids=RequiredArray(row,"selectedPersonIds").Select(id=>
                id?.GetValue<string>()??throw new InvalidDataException("Missing draft person identity.")).ToArray();
            return new NativeAttendanceDraft(RequiredText(row,"draftId"),date,
                row["siteId"]?.GetValue<string>()??"",RequiredText(row,"targetStatusId"),updated,Array.AsReadOnly(ids))
            { RangeEvidence=ParseNativeDraftRange(row) };
        }).ToArray();
        if(!NativeAttendanceDraft.ValidCatalogue(rows))
            throw new InvalidDataException("Invalid canonical owner draft catalogue.");
        return Array.AsReadOnly(rows);
    }

    private static NativeAttendanceDraftRangeEvidence? ParseNativeDraftRange(JsonObject row)
    {
        if(!row.ContainsKey("rangeStartDate") || !row.ContainsKey("rangeEndDate") || !row.ContainsKey("rangeIsInitial"))
            throw new InvalidDataException("Missing native range evidence fields.");
        var start=row["rangeStartDate"];var end=row["rangeEndDate"];var mode=row["rangeIsInitial"];
        if(start is null && end is null && mode is null)return null;
        if(start is null || end is null || mode is null)
            throw new InvalidDataException("Partial stored range evidence.");
        return new(start.GetValue<string>(),end.GetValue<string>(),mode.GetValue<bool>());
    }

    public async Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutAsync(
        NativeIdentity actor,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        await VerifyPersonnelManagerAsync(actor,ct).ConfigureAwait(false);
        var principal=await ResolveSitesPrincipalAsync(actor,ct).ConfigureAwait(false);
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation);
        return await ReadRosterLayoutCoreAsync(actor,lifetime.Token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutCoreAsync(
        NativeIdentity actor,CancellationToken ct)
    {
        // The caller owns the admission lease and session-linked cancellation.
        var response=await new RosterColumnStructureService(database)
            .ListAsync(new JsonObject{["includeInactive"]=true},ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var data=RequiredObject(response,"GetCustomFieldDefinitions(native)");
        var rows=RequiredArray(data,"items").Select(node=>
        {
            var item=RequireObject(node);
            return new NativeRosterColumn(RequiredText(item,"columnId"),RequiredText(item,"technicalKey"),
                RequiredText(item,"caption"),checked((int)RequiredNumber(item,"fieldType")),
                checked((int)RequiredNumber(item,"displayOrder")),RequiredBoolean(item,"isSystemProtected"),
                RequiredBoolean(item,"visible"),RequiredBoolean(item,"active"),
                RequiredNumber(item,"storedValueCount"),RequiredText(item,"snapshot"));
        }).ToArray();
        if(RequiredNumber(data,"total")!=rows.Length || !NativeRosterColumn.ValidCatalogue(rows))
            throw new InvalidDataException("Invalid canonical roster layout catalogue.");
        return Array.AsReadOnly(rows);
    }

    public async Task<NativeRosterColumn> CommitRosterColumnAsync(
        NativeRosterColumnCommit operation,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(operation is null || operation.Change is null ||
            !Guid.TryParseExact(operation.OperationId,"N",out _))
            throw new InvalidDataException("Missing reviewed column operation.");
        var actor=operation.Actor;
        await VerifyPersonnelManagerAsync(actor,ct).ConfigureAwait(false);
        var principal=await ResolveSitesPrincipalAsync(actor,ct).ConfigureAwait(false);
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation);
        var before=await ReadRosterLayoutCoreAsync(actor,lifetime.Token).ConfigureAwait(false);
        var change=operation.Change;
        if(!change.CanApply(before))
            throw new InvalidDataException("Column metadata changed or protected.");
        var payload=new JsonObject{["operationId"]=operation.OperationId};
        if(change.Previous is { } previous)
        {
            payload["columnId"]=previous.ColumnId;
            payload["expectedSnapshot"]=previous.Snapshot;
        }
        if(change.Operation!=NativeRosterColumnOperation.Deactivate)
        {
            payload["caption"]=change.Caption;
            payload["fieldType"]=change.FieldType;
            if(change.Operation==NativeRosterColumnOperation.Update)
            {
                payload["displayOrder"]=change.DisplayOrder;
                payload["visible"]=change.Visible;
            }
        }
        else payload["confirmed"]=true;
        await VerifyPersonnelManagerAsync(actor,lifetime.Token).ConfigureAwait(false);
        lifetime.Token.ThrowIfCancellationRequested();
        var service=new RosterColumnStructureService(database);
        BridgeResponse response=change.Operation switch
        {
            NativeRosterColumnOperation.Create=>await service.CreateAsync(payload,principal,lifetime.Token).ConfigureAwait(false),
            NativeRosterColumnOperation.Update=>await service.UpdateAsync(payload,principal,lifetime.Token).ConfigureAwait(false),
            NativeRosterColumnOperation.Deactivate=>await service.DeactivateAsync(payload,principal,lifetime.Token).ConfigureAwait(false),
            _=>throw new InvalidDataException("Unknown column operation.")
        };
        var data=RequiredObject(response,"RosterColumnMutation(native)");
        string id=RequiredText(data,"columnId");
        string snapshot=RequiredText(data,"snapshot");
        var after=await ReadRosterLayoutCoreAsync(actor,lifetime.Token).ConfigureAwait(false);
        var result=after.SingleOrDefault(row=>row.ColumnId==id);
        if(result is null || result.Snapshot!=snapshot ||
            result.TechnicalKey!=RequiredText(data,"technicalKey") ||
            result.Caption!=RequiredText(data,"caption") ||
            result.FieldType!=RequiredNumber(data,"fieldType") ||
            result.DisplayOrder!=RequiredNumber(data,"displayOrder") ||
            result.Active!=RequiredBoolean(data,"active") ||
            result.Visible!=RequiredBoolean(data,"visible") ||
            RequiredBoolean(data,"isSystemProtected") ||
            !change.MatchesReceipt(result) ||
            (change.Operation==NativeRosterColumnOperation.Create && before.Any(row=>row.ColumnId==id || row.TechnicalKey==result.TechnicalKey)))
            throw new InvalidDataException("Canonical column receipt/read-back mismatch.");
        return result;
    }

    public async Task<IReadOnlyList<NativeStatusRow>> ReadActiveStatusesAsync(
        NativeIdentity actor, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await services.ListActiveStatusesAsync(ct)
            .ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var rows = RequiredArray(response, "GetActiveAttendanceStatuses(native)")
            .Select(node =>
            {
                JsonObject status = RequireObject(node);
                if (!RequiredBoolean(status, "active"))
                    throw new InvalidDataException("Inactive status in canonical active list.");
                var row = new NativeStatusRow(
                    RequiredText(status, "statusId"),
                    RequiredText(status, "displayName"),
                    RequiredText(status, "shortSymbol"),
                    RequiredText(status, "color"),
                    RequiredNumber(status, "version"),
                    RequiredNumber(status, "todayCount"));
                ValidateStatusRow(row);
                return row;
            }).ToArray();
        if (rows.Length > 10000 ||
            rows.Select(row => row.StatusId)
                .Distinct(StringComparer.Ordinal).Count() != rows.Length)
            throw new InvalidDataException("Duplicated canonical attendance status ID.");
        return rows;
    }

    public async Task<NativeStatusMutationReceipt> CreateStatusAsync(
        NativeIdentity actor, string name, string symbol, string color,
        string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateStatusEdit(name, symbol, color, operationId);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgePrincipal principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);
        BridgeResponse response = await new AdminConfigurationService(database)
            .CreateStatusAsync(new JsonObject
            {
                ["name"] = name, ["symbol"] = symbol, ["color"] = color,
                ["operationId"] = operationId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "CreateAttendanceStatus(native)");
        string id = RequiredText(data, "statusId");
        if (id.Length is < 5 or > 128 ||
            !id.StartsWith("STS-", StringComparison.Ordinal) ||
            RequiredNumber(data, "rowVersion") != 1 ||
            string.IsNullOrWhiteSpace(RequiredText(data, "auditEventId")))
            throw new InvalidDataException("Invalid audited new status receipt.");
        return new NativeStatusMutationReceipt(id, name, symbol, color,
            1, true, 0);
    }

    public async Task<NativeStatusMutationReceipt> UpdateStatusAsync(
        NativeIdentity actor, string statusId, long revision,
        string name, string symbol, string color, string operationId,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateStatusEdit(name, symbol, color, operationId);
        ValidateStatusIdentity(statusId, revision);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgePrincipal principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);
        BridgeResponse response = await new AdminConfigurationService(database)
            .UpdateStatusAsync(new JsonObject
            {
                ["statusId"] = statusId, ["expectedRevision"] = revision,
                ["name"] = name, ["symbol"] = symbol, ["color"] = color,
                ["operationId"] = operationId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "UpdateAttendanceStatus(native)");
        if (RequiredText(data, "statusId") != statusId ||
            RequiredNumber(data, "rowVersion") != revision + 1 ||
            string.IsNullOrWhiteSpace(RequiredText(data, "auditEventId")))
            throw new InvalidDataException("Invalid status update receipt.");
        return new NativeStatusMutationReceipt(statusId, name, symbol,
            color, revision + 1, true, 0);
    }

    public async Task<NativeStatusMutationReceipt> DeactivateStatusAsync(
        NativeIdentity actor, string statusId, long revision,
        string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateStatusIdentity(statusId, revision);
        if (!Guid.TryParseExact(operationId, "N", out _))
            throw new InvalidDataException("Invalid status deactivation idempotency.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgePrincipal principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);

        // The canonical service independently rejects the automatic-attendance
        // default status and retains all historical AttendanceRecords.
        // Freeze the input fields before disabling, but do not pretend the
        // active-status list will include a disabled row afterwards.
        BridgeResponse current = await services.ListActiveStatusesAsync(ct)
            .ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var entries = RequiredArray(current, "NativeStatusBeforeDeactivation")
            .Select(RequireObject).ToArray();
        var found = entries.Where(x =>
            RequiredText(x, "statusId") == statusId &&
            RequiredNumber(x, "version") == revision &&
            RequiredBoolean(x, "active")).ToArray();
        if (found.Length != 1)
            throw new InvalidDataException("Status changed before deactivation.");
        string name = RequiredText(found[0], "displayName");
        string symbol = RequiredText(found[0], "shortSymbol");
        string color = RequiredText(found[0], "color");
        ValidateStatusRow(new NativeStatusRow(statusId, name, symbol, color,
            revision, RequiredNumber(found[0], "todayCount")));

        BridgeResponse response = await new AdminConfigurationService(database)
            .DeactivateStatusAsync(new JsonObject
            {
                ["statusId"] = statusId, ["expectedRevision"] = revision,
                ["operationId"] = operationId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "DeactivateOrDeleteStatus(native)");
        if (RequiredText(data, "statusId") != statusId ||
            RequiredBoolean(data, "active") ||
            string.IsNullOrWhiteSpace(RequiredText(data, "auditEventId")))
            throw new InvalidDataException("Invalid canonical status deactivate receipt.");
        // Original deactivate receipt intentionally contains no rowVersion:
        // revision+1 follows the original version-checked SQL UPDATE.
        return new NativeStatusMutationReceipt(statusId, name, symbol,
            color, checked(revision + 1), false, 0);
    }

    private static void ValidateStatusIdentity(string statusId, long revision)
    {
        if (statusId is not { Length: >= 1 and <= 128 } ||
            statusId.Any(char.IsControl) || revision < 1 ||
            revision == long.MaxValue)
            throw new InvalidDataException("Invalid stable status ID/version.");
    }

    private static void ValidateStatusEdit(string name, string symbol,
        string color, string operationId)
    {
        if (name is not { Length: >= 2 and <= 75 } ||
            name != name.Trim() || name.Any(char.IsControl) ||
            symbol is not { Length: >= 1 and <= 5 } ||
            symbol != symbol.Trim() || symbol.Any(char.IsControl) ||
            color is null || !System.Text.RegularExpressions.Regex.IsMatch(
                color, "^#[0-9a-fA-F]{6}$") ||
            !Guid.TryParseExact(operationId, "N", out _))
            throw new InvalidDataException("Invalid status name, symbol or color.");
    }

    private static void ValidateStatusRow(NativeStatusRow row)
    {
        if (row.StatusId is not { Length: >= 1 and <= 128 } ||
            row.StatusId.Any(char.IsControl) ||
            row.Name is not { Length: >= 2 and <= 75 } ||
            row.Name.Any(char.IsControl) ||
            row.Symbol is not { Length: >= 1 and <= 5 } ||
            row.Symbol.Any(char.IsControl) ||
            row.Color is null ||
            !System.Text.RegularExpressions.Regex.IsMatch(
                row.Color, "^#[0-9a-fA-F]{6}$") ||
            row.Revision < 1 || row.TodayCount < 0)
            throw new InvalidDataException("Malformed canonical active attendance status.");
    }

    // Phase 8 site retirement: no native SQL writer, no alternate destination.
    // Original service owns the impact preflight and ATOMIC dated reassignment.
    public async Task<NativeSiteRetirementPreview> PreviewRetirementAsync(
        NativeIdentity actor, string siteId, long revision,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateSiteRetirementIdentity(siteId, revision);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgePrincipal principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);
        BridgeResponse result = await new AdminConfigurationService(database)
            .PreviewSiteRetirementAsync(new JsonObject
            {
                ["siteId"] = siteId, ["expectedRevision"] = revision
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(result, "PreviewSiteRetirement(native)");
        var snapshot = new NativeSiteRetirementPreview(
            RequiredText(data, "siteId"),
            RequiredText(data, "siteName"),
            RequiredNumber(data, "rowVersion"),
            RequiredNumber(data, "activeCount"),
            RequiredText(data, "effectiveDate"),
            RequiredText(data, "destinationSiteId"),
            RequiredText(data, "destinationSiteName"),
            RequiredBoolean(data, "eligible"),
            RequiredText(data, "blockReason"));
        if (snapshot.SiteId != siteId ||
            snapshot.Revision != revision ||
            snapshot.SiteName is not { Length: >= 2 and <= 100 } ||
            snapshot.ActiveCount < 0 ||
            snapshot.EffectiveDate !=
                DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ||
            snapshot.DestinationSiteId != "SITE-UNSPECIFIED" ||
            snapshot.DestinationSiteName != "غير محدد" ||
            (snapshot.Eligible && snapshot.BlockReason.Length != 0) ||
            (!snapshot.Eligible && string.IsNullOrWhiteSpace(snapshot.BlockReason)))
            throw new InvalidDataException("Invalid original site impact snapshot.");
        return snapshot;
    }

    public async Task<NativeSiteRetirementReceipt> CommitRetirementAsync(
        NativeIdentity actor, NativeSiteRetirementPreview preview,
        string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (preview is null) throw new InvalidDataException("Missing impact review.");
        ValidateSiteRetirementIdentity(preview.SiteId, preview.Revision);
        if (!preview.Eligible || preview.BlockReason != "" ||
            preview.ActiveCount < 0 || preview.Revision == long.MaxValue ||
            preview.SiteName is not { Length: >= 2 and <= 100 } ||
            preview.DestinationSiteId != "SITE-UNSPECIFIED" ||
            preview.DestinationSiteName != "غير محدد" ||
            preview.EffectiveDate !=
                DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ||
            !Guid.TryParseExact(operationId, "N", out _))
            throw new InvalidDataException("Stale or unconfirmed site retirement.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgePrincipal principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);
        // The original service rechecks exact site revision, current number
        // of open assignments, day/month closures, same-day attendance and
        // movement, then executes ALL reassignment/audit/site-retirement in
        // one SQLCipher transaction. No client can redirect the destination.
        BridgeResponse result = await new AdminConfigurationService(database)
            .RemoveSiteAndReassignCurrentPersonsAsync(new JsonObject
            {
                ["siteId"] = preview.SiteId,
                ["expectedRevision"] = preview.Revision,
                ["expectedActiveCount"] = preview.ActiveCount,
                ["confirmed"] = true,
                ["operationId"] = operationId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(result, "RemoveSiteAndReassignCurrentPersons(native)");
        if (RequiredText(data, "siteId") != preview.SiteId ||
            RequiredText(data, "siteName") != preview.SiteName ||
            RequiredBoolean(data, "active") ||
            RequiredNumber(data, "rowVersion") != preview.Revision + 1 ||
            RequiredNumber(data, "reassignedCount") != preview.ActiveCount ||
            RequiredText(data, "effectiveDate") != preview.EffectiveDate)
            throw new InvalidDataException("Site retirement receipt changed.");
        return new NativeSiteRetirementReceipt(
            preview.SiteId, preview.SiteName, preview.Revision + 1,
            preview.ActiveCount, preview.EffectiveDate,
            "SITE-UNSPECIFIED", false);
    }

    private static void ValidateSiteRetirementIdentity(string siteId,
        long revision)
    {
        if (siteId is not { Length: >= 3 and <= 128 } ||
            siteId.Any(char.IsControl) || siteId == "SITE-UNSPECIFIED" ||
            revision < 1 || revision == long.MaxValue)
            throw new InvalidDataException("Protected or stale site identity.");
    }

    // Phase 8 sites: canonical SQLCipher catalogue and original audit writer.
    // No native site SQL, deletion, reassignment or role/nonce shortcuts.
    public async Task<IReadOnlyList<NativeSiteRow>> ReadSitesAsync(
        NativeIdentity actor, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await services.ListSitesAsync(
            includeInactive: true, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var sites = RequiredArray(response, "QuerySites(native)")
            .Select(node =>
            {
                JsonObject row = RequireObject(node);
                return new NativeSiteRow(
                    RequiredText(row, "siteId"),
                    RequiredText(row, "name"),
                    RequiredBoolean(row, "active"),
                    RequiredNumber(row, "rowVersion"));
            }).ToArray();
        if (sites.Length > 10001 ||
            sites.Any(row => row.SiteId.Length is < 1 or > 128 ||
                row.SiteId.Any(char.IsControl) ||
                row.Name.Length is < 1 or > 200 ||
                row.Name.Any(char.IsControl) || row.Revision < 1) ||
            sites.Select(x => x.SiteId).Distinct(StringComparer.Ordinal).Count()
                != sites.Length)
            throw new InvalidDataException("Invalid native site catalogue.");
        return sites;
    }

    public async Task<NativeSiteMutationReceipt> CreateSiteAsync(
        NativeIdentity actor, string name, string operationId,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateSiteEdit(name, operationId);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await ResolveSitesPrincipalAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new AdminConfigurationService(database)
            .CreateSiteAsync(new JsonObject
            {
                ["name"] = name, ["operationId"] = operationId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "CreateSite(native)");
        string id = RequiredText(data, "siteId");
        if (id.Length is < 6 or > 128 ||
            !id.StartsWith("SITE-", StringComparison.Ordinal) ||
            id == "SITE-UNSPECIFIED" ||
            RequiredText(data, "name") != name ||
            RequiredNumber(data, "rowVersion") != 1 ||
            string.IsNullOrWhiteSpace(RequiredText(data, "auditEventId")))
            throw new InvalidDataException("Site create receipt mismatch.");
        return new NativeSiteMutationReceipt(id, name, 1);
    }

    public async Task<NativeSiteMutationReceipt> RenameSiteAsync(
        NativeIdentity actor, string siteId, long revision,
        string name, string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateSiteEdit(name, operationId);
        if (siteId is not { Length: >= 1 and <= 128 } ||
            siteId == "SITE-UNSPECIFIED" ||
            siteId.Any(char.IsControl) || revision < 1)
            throw new InvalidDataException("Invalid original site version.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await ResolveSitesPrincipalAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new AdminConfigurationService(database)
            .RenameSiteAsync(new JsonObject
            {
                ["siteId"] = siteId, ["expectedRevision"] = revision,
                ["name"] = name, ["operationId"] = operationId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "RenameSite(native)");
        if (RequiredText(data, "siteId") != siteId ||
            RequiredNumber(data, "rowVersion") != revision + 1 ||
            string.IsNullOrWhiteSpace(RequiredText(data, "auditEventId")))
            throw new InvalidDataException("Site rename receipt mismatch.");
        return new NativeSiteMutationReceipt(siteId, name, revision + 1);
    }

    private async Task<BridgePrincipal> ResolveSitesPrincipalAsync(
        NativeIdentity actor, CancellationToken ct)
    {
        var principal = await services.ResolveSessionAsync(actor.SessionNonce, ct)
            .ConfigureAwait(false) ??
            throw new UnauthorizedAccessException("Site manager session expired.");
        if (principal.UserId != actor.UserId ||
            principal.Username != actor.Username ||
            principal.Role != actor.Role ||
            principal.SessionNonce != actor.SessionNonce)
            throw new UnauthorizedAccessException("Site manager actor changed.");
        return principal;
    }

    private static void ValidateSiteEdit(string name, string operationId)
    {
        if (name is not { Length: >= 2 and <= 100 } ||
            name != name.Trim() || name == "غير محدد" ||
            name.Any(char.IsControl) ||
            !Guid.TryParseExact(operationId, "N", out _))
            throw new InvalidDataException("Invalid site name or operation ID.");
    }

    // Phase 8: archived return reuses the SAME stable PersonId.
    // The original ArchivedReturnService is the only preview/commit writer.
    public async Task<NativeArchivedReturnPreview> PreviewReturnAsync(
        NativeIdentity actor, NativeArchivedReturnDraft draft,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateArchivedReturnDraft(draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await services.ResolveSessionAsync(actor.SessionNonce, ct)
            .ConfigureAwait(false) ??
            throw new UnauthorizedAccessException("Archived identity session expired.");
        if (principal.UserId != actor.UserId ||
            principal.Username != actor.Username ||
            principal.Role != actor.Role ||
            principal.SessionNonce != actor.SessionNonce)
            throw new UnauthorizedAccessException("Archived identity actor mismatch.");

        BridgeResponse response = await new ArchivedReturnService(database)
            .PreviewAsync(new JsonObject
            {
                ["visibleName"] = draft.OriginalName,
                ["serviceNumberA"] = draft.ServiceNumberA,
                ["siteId"] = draft.DestinationSiteId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "ResolveArchivedIdentity(native)");
        JsonArray matches = RequiredArray(data, "candidates");
        if (matches.Count is < 1 or > 50 ||
            !RequiredBoolean(data, "requiresResolution") ||
            !RequiredBoolean(data, "numberContested"))
            throw new InvalidDataException("Selected archived identity was not resolved.");
        string businessDate = RequiredText(data, "businessDate");
        string token = RequiredText(data, "previewToken");
        if (businessDate != LocalPersonDay() || token.Length != 64 ||
            token.Any(ch => !char.IsAsciiHexDigit(ch)))
            throw new InvalidDataException("Archived identity preview expired or malformed.");

        var candidates = matches.Select(node =>
        {
            JsonObject x = RequireObject(node);
            var result = new NativeArchivedReturnCandidate(
                RequiredText(x, "personId"),
                RequiredText(x, "name"),
                RequiredText(x, "serviceNumberA"),
                RequiredNumber(x, "revision"),
                checked((int)RequiredNumber(x, "organizationalStatus")),
                RequiredText(x, "exitDate"),
                RequiredText(x, "previousSiteName"),
                RequiredText(x, "exitType"),
                RequiredBoolean(x, "numberMatch"),
                RequiredBoolean(x, "nameMatch"));
            if (result.Revision < 1 ||
                result.OrganizationalStatus is not (2 or 3 or 4) ||
                string.IsNullOrWhiteSpace(result.PersonId) ||
                string.IsNullOrWhiteSpace(result.Name) ||
                string.IsNullOrWhiteSpace(result.ServiceNumberA))
                throw new InvalidDataException("Malformed archived identity candidate.");
            return result;
        }).ToArray();
        if (candidates.Select(x => x.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != candidates.Length ||
            !candidates.Any(x =>
                x.PersonId == draft.PersonId &&
                x.Name == draft.OriginalName &&
                x.ServiceNumberA == draft.ServiceNumberA &&
                x.Revision == draft.Revision &&
                x.OrganizationalStatus == draft.OrganizationalStatus &&
                x.NumberMatch && x.NameMatch))
            throw new InvalidDataException("Original archived person was not in canonical matches.");
        return new NativeArchivedReturnPreview(draft, businessDate, token, true, candidates);
    }

    public async Task<NativeArchivedReturnReceipt> CommitReturnAsync(
        NativeIdentity actor, NativeArchivedReturnPreview preview,
        string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (preview is null || !Guid.TryParseExact(operationId, "N", out _) ||
            preview.BusinessDate != LocalPersonDay() ||
            preview.PreviewToken is not { Length: 64 } ||
            preview.PreviewToken.Any(x => !char.IsAsciiHexDigit(x)) ||
            preview.Candidates is null || preview.Candidates.Count is < 1 or > 50 ||
            preview.Candidates.Count(x => x.PersonId == preview.Draft.PersonId &&
                x.Revision == preview.Draft.Revision &&
                x.Name == preview.Draft.OriginalName &&
                x.ServiceNumberA == preview.Draft.ServiceNumberA &&
                x.OrganizationalStatus == preview.Draft.OrganizationalStatus &&
                x.NumberMatch && x.NameMatch) != 1)
            throw new InvalidDataException("Invalid archived identity approval.");
        ValidateArchivedReturnDraft(preview.Draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await services.ResolveSessionAsync(actor.SessionNonce, ct)
            .ConfigureAwait(false) ??
            throw new UnauthorizedAccessException("Archived return session expired.");
        if (principal.UserId != actor.UserId ||
            principal.Username != actor.Username ||
            principal.Role != actor.Role ||
            principal.SessionNonce != actor.SessionNonce)
            throw new UnauthorizedAccessException("Archived return principal changed.");

        // Explicit same-human decision; no new person, direct SQL, or silent retry.
        BridgeResponse response = await new ArchivedReturnService(database)
            .CommitReturnAsync(new JsonObject
            {
                ["previewToken"] = preview.PreviewToken,
                ["personId"] = preview.Draft.PersonId,
                ["decision"] = "same",
                ["operationId"] = operationId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "CommitArchivedReturn(native)");
        string movement = RequiredText(data, "movementId");
        if (!RequiredBoolean(data, "returned") ||
            RequiredText(data, "personId") != preview.Draft.PersonId ||
            RequiredText(data, "siteId") != preview.Draft.DestinationSiteId ||
            RequiredText(data, "businessDate") != preview.BusinessDate ||
            RequiredNumber(data, "revision") != preview.Draft.Revision + 1 ||
            movement.Length is < 5 or > 128 ||
            !movement.StartsWith("MOV-", StringComparison.Ordinal))
            throw new InvalidDataException("Archived return receipt identity mismatch.");
        return new NativeArchivedReturnReceipt(
            preview.Draft.PersonId, movement, preview.Draft.DestinationSiteId,
            preview.BusinessDate, preview.Draft.Revision + 1, true);
    }

    private static void ValidateArchivedReturnDraft(NativeArchivedReturnDraft draft)
    {
        if (draft is null || draft.PersonId is not { Length: >= 1 and <= 128 } ||
            draft.PersonId.Any(char.IsControl) ||
            draft.OriginalName is not { Length: >= 3 and <= 200 } ||
            draft.OriginalName != draft.OriginalName.Trim() ||
            draft.OriginalName.Any(char.IsControl) ||
            draft.Revision < 1 || draft.OrganizationalStatus is not (2 or 3 or 4) ||
            draft.DestinationSiteId is not { Length: >= 1 and <= 128 } ||
            draft.DestinationSiteId.Any(char.IsControl) ||
            draft.DestinationSiteName is not { Length: >= 1 and <= 200 } ||
            draft.DestinationSiteName.Any(char.IsControl) ||
            draft.ServiceNumberA != CanonicalNumber(draft.ServiceNumberA))
            throw new InvalidDataException("Archived return identity or destination not valid.");
    }

    // Phase 8: only the original SQLCipher personnel separation archive owns
    // historical separation queries; every read rechecks exact manager nonce.
    public async Task<NativeSeparationArchivePage> ReadSeparationsAsync(
        NativeIdentity actor, NativeSeparationArchiveFilter filter,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (filter is null || filter.Page is < 1 or > 100000 ||
            filter.PageSize != 50 || filter.SearchTerm is null ||
            filter.SearchTerm.Length > 100 || filter.SearchTerm.Any(char.IsControl) ||
            filter.SiteId is null || filter.SiteId.Length > 128 ||
            filter.SiteId.Any(char.IsControl))
            throw new InvalidDataException("Invalid separation archive filter.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new PersonnelSeparationService(database)
            .ArchiveAsync(new JsonObject
            {
                ["page"] = filter.Page,
                ["pageSize"] = filter.PageSize,
                ["term"] = filter.SearchTerm,
                ["siteId"] = filter.SiteId
            }, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "PersonnelSeparationArchive");
        if (RequiredNumber(data, "page") != filter.Page ||
            RequiredNumber(data, "pageSize") != filter.PageSize)
            throw new InvalidDataException("Canonical separation archive page mismatch.");
        var items = RequiredArray(data, "items");
        if (items.Count > filter.PageSize)
            throw new InvalidDataException("Unbounded separation archive response.");
        var rows = items.Select(node =>
        {
            JsonObject entry = RequireObject(node);
            return new NativeSeparationArchiveRow(
                RequiredText(entry, "movementId"),
                RequiredText(entry, "personId"),
                RequiredText(entry, "name"),
                RequiredText(entry, "serviceNumberA"),
                RequiredText(entry, "siteName"),
                RequiredText(entry, "reason"),
                RequiredText(entry, "date"),
                RequiredText(entry, "committedAtUtc"),
                RequiredText(entry, "actorUserId"),
                RequiredText(entry, "actorDisplay"));
        }).ToArray();
        return new NativeSeparationArchivePage(filter,
            RequiredNumber(data, "total"),
            RequiredBoolean(data, "hasNext"), rows);
    }

    public async Task<NativeSeparationArchiveDetail> ReadSeparationDetailsAsync(
        NativeIdentity actor, string movementId,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (movementId is null || movementId.Length is < 3 or > 128 ||
            movementId.Any(char.IsControl))
            throw new InvalidDataException("Invalid separation movement id.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new PersonnelSeparationService(database)
            .DetailsAsync(new JsonObject { ["movementId"] = movementId }, ct)
            .ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "PersonnelSeparationDetails");
        if (RequiredText(data, "movementId") != movementId)
            throw new InvalidDataException("Canonical separation detail id changed.");
        return new NativeSeparationArchiveDetail(
            RequiredText(data, "movementId"),
            RequiredText(data, "personId"),
            RequiredText(data, "name"),
            RequiredText(data, "serviceNumberA"),
            RequiredText(data, "siteName"),
            RequiredText(data, "reason"),
            RequiredText(data, "date"),
            RequiredText(data, "committedAtUtc"),
            RequiredText(data, "actorUserId"));
    }

    // Phase 8: original canonical PersonnelSeparationService owns all
    // dismissal previews, five-second approval delays, transactions and audit.
    public async Task<NativeRosterRemovalPreview> PreviewRosterRemovalAsync(
        NativeIdentity actor,IReadOnlyList<NativeRosterRemovalMember> members,CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        if(!RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce(actor.Role))
            throw new UnauthorizedAccessException("Roster removal permission denied.");
        if(!NativeRosterRemovalEvidence.ValidSelection(members))
            throw new InvalidDataException("Invalid roster removal selection.");
        var selection=Array.AsReadOnly(members.ToArray());
        await VerifyPersonnelManagerAsync(actor,ct).ConfigureAwait(false);
        var principal=await ResolveSitesPrincipalAsync(actor,ct).ConfigureAwait(false);
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation);
        string today=LocalPersonDay();
        var ids=new JsonArray(selection.Select(x=>(JsonNode?)JsonValue.Create(x.PersonId)).ToArray());
        var response=await new AlTamam.Application.Roster.RosterAuthorizedCommands(database)
            .PreviewRemovalAsync(principal,new JsonObject{["personIds"]=ids,["effectiveLocalDate"]=today},lifetime.Token)
            .ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor,lifetime.Token).ConfigureAwait(false);
        lifetime.Token.ThrowIfCancellationRequested();
        var data=RequiredObject(response,"PreviewArchivePersons(native)");
        var candidates=RequiredArray(data,"candidates");
        var versions=RequiredArray(data,"expectedVersions");
        if(candidates.Count!=selection.Count || versions.Count!=selection.Count ||
           !RequiredBoolean(data,"reversible") || today!=LocalPersonDay())
            throw new InvalidDataException("Invalid canonical roster removal scope.");
        var rows=new List<NativeRosterRemovalCandidate>(selection.Count);
        for(int i=0;i<selection.Count;i++)
        {
            var row=RequireObject(candidates[i]);var version=RequireObject(versions[i]);
            string id=RequiredText(row,"personId");
            if(RequiredText(version,"personId")!=id || !row.ContainsKey("blocker"))
                throw new InvalidDataException("Missing exact removal evidence.");
            string? blocker=row["blocker"]?.GetValue<string>();
            var member=new NativeRosterRemovalMember(id,RequiredText(row,"displayName"),
                RequiredText(row,"serviceNumberA"),RequiredText(row,"currentSiteName"),RequiredNumber(version,"rowVersion"));
            rows.Add(new NativeRosterRemovalCandidate(member,checked((int)RequiredNumber(row,"organizationalStatus")),
                RequiredNumber(row,"attendanceDays"),blocker));
        }
        return NativeRosterRemovalEvidence.ValidateAndFreeze(selection,new NativeRosterRemovalPreview(
            RequiredText(data,"effectiveLocalDate"),RequiredText(data,"previewToken"),
            RequiredNumber(data,"baseRevision"),rows),today);
    }

    public async Task<NativePersonnelSeparationPreview> PreviewSeparationAsync(
        NativeIdentity actor, NativePersonnelSeparationDraft draft,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateSeparationDraft(draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await services.ResolveSessionAsync(actor.SessionNonce, ct)
            .ConfigureAwait(false) ??
            throw new UnauthorizedAccessException("Separation session expired.");
        if (principal.UserId != actor.UserId || principal.Username != actor.Username ||
            principal.Role != actor.Role || principal.SessionNonce != actor.SessionNonce)
            throw new UnauthorizedAccessException("Separation actor changed.");
        string today = LocalPersonDay();
        BridgeResponse response = await new PersonnelSeparationService(database)
            .PreviewAsync(new JsonObject
            {
                ["personId"] = draft.PersonId,
                ["reason"] = draft.Reason,
                ["businessDate"] = today
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "PersonnelSeparationPreview");
        string token = RequiredText(data, "previewToken");
        string assignment = RequiredText(data, "assignmentId");
        int minimumSeconds = checked((int)RequiredNumber(data, "minimumConfirmationSeconds"));
        if (RequiredText(data, "personId") != draft.PersonId ||
            RequiredText(data, "name") != draft.Name ||
            RequiredText(data, "serviceNumberA") != draft.ServiceNumberA ||
            RequiredNumber(data, "rowVersion") != draft.Revision ||
            RequiredText(data, "siteId") != draft.SourceSiteId ||
            RequiredText(data, "siteName") != draft.SourceSiteName ||
            RequiredText(data, "reason") != draft.Reason ||
            RequiredText(data, "businessDate") != today ||
            assignment.Length is < 3 or > 128 || token.Length != 64 ||
            token.Any(c => !char.IsAsciiHexDigit(c)) || minimumSeconds != 5)
            throw new InvalidDataException("Canonical separation preview changed.");
        return new NativePersonnelSeparationPreview(draft, today, token,
            assignment, minimumSeconds);
    }

    public async Task<NativePersonnelSeparationReceipt> CommitSeparationAsync(
        NativeIdentity actor, NativePersonnelSeparationPreview preview,
        string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (preview is null || !Guid.TryParseExact(operationId, "N", out _) ||
            preview.BusinessDate != LocalPersonDay() ||
            preview.PreviewToken.Length != 64 ||
            preview.PreviewToken.Any(c => !char.IsAsciiHexDigit(c)) ||
            preview.SourceAssignmentId.Length is < 3 or > 128 ||
            preview.MinimumConfirmationSeconds != 5)
            throw new InvalidDataException("Invalid separation approval.");
        ValidateSeparationDraft(preview.Draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await services.ResolveSessionAsync(actor.SessionNonce, ct)
            .ConfigureAwait(false) ??
            throw new UnauthorizedAccessException("Separation session expired.");
        if (principal.UserId != actor.UserId || principal.Username != actor.Username ||
            principal.Role != actor.Role || principal.SessionNonce != actor.SessionNonce)
            throw new UnauthorizedAccessException("Separation actor changed.");
        BridgeResponse response = await new PersonnelSeparationService(database)
            .CommitAsync(new JsonObject
            {
                ["previewToken"] = preview.PreviewToken,
                ["operationId"] = operationId
            }, principal, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "PersonnelSeparationCommit");
        string movementId = RequiredText(data, "movementId");
        if (RequiredText(data, "personId") != preview.Draft.PersonId ||
            RequiredText(data, "name") != preview.Draft.Name ||
            RequiredText(data, "serviceNumberA") != preview.Draft.ServiceNumberA ||
            RequiredText(data, "date") != preview.BusinessDate ||
            !RequiredBoolean(data, "separated") ||
            movementId.Length is < 5 or > 128 ||
            !movementId.StartsWith("MOV-", StringComparison.Ordinal))
            throw new InvalidDataException("Canonical separation receipt mismatch.");
        return new NativePersonnelSeparationReceipt(preview.Draft.PersonId,
            movementId, preview.BusinessDate, true);
    }

    private static void ValidateSeparationDraft(NativePersonnelSeparationDraft draft)
    {
        if (draft is null ||
            string.IsNullOrWhiteSpace(draft.PersonId) ||
            draft.PersonId.Length > 128 || draft.PersonId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(draft.Name) ||
            draft.Name.Length > 200 || draft.Name.Any(char.IsControl) ||
            draft.ServiceNumberA is null || draft.Revision < 1 ||
            string.IsNullOrWhiteSpace(draft.SourceSiteId) ||
            draft.SourceSiteId.Length > 128 || draft.SourceSiteId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(draft.SourceSiteName) ||
            draft.SourceSiteName.Length > 150 ||
            draft.Reason is null || draft.Reason.Length is < 5 or > 1000 ||
            draft.Reason != draft.Reason.Trim() || draft.Reason.Any(char.IsControl))
            throw new InvalidDataException("Invalid canonical separation intent.");
        _ = CanonicalNumber(draft.ServiceNumberA);
    }

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
            draft.DestinationSiteId is null ||
            draft.ExternalDestinationName is null ||
            (draft.IsExternal
                ? draft.ExternalDestinationName.Length is < 2 or > 150 ||
                  draft.ExternalDestinationName.Any(char.IsControl) ||
                  draft.DestinationSiteId.Length != 0
                : string.IsNullOrWhiteSpace(draft.DestinationSiteId) ||
                  draft.DestinationSiteId.Length > 128 ||
                  draft.DestinationSiteId.Any(char.IsControl) ||
                  draft.DestinationSiteId == draft.SourceSiteId ||
                  draft.ExternalDestinationName.Length != 0))
            throw new InvalidDataException("Invalid transfer intent.");
        _ = CanonicalNumber(draft.ServiceNumberA);
    }

    // External transfer's canonical commit also suspends linked account
    // credentials and revokes current sessions in the SAME SQLCipher transaction.
    // Reuse original PersonnelMovementService, NEVER mirror account-writes here.
    public async Task<NativeBulkExternalTransferPreview> PreviewExternalBatchAsync(
        NativeIdentity actor, NativeBulkExternalTransferDraft draft,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateBulkExternalDraft(draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);
        string today = LocalPersonDay();
        BridgeResponse response = await new PersonnelMovementService(database)
            .PreviewAsync(new JsonObject
            {
                ["businessDate"] = today,
                ["sourceSiteId"] = draft.SourceSiteId,
                ["destinationSiteId"] = null,
                ["destinationExternalName"] = draft.ExternalDestinationName,
                ["personIds"] = new JsonArray(draft.Persons.Select(p =>
                    (JsonNode?)JsonValue.Create(p.PersonId)).ToArray())
            }, principal, true, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "BulkExternalPreview(native)");
        string date = RequiredText(data, "businessDate");
        string name = RequiredText(data, "destinationName");
        string token = RequiredText(data, "previewToken");
        JsonArray candidates = RequiredArray(data, "persons");
        if (date != today ||
            RequiredText(data, "sourceSiteId") != draft.SourceSiteId ||
            !RequiredBoolean(data, "external") ||
            data["destinationSiteId"] is not null ||
            name != draft.ExternalDestinationName ||
            RequiredNumber(data, "count") != draft.Persons.Count ||
            candidates.Count != draft.Persons.Count ||
            token is not { Length: 64 } ||
            token.Any(ch => !char.IsAsciiHexDigit(ch)))
            throw new InvalidDataException("Canonical external preview mismatch.");
        var people = candidates.Select(node =>
        {
            JsonObject row = RequireObject(node);
            return new NativePersonnelMovementPerson(
                RequiredText(row, "personId"),
                RequiredText(row, "name"),
                RequiredText(row, "serviceNumberA"),
                RequiredNumber(row, "rowVersion"),
                RequiredText(row, "sourceSiteId"),
                RequiredText(row, "sourceSiteName"),
                RequiredText(row, "sourceAssignmentId"));
        }).ToArray();
        ValidateBulkExternalPersons(draft, people);
        return new NativeBulkExternalTransferPreview(
            draft, date, token, name, true, people);
    }

    public async Task<NativeBulkExternalTransferReceipt> CommitExternalBatchAsync(
        NativeIdentity actor, NativeBulkExternalTransferPreview preview,
        string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (preview is null || !preview.IsExternal ||
            !Guid.TryParseExact(operationId, "N", out _) ||
            preview.BusinessDate != LocalPersonDay() ||
            preview.PreviewToken is not { Length: 64 } ||
            preview.PreviewToken.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            preview.DestinationName != preview.Draft.ExternalDestinationName)
            throw new InvalidDataException("External movement approval expired or changed.");
        ValidateBulkExternalDraft(preview.Draft);
        ValidateBulkExternalPersons(preview.Draft, preview.Persons);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);
        // Exactly ONE canonical transaction for 1–100 people, including
        // immutable movements, linked account suspension and session revocation.
        BridgeResponse response = await new PersonnelMovementService(database)
            .CommitAsync(new JsonObject
            {
                ["previewToken"] = preview.PreviewToken,
                ["operationId"] = operationId
            }, principal, true, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "BulkExternalCommit(native)");
        long count = RequiredNumber(data, "count");
        string date = RequiredText(data, "businessDate");
        string name = RequiredText(data, "destinationName");
        if (!RequiredBoolean(data, "external") ||
            count != preview.Persons.Count ||
            date != preview.BusinessDate ||
            name != preview.DestinationName)
            throw new InvalidDataException("Original external transfer receipt mismatch.");
        return new NativeBulkExternalTransferReceipt(date,
            checked((int)count), name, true);
    }

    private static void ValidateBulkExternalDraft(
        NativeBulkExternalTransferDraft draft)
    {
        if (draft is null ||
            draft.SourceSiteId is not { Length: >= 1 and <= 128 } ||
            draft.SourceSiteId.Any(char.IsControl) ||
            draft.SourceSiteName is not { Length: >= 1 and <= 200 } ||
            draft.SourceSiteName.Any(char.IsControl) ||
            draft.ExternalDestinationName is not { Length: >= 2 and <= 150 } ||
            draft.ExternalDestinationName != draft.ExternalDestinationName.Trim() ||
            draft.ExternalDestinationName.Any(char.IsControl) ||
            draft.Persons is null ||
            draft.Persons.Count is < 1 or > 100 ||
            draft.Persons.Any(p => p is null ||
                p.PersonId is not { Length: >= 1 and <= 128 } ||
                p.PersonId.Any(char.IsControl) ||
                p.Name is not { Length: >= 2 and <= 200 } ||
                p.ServiceNumberA is not { Length: >= 4 and <= 70 } ||
                p.Revision < 1) ||
            draft.Persons.Select(p => p.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != draft.Persons.Count)
            throw new InvalidDataException("Invalid external 1–100 selected persons.");
        foreach (var person in draft.Persons)
            if (CanonicalNumber(person.ServiceNumberA) != person.ServiceNumberA)
                throw new InvalidDataException("Noncanonical external exit number.");
    }

    private static void ValidateBulkExternalPersons(
        NativeBulkExternalTransferDraft draft,
        IReadOnlyList<NativePersonnelMovementPerson> actual)
    {
        if (actual is null || actual.Count != draft.Persons.Count)
            throw new InvalidDataException("Missing external batch member.");
        for (int i = 0; i < draft.Persons.Count; i++)
        {
            var expected = draft.Persons[i];
            var candidate = actual[i];
            if (candidate is null ||
                candidate.PersonId != expected.PersonId ||
                candidate.Name != expected.Name ||
                candidate.ServiceNumberA != expected.ServiceNumberA ||
                candidate.Revision != expected.Revision ||
                candidate.SourceSiteId != draft.SourceSiteId ||
                candidate.SourceSiteName != draft.SourceSiteName ||
                candidate.SourceAssignmentId is not { Length: >= 3 and <= 128 })
                throw new InvalidDataException("Stale or forged external exit identity.");
        }
    }

    // Phase 8: one ORIGINAL transactional multi-person internal transfer.
    // The same canonical service is used for 1 and up to 100 persons.
    public async Task<NativeBulkInternalTransferPreview> PreviewInternalBatchAsync(
        NativeIdentity actor, NativeBulkInternalTransferDraft draft,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        ValidateBulkInternalDraft(draft);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);
        string today = LocalPersonDay();
        BridgeResponse response = await new PersonnelMovementService(database)
            .PreviewAsync(new JsonObject
            {
                ["businessDate"] = today,
                ["sourceSiteId"] = draft.SourceSiteId,
                ["destinationSiteId"] = draft.DestinationSiteId,
                ["destinationExternalName"] = null,
                ["personIds"] = new JsonArray(draft.Persons.Select(p =>
                    (JsonNode?)JsonValue.Create(p.PersonId)).ToArray())
            }, principal, false, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "BulkInternalTransferPreview(native)");
        string businessDay = RequiredText(data, "businessDate");
        string destinationName = RequiredText(data, "destinationName");
        string token = RequiredText(data, "previewToken");
        var items = RequiredArray(data, "persons");
        if (businessDay != today ||
            RequiredText(data, "sourceSiteId") != draft.SourceSiteId ||
            RequiredText(data, "destinationSiteId") != draft.DestinationSiteId ||
            RequiredBoolean(data, "external") ||
            RequiredNumber(data, "count") != draft.Persons.Count ||
            items.Count != draft.Persons.Count ||
            string.IsNullOrWhiteSpace(destinationName) ||
            token is not { Length: 64 } ||
            token.Any(ch => !char.IsAsciiHexDigit(ch)))
            throw new InvalidDataException("Bulk internal preview head mismatch.");

        var people = items.Select(node =>
        {
            JsonObject x = RequireObject(node);
            return new NativePersonnelMovementPerson(
                RequiredText(x, "personId"),
                RequiredText(x, "name"),
                RequiredText(x, "serviceNumberA"),
                RequiredNumber(x, "rowVersion"),
                RequiredText(x, "sourceSiteId"),
                RequiredText(x, "sourceSiteName"),
                RequiredText(x, "sourceAssignmentId"));
        }).ToArray();
        ValidateBulkCanonicalPersons(draft, people);
        return new NativeBulkInternalTransferPreview(
            draft, businessDay, token, destinationName, people);
    }

    public async Task<NativeBulkInternalTransferReceipt> CommitInternalBatchAsync(
        NativeIdentity actor, NativeBulkInternalTransferPreview preview,
        string operationId, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (preview is null || !Guid.TryParseExact(operationId, "N", out _) ||
            preview.BusinessDate != LocalPersonDay() ||
            preview.PreviewToken is not { Length: 64 } ||
            preview.PreviewToken.Any(ch => !char.IsAsciiHexDigit(ch)) ||
            string.IsNullOrWhiteSpace(preview.DestinationName))
            throw new InvalidDataException("Bulk internal approval token expired.");
        ValidateBulkInternalDraft(preview.Draft);
        ValidateBulkCanonicalPersons(preview.Draft, preview.Persons);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        var principal = await ResolveSitesPrincipalAsync(actor, ct)
            .ConfigureAwait(false);
        // Exactly ONE atomic original batch commit. No per-person SQL,
        // automatic retry, or alternate/nonexistent original service.
        BridgeResponse response = await new PersonnelMovementService(database)
            .CommitAsync(new JsonObject
            {
                ["previewToken"] = preview.PreviewToken,
                ["operationId"] = operationId
            }, principal, false, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject result = RequiredObject(response, "BulkInternalTransferCommit(native)");
        int count = checked((int)RequiredNumber(result, "count"));
        string businessDate = RequiredText(result, "businessDate");
        string destination = RequiredText(result, "destinationName");
        if (RequiredBoolean(result, "external") ||
            count != preview.Draft.Persons.Count ||
            businessDate != preview.BusinessDate ||
            destination != preview.DestinationName)
            throw new InvalidDataException("Original batch commit receipt mismatch.");
        return new NativeBulkInternalTransferReceipt(
            businessDate, count, destination);
    }

    private static void ValidateBulkInternalDraft(
        NativeBulkInternalTransferDraft draft)
    {
        if (draft is null ||
            draft.SourceSiteId is not { Length: >= 1 and <= 128 } ||
            draft.DestinationSiteId is not { Length: >= 1 and <= 128 } ||
            draft.SourceSiteId == draft.DestinationSiteId ||
            draft.SourceSiteId.Any(char.IsControl) ||
            draft.DestinationSiteId.Any(char.IsControl) ||
            draft.SourceSiteName is not { Length: >= 1 and <= 200 } ||
            draft.SourceSiteName.Any(char.IsControl) ||
            draft.Persons is null ||
            draft.Persons.Count is < 1 or > 100 ||
            draft.Persons.Any(x => x is null ||
                x.PersonId is not { Length: >= 1 and <= 128 } ||
                x.PersonId.Any(char.IsControl) ||
                x.Name is not { Length: >= 2 and <= 200 } ||
                x.ServiceNumberA is not { Length: >= 4 and <= 70 } ||
                x.Revision < 1) ||
            draft.Persons.Select(x => x.PersonId)
                .Distinct(StringComparer.Ordinal).Count() != draft.Persons.Count)
            throw new InvalidDataException("Invalid selected current page batch.");
        foreach (var person in draft.Persons)
            if (CanonicalNumber(person.ServiceNumberA) != person.ServiceNumberA)
                throw new InvalidDataException("Uncanonical service number in batch.");
    }

    private static void ValidateBulkCanonicalPersons(
        NativeBulkInternalTransferDraft draft,
        IReadOnlyList<NativePersonnelMovementPerson> persons)
    {
        if (persons is null || persons.Count != draft.Persons.Count)
            throw new InvalidDataException("Incomplete bulk PersonId list.");
        for (int i = 0; i < draft.Persons.Count; i++)
        {
            var original = draft.Persons[i];
            var actual = persons[i];
            if (actual is null ||
                actual.PersonId != original.PersonId ||
                actual.Name != original.Name ||
                actual.ServiceNumberA != original.ServiceNumberA ||
                actual.Revision != original.Revision ||
                actual.SourceSiteId != draft.SourceSiteId ||
                actual.SourceSiteName != draft.SourceSiteName ||
                actual.SourceAssignmentId is not { Length: >= 3 and <= 128 })
                throw new InvalidDataException("Person identity or assignment changed.");
        }
    }

    public async Task<NativePersonnelMovementPreview> PreviewMovementAsync(
        NativeIdentity actor, NativePersonnelMovementDraft draft,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
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
        using var nativeOperation = operations.Enter();
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
        using var nativeOperation = operations.Enter();
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
        using var nativeOperation = operations.Enter();
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
        using var nativeOperation = operations.Enter();
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
        using var nativeOperation = operations.Enter();
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
        using var nativeOperation = operations.Enter();
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

    /// <summary>
    /// Historical personnel movements remain immutable. The original SQLCipher
    /// movement service owns both archive queries and historical detail reads.
    /// Every read is bounded by a fresh exact manager/nonce authorization.
    /// </summary>
    public async Task<NativeMovementArchivePage> ReadMovementsAsync(
        NativeIdentity actor, NativeMovementArchiveFilter filter,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (filter is null || filter.Page is < 1 or > 100000 ||
            filter.PageSize != 50 || filter.SearchTerm is null ||
            filter.SearchTerm.Length > 100 || filter.SearchTerm.Any(char.IsControl) ||
            filter.Type is not ("all" or "internal" or "external" or "return"))
            throw new InvalidDataException("Invalid native movement archive filter.");

        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new PersonnelMovementService(database)
            .ArchiveAsync(new JsonObject
            {
                ["page"] = filter.Page,
                ["pageSize"] = filter.PageSize,
                ["term"] = filter.SearchTerm,
                ["type"] = filter.Type
            }, ct).ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);

        JsonObject data = RequiredObject(response, "PersonnelMovementArchive");
        if (RequiredNumber(data, "page") != filter.Page ||
            RequiredNumber(data, "pageSize") != filter.PageSize)
            throw new InvalidDataException("Canonical movement archive page mismatch.");
        var items = RequiredArray(data, "items");
        if (items.Count > filter.PageSize)
            throw new InvalidDataException("Unbounded movement archive response.");
        var rows = items.Select(node =>
        {
            JsonObject entry = RequireObject(node);
            return new NativeMovementArchiveRow(
                RequiredText(entry, "movementId"),
                RequiredText(entry, "personId"),
                RequiredText(entry, "name"),
                RequiredText(entry, "serviceNumberA"),
                RequiredText(entry, "type"),
                RequiredText(entry, "sourceSite"),
                RequiredText(entry, "destination"),
                RequiredText(entry, "date"),
                RequiredText(entry, "committedAtUtc"));
        }).ToArray();
        return new NativeMovementArchivePage(filter,
            RequiredNumber(data, "total"),
            RequiredBoolean(data, "hasNext"), rows);
    }

    public async Task<NativeMovementArchiveDetail> ReadMovementDetailsAsync(
        NativeIdentity actor, string movementId,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        if (movementId is null || movementId.Length is < 3 or > 128 ||
            movementId.Any(char.IsControl))
            throw new InvalidDataException("Invalid movement id.");
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        BridgeResponse response = await new PersonnelMovementService(database)
            .DetailsAsync(new JsonObject { ["movementId"] = movementId }, ct)
            .ConfigureAwait(false);
        await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false);
        JsonObject data = RequiredObject(response, "PersonnelMovementDetails");
        if (RequiredText(data, "movementId") != movementId)
            throw new InvalidDataException("Canonical movement detail id changed.");
        return new NativeMovementArchiveDetail(
            RequiredText(data, "movementId"),
            RequiredText(data, "personId"),
            RequiredText(data, "name"),
            RequiredText(data, "serviceNumberA"),
            RequiredText(data, "type"),
            RequiredText(data, "sourceSite"),
            RequiredText(data, "destination"),
            RequiredText(data, "date"),
            RequiredText(data, "committedAtUtc"),
            RequiredText(data, "actorUsername"));
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
        using var nativeOperation = operations.Enter();
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
        using var nativeOperation = operations.Enter();
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
        bool historical = filter.ExplicitArchive || monthStart.Year != DateTime.Now.Year ||
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
                    info["color"]?.GetValue<string>(), movement)
                {
                    MovementKind = info["movementKind"]?.GetValue<string>() ?? ""
                };
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
    Task<IReadOnlyList<NativeAttendanceStatusChoice>> INativeClosedDayCorrectionBackend.ReadCorrectionChoicesAsync(
        NativeIdentity actor, CancellationToken ct) =>
        operations.RunAsync(() => ((INativeClosedDayCorrectionBackend)_review).ReadCorrectionChoicesAsync(actor, ct));

    Task<NativeClosedDayCorrectionPreview> INativeClosedDayCorrectionBackend.PreviewAsync(
        NativeIdentity actor, NativeClosedDayCorrectionRequest request, CancellationToken ct) =>
        operations.RunAsync(() => ((INativeClosedDayCorrectionBackend)_review).PreviewAsync(actor, request, ct));

    Task<NativeClosedDayCorrectionReceipt> INativeClosedDayCorrectionBackend.CommitAsync(
        NativeIdentity actor, string token, string operationId, CancellationToken ct) =>
        operations.RunAsync(() => ((INativeClosedDayCorrectionBackend)_review).CommitAsync(actor, token, operationId, ct));

    Task<NativeHistoricalRangePreview> INativeHistoricalRangeBackend.PreviewRangeAsync(
        NativeIdentity actor, NativeHistoricalRangeRequest request, CancellationToken ct) =>
        operations.RunAsync(() => _review.PreviewRangeAsync(actor, request, ct));

    Task<NativeHistoricalRangeReceipt> INativeHistoricalRangeBackend.CommitRangeAsync(
        NativeIdentity actor, NativeHistoricalRangePreview approved, string operationId, CancellationToken ct) =>
        operations.RunAsync(() => _review.CommitRangeAsync(actor, approved, operationId, ct));

    Task<NativeAdministrativeCatchUpPreview> INativeAdministrativeCatchUpBackend.PreviewCatchUpAsync(
        NativeIdentity actor, NativeAdministrativeCatchUpRequest request, CancellationToken ct) =>
        operations.RunAsync(() => _review.PreviewCatchUpAsync(actor,request,ct));

    Task<NativeAdministrativeCatchUpReceipt> INativeAdministrativeCatchUpBackend.CommitCatchUpAsync(
        NativeIdentity actor, NativeAdministrativeCatchUpPreview approved, string operationId, CancellationToken ct) =>
        operations.RunAsync(() => _review.CommitCatchUpAsync(actor,approved,operationId,ct));

    Task<NativeClosedDayPolicyReceipt> INativeClosedDayPolicyBackend.SetClosedDayPolicyAsync(
        NativeIdentity actor, NativeClosedDayPolicyRequest approved, string operationId, CancellationToken ct) =>
        operations.RunAsync(() => _review.SetClosedDayPolicyAsync(actor,approved,operationId,ct));

    public async Task<NativeMonthlyCellDetails> ReadCellDetailsAsync(
        NativeIdentity actor, NativeMonthlyCellRequest request,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
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
        NativeMonthlyExcelFilter filter, int year, int month)
    {
        var payload=new JsonObject
        {
            ["year"] = year, ["month"] = month,
            ["siteId"] = filter.SiteId, ["term"] = filter.SearchTerm
        };
        if(filter.OutputDirectory is not null && filter.OutputFileName is not null)
        {
            payload["directory"]=filter.OutputDirectory;
            payload["fileName"]=filter.OutputFileName;
        }
        return payload;
    }

    public async Task<NativeMonthlyExcelPreview> PreviewExcelAsync(
        NativeIdentity actor, NativeMonthlyExcelFilter filter,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
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
        string configured=await new OutputDirectorySettings(database).GetAsync(ct).ConfigureAwait(false);
        string directory=OutputDirectorySettings.IsSafeDirectory(configured,database.DatabasePath,out var safe)?safe:"";
        await VerifyContinuationIdentityAsync(actor,ct).ConfigureAwait(false);
        return new NativeMonthlyExcelPreview(
            filter.Period, filter.SiteId, filter.SearchTerm,
            RequiredText(data, "siteName"), name, total,
            DateTime.DaysInMonth(year, month)){OutputDirectory=directory};
    }

    public async Task<NativeMonthlyExcelReceipt> ExportExcelAsync(
        NativeIdentity actor, NativeMonthlyExcelFilter filter,
        CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
        return await ExportMonthlyExcelCoreAsync(actor,filter,(_,_)=>{},ct).ConfigureAwait(false);
    }

    public async Task<NativeMonthlyExcelReceipt> ExportWithProgressAsync(
        NativeIdentity actor,NativeMonthlyExcelFilter filter,Action<int,int> progress,
        CancellationToken ct=default)
    {
        using var nativeOperation = operations.Enter();
        return await ExportMonthlyExcelCoreAsync(actor,filter,progress,ct).ConfigureAwait(false);
    }

    // Both public entrypoints admit exactly one operation and keep that
    // lease across the entire private shared export, including receipt checks.
    private async Task<NativeMonthlyExcelReceipt> ExportMonthlyExcelCoreAsync(
        NativeIdentity actor,NativeMonthlyExcelFilter filter,Action<int,int> progress,
        CancellationToken ct)
    {
        var (year, month) = ValidateMonthlyExcelFilter(filter);
        await VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false);
        var principal=await services.ResolveSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);
        if(principal is null || principal.UserId!=actor.UserId || principal.Username!=actor.Username ||
            principal.Role!=actor.Role || principal.SessionNonce!=actor.SessionNonce)
            throw new UnauthorizedAccessException("Monthly export session changed.");
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation);
        if(filter.OutputDirectory is not null || filter.OutputFileName is not null)
        {
            if(!OutputDirectorySettings.IsSafeDirectory(filter.OutputDirectory,database.DatabasePath,out var folder) ||
               !MonthlyExcelOutputNamePolicy.TryNormalize(filter.OutputFileName,out var name) ||
               name.Length==0 || filter.OutputFileName!=name)
                throw new InvalidDataException("Unsafe monthly Excel destination.");
            filter=filter with {OutputDirectory=folder,OutputFileName=name};
        }
        BridgeResponse response = await new MonthlyExcelExportService(database)
            .ExportAsync(MonthlyExcelPayload(filter, year, month), lifetime.Token, progress)
            .ConfigureAwait(false);
        // An explicit stop may race a completed atomic publication. Only the
        // live session, not the stop token, determines whether its receipt
        // can be returned. The service itself owns actual cancellation.
        await VerifyContinuationIdentityAsync(actor, principal.SessionRevocation).ConfigureAwait(false);
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
        if(filter.OutputDirectory is { } destination && filter.OutputFileName is { } fileName &&
           !string.Equals(Path.GetFullPath(filePath),Path.GetFullPath(Path.Combine(destination,fileName)),
               StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Monthly Excel receipt does not match reviewed destination.");
        var files=new List<NativeMonthlyExcelFile>();
        if(filter.SiteId.Length==0)
        {
            var rows=RequiredArray(data,"files");
            if(rows.Count!=fileCount || rows.Count>10001)
                throw new InvalidDataException("Incomplete monthly workbook set.");
            foreach(var item in rows)
            {
                var row=RequireObject(item);
                files.Add(new NativeMonthlyExcelFile(RequiredText(row,"siteId"),RequiredText(row,"siteName"),
                    RequiredText(row,"filePath"),RequiredNumber(row,"personCount")));
            }
        }
        else
            files.Add(new NativeMonthlyExcelFile(filter.SiteId,RequiredText(data,"siteName"),filePath,total));
        var receipt=new NativeMonthlyExcelReceipt(filter.Period,filter.SiteId,filter.SearchTerm,
            filePath,fileCount,total){Files=files};
        var verified=NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(receipt,filter);
        // Only paths returned by the canonical writer are checked. No directory
        // scanning, file opening, or inferred success from an existing filename.
        foreach(var file in verified)
        {
            var info=new FileInfo(file.FilePath);
            if(!info.Exists || info.Length<=0 || (info.Attributes & FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("Published monthly workbook is missing or invalid.");
        }
        await VerifyContinuationIdentityAsync(actor,principal.SessionRevocation).ConfigureAwait(false);
        return receipt with{Files=verified};
    }

    public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(
        NativeIdentity actor, CancellationToken ct = default) =>
        operations.RunAsync(() => _review.ReadChoicesAsync(actor, ct));

    public Task<NativeAttendanceRevisionPreview> PreviewAsync(
        NativeIdentity actor, DateOnly date, string site,
        IReadOnlyList<string> people, string statusId, CancellationToken ct = default,
        NativeAttendanceDateRange? range = null) =>
        operations.RunAsync(() => _review.PreviewAsync(actor, date, site, people, statusId, ct, range));

    public Task<NativeAttendanceCommitReceipt> CommitAsync(
        NativeIdentity actor, string previewToken, string operationId,
        CancellationToken ct = default) =>
        operations.RunAsync(() => _review.CommitAsync(actor, previewToken, operationId, ct));

    public Task<NativeAttendanceRevisionPreview> PreviewInitialAsync(
        NativeIdentity actor, DateOnly date, string site, IReadOnlyList<string> people,
        string statusId, CancellationToken ct = default, NativeAttendanceDateRange? range = null) =>
        operations.RunAsync(() => _review.PreviewInitialAsync(actor, date, site, people, statusId, ct, range));

    public Task<NativeAttendanceCommitReceipt> CommitInitialAsync(
        NativeIdentity actor, string previewToken, string operationId, CancellationToken ct = default) =>
        operations.RunAsync(() => _review.CommitInitialAsync(actor, previewToken, operationId, ct));

    public async Task<NativeDashboardSnapshot> ReadAsync(
        NativeIdentity identity, NativeDashboardFilter filters, CancellationToken ct = default)
    {
        using var nativeOperation = operations.Enter();
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
            bool transferDay = RequiredBoolean(item, "transferDay");
            if (!item.ContainsKey("statusId"))
                throw new InvalidDataException("Missing canonical attendance status identity.");
            return new NativeDashboardRow(
                RequiredText(item, "personId"), RequiredText(item, "serviceNumberA"),
                RequiredText(item, "name"), RequiredText(item, "siteName"),
                RequiredText(item, "statusName"),
                item["statusColor"]?.GetValue<string>(), cells)
            {
                RowNumber = checked((filters.Page - 1) * filters.PageSize + index + 1),
                IsUnprepared = !transferDay && item["statusId"] is null,
                IsTransferDay = transferDay
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
        using var nativeOperation = operations.Enter();
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
        using var nativeOperation = operations.Enter();
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
