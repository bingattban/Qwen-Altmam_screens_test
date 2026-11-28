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
 "monthly_canonical":"src/AlTamam.DesktopHost/Scheduling/AutomaticMonthlyExcelSchedulerService.cs",
 "monthly_adapter":"src/AlTamam.NativeDesktop/Scheduling/NativeDurableMonthlyExcelScheduler.cs",
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
 "recovery_intent":"src/AlTamam.NativePresentation/NativeRecoveryExportIntent.cs",
 "recovery_tests":"tests/AlTamam.UnitTests/NativeRecoveryExportPhase12Tests.cs",
 "settings_vm":"src/AlTamam.NativePresentation/NativePageViewModels.cs",
 "theme_vm":"src/AlTamam.NativePresentation/NativeSettingsOverview.cs",
 "overview_tests":"tests/AlTamam.UnitTests/NativeSettingsOverviewPhase12Tests.cs",
 "monthly_config":"src/AlTamam.Application/Scheduling/AutomaticMonthlyExcelService.cs",
 "monthly_review":"src/AlTamam.NativePresentation/NativeMonthlyExportScheduleReview.cs",
 "monthly_view":"src/AlTamam.NativeDesktop/Views/NativeMonthlyExportScheduleView.xaml",
 "monthly_tests":"tests/AlTamam.UnitTests/NativeMonthlyExportSchedulePhase12Tests.cs",
 "monthly_sql_tests":"tests/AlTamam.Tests/Bridge/AutomaticMonthlyExcelTests.cs",
 "attendance_clock_review":"src/AlTamam.NativePresentation/NativeAttendanceClockReview.cs",
 "attendance_clock_service":"src/AlTamam.Application/Bridge/AutoAttendancePolicyService.cs",
 "attendance_clock_tests":"tests/AlTamam.UnitTests/NativeAttendanceClockPhase12Tests.cs",
 "attendance_clock_sql":"tests/AlTamam.Tests/Bridge/AutoAttendanceDefaultPolicyTests.cs",
 "theme_tests":"tests/AlTamam.UnitTests/NativeThemePhase12Tests.cs",
 "diagnostics_vm":"src/AlTamam.NativePresentation/NativeProgramDiagnosticsViewModel.cs",
 "diagnostics_tests":"tests/AlTamam.UnitTests/NativeProgramDiagnosticsPhase12Tests.cs",
 "settings_shell":"src/AlTamam.NativePresentation/NativeShellViewModel.cs",
 "navigation_tests":"tests/AlTamam.UnitTests/NativeWpfNavigationTests.cs",
 "layout_vm":"src/AlTamam.NativePresentation/NativeRosterLayoutViewModel.cs",
 "layout_mutation":"src/AlTamam.NativePresentation/NativeRosterColumnMutationReview.cs",
 "layout_mutation_tests":"tests/AlTamam.UnitTests/NativeRosterColumnMutationPhase12Tests.cs",
 "layout_tests":"tests/AlTamam.UnitTests/NativeRosterLayoutPhase12Tests.cs",
 "layout_admin":"src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
 "layout_view":"src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
 "settings_templates":"src/AlTamam.NativeDesktop/Views/NativeSectionTemplates.xaml",
 "restore_code":"src/AlTamam.NativeDesktop/Views/NativeManualBackupView.xaml.cs",
 "restore_tests":"tests/AlTamam.UnitTests/NativeBackupInspectionPhase12Tests.cs",
 "restore_integration":"tests/AlTamam.Tests/Bridge/NativeRestoreActivationCoordinatorTests.cs",
 "reset_service":"src/AlTamam.Application/Bridge/FullDataResetService.cs",
 "reset_preview":"src/AlTamam.NativePresentation/NativeFullResetPreview.cs",
 "reset_dialog":"src/AlTamam.NativeDesktop/Views/NativeFullResetDialog.cs",
 "reset_ui_tests":"tests/AlTamam.UnitTests/NativeFullResetPhase12Tests.cs",
 "reset_tests":"tests/AlTamam.Tests/Bridge/Section03FullResetTests.cs",
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
    need("settings_shell","if (CurrentPage.Screen == screen) return;")
    for token in ('RequireObject(data["autoMonthlyExcel"])', 'MonthlyExport=new NativeAutomaticMonthlyExcelState(',
                  'RequiredText(monthly,"lastSuccessDate")', 'RequiredText(monthly,"lastFailure")'):
        need("reader",token)
    need("theme_vm","MonthlyExport is not {} monthly || monthly.IsValid")
    for token in ("Overview.Snapshot.MonthlyExport.EnabledLabel","Overview.Snapshot.MonthlyExport.LastSuccessLabel",
                  "Overview.Snapshot.MonthlyExport.LastFailure","هذه الإعدادات المحفوظة لا تثبت نجاح تصدير جديد أو توفر المجلد حاليا"):
        need("settings_templates",token)
    need("overview_tests","Monthly_export_overview_rejects_invalid_evidence_and_never_shows_replaced_session_state")
    for token in ('new AutoAttendancePolicyService(database).GetAsync(ct).ConfigureAwait(false),"GetAutoAttendancePolicy(settings)"',
                  'if(RoleAuthorizationPolicy.CanAccessUsersManagement(principal.Role))',
                  'RequiredText(policy,"dailyLockTimeLocal")','AttendanceSettings=attendanceSettings'):
        need("reader",token)
    need("theme_vm","AttendanceSettings is not {} policy || (RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) && policy.IsValid)")
    need("theme_vm",'DefaultStatusId is not ("STS-LEAVE" or "STS-PERMISSION")')
    for token in ("Overview.Snapshot.HasAttendanceSettings","Overview.Snapshot.AttendanceSettings.DueTime",
                  "Overview.Snapshot.AttendanceSettings.LockTime","Overview.Snapshot.AttendanceSettings.DefaultLabel"):
        need("settings_templates",token)
    need("overview_tests","Attendance_policy_read_is_session_bound_and_rejects_malformed_evidence")
    for token in ("NativeAttendanceClockKind.Due","NativeAttendanceClockKind.Lock","_busy=_attempted=_uncertain=true",
                  "ReferenceEquals(_current?.Invoke(),_snapshot)","intent.Matches(result)"):
        need("attendance_clock_review",token)
    for token in ('payload?.ContainsKey("expectedValue")==true','if(expected is not null &&','BridgeErrorCodes.ConflictStale'):
        need("attendance_clock_service",token)
    for token in ("SaveAttendanceClockAsync(", 'service.SetScheduleAsync(new JsonObject',
                  '[intent.PayloadKey]=intent.Time,["expectedValue"]=intent.Previous', 'if(!intent.Matches(current))'):
        need("reader",token)
    need("settings_vm","AttendanceClock.ForceClear();")
    need("settings_vm","|| AttendanceClock.IsOpen || MonthlyExportSchedule.IsOpen;")
    need("settings_shell","!Settings.AttendanceClock.IsOpen")
    need("settings_templates","AttendanceClock.ConfirmCommand")
    need("attendance_clock_tests","Clock_requires_exact_review_and_never_repeats_write")
    need("attendance_clock_sql","Reviewed_clock_rejects_stale_value_and_preserves_other_clock_and_legacy_writes")
    for token in ('payload.ContainsKey("expectedConfiguration")','previouslyEnabled!=expectedEnabled.Value',
                  'previousDirectory!=expectedDirectory','BridgeErrorCodes.ConflictStale'):
        need("monthly_config",token)
    for token in ("SaveMonthlyExportScheduleAsync(","service.ConfigureAsync(new JsonObject",
                  '["expectedConfiguration"]=new JsonObject','!intent.Matches(receipt) || !intent.Matches(current)'):
        need("reader",token)
    for token in ("ReferenceEquals(_current?.Invoke(),_snapshot)","_busy=_attempted=_uncertain=true",
                  "intent.Matches(result)","RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)"):
        need("monthly_review",token)
    need("settings_vm","MonthlyExportSchedule.ForceClear();")
    need("settings_vm","|| MonthlyExportSchedule.IsOpen;")
    need("settings_shell","!Settings.MonthlyExportSchedule.IsOpen")
    need("settings_templates","<views:NativeMonthlyExportScheduleView/>")
    if "هذه البيانات لا تثبت تشغيل مجدول التصدير في الواجهة الأصلية" in contents["settings_templates"]:
        errors.append("Native settings overview retains obsolete pre-scheduler warning")
    need("monthly_view","MonthlyExportSchedule.ConfirmCommand")
    need("monthly_tests","Unverified_receipt_blocks_repeat_and_success_message")
    need("monthly_sql_tests","Reviewed_configuration_rejects_stale_settings_and_preserves_legacy_writes")
    need("settings_shell","(screen == NativeScreen.Administration ||\n            !ReferenceEquals(CurrentPage,Administration) || Administration.CanLeaveForHistoricalReview) &&")
    need("navigation_tests","Administration_review_blocks_all_other_navigation_and_same_page_does_not_reload")
    need("navigation_tests","Repeated_click_on_current_settings_does_not_start_second_read")
    for token in ("OpenPersonnelReportsCommand","OpenMonthlyReportsCommand",
                  "reader is INativePersonnelDirectoryReader &&",
                  "reader is INativePersonnelReportReader && reader is INativePersonnelGroupReportReader",
                  "reader is INativeMonthlyAttendanceReader && reader is INativeMonthlyExcelExportBackend",
                  "_personnelAvailable && _canNavigate(NativeScreen.Administration)",
                  "_monthlyAvailable && _canNavigate(NativeScreen.MonthlyAttendance)"):
        need("settings_vm",token)
    for token in ("!IsBusy && ReferenceEquals(CurrentPage,Reports) && CanNavigate(destination)",
                  "if(CanOpenReportDestination(destination))","_navigateCommand.Execute(destination);",
                  "Reports.AttachReader(reader);"):
        need("settings_shell",token)
    for token in ("Binding OpenPersonnelReportsCommand","Binding OpenMonthlyReportsCommand"):
        need("settings_templates",token)
    for token in ("Report_entries_reuse_existing_routes_and_never_elevate_roles",
                  "Reports_without_backend_never_advertise_working_actions"):
        need("navigation_tests",token)

    for token in ("NativeRosterColumn.ValidCatalogue(rows)","Snapshot.All(Uri.IsHexDigit)",
                  "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
                  "version!=_generation || linked.IsCancellationRequested",
                  "if(!CanRead){Clear();return;}", "_pending?.Cancel()"):
        need("layout_vm",token)
    for token in ("ReadRosterLayoutAsync(", "new RosterColumnStructureService(database)",
                  '.ListAsync(new JsonObject{["includeInactive"]=true},ct)',
                  'RequiredNumber(data,"total")!=rows.Length || !NativeRosterColumn.ValidCatalogue(rows)'):
        need("reader",token)
    for token in ("CanLeaveForHistoricalReview => !Removal.IsOpen && !_loading && !ColumnLayout.IsOpen",
                  "ColumnLayout.Clear();","ColumnLayout.Attach(layoutReader,()=>_liveActor?.Invoke());",
                  "if(ColumnLayout.IsOpen || Removal.IsOpen)return;"):
        need("layout_admin",token)
    for token in ("Binding OpenColumnLayoutCommand","Binding ColumnLayout","Binding StoredValueCount"):
        need("layout_view",token)
    for token in ("Metadata_denies_duplicate_identity_bad_snapshot_and_unprotected_system_positions",
                  "Catalogue_is_read_only_and_denies_supervisor_or_missing_session",
                  "Late_catalogue_is_discarded_after_close_or_session_change"):
        need("layout_tests",token)
    for token in ("!change.CanApply(before)", 'payload["expectedSnapshot"]=previous.Snapshot',
                  'payload["confirmed"]=true', "service.CreateAsync(payload,principal,lifetime.Token)",
                  "service.UpdateAsync(payload,principal,lifetime.Token)",
                  "service.DeactivateAsync(payload,principal,lifetime.Token)",
                  "ReadRosterLayoutCoreAsync(actor,lifetime.Token)", "!change.MatchesReceipt(result)",
                  "result.Snapshot!=snapshot"):
        need("reader",token)
    # Check the scoped call chain as well as the private catalogue helper:
    # moving the read into a helper must not bypass the caller's session or lease.
    reader_source=contents["reader"]
    read_start=reader_source.find("public async Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutAsync(")
    core_start=reader_source.find("private async Task<IReadOnlyList<NativeRosterColumn>> ReadRosterLayoutCoreAsync(")
    write_start=reader_source.find("public async Task<NativeRosterColumn> CommitRosterColumnAsync(")
    write_end=reader_source.find("public async Task<IReadOnlyList<NativeStatusRow>> ReadActiveStatusesAsync(",write_start)
    if not (0<=read_start<core_start<write_start<write_end):
        errors.append("Missing or reordered native roster catalogue/read/write entry points")
    else:
        read=reader_source[read_start:core_start]
        core=reader_source[core_start:write_start]
        write=reader_source[write_start:write_end]
        for entry in (read,write):
            if entry.count("using var nativeOperation = operations.Enter();")!=1 or "CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation)" not in entry or "ResolveSitesPrincipalAsync(actor,ct)" not in entry:
                errors.append("Roster layout entry must own one admission lease and an exact session-linked lifetime")
        if "operations.Enter()" in core or '.ListAsync(new JsonObject{["includeInactive"]=true},ct)' not in core or "VerifyPersonnelManagerAsync(actor,ct)" not in core:
            errors.append("Roster layout helper must preserve the caller lifetime and revalidate the session without nested admission")
        ordered=("var before=await ReadRosterLayoutCoreAsync(actor,lifetime.Token)", "!change.CanApply(before)", "BridgeResponse response=change.Operation switch", "var after=await ReadRosterLayoutCoreAsync(actor,lifetime.Token)", "!change.MatchesReceipt(result)")
        offsets=[write.find(token) for token in ordered]
        if min(offsets)<0 or offsets!=sorted(offsets):
            errors.append("Roster write must validate fresh evidence before mutation and verify read-back afterwards")
    for token in ("_issued is null", "ReferenceEquals(reviewed,_draft)", "Guid.NewGuid().ToString(\"N\")",
                  "result.StoredValueCount!=old.StoredValueCount", "IsSystemProtected:false",
                  "FieldType==old.FieldType || old.StoredValueCount==0"):
        need("layout_mutation",token)
    for token in ("_mutation.Take(_liveActor?.Invoke(),_rows)", "_mutation.Accept(operation,receipt)",
                  "_uncertainActor=operation.Actor", "if(_busy && _mutation.HasUnverifiedCommit)return;",
                  "InvalidateReview()", "_uncertainActor!=_actor"):
        need("layout_vm",token)
    for token in ("Binding CreateCommand","Binding EditCommand","Binding DeactivateCommand",
                  "Binding ReviewCommand","Binding ConfirmCommand","Binding CanEditType"):
        need("layout_view",token)
    for token in ("Exact_current_metadata_and_protected_columns_are_required",
                  "Create_and_retire_have_distinct_rules_and_cannot_change_saved_values",
                  "Commit_requires_review_and_does_not_automatically_retry_uncertain_result",
                  "ViewModel_requires_fresh_review_and_clears_saved_metadata_on_close",
                  "Lost_commit_reply_blocks_repeat_mutation_for_same_session"):
        need("layout_mutation_tests",token)
    for key in ("register_xaml","review_xaml","project","layout_view"):
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
    shutdown = contents["backend"].split("private async Task DrainNativeWorkAsync()", 1)
    if len(shutdown) != 2:
        errors.append("Missing shared native drain")
    else:
        stop = shutdown[1]
        tokens = ("Task backupDrain = _autoBackup.DisposeAsync().AsTask();",
                  "await _autoAttendance.DisposeAsync()", "await backupDrain.ConfigureAwait(false);",
                  "await nativeDrain.ConfigureAwait(false);")
        indices = [stop.find(token) for token in tokens]
        if min(indices) < 0 or indices != sorted(indices):
            errors.append("Both scheduler drains must complete before shared drain returns")
    need("backend","private async Task ShutdownCoreAsync()\n    {\n        await DrainNativeWorkAsync().ConfigureAwait(false);\n        _database.Dispose();")
    need("host", "_backend.StartAutomaticBackupScheduler();")
    need("project",'Compile Include="../AlTamam.DesktopHost/Scheduling/AutomaticMonthlyExcelSchedulerService.cs"')
    need("backup_test_project",'Compile Include="../../src/AlTamam.DesktopHost/Scheduling/AutomaticMonthlyExcelSchedulerService.cs"')
    for token in ("_service.RunIfDueAsync(ct)", "_service.RecordFailureAsync", "await _loop.ConfigureAwait(false)"):
        need("monthly_canonical",token)
    for token in ("new AutomaticMonthlyExcelSchedulerService(new AutomaticMonthlyExcelService(database))",
                  "_original.Start()", "_original.DisposeAsync()"):
        need("monthly_adapter",token)
    for token in ("new NativeDurableMonthlyExcelScheduler(database)", "_autoMonthlyExcel.StartOnce()",
                  "Task monthlyDrain = _autoMonthlyExcel.DisposeAsync().AsTask();",
                  "await monthlyDrain.ConfigureAwait(false);"):
        need("backend",token)
    need("host","_backend.StartAutomaticMonthlyExcelScheduler();")
    if contents["host"].count("_backend.StartAutomaticMonthlyExcelScheduler();") != 1:
        errors.append("Monthly export scheduler must have one native startup call")
    if len(shutdown)==2:
        stop=shutdown[1]
        tokens=("Task nativeDrain = _operations.StopAndDrainAsync();",
                "Task backupDrain = _autoBackup.DisposeAsync().AsTask();",
                "Task monthlyDrain = _autoMonthlyExcel.DisposeAsync().AsTask();",
                "await _autoAttendance.DisposeAsync()", "await backupDrain.ConfigureAwait(false);",
                "await monthlyDrain.ConfigureAwait(false);","await nativeDrain.ConfigureAwait(false);")
        indices=[stop.find(token) for token in tokens]
        if min(indices)<0 or indices!=sorted(indices):
            errors.append("All three canonical schedulers and native work must drain")
    for token in ("ConditionalWeakTable<AlTamamDbContext,SemaphoreSlim>",
                  "DatabaseGates.GetValue(db,static _=>new SemaphoreSlim(1,1))",
                  "return await ConfigureCoreAsync(payload,actor,ct).ConfigureAwait(false);"):
        need("monthly_config",token)
    need("monthly_sql_tests","OriginalHostMonthlyScheduler_StartsDueExportAndDrainsBeforeDatabaseDisposal")
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
    if contents["backend"].count("using var nativeOperation = _operations.Enter();") != 3:
        errors.append("Authentication and session resolution must be admitted")
    if len(shutdown) == 2:
        stop = shutdown[1]
        tokens = ("Task nativeDrain = _operations.StopAndDrainAsync();",
                  "Task backupDrain = _autoBackup.DisposeAsync().AsTask();",
                  "await nativeDrain.ConfigureAwait(false);")
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
    need("project",'Compile Include="../AlTamam.DesktopHost/Bridge/NativeRecoveryKeyExportDialog.cs"')
    need("restore_view",'Click="OnRecoveryExportClick"')
    need("restore_view","Binding CanExportRecovery")
    need("restore_code","await host.RequestRecoveryExportAsync(settings)")
    for token in ("Role:UserRole.GeneralAdministration","Actor==liveActor","Path.IsPathFullyQualified(path)",
                  'Path.GetExtension(path),".altrecovery"',"Path.GetFullPath(path)==path"):
        need("recovery_intent",token)
    for token in ("!IsReviewOpen && !Overview.IsLoading","GetRecoveryExportIntent(string destination)",
                  "intent.Matches(RecoveryExportActor)"):
        need("settings_vm",token)
    for token in ("NativeRecoveryKeyExportDialog.RequestCredentials(this,intent.Destination)",
                  "intent.Matches(settings.RecoveryExportActor)", "OverwritePrompt=false",
                  "System.IO.File.Exists(intent.Destination)", "if (_recoveryExportInProgress)",
                  "credentials=null;"):
        need("host",token)
    recovery=contents["backend"].split("internal async Task<bool> ExportRecoveryKeyAsync(",1)
    if len(recovery)!=2:
        errors.append("Missing recovery export backend")
    else:
        export=recovery[1].split("public ValueTask DisposeAsync()",1)[0]
        for token in ("using var nativeOperation = _operations.Enter();",
                      "_services.ResolveSessionAsync(actor.SessionNonce,ct)",
                      "principal.SessionNonce!=actor.SessionNonce",
                      "new NativePortableRecoveryExportService(_database,_services,keyStore)",
                      ".TryExportAsync(principal,currentAdminPassword,recoveryPassphrase,intent.Destination,ct)"):
            if token not in export:
                errors.append("Recovery export invariant missing: "+token)
    for token in ("Intent_requires_founder_and_live_same_nonce",
                  "Rejects_nonlocal_or_relative_destinations",
                  "Rejects_wrong_extension_and_noncanonical_path",
                  "Settings_require_founder_and_no_other_review"):
        need("recovery_tests",token)

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
        adapter=restore[1].split("internal async Task<bool> ExportRecoveryKeyAsync(",1)[0]
        tokens=("await enteringShutdown()", "await _operations.StopAndDrainAsync()", "await _services.RevokeSessionAsync(actor.SessionNonce,ct)", "await DisposeAsync()", "await drainCompleted()")
        indices=[adapter.find(token) for token in tokens]
        if min(indices)<0 or indices!=sorted(indices) or "using var nativeOperation" in adapter:
            errors.append("Restore must drain outside its own operation lease and revoke before disposal")
    for token in ("Restore_intent_requires_live_general_admin_and_exact_completed_comparison",
                  "Ordinary_admin_can_preview_but_cannot_request_native_activation",
                  "Reinspection_invalidates_previous_restore_intent"):
        need("restore_tests",token)
    need("restore_integration","NativeAdmissionDrain_PrecedesReplacement_AndRestartRevokesRestoredSessions")
    for token in ("RuntimeInformation.ProcessArchitecture.ToString()",
                  "GetCustomAttribute<AssemblyInformationalVersionAttribute>()",
                  "AlTamamDbContext.CurrentSchemaVersion",
                  "new SettingsReadBackupService(database).Integrity()",
                  "response.Code==BridgeErrorCodes.BackupIntegrityFailed",
                  'RequiredText(data,"integrity")!="ok"',
                  'RequiredBoolean(data,"encrypted")'):
        need("reader",token)
    for method in ("ReadProgramInformationAsync","CheckDatabaseIntegrityAsync"):
        match=re.search(r"public async Task[^\n]* "+method+r"\([\s\S]*?(?=\n    (?:public|private) |\Z)",reader)
        if not match:
            errors.append("Missing native diagnostics adapter: "+method)
        else:
            body=match.group(0)
            for token in ("using var nativeOperation = operations.Enter();",
                          "await ResolveBackupPrincipalAsync(actor,ct)",
                          "await VerifyContinuationIdentityAsync(actor,ct)",
                          "ct.ThrowIfCancellationRequested();"):
                if token not in body:
                    errors.append("Diagnostics identity/drain invariant missing: "+token)
    for token in ("RoleAuthorizationPolicy.CanPerformStandardOperations(actor.Role)",
                  "generation!=_generation || pending.IsCancellationRequested || Authorized!=actor",
                  "Information is not null?_integrity:null",
                  "if(!CanRun || (check && Information is null)",
                  "_pending?.Cancel()", "NotifyAvailability()"):
        need("diagnostics_vm",token)
    for token in ("Overview.IsSavingTheme || Diagnostics.IsBusy || Inspection.IsOpen",
                  "Diagnostics.Clear();","Diagnostics.NotifyAvailability();"):
        need("settings_vm",token)
    for token in ("Settings.Diagnostics.Clear();","Settings.Diagnostics.Attach(diagnosticsBackend, () => _active,",
                  "ReferenceEquals(CurrentPage,Settings)"):
        need("settings_shell",token)
    for token in ("Binding Diagnostics.RefreshCommand","Binding Diagnostics.CheckCommand",
                  "Binding Diagnostics.Information.Version","Binding Diagnostics.IntegrityLabel"):
        need("settings_templates",token)
    for token in ("Authorized_roles_read_info_but_integrity_requires_explicit_action",
                  "Failed_integrity_and_unavailable_service_are_distinct",
                  "Revoked_or_replaced_session_cannot_receive_late_result",
                  "Invalid_metadata_and_blocked_review_do_not_allow_check",
                  "Diagnostics_busy_blocks_settings_reviews_and_clear_releases_guard"):
        need("diagnostics_tests",token)
    for token in ("INativeThemeBackend","IsSavingTheme","Snapshot is { } snapshot && snapshot.Theme!=theme",
                  "generation!=_generation || pending.IsCancellationRequested || Authorized!=actor",
                  "result.Theme!=theme","_snapshot=null; _bound=null;",
                  "SaveLightCommand.NotifyAvailabilityChanged(); SaveDarkCommand.NotifyAvailabilityChanged();"):
        need("theme_vm",token)
    need("settings_vm","Overview.PropertyChanged+=(_,_)=>NotifySettingsAvailability();")
    need("settings_shell","Settings.Overview.AttachTheme(themeBackend, () => !Settings.IsReviewOpen &&")
    need("settings_templates","Binding Overview.SaveLightCommand")
    need("settings_templates","Binding Overview.SaveDarkCommand")
    for token in ("Save_uses_canonical_receipt_for_each_authorized_role",
                  "Invalid_same_or_disallowed_theme_is_never_sent",
                  "Invalid_receipt_clears_uncertain_snapshot",
                  "Late_save_never_repaints_new_session_or_cleared_view"):
        need("theme_tests",token)
    theme=reader.split("public async Task<NativeSettingsOverview> SaveThemeAsync(",1)
    if len(theme)!=2:
        errors.append("Missing native theme adapter")
    else:
        adapter=theme[1].split("private async Task<NativeSettingsOverview> ReadSettingsOverviewCoreAsync",1)[0]
        order=[adapter.find(t) for t in ("using var nativeOperation = operations.Enter();",
                 "ResolveBackupPrincipalAsync(actor,ct)",
                 "CreateLinkedTokenSource(ct,principal.SessionRevocation)",
                 '.SetThemeAsync(new JsonObject{["theme"]=theme},lifetime.Token,principal)',
                 'RequiredText(receipt,"theme")!=theme',
                 "ReadSettingsOverviewCoreAsync(actor,lifetime.Token)",
                 "saved.Theme!=theme")]
        if min(order)<0 or order!=sorted(order) or "ReadSettingsOverviewAsync(" in adapter:
            errors.append("Theme save requires one lease, real principal, canonical audit and verified readback")


    need("reset_tests","NativeReset_ChangedDataFromAdmittedWorkDuringDrainRejectsPreviewWithoutRescue")
    need("restore_view",'Click="OnFullResetClick"')
    need("restore_code","await host.RequestFullResetAsync(settings)")
    for token in ("Actor==liveActor","Actor.Role==UserRole.GeneralAdministration",
                  "DeleteLiveData:true,DeleteOwnedBackups:true","consent.Phrase==RequiredPhrase",
                  "elapsed>=TimeSpan.FromSeconds(CountdownSeconds)","revision>9007199254740991L",
                  "counts.Any(n=>n<0)"):
        need("reset_preview",token)
    for token in ("IsChecked=false","IsCancel=true,IsDefault=true","new PasswordBox",
                  "new Stopwatch()","review.CanCommit(review.Actor,Consent(),elapsed.Elapsed)",
                  "timer.Stop(); elapsed.Stop(); password.Clear();"):
        need("reset_dialog",token)
    for token in ("Review_requires_founder_and_complete_canonical_evidence",
                  "Missing_required_evidence_is_denied",
                  "Rejects_forged_revision_countdown_counts_and_path",
                  "Confirmation_requires_exact_phrase_both_consents_five_seconds_and_same_session"):
        need("reset_ui_tests",token)
    for token in ("_operations.RunAsync(async ()=>","PreviewNativeAsync(principal,ct)",
                  "NativeFullResetPreview.Read(actor,response.Data as JsonObject)",
                  "review.CanCommit(review.Actor,consent,elapsed)",
                  '["confirmDeleteLiveData"]=consent.DeleteLiveData',
                  '["confirmDeleteOwnedBackups"]=consent.DeleteOwnedBackups',
                  'payload.Remove("password")'):
        need("backend",token)
    reset_adapter=contents["backend"].split("internal async Task<BridgeResponse?> CommitFullResetAsync(",1)
    if len(reset_adapter)!=2:
        errors.append("Missing native reset commit")
    else:
        adapter=reset_adapter[1].split("public ValueTask DisposeAsync()",1)[0]
        order=[adapter.find(t) for t in ("await DrainNativeWorkAsync().ConfigureAwait(false);",
                 "_services.ResolveSessionAsync(actor.SessionNonce,CancellationToken.None)",
                 ".CommitNativeAsync(payload,principal,_services,CancellationToken.None)")]
        if min(order)<0 or order!=sorted(order) or "_operations.Enter()" in adapter or "_operations.RunAsync(" in adapter:
            errors.append("Native reset must drain outside a lease before live authorization and commit")
    for token in ("NativeFullResetDialog.Confirm(this,review)",
                  "review.CanCommit(settings.RecoveryExportActor,approval.Consent,approval.Elapsed)",
                  "_resetQuiescing=true;", "_resetInProgress || _resetQuiescing",
                  "backend.CommitFullResetAsync(review,accepted.Consent",
                  'data["requiresFirstSetup"]?.GetValue<bool>()==true',
                  'data["rescuePath"]?.GetValue<string>()',
                  "if(_resetQuiescing)"):
        need("host",token)
    reset_host=contents["host"].split("internal async Task RequestFullResetAsync(",1)
    if len(reset_host)!=2:
        errors.append("Missing physical reset host")
    else:
        reset_flow=reset_host[1].split("private async void OnWindowClosing",1)[0]
        order=[reset_flow.find(t) for t in ("_resetQuiescing=true;", "_sessionTimer.Stop();",
                "DataContext=null;", "backend.CommitFullResetAsync(", "await backend.DisposeAsync();",
                "_mayClose=true;", "Close();")]
        if min(order)<0 or order!=sorted(order):
            errors.append("Native reset must close admission and detach UI before commit; disposal before process close")

    for token in ('Operation+":native:"+',
                  "SHA256.HashData(Encoding.UTF8.GetBytes(actor.SessionNonce))",
                  "actor.SessionRevocation.IsCancellationRequested",
                  "s.SessionNonce=$nonce AND s.RevokedFlag=0 AND s.ExpiresAtUtc>$now",
                  "u.UserId=$user AND u.Username=$username AND u.ActiveFlag=1",
                  "s.Role=1 AND u.Role=1",
                  "PreviewCoreAsync(actor,NativeOperation(actor),true,ct)",
                  "CommitCoreAsync(payload,actor,NativeOperation(actor),true,ct)",
                  "CommitCoreAsync(payload,actor,Operation,false,ct)",
                  "PreviewCoreAsync(actor,Operation,false,ct)",
                  "VerifyCurrentGeneralAdministratorPasswordAsync(actor,password,ct)",
                  "IsNativeSessionLiveAsync(c,null,actor,ct)",
                  "IsNativeSessionLiveAsync(connection,null,actor,ct)",
                  "IsNativeSessionLiveAsync(c,t,actor,ct)",
                  "BridgePreviewTokens.Issue(_db,_clock,operation,actor.UserId",
                  "_db,_clock,c,t,token,operation,actor.UserId,expected,ct"):
        need("reset_service",token)
    reset=contents["reset_service"]
    order=[reset.find(t) for t in ("IsNativeSessionLiveAsync(connection,null,actor,ct)",
                    "new ResetRescueService(_db).CreateAsync(ct)",
                    "IsNativeSessionLiveAsync(c,t,actor,ct)",
                    "BridgePreviewTokens.ConsumeAsync(",
                    "foreach(string table in tables)\n                await Execute(c,t,")]
    if min(order)<0 or order!=sorted(order):
        errors.append("Native reset must check session before rescue and inside transaction before consuming token or deleting rows")
    for token in ("NativeResetPreview_RejectsForgedAndRevokedSession",
                  "NativeResetPreview_CannotBeConsumedByAnotherSessionOrLegacyEntry",
                  "NativeResetWrongPassword_UsesExistingLockoutGateBeforeRescue",
                  "NativeReset_ValidSessionAndFreshPasswordPreserveVerifiedRescueAndClearSessions"):
        need("reset_tests",token)

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
        ("restore_view",'Click="OnRecoveryExportClick"'),
        ("recovery_intent","Actor==liveActor"),
        ("backend","new NativePortableRecoveryExportService(_database,_services,keyStore)"),
        ("host","NativeRecoveryKeyExportDialog.RequestCredentials(this,intent.Destination)"),
        ("restore_integration","NativeAdmissionDrain_PrecedesReplacement_AndRestartRevokesRestoredSessions"),
        ("reset_service","IsNativeSessionLiveAsync(c,t,actor,ct)"),
        ("reader","new SettingsReadBackupService(database).Integrity()"),
        ("diagnostics_vm","generation!=_generation || pending.IsCancellationRequested || Authorized!=actor"),
        ("theme_vm","result.Theme!=theme"),
        ("theme_vm","MonthlyExport is not {} monthly || monthly.IsValid"),
        ("reader",'RequiredText(policy,"dailyLockTimeLocal")'),
        ("theme_vm","AttendanceSettings is not {} policy || (RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role) && policy.IsValid)"),
        ("settings_templates","Overview.Snapshot.AttendanceSettings.DefaultLabel"),
        ("attendance_clock_service","if(expected is not null &&"),
        ("reader",'[intent.PayloadKey]=intent.Time,["expectedValue"]=intent.Previous'),
        ("settings_shell","!Settings.AttendanceClock.IsOpen"),
        ("settings_templates","AttendanceClock.ConfirmCommand"),
        ("settings_templates","هذه الإعدادات المحفوظة لا تثبت نجاح تصدير جديد أو توفر المجلد حاليا"),
        ("monthly_config","previousDirectory!=expectedDirectory"),
        ("reader",'["expectedConfiguration"]=new JsonObject'),
        ("monthly_review","intent.Matches(result)"),
        ("settings_vm","MonthlyExportSchedule.ForceClear();"),
        ("reader",'RequireObject(data["autoMonthlyExcel"])'),
        ("settings_templates","Overview.Snapshot.MonthlyExport.LastSuccessLabel"),
        ("reader",'.SetThemeAsync(new JsonObject{["theme"]=theme},lifetime.Token,principal)'),
        ("settings_templates","Binding Overview.SaveDarkCommand"),
        ("settings_shell","Settings.Diagnostics.Clear();"),
        ("settings_shell","if (CurrentPage.Screen == screen) return;"),
        ("layout_vm","Snapshot.All(Uri.IsHexDigit)"),
        ("layout_mutation","result.StoredValueCount!=old.StoredValueCount"),
        ("layout_mutation","ReferenceEquals(reviewed,_draft)"),
        ("reader",'payload["expectedSnapshot"]=previous.Snapshot'),
        ("layout_vm","_mutation.Take(_liveActor?.Invoke(),_rows)"),
        ("layout_view","Binding ConfirmCommand"),
        ("layout_admin","CanLeaveForHistoricalReview => !Removal.IsOpen && !_loading && !ColumnLayout.IsOpen"),
        ("layout_admin","if(ColumnLayout.IsOpen || Removal.IsOpen)return;"),
        ("reader",'.ListAsync(new JsonObject{["includeInactive"]=true},ct)'),
        ("layout_view","Binding StoredValueCount"),
        ("reader","var before=await ReadRosterLayoutCoreAsync(actor,lifetime.Token)"),
        ("reader","var after=await ReadRosterLayoutCoreAsync(actor,lifetime.Token)"),
        ("reader","CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation)"),
        ("settings_shell","!IsBusy && ReferenceEquals(CurrentPage,Reports) && CanNavigate(destination)"),
        ("settings_vm","_personnelAvailable && _canNavigate(NativeScreen.Administration)"),
        ("settings_templates","Binding OpenMonthlyReportsCommand"),
        ("settings_shell","(screen == NativeScreen.Administration ||\n            !ReferenceEquals(CurrentPage,Administration) || Administration.CanLeaveForHistoricalReview) &&"),
        ("settings_templates","Binding Diagnostics.CheckCommand"),
        ("reset_preview","consent.Phrase==RequiredPhrase"),
        ("reset_dialog","password.Clear();"),
        ("restore_view",'Click="OnFullResetClick"'),
        ("backend","await DrainNativeWorkAsync().ConfigureAwait(false);"),
        ("host","_resetQuiescing=true;"),
        ("reset_service","CommitCoreAsync(payload,actor,NativeOperation(actor),true,ct)"),
        ("reset_service","VerifyCurrentGeneralAdministratorPasswordAsync(actor,password,ct)"),
        ("reset_tests","NativeResetPreview_CannotBeConsumedByAnotherSessionOrLegacyEntry"),
        ("host","_backend.StartAutomaticBackupScheduler();"),
        ("host","_backend.StartAutomaticMonthlyExcelScheduler();"),
        ("backend","await monthlyDrain.ConfigureAwait(false);"),
        ("monthly_adapter","new AutomaticMonthlyExcelSchedulerService(new AutomaticMonthlyExcelService(database))"),
        ("monthly_config","DatabaseGates.GetValue(db,static _=>new SemaphoreSlim(1,1))"),
        ("monthly_sql_tests","OriginalHostMonthlyScheduler_StartsDueExportAndDrainsBeforeDatabaseDisposal"),
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
        # A stale mutation target is a broken test, not a security regression.
        if token not in TEXT[key]:
            raise SystemExit(f"Stale negative-test target in {key}: {token}")
        mutated=TEXT.copy()
        mutated[key]=mutated[key].replace(token,"REMOVED_BY_NEGATIVE_TEST")
        if mutated[key]==TEXT[key]:
            raise SystemExit(f"Negative test did not change source in {key}: {token}")
        if not problems(mutated):
            raise SystemExit(f"Unsafe mutation incorrectly passed for {key}:{token}")
    # Theme resources are checked in both mandatory Windows CI workflows.
    import subprocess, sys
    subprocess.run([sys.executable, str(ROOT / "build/test-native-theme-palette-contract.py")], check=True)
    # Source-level route review is not production acceptance.
    import json
    canonical=json.loads((ROOT/"docs/native-wpf-migration/SCREEN_MIGRATION_MATRIX.json").read_text(encoding="utf-8"))
    review=json.loads((ROOT/"docs/native-wpf-migration/PHASE12_ROUTE_REVIEW.json").read_text(encoding="utf-8"))
    identities=lambda rows:{(r["screenId"],r["routeKey"]) for r in rows}
    assert len(review["routes"])==len(identities(review["routes"]))==69
    assert identities(review["routes"])==identities(canonical["routes"])
    assert review["productionSwitchAllowed"] is False
    for row in review["routes"]:
        assert row["reviewStatus"] in ("candidate","partial","gap") and row["functionalParityVerified"] is False
        assert row["note"].strip()
        assert row["reviewStatus"]=="gap" or row["evidence"]
        assert all((ROOT/p).is_file() for p in row["evidence"]), row["screenId"]
    print("NATIVE ROUTE REVIEW PASS: 69 source-level entries, no false physical acceptance")
    print("NATIVE PHASE 4 PASS: exact durable engine, one-owner scheduler, audited human-preview/confirm/replay and four old customer packages intact")
