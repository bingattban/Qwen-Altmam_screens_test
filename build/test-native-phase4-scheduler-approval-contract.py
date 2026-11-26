#!/usr/bin/env python3
"""Phase 4 native WPF scheduler + attendance review safety contract.

Fail-closed source gate executed identically in Windows Core and Release,
in addition to real xUnit and live SQLCipher/WebView2/full-package CI tests.
This is not a replacement for native interactive acceptance on clean Windows.
"""
import re
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
FILES={
 "project":"src/AlTamam.NativeDesktop/AlTamam.NativeDesktop.csproj",
 "canonical":"src/AlTamam.DesktopHost/Scheduling/EightPmSchedulerService.cs",
 "backup_canonical":"src/AlTamam.DesktopHost/Scheduling/AutomaticBackupSchedulerService.cs",
 "backup_adapter":"src/AlTamam.NativeDesktop/Scheduling/NativeDurableBackupScheduler.cs",
 "backup_tests":"tests/AlTamam.Tests/Bridge/Phase12BackupDiskRecoveryTests.cs",
 "backup_test_project":"tests/AlTamam.Tests/AlTamam.Tests.csproj",
 "attendance":"src/AlTamam.Application/Scheduling/AutoAttendanceService.cs",
 "host":"src/AlTamam.NativeDesktop/MainWindow.xaml.cs",
 "backend":"src/AlTamam.NativeDesktop/Auth/NativeSqlCipherAuthenticationBackend.cs",
 "scheduler":"src/AlTamam.NativeDesktop/Scheduling/NativeDurableAttendanceScheduler.cs",
 "gate":"src/AlTamam.NativePresentation/NativeAutomaticSchedulerGate.cs",
 "gate_tests":"tests/AlTamam.UnitTests/NativeAutomaticSchedulerPhase4Tests.cs",
 "operation_gate":"src/AlTamam.NativePresentation/NativeOperationDrainGate.cs",
 "operation_tests":"tests/AlTamam.UnitTests/NativeOperationDrainPhase12Tests.cs",
 "restore_review":"src/AlTamam.NativePresentation/NativeBackupInspectionReview.cs",
 "restore_view":"src/AlTamam.NativeDesktop/Views/NativeManualBackupView.xaml",
 "restore_code":"src/AlTamam.NativeDesktop/Views/NativeManualBackupView.xaml.cs",
 "restore_tests":"tests/AlTamam.UnitTests/NativeBackupInspectionPhase12Tests.cs",
 "restore_integration":"tests/AlTamam.Tests/Bridge/NativeRestoreActivationCoordinatorTests.cs",
 "contracts":"src/AlTamam.NativePresentation/NativeAttendanceRevisionContracts.cs",
 "review_vm":"src/AlTamam.NativePresentation/NativeAttendanceReviewViewModel.cs",
 "review_backend":"src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherAttendanceReviewBackend.cs",
 "review_tests":"tests/AlTamam.UnitTests/NativeAttendanceReviewPhase4Tests.cs",
 "closed_vm":"src/AlTamam.NativePresentation/NativeClosedDayCorrectionReview.cs",
 "closed_tests":"tests/AlTamam.UnitTests/NativeClosedDayCorrectionPhase10Tests.cs",
 "closed_service":"src/AlTamam.Application/Bridge/ClosedDayCorrectionService.cs",
 "range_contract":"src/AlTamam.NativePresentation/NativeHistoricalRangeContracts.cs",
 "range_tests":"tests/AlTamam.UnitTests/NativeHistoricalRangePhase10Tests.cs",
 "closed_integration":"tests/AlTamam.Tests/Bridge/ClosedDayCorrectionIntegrationTests.cs",
 "reader":"src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
 "register_vm":"src/AlTamam.NativePresentation/NativeAutoPreparedRegisterViewModel.cs",
 "register_xaml":"src/AlTamam.NativeDesktop/Views/NativeAutoPreparedRegisterView.xaml",
 "review_xaml":"src/AlTamam.NativeDesktop/Views/NativeAttendanceReviewView.xaml",
 "review_code":"src/AlTamam.NativeDesktop/Views/NativeAttendanceReviewView.xaml.cs",
 "register_code":"src/AlTamam.NativeDesktop/Views/NativeAutoPreparedRegisterView.xaml.cs",
 "manifest":"src/AlTamam.Application/Bridge/ActionManifest.cs",
 "commit":"src/AlTamam.Application/Bridge/AttendanceCommitService.cs",
 "release":".github/workflows/windows-release.yml",
}
TEXT={k:(ROOT / p).read_text(encoding="utf-8") for k,p in FILES.items()}

def problems(contents):
    errors=[]
    def need(key, token):
        if token not in contents[key]:
            errors.append(f"Missing {key} invariant: {token}")
    for key in ("register_xaml","review_xaml","project"):
        try: ET.fromstring(contents[key])
        except ET.ParseError as ex: errors.append(f"Invalid XML/XAML in {key}: {ex}")
    need("project",'Compile Include="../AlTamam.DesktopHost/Scheduling/EightPmSchedulerService.cs"')
    for token in ("RunPendingAsync(ct)", "GetDueTimeAsync(ct)",
                  "await _attendance.RunPendingAsync(ct)", "public async ValueTask DisposeAsync()"):
        need("canonical",token)
    need("project",'Compile Include="../AlTamam.DesktopHost/Scheduling/AutomaticBackupSchedulerService.cs"')
    need("backup_test_project",'Compile Include="../../src/AlTamam.DesktopHost/Scheduling/AutomaticBackupSchedulerService.cs"')
    for token in ("_service.RunIfDueAsync(ct)", "_service.RecordFailureAsync", "await _loop.ConfigureAwait(false)"):
        need("backup_canonical", token)
    for token in ("new AutomaticBackupSchedulerService(", "new AutomaticBackupService(database)",
                  "_original.Start()", "_original.DisposeAsync()"):
        need("backup_adapter", token)
    for token in ("new NativeDurableBackupScheduler(database)", "_autoBackup.StartOnce()",
                  "Task backupDrain = _autoBackup.DisposeAsync().AsTask();",
                  "await backupDrain.ConfigureAwait(false);"):
        need("backend", token)
    shutdown = contents["backend"].split("private async Task ShutdownCoreAsync()", 1)
    if len(shutdown) != 2:
        errors.append("Missing native shutdown core")
    else:
        stop = shutdown[1]
        tokens = ("Task backupDrain = _autoBackup.DisposeAsync().AsTask();",
                  "await _autoAttendance.DisposeAsync()", "await backupDrain.ConfigureAwait(false);",
                  "_database.Dispose();")
        indices = [stop.find(token) for token in tokens]
        if min(indices) < 0 or indices != sorted(indices):
            errors.append("Both scheduler drains must complete before SQLCipher disposal")
    need("host", "_backend.StartAutomaticBackupScheduler();")
    for token in ("ObjectDisposedException.ThrowIf(_stopping,this)", "_stopping=true",
                  "if(_stopping && _active==0) _drained.TrySetResult()", "Interlocked.Exchange(ref _gate,null)"):
        need("operation_gate", token)
    for token in ("Drain_waits_for_all_existing_operations_and_denies_new_ones",
                  "Concurrent_stop_callers_share_completion",
                  "Async_wrapper_keeps_lease_until_completion_and_releases_on_failure",
                  "Idle_gate_stops_immediately_and_cancellation_does_not_leak"):
        need("operation_tests", token)
    for token in ("new NativeSqlCipherDashboardReader(_services, _database, _operations)",
                  "Task nativeDrain = _operations.StopAndDrainAsync();",
                  "await nativeDrain.ConfigureAwait(false);",
                  "_operations.RunAsync(() => _services.IsProvisionedAsync(ct))",
                  "_operations.RunAsync(() => _services.RefreshSessionIdleDeadlineAsync(nonce, ct))",
                  "_operations.RunAsync(() => _services.RevokeSessionAsync(nonce, ct))"):
        need("backend", token)
    if contents["backend"].count("using var nativeOperation = _operations.Enter();") != 2:
        errors.append("Authentication and session resolution must be admitted")
    if len(shutdown) == 2:
        stop = shutdown[1]
        tokens = ("Task nativeDrain = _operations.StopAndDrainAsync();",
                  "Task backupDrain = _autoBackup.DisposeAsync().AsTask();",
                  "await nativeDrain.ConfigureAwait(false);", "_database.Dispose();")
        indices = [stop.find(token) for token in tokens]
        if min(indices) < 0 or indices != sorted(indices):
            errors.append("All admitted native requests must drain before DB disposal")
    reader = contents["reader"]
    async_entries = re.findall(r"^    public async Task[^\{]+?\n    \{([^\n]*\n[^\n]*)", reader, re.M)
    forwarded = re.findall(r"^    (?:public )?Task[^\n]*[\s\S]*?=>\s*([^;]+);", reader, re.M)
    if len(async_entries) < 62 or any("using var nativeOperation = operations.Enter();" not in e for e in async_entries):
        errors.append("Every async native reader entrypoint requires an operation lease")
    if len(forwarded) < 16 or any(not e.startswith("operations.RunAsync(") for e in forwarded):
        errors.append("Every native task forwarder requires an operation lease")
    need("host", "IsEnabled = false;")
    need("host", "if (!_mayClose && !drainStarted)")
    need("project",'Compile Include="../AlTamam.DesktopHost/Bridge/NativeRestoreConfirmationDialog.cs"')
    for token in ("_actor?.Role==UserRole.GeneralAdministration", "comparison.Matches(evidence)",
                  "ReferenceEquals(Evidence,intent.Evidence)", "ReferenceEquals(Comparison,intent.Comparison)"):
        need("restore_review",token)
    need("restore_view",'Click="OnRestoreClick"')
    need("restore_view","Binding Inspection.CanRequestRestore")
    need("restore_code","await host.RequestRestoreAsync(settings)")
    for token in ("NativeRestoreConfirmationDialog.RequestFreshPassword(", "NativeRestoreConfirmationDialog.ConfirmFinal(this,prepared)",
                  "settings.Inspection.MatchesRestoreIntent(intent)", "private volatile bool _restoreQuiescing;",
                  "_restoreQuiescing=true;", "DataContext=null;", "_restoreDrainCompleted=true;",
                  "if(!_restoreQuiescing && !_mayClose)", "if(_restoreQuiescing && _restoreDrainCompleted)"):
        need("host",token)
    for token in ("new NativeRestoreActivationCoordinator(_database,_services)",
                  "intent.Evidence.Sha256,key,finalApproval", "principal.SessionNonce!=actor.SessionNonce",
                  "await enteringShutdown().ConfigureAwait(false);",
                  "await _operations.StopAndDrainAsync().ConfigureAwait(false);",
                  "await _services.RevokeSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);",
                  "await DisposeAsync().ConfigureAwait(false);", "await drainCompleted().ConfigureAwait(false);"):
        need("backend",token)
    restore=contents["backend"].split("internal async Task<NativeRestoreActivationCoordinator.Result?> TryRestoreAsync(",1)
    if len(restore)!=2:
        errors.append("Missing physical restore adapter")
    else:
        adapter=restore[1].split("public ValueTask DisposeAsync()",1)[0]
        tokens=("await enteringShutdown()", "await _operations.StopAndDrainAsync()", "await _services.RevokeSessionAsync(actor.SessionNonce,ct)", "await DisposeAsync()", "await drainCompleted()")
        indices=[adapter.find(token) for token in tokens]
        if min(indices)<0 or indices!=sorted(indices) or "using var nativeOperation" in adapter:
            errors.append("Restore must drain outside its own operation lease and revoke before disposal")
    for token in ("Restore_intent_requires_live_general_admin_and_exact_completed_comparison",
                  "Ordinary_admin_can_preview_but_cannot_request_native_activation",
                  "Reinspection_invalidates_previous_restore_intent"):
        need("restore_tests",token)
    need("restore_integration","NativeAdmissionDrain_PrecedesReplacement_AndRestartRevokesRestoredSessions")
    need("gate", "_stopTask ??= StopCoreAsync()")
    need("gate_tests", "Concurrent_shutdown_callers_share_the_same_drain")
    need("gate_tests", "Failed_drain_never_reports_success_to_a_later_disposal")
    need("backup_tests", "OriginalHostScheduler_StartsDueBackupAndDrainsBeforeDatabaseDisposal")
    need("attendance", "IsCatchUpRun")
    for token in ("new EightPmSchedulerService(",
                  "new AutoAttendanceService(database)",
                  "new PreviewTokenRetentionService(database)",
                  "_original.Start()", "_original.DisposeAsync()"):
        need("scheduler",token)
    for token in ("INativeAutomaticScheduler", "StartOnce()", "_scheduler.Start()",
                  "await _scheduler.StopAsync().ConfigureAwait(false)", "ObjectDisposedException"):
        need("gate",token)
    for token in ("new NativeAutomaticSchedulerGate(", "StartAutomaticAttendanceScheduler()",
                  "await _autoAttendance.DisposeAsync()", "_database.Dispose()",
                  "public ValueTask DisposeAsync()"):
        need("backend",token)
    for token in ("_backend.StartAutomaticAttendanceScheduler();",
                  "await _backend.DisposeAsync();", "_mayClose = true;", "Close();"):
        need("host",token)
    if contents["host"].find("await _backend.DisposeAsync();") > contents["host"].find("_mayClose = true;"):
        errors.append("Native host must drain scheduler BEFORE releasing mutex/closing SQLCipher")
    for token in ("INativeAttendanceReviewBackend", "NativeAttendanceRevisionPreview",
                  "NativeAttendanceCommitReceipt", "PreviewToken", "OperationId"):
        # Contract names use lowercase positional params for 2 last fields:
        need("contracts",token if token not in ("PreviewToken","OperationId") else "string")
    for token in ("new AttendanceCommitService(database)",
                  ".PreviewAsync(payload, principal, combined.Token)",
                  "CommitAsync(new JsonObject", '"previewToken"','"operationId"',
                  "revise: revise", 'revise ? "ReviseTodayAttendance" : "SubmitAttendance"',
                  "CommitCoreAsync(identity, previewToken, operationId, true, ct)",
                  "CommitCoreAsync(identity, previewToken, operationId, false, ct)",
                  'RequiredText(data, "mode") != (initial ? "initial" : "revise")',
                  'data["revised"]?.GetValue<bool>() != revise',
                  "MutationIdempotency", "ResolveSessionAsync(expected.SessionNonce",
                  "ActionManifest.All.Single", "principal.SessionRevocation",
                  "AttendanceDateRangePolicy.TryParse",
                  "NativeAttendanceDateRange.TryCreate",
                  "actualStart != validatedRange?.StartDate",
                  "actualEnd != validatedRange?.EndDate"):
        # The idempotency implementation is in canonical commit service.
        need("commit" if token=="MutationIdempotency" else "review_backend",token)
    for token in ("_operationId = Guid.NewGuid().ToString(\"N\")",
                  "draft.PreviewToken, _operationId", "public async Task ConfirmAsync(",
                  "if (!HasValidActor(actor))", "draft.Persons.Count != _personIds.Length",
                  "returned.SequenceEqual(_personIds", "ForceCancel()", "IsCommitting", "draft.IsInitial != _initial",
                  "_backend.PreviewInitialAsync(", "_backend.CommitInitialAsync("):
        need("review_vm",token)
    for token in ("INativeAttendanceReviewBackend", "ReadChoicesAsync(",
                  "_review.PreviewAsync(", "_review.CommitAsync(", "SavedStatusId",
                  "NativeAttendanceDateRange? range = null",
                  "_review.PreviewAsync(actor, date, site, people, statusId, ct, range)"):
        need("reader",token)
    for token in ("_selection.Count >= 100", "SelectVisiblePage()", "ClearSelection()",
                  "OpenReviewAsync(", "Review.Committed", "Review.ForceCancel()",
                  "Review.AttachBackend("):
        need("register_vm",token)
    for token in ("<views:NativeAttendanceReviewView", "DataContext=\"{Binding Review}\"",
                  "SelectVisiblePageCommand", "ClearSelectionCommand",
                  "ReviewSelectedCommand", "OnRegisterCheckboxChanged"):
        need("register_xaml",token)
    for token in ("PreviewCommand", "ConfirmCommand", "CancelCommand", "PreviewPersons",
                  "الحالة السابقة", "تأكيد", 'IsReadOnly="True"',
                  'KeyboardNavigation.TabNavigation="Cycle"'):
        need("review_xaml",token)
    need("review_code","Key.Escape")
    need("register_code","OnRegisterCheckboxChanged")
    for t in ("PreviewAttendanceChange", "ReviseTodayAttendance", "SubmitAttendance", "GeneralAdministration",
              "OrdinaryAdministration", "Supervisor"):
        need("manifest",t)
    if contents["gate_tests"].count("[Fact]") < 5:
        errors.append("Scheduler gate lost a required xUnit regression")
    if contents["review_tests"].count("[Fact]") < 10:
        errors.append("Operator-intent review gate lost xUnit regression coverage")
    need("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj")
    for token in ("CanExecuteClosedDayEmergencyOverride(actor.Role)",
                  "ReferenceEquals(_currentDetail?.Invoke(), detail)",
                  "response.Request != request", "response.RequiresExplicitConfirmation",
                  "result.RevisionNumber != preview.PreviousRevision + 1",
                  "_uncertain = true", "preview.Token, _operationId"):
        need("closed_vm",token)
    for token in ("PreviewClosedDayCorrection", "CommitClosedDayCorrection",
                  "new ClosedDayCorrectionService(database)", 'data.ContainsKey("days")'):
        need("review_backend",token)
    for token in ("Historical_review_requires_valid_reason_and_explicit_confirmation",
                  "Wrong_role_future_day_and_missing_saved_record_are_rejected",
                  "Forged_preview_and_changed_context_cannot_commit",
                  "Uncertain_receipt_reuses_same_operation_and_disallows_new_preview"):
        need("closed_tests",token)
    # A business-error response does not roll back ExecuteInWriteTransactionAsync.
    # All range-day conflicts must be resolved before consuming the token or
    # entering the mutation loop.
    source = contents["closed_service"]
    for token in ("var verifiedDays = new List<RangeDayState>(days.Count);",
                  "verifiedDays.Add(live);", "long? consumedRange",
                  "foreach (var live in verifiedDays)",
                  'live.BeforeName != Read(dayEntry, "beforeStatusName")',
                  'live.AssignmentId != Read(dayEntry, "assignmentId")',
                  'live.AfterName != Read(dayEntry, "afterStatusName")',
                  "var rangeResult = new JsonObject"):
        need("closed_service", token)
    verified = source.find("verifiedDays.Add(live);")
    consume = source.find("long? consumedRange")
    writes = source.find("foreach (var live in verifiedDays)")
    end = source.find("var rangeResult = new JsonObject")
    if not (0 <= verified < consume < writes < end) or "return Deny(" in source[writes:end]:
        errors.append("Historical range must validate every day before any mutation; no partial conflict return.")
    need("closed_integration", "HistoricalRangeCorrection_StaleLastDayRejectsEntireRangeWithoutPartialWrites")
    for token in ("SpanDays is >= 1 and <= 62 && To <= today",
                  "day.BusinessDate <= previous", "day.HasExistingRecord != (day.PreviousRevision > 0)",
                  "day.BeforeStatusId == expected.StatusId", "MatchesReceipt(",
                  "receipt.FinalRevisionNumber == Days[^1].PreviousRevision + 1"):
        need("range_contract",token)
    for token in ("PreviewRangeAsync(", "CommitRangeAsync(", 'RequiredText(data, "mode") != "range"',
                  '["dateFrom"] = from', '["dateTo"] = to',
                  "revisionSum != aggregate", "approved.MatchesReceipt(receipt)"):
        need("review_backend",token)
    for token in ("Request_bounds_match_canonical_inclusive_62_day_policy",
                  "Preview_preserves_skipped_days_and_accepts_new_or_existing_records",
                  "Preview_rejects_duplicate_out_of_range_or_forged_days",
                  "Range_receipt_must_match_person_start_and_final_approved_revision"):
        need("range_tests",token)
    for key in ("register_vm","review_vm","closed_vm","range_contract","host"):
        for forbidden in ("new AttendanceCommitService", "ExecuteInWriteTransactionAsync(",
                          "RunPendingAsync(", "new BridgePrincipal("):
            if forbidden in contents[key]:
                errors.append(f"Unauthorized direct write/scheduler or forged principal in {key}: {forbidden}")
    return errors

if __name__=="__main__":
    faults=problems(TEXT)
    if faults: raise SystemExit("NATIVE PHASE 4 CONTRACT FAILED:\n"+"\n".join(faults))
    for key,token in (
        ("project",'Compile Include="../AlTamam.DesktopHost/Scheduling/EightPmSchedulerService.cs"'),
        ("backend","await _autoAttendance.DisposeAsync()"),
        ("backend","await backupDrain.ConfigureAwait(false);"),
        ("backend","await nativeDrain.ConfigureAwait(false);"),
        ("reader","using var nativeOperation = operations.Enter();"),
        ("operation_gate","ObjectDisposedException.ThrowIf(_stopping,this)"),
        ("restore_review","ReferenceEquals(Comparison,intent.Comparison)"),
        ("host","NativeRestoreConfirmationDialog.ConfirmFinal(this,prepared)"),
        ("backend","await _services.RevokeSessionAsync(actor.SessionNonce,ct).ConfigureAwait(false);"),
        ("restore_view",'Click="OnRestoreClick"'),
        ("restore_integration","NativeAdmissionDrain_PrecedesReplacement_AndRestartRevokesRestoredSessions"),
        ("host","_backend.StartAutomaticBackupScheduler();"),
        ("gate","_stopTask ??= StopCoreAsync()"),
        ("backup_adapter","new AutomaticBackupService(database)"),
        ("backup_tests","OriginalHostScheduler_StartsDueBackupAndDrainsBeforeDatabaseDisposal"),
        ("host","_backend.StartAutomaticAttendanceScheduler();"),
        ("review_backend","principal.SessionRevocation"),
        ("review_backend",'"operationId"'),
        ("review_backend",'revise ? "ReviseTodayAttendance" : "SubmitAttendance"'),
        ("review_vm","draft.IsInitial != _initial"),
        ("closed_vm","response.Request != request"),
        ("closed_service","verifiedDays.Add(live);"),
        ("range_contract","SpanDays is >= 1 and <= 62 && To <= today"),
        ("range_contract","day.BusinessDate <= previous"),
        ("review_backend","approved.MatchesReceipt(receipt)"),
        ("review_backend","revisionSum != aggregate"),
        ("range_tests","Preview_rejects_duplicate_out_of_range_or_forged_days"),
        ("closed_service","foreach (var live in verifiedDays)"),
        ("closed_integration","HistoricalRangeCorrection_StaleLastDayRejectsEntireRangeWithoutPartialWrites"),
        ("closed_vm","result.RevisionNumber != preview.PreviousRevision + 1"),
        ("review_backend",'data.ContainsKey("days")'),
        ("review_backend","AttendanceDateRangePolicy.TryParse"),
        ("review_backend","NativeAttendanceDateRange.TryCreate"),
        ("review_backend","actualStart != validatedRange?.StartDate"),
        ("reader","_review.PreviewAsync(actor, date, site, people, statusId, ct, range)"),
        ("review_vm","draft.PreviewToken, _operationId"),
        ("register_vm","Review.ForceCancel()"),
        ("review_xaml","ConfirmCommand"),
        ("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj")
    ):
        mutated=TEXT.copy()
        mutated[key]=mutated[key].replace(token,"REMOVED_BY_NEGATIVE_TEST")
        if not problems(mutated):
            raise SystemExit(f"Unsafe mutation incorrectly passed for {key}:{token}")
    print("NATIVE PHASE 4 PASS: exact durable engine, one-owner scheduler, audited human-preview/confirm/replay and four old customer packages intact")
