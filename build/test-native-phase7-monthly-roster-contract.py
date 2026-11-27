#!/usr/bin/env python3
"""Phase 7 monthly SQLCipher historical WPF — fail-closed static regression.

Both Windows workflows run this before C# compilation/testing and four-package
Release. This is additive to real xUnit, SQLCipher and E2E tests, NOT a substitute.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "contracts": "src/AlTamam.NativePresentation/NativeMonthlyAttendanceContracts.cs",
    "vm": "src/AlTamam.NativePresentation/MonthlyAttendancePageViewModel.cs",
    "tests": "tests/AlTamam.UnitTests/NativeMonthlyAttendancePhase7Tests.cs",
    "detail_tests": "tests/AlTamam.UnitTests/NativeMonthlyCellDetailsPhase7Tests.cs",
    "single_vm": "src/AlTamam.NativePresentation/NativeClosedDayCorrectionReview.cs",
    "range_vm": "src/AlTamam.NativePresentation/NativeHistoricalRangeReview.cs",
    "period_review": "src/AlTamam.NativePresentation/NativeHistoricalPeriodReview.cs",
    "historical_settings_tests": "tests/AlTamam.UnitTests/NativeHistoricalSettingsPhase10Tests.cs",
    "period_review_tests": "tests/AlTamam.UnitTests/NativeHistoricalPeriodReviewPhase10Tests.cs",
    "period_evidence": "src/AlTamam.NativePresentation/NativeHistoricalPeriodEvidence.cs",
    "period_tests": "tests/AlTamam.UnitTests/NativeHistoricalPeriodEvidencePhase10Tests.cs",
    "range_tests": "tests/AlTamam.UnitTests/NativeHistoricalRangeReviewPhase10Tests.cs",
    "catchup_contract": "src/AlTamam.NativePresentation/NativeAdministrativeCatchUpContracts.cs",
    "catchup_review": "src/AlTamam.NativePresentation/NativeAdministrativeCatchUpReview.cs",
    "policy_review": "src/AlTamam.NativePresentation/NativeClosedDayPolicyReview.cs",
    "policy_review_tests": "tests/AlTamam.UnitTests/NativeClosedDayPolicyReviewPhase10Tests.cs",
    "policy_sql_tests": "tests/AlTamam.Tests/Bridge/ClosedDayCorrectionIntegrationTests.cs",
    "catchup_review_tests": "tests/AlTamam.UnitTests/NativeAdministrativeCatchUpReviewPhase10Tests.cs",
    "catchup_tests": "tests/AlTamam.UnitTests/NativeAdministrativeCatchUpPhase10Tests.cs",
    "catchup_adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherAttendanceReviewBackend.cs",
    "catchup_service": "src/AlTamam.Application/Bridge/AdministrativeAttendanceCatchUpService.cs",
    "catchup_sql_tests": "tests/AlTamam.Tests/Bridge/AdministrativeAttendanceCatchUpTests.cs",
    "excel_tests": "tests/AlTamam.UnitTests/NativeMonthlyExcelExportPhase7Tests.cs",
    "progress_tests": "tests/AlTamam.UnitTests/NativeMonthlyExportProgressPhase12Tests.cs",
    "destination_tests": "tests/AlTamam.UnitTests/NativeMonthlyExportDestinationPhase12Tests.cs",
    "receipt_tests": "tests/AlTamam.UnitTests/NativeMonthlyExportReceiptPhase12Tests.cs",
    "receipt_evidence": "src/AlTamam.NativePresentation/NativeMonthlyExcelReceiptEvidence.cs",
    "excel_sql_tests": "tests/AlTamam.Tests/Bridge/MonthlyExcelExportTests.cs",
    "name_policy": "src/AlTamam.Domain/Policies/MonthlyExcelOutputNamePolicy.cs",
    "excel_vm": "src/AlTamam.NativePresentation/NativeMonthlyExcelExportViewModel.cs",
    "excel_service": "src/AlTamam.Application/Bridge/MonthlyExcelExportService.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/DailyMonthlyReadService.cs",
    "admin_vm": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "admin_xaml": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "admin_tests": "tests/AlTamam.UnitTests/NativePersonnelDirectoryPhase8Tests.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "pages": "src/AlTamam.NativePresentation/NativePageViewModels.cs",
    "templates": "src/AlTamam.NativeDesktop/Views/NativeSectionTemplates.xaml",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeMonthlyAttendanceView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeMonthlyAttendanceView.xaml.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k: (ROOT / p).read_text(encoding="utf-8") for k, p in FILES.items()}


def problems(content):
    errors = []

    def require(k, token):
        if token not in content[k]:
            errors.append(f"{k}: required invariant missing: {token}")

    for key in ("templates", "xaml", "admin_xaml"):
        try:
            ET.fromstring(content[key])
        except ET.ParseError as e:
            errors.append(f"Malformed native WPF {key}: {e}")

    for token in ("interface INativeMonthlyAttendanceReader",
                  "ReadCatalogueAsync(", "ReadMonthAsync(",
                  "NativeMonthlyFilter(", "NativeMonthlyCell(",
                  "NativeMonthlySnapshot(",
                  "INativeMonthlyCellDetailsReader",
                  "NativeMonthlyCellDetails", "NativeMonthlyCellRequest(",
                  "INativeMonthlyExcelExportBackend",
                  "NativeMonthlyExcelFilter(", "NativeMonthlyExcelPreview(",
                  "NativeMonthlyExcelReceipt("):

        require("contracts", token)
    for token in ("VerifyContinuationIdentityAsync(actor, ct)",
                  "ArchivePeriodsAsync(ct)", "MonthlyGridAsync(new JsonObject",
                  '["archiveMode"] = historical', "bool historical = filter.ExplicitArchive ||", '["pageSize"] = 100',
                  "DateOnly.TryParseExact(saved.Key", "savedDays",
                  "movementNote", "RequiredText(row, \"personId\")", "RequiredArray(data, \"customColumns\")",
                  "new NativeMonthlySnapshot(",
                  "MonthlyCellDetailsAsync(new JsonObject",
                  '["personId"] = request.PersonId',
                  '["businessDate"] = request.BusinessDate.ToString(',
                  "ReadCellDetailsAsync(", "new NativeMonthlyCellDetails(",
                  "new MonthlyExcelExportService(database)",
                  ".PreviewAsync(MonthlyExcelPayload(",
                  ".ExportAsync(MonthlyExcelPayload(",
                  "PreviewExcelAsync(", "ExportExcelAsync(",
                  "VerifyContinuationIdentityAsync(actor, ct).ConfigureAwait(false)",
                  "new NativeMonthlyExcelReceipt("):

        require("backend", token)
    for token in ("ArchivePeriodsAsync(", "MonthlyGridAsync(",
                  "PersonIdentityHistory", "SiteNameHistory",
                  "MonthlyAttendanceCellPolicy.TemporalMembershipSql",
                  "movementNote", "RosterCustomFieldProjection.AttachAsync",
                  "MonthlyCellDetailsAsync("):

        require("canonical", token)
    for token in ("ValidPeriod(", "_boundActor != actor",
                  "_liveActor?.Invoke() != actor", "request != _generation",
                  "_pending?.Cancel()", "HideRows(", "Validate(result, filter)",
                  "result.Rows.Select", "row.DayCells.Select",
                  "SelectPeriodAsync(", "SelectSiteAsync(", "SearchAsync(",
                  "NextPageAsync(", "PreviousPageAsync(", "CurrentMonth()",
                  "OpenCellDetailsAsync(", "CloseCellDetails()",
                  "_detailsReader.ReadCellDetailsAsync(", "ReferenceEquals(item, row)",
                  "version != _detailGeneration", "result.PersonId != row.PersonId",
                  "result.BusinessDate != date",
                  "Excel = new NativeMonthlyExcelExportViewModel()",
                  "Excel.Attach(exportBackend",
                  "Excel.Close();",
                  "Excel.NotifyContextChanged()",
                  "new NativeMonthlyExcelFilter(_period, _site, _term)"):

        require("vm", token)
    for token in ("CanOpenCorrection", "Correction.Open(backend", "Correction.LoadChoicesAsync(ct)",
                  "Correction.ForceClear();", "CanChangeMonthlyScope => !Correction.IsOpen",
                  "if (!CanChangeMonthlyScope) return;", "bool committed = Correction.Receipt is not null;",
                  "if (committed) await RefreshAsync(ct)"):
        require("vm", token)
    for token in ("INativeClosedDayCorrectionBackend.ReadCorrectionChoicesAsync(",
                  "INativeClosedDayCorrectionBackend.PreviewAsync(",
                  "INativeClosedDayCorrectionBackend.CommitAsync("):
        require("backend", token)
    for token in ("CanChangeMonthlyScope => !Correction.IsOpen && !RangeCorrection.IsOpen",
                  "RangeCorrection.Open(backend, catalogue", "RangeCorrection.LoadChoicesAsync(ct)",
                  "RangeCorrection.ForceClear();", "bool committed = RangeCorrection.Receipt is not null;"):
        require("vm", token)
    for token in ("INativeHistoricalRangeBackend.PreviewRangeAsync(", "INativeHistoricalRangeBackend.CommitRangeAsync("):
        require("backend", token)
    require("shell", "screen == NativeScreen.MonthlyAttendance || !MonthlyAttendance.Correction.IsOpen && !MonthlyAttendance.RangeCorrection.IsOpen")
    for token in ("ReferenceEquals(_currentDetail?.Invoke(), detail)", "response.Matches(request, Today)",
                  "approved.MatchesReceipt(result)", "_uncertain = true;", "_pending?.Cancel()",
                  "_backend.CommitRangeAsync(_actor!, approved, _operationId", "RevokePreview();"):
        require("range_vm",token)
    for token in ("Range_requires_explicit_approval_and_draft_edits_revoke_preview",
                  "Range_uncertain_commit_retries_only_identical_approved_operation",
                  "Range_discards_late_preview_after_session_clear_and_rejects_changed_person"):
        require("range_tests",token)
    require("detail_tests","Range_review_freezes_scope_and_refreshes_only_after_verified_receipt")
    for token in ("RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actor.Role)",
                  "ReferenceEquals(x,value)", "generation!=_generation",
                  "response.Matches(request,Today)", "Array.AsReadOnly(response.Days.ToArray())",
                  "_pending?.Cancel();", "ClearSearchResults();"):
        require("period_review",token)
    for token in ("Direct_search_requires_admin_and_exact_candidate_then_reads_complete_period",
                  "Direct_search_discards_late_results_after_clear_and_session_replacement",
                  "Direct_period_rejects_mismatched_evidence_and_invalidates_on_search_edit"):
        require("period_review_tests",token)
    for token in ("Historical.Open(_historicalReader,_historicalActor,()=>HistoricalRange.IsOpen || HistoricalSingle.IsOpen)",
                  "HistoricalRange.OpenForPeriod(", "HistoricalRange.LoadChoicesAsync(ct)",
                  "Historical.InvalidateEvidence();", "await Historical.ReadAsync(ct);",
                  "HistoricalRange.ForceClear();", "Historical.ForceClear();",
                  "CatchUp.IsOpen || Policy.IsOpen || Historical.IsOpen || HistoricalRange.IsOpen || HistoricalSingle.IsOpen"):
        require("pages",token)
    require("shell","Settings.AttachHistorical(historicalReader,historicalBackend,historicalCatalogue,() => _active)")
    require("period_review","_scopeLocked?.Invoke()!=true")
    for token in ("ReferenceEquals(CurrentPage,Administration)", "Administration.CanLeaveForHistoricalReview",
                  "Settings.CanOpenHistorical", "CanNavigate(NativeScreen.Settings)",
                  "_navigateCommand.Execute(NativeScreen.Settings)", "Settings.OpenHistoricalCommand.Execute(null)"):
        require("shell",token)
    for token in ("CanLeaveForHistoricalReview => !_loading", "!Mutation.HasUnverifiedCommit",
                  "!ReportHistory.IsOpen && !GroupReport.IsOpen", "Changed(nameof(CanLeaveForHistoricalReview))"):
        require("admin_vm",token)
    require("admin_xaml","DataContext.OpenAdministrativeHistoricalCommand")
    require("historical_settings_tests","Administration_shortcut_reuses_settings_review_and_obeys_session_and_navigation")
    require("admin_tests","Historical_shortcut_preserves_open_personnel_profile_and_group_selection")
    for token in ("CanUsePeriod(", "period.Request.SpanDays==1", "period.Days[0].HasSavedRecord",
                  "ReferenceEquals(_currentPeriod?.Invoke(),period)", "response.PreviousRevision!=day.Revision",
                  "NeedsPeriodReload=true;", "_backend.CommitAsync(_actor!, preview.Token, _operationId"):
        require("single_vm",token)
    for token in ("HistoricalSingle.OpenForPeriod(", "HistoricalSingle.LoadChoicesAsync(ct)",
                  "HistoricalSingle.Receipt is not null || HistoricalSingle.NeedsPeriodReload",
                  "HistoricalSingle.ForceClear();"):
        require("pages",token)
    for token in ("OpenHistoricalSingleCommand", "HistoricalSingle.PreviewCommand",
                  "HistoricalSingle.ConfirmCommand", "CloseHistoricalSingleCommand"):
        require("templates",token)
    require("historical_settings_tests","Settings_direct_single_closed_day_uses_single_service_and_reloads_on_stale_or_receipt")
    for token in ("Historical.Candidates", "Historical.SelectedPerson", "Historical.Evidence.Days",
                  "HistoricalRange.Preview.Days", "HistoricalRange.ConfirmCommand",
                  "CloseHistoricalRangeCommand", "Historical.CloseCommand"):
        require("templates",token)
    for token in ("Settings_historical_range_freezes_evidence_and_refreshes_after_verified_receipt",
                  "Settings_historical_uncertain_commit_blocks_close_and_logout_clears_reviews"):
        require("historical_settings_tests",token)
    for token in ("INativeHistoricalPeriodReader","SpanDays is >= 1 and <= 62",
                  "report.AttendanceTotal != report.Attendance.Count","saved.TryAdd(date",
                  "Array.AsReadOnly(days)","لا يوجد سجل تحضير"):
        require("period_evidence",token)
    for token in ("SearchHistoricalPeopleAsync(", "services.SearchPersonsAsync(term,50,ct,activeOnly:false)",
                  "ReadHistoricalPeriodAsync(", "NativeHistoricalPeriodEvidence.FromReport(request,report"):
        require("backend",token)
    for token in ("OpenForPeriod(", "CanEditDates => CanEdit && _period is null",
                  "ReferenceEquals(_currentPeriod?.Invoke(),period)", "period.Matches(period.Request,Today)",
                  "_detail?.PersonId ?? _period!.Request.PersonId"):
        require("range_vm",token)
    for token in ("Historical_person_search_rejects_empty_control_and_overlong_queries",
                  "Historical_period_requires_inclusive_bounded_past_or_today",
                  "Historical_period_reads_saved_days_and_never_infers_missing_day_eligibility",
                  "Historical_period_rejects_wrong_identity_scope_partial_and_duplicate_report"):
        require("period_tests",token)
    require("range_tests","Direct_range_requires_explicit_current_period_and_freezes_read_dates")
    for token in ("MatchesPreviewState(", "saved.HasSavedRecord!=proposed.HasExistingRecord",
                  "saved.Revision!=proposed.PreviousRevision"):
        require("period_evidence",token)
    for token in ("NeedsPeriodReload=true;", "!readEvidence.MatchesPreviewState(response,Today)",
                  "!NeedsPeriodReload", "NeedsPeriodReload=false;"):
        require("range_vm",token)
    require("pages","HistoricalRange.Receipt is not null || HistoricalRange.NeedsPeriodReload")
    require("range_tests","Direct_range_rejects_changed_saved_state_or_revision")
    require("historical_settings_tests","Settings_historical_stale_preview_requires_reread_without_commit")
    require("contracts","public bool ExplicitArchive { get; init; }")
    for token in ("NativeMonthlyCellDisplay From(", "color.Skip(1).All(Uri.IsHexDigit)",
                  "luminance > 0.179", '"internal-transfer" => "↔"',
                  '"external-transfer" => "↗"', '"separation" => "⊗"', '"return" => "↩"'):
        require("contracts",token)
    require("backend", 'MovementKind = info["movementKind"]?.GetValue<string>() ?? ""')
    require("vm","NativeMonthlyCellDisplay.From(cell)")
    for token in ("ExplicitArchive = IsArchiveMode", "SwitchCurrentMonthModeAsync",
                  "CanSwitchMonthMode", "_explicitArchive = false;", "nameof(MonthModeLabel)"):
        require("vm",token)
    for token in ("ShowLiveMonthCommand","ShowCurrentArchiveCommand","MonthModeLabel"):
        require("xaml",token)
    for token in ("NativeClosedDayPolicyRequest", "if (!CanEdit) return;", "Approved=request;",
                  "result.Enabled != approved.Enabled", "_busy=_uncertain=true;",
                  "_pending?.Cancel()", "_liveActor?.Invoke() == actor",
                  "SetClosedDayPolicyAsync(_actor!,approved,_operationId"):
        require("policy_review",token)
    for token in ("INativeClosedDayPolicyBackend", 'const string action = "SetClosedDayCorrectionPolicy"',
                  "new ClosedDayCorrectionService(database).SetPolicyAsync(",
                  '["enabled"] = approved.Enabled', '["reason"] = approved.Reason',
                  "enabled != approved.Enabled"):
        require("catchup_adapter",token)
    for token in ("Settings.AttachClosedDayPolicy(policyBackend, () => _active)", "Settings.PropertyChanged += (_, _) => _navigateCommand.NotifyAvailabilityChanged();"):
        require("shell",token)
    for token in ("INativeClosedDayPolicyBackend.SetClosedDayPolicyAsync(", "_review.SetClosedDayPolicyAsync(actor,approved,operationId,ct)"):
        require("backend",token)
    for token in ("EnablePolicyCommand","DisablePolicyCommand","Policy.PrepareCommand","Policy.ConfirmCommand",
                  "Policy.HasApprovedIntent","Policy.CloseCommand","Policy.CompletionMessage"):
        require("templates",token)
    for token in ("Settings_policy_and_catch_up_reviews_are_mutually_exclusive",
                  "Settings_policy_uncertain_commit_blocks_other_review_and_logout_clears_late_result"):
        require("catchup_review_tests",token)
    for token in ("Policy_reason_is_optional_but_nonempty_reason_must_be_valid",
                  "Policy_requires_authorized_explicit_confirmation_and_invalidates_edited_intent",
                  "Policy_wrong_receipt_freezes_intent_and_retries_same_operation",
                  "Policy_session_replacement_and_late_reply_never_publish_another_users_state"):
        require("policy_review_tests",token)
    for token in ("CorrectionPolicy_ExactRetryMustNotGenerateSecondAuditEvent",
                  "JsonNode.DeepEquals(firstReceipt,replayedReceipt)"):
        require("policy_sql_tests",token)
    for token in ("answer.Matches(request,Today)", "approved.MatchesReceipt(result)",
                  "_busy = _uncertain = true;", "_pending?.Cancel()", "Guid.NewGuid().ToString",
                  "CanExecuteClosedDayEmergencyOverride(actor.Role)", "_liveActor?.Invoke() == actor"):
        require("catchup_review",token)
    for token in ("Settings.AttachCatchUp(catchUpBackend, () => _active)", "Settings.Clear();",
                  "screen == NativeScreen.Settings || !Settings.IsReviewOpen"):
        require("shell",token)
    for token in ("INativeAdministrativeCatchUpBackend.PreviewCatchUpAsync(", "INativeAdministrativeCatchUpBackend.CommitCatchUpAsync("):
        require("backend",token)
    for token in ('<BooleanToVisibilityConverter x:Key="VisibilityIfTrue"/>', "CatchUp.PreviewCommand","CatchUp.ConfirmCommand","CatchUp.CloseCommand",
                  "CatchUp.Preview.PendingCount","CatchUp.Preview.EligibleCount","OpenCatchUpCommand"):
        require("templates",token)
    for token in ("Catch_up_requires_admin_valid_date_reason_and_separate_confirmation",
                  "Catch_up_rejects_mismatched_preview_and_retries_exact_uncertain_operation",
                  "Catch_up_discards_late_preview_on_logout_and_never_commits_as_replaced_actor",
                  "Settings_catch_up_is_disabled_for_supervisor_and_cleared_on_session_reset",
                  "Settings_status_and_connection_follow_live_authorized_session"):
        require("catchup_review_tests",token)
    for token in ("today.DayNumber - BusinessDate.DayNumber is >= 1 and <= 7",
                  "PendingCount > 0 && EligibleCount >= PendingCount",
                  "receipt.PendingBefore == PendingCount", "receipt.AppliedCount <= PendingCount",
                  "IsHexDigest(Token) && IsHexDigest(Fingerprint)", "RequiresExplicitConfirmation"):
        require("catchup_contract",token)
    for token in ("PreviewAdministrativeAttendanceCatchUp", "CommitAdministrativeAttendanceCatchUp",
                  "new AdministrativeAttendanceCatchUpService(database).PreviewAsync(",
                  "new AdministrativeAttendanceCatchUpService(database).CommitAsync(",
                  "preview.Matches(request, DateOnly.FromDateTime(DateTime.Now))",
                  "approved.MatchesReceipt(receipt)", "principal.SessionRevocation",
                  '["previewToken"] = approved.Token', '["operationId"] = operationId'):
        require("catchup_adapter",token)
    for token in ("NativeAdministrativeCatchUpRequest", "Receipt_accepts_only_canonical_date_and_count_with_no_overstatement",
                  "Approved_token_remains_structurally_valid_for_exact_replay_after_window_closes"):
        require("catchup_tests",token)
    for token in ("RunForDateInTransactionAsync", "ADMIN_ATTENDANCE_CATCHUP_APPROVED",
                  "now.Fingerprint != Read(preview, \"fingerprint\")"):
        require("catchup_service",token)
    for token in ("StalePreviewNeverOverwritesManualAttendance", "var replayedReceipt = Assert.IsType<JsonObject>(retry.Data);", "JsonNode.DeepEquals(receipt,replayedReceipt)"):
        require("catchup_sql_tests",token)
    for token in ("OpenRangeCorrectionCommand","RangeCorrection.FromText","RangeCorrection.ToText",
                  "RangeCorrection.Preview.Days","RangeCorrection.ConfirmCommand","CloseRangeCorrectionCommand"):
        require("xaml",token)
    for token in ("OpenCorrectionCommand", "Correction.StatusChoices", "Correction.Reason",
                  "Correction.PreviewCommand", "Correction.ConfirmCommand", "CloseCorrectionCommand",
                  "Correction.Preview.BeforeStatusName", "Correction.Preview.AfterStatusName",
                  "CanChangeMonthlyScope"):
        require("xaml", token)
    for token in ("Correction_uses_selected_historical_cell_and_refreshes_only_after_verified_receipt",
                  "Correction_rejects_supervisor_and_discards_late_statuses_on_logout"):
        require("detail_tests", token)
    for token in ("MonthlyAttendance = new MonthlyAttendancePageViewModel()",
                  "MonthlyAttendance.AttachReader(archiveReader, () => _active)",
                  "MonthlyAttendance.RefreshAsync()", "MonthlyAttendance.Clear();",
                  "MonthlyAttendance.CloseCellDetails();",
                  "MonthlyAttendance.Excel.Close();"):

        require("shell", token)
    if "public sealed class MonthlyAttendancePageViewModel()" in content["pages"]:
        errors.append("Disconnected monthly placeholder must be removed.")
    require("templates", "<views:NativeMonthlyAttendanceView")
    for token in ('Header="الرقم"', 'Header="الرقم (ع)"',
                  'Header="الرتبة"', 'Header="الاسم"',
                  'Header="الموقع"', 'IsReadOnly="True"',
                  "SelectedPeriod", "SelectedSiteId", "MonthlyGrid",
                  "SearchCommand",
                  "SelectedCellDetails", "IsCellDetailsOpen",
                  "CellDetailsError", "CloseCellDetailsCommand",
                  "Excel.OpenPreviewCommand", "Excel.ConfirmCommand",
                  "Excel.Preview.PersonCount", "Excel.Receipt.PrimaryFilePath",
                  "Excel.IsOpen", "Excel.CloseCommand"):

        require("xaml", token)
    for token in ("RenderColumns()", "MonthlyGrid.Columns.Add(",
                  "Days[{i}].DisplaySymbol", "Days[{i}].Description",
                  "Days[{i}].BackgroundColor", "Days[{i}].ForegroundColor",
                  "CustomCells[{i}]", "OnPeriodChanged(",
                  "OnSiteChanged(",
                  "OnDayCellClick(", "Button.ClickEvent",
                  "OpenCellDetailsAsync(row, day)"):

        require("view", token)
    for token in ("No_live_session_does_not_read_months_or_people",
                  "Native_monthly_page_uses_real_historical_cells",
                  "Monthly_cell_preserves_saved_color_with_readable_ink",
                  "Monthly_cell_invalid_or_missing_color_has_safe_neutral_display",
                  "Monthly_movement_marker_uses_only_canonical_kind",
                  "Monthly_grid_preserves_saved_cell_color_and_movement_without_fabricating_empty_days",
                  "Site_search_archive_and_pagination",
                  "Explicit_current_month_archive_is_distinct_from_live_month_and_preserves_scope",
                  "Archive_mode_commands_require_live_session_and_catalogue",
                  "Future_or_unlisted_month",
                  "Logout_and_replaced_nonce",
                  "Malformed_service_response"):
        require("tests", token)
    if content["tests"].count("[Fact]") < 6:
        errors.append("Missing monthly test-first positive/negative regressions.")
    for token in ("A_real_day_cell_opens_historical_identity_status_movement_and_revisions",
                  "Forged_old_or_out_of_range_rows_never_start_detail_reads",
                  "Logout_and_nonce_change_revoke_in_flight_historical_details",
                  "Invalid_canonical_detail_echo_cannot_show_old_or_foreign_person"):
        require("detail_tests", token)
    if content["detail_tests"].count("[Fact]") < 6:
        errors.append("Monthly canonical detail security/replay tests are missing.")
    for token in ("PreviewAsync(", "ExportAsync(",
                  "OutputDirectorySettings.IsSafeDirectory",
                  "MonthlyGridForExcelAsync", "ReportWorkbookCommit.VerifyFlushAndMove"):
        require("excel_service", token)
    for token in ("OpenPreviewAsync(", "ConfirmAsync(", "Close()",
                  "PreviewExcelAsync(actor, filter, linked.Token)",
                  "ExportExcelAsync(actor, request, linked.Token)",
                  "ValidatePreview(result, filter)", "ValidateReceipt(result, request)",
                  "_currentFilter?.Invoke() != filter",
                  "_liveActor?.Invoke() != actor"):
        require("excel_vm", token)
    for token in ("Preview_does_not_write_and_confirm_uses_the_same_month_site",
                  "All_sites_export_uses_canonical_consolidated_and_per_site_files",
                  "Unapplied_search_and_stale_period_site_or_filter_cannot_export",
                  "Logout_and_nonce_replacement_hide_inflight_preview_and_export_results",
                  "Forged_preview_or_export_receipt_is_rejected"):
        require("excel_tests", token)
    if content["excel_tests"].count("[Fact]") < 5:
        errors.append("Missing monthly Excel preview/confirm negative-path tests.")
    require("contracts","interface INativeMonthlyExcelProgressBackend")
    for token in ("progressBackend.ExportWithProgressAsync(actor, request, Report, linked.Token)",
                  "context.Post(_ => Apply(), null)", "Math.Min(99, (int)((long)processed * 100 / total))",
                  "version != _generation || !_exporting", "_progressPercent = 100;",
                  "if (!_exporting || _cancelRequested) return;", "_pending?.Cancel();",
                  "IsOpen && !IsExporting"):
        require("excel_vm",token)
    # Each public export entrypoint must admit its own operation exactly once.
    # The private core is shared but must not acquire a second lease during drain.
    source = content["backend"]
    export_start = source.find("public async Task<NativeMonthlyExcelReceipt> ExportExcelAsync(")
    progress_start = source.find("public async Task<NativeMonthlyExcelReceipt> ExportWithProgressAsync(")
    core_start = source.find("private async Task<NativeMonthlyExcelReceipt> ExportMonthlyExcelCoreAsync(")
    core_end = source.find("public Task<IReadOnlyList<NativeAttendanceStatusChoice>> ReadChoicesAsync(",core_start)
    if not (0 <= export_start < progress_start < core_start < core_end):
        errors.append("Native monthly export entrypoint boundaries missing")
    else:
        for entry in (source[export_start:progress_start],source[progress_start:core_start]):
            if (entry.count("using var nativeOperation = operations.Enter();") != 1 or
                entry.count("return await ExportMonthlyExcelCoreAsync(") != 1 or
                entry.find("using var nativeOperation = operations.Enter();") >
                entry.find("return await ExportMonthlyExcelCoreAsync(")):
                errors.append("Monthly export must hold exactly one lease before calling the shared core")
        core = source[core_start:core_end]
        if ("operations.Enter(" in core or "operations.RunAsync(" in core or
            "new MonthlyExcelExportService(database)" not in core):
            errors.append("Monthly export core must reuse the writer without nested operation admission")
    for token in ("request=filter with { OutputDirectory=_outputDirectory,OutputFileName=name }",
                  "MonthlyExcelOutputNamePolicy.TryNormalize(_outputFileName", "ValidDestination",
                  "TrySelectOutputDirectory", "Monthly Excel receipt destination mismatch."):
        require("excel_vm",token)
    for token in ('payload["directory"]=filter.OutputDirectory',
                  'payload["fileName"]=filter.OutputFileName',
                  "OutputDirectorySettings.IsSafeDirectory(filter.OutputDirectory,database.DatabasePath",
                  "MonthlyExcelOutputNamePolicy.TryNormalize(filter.OutputFileName",
                  "Monthly Excel receipt does not match reviewed destination."):
        require("backend",token)
    require("excel_service","MonthlyExcelOutputNamePolicy.TryNormalize(input,out name)")
    for token in ('stem.Equals("CON"', 'stem.StartsWith("COM"', 'stem.StartsWith("LPT"', "stem[3] is >= '1' and <= '9'"):
        require("name_policy",token)
    for token in ("Excel.HasDestinationReview", "Excel.OutputFileName", "Excel.OutputDirectory",
                  "Excel.CanEditDestination", 'Click="OnChooseExportFolder"'):
        require("xaml",token)
    for token in ("new OpenFolderDialog", "ReferenceEquals(export.Preview,preview)", "export.TrySelectOutputDirectory"):
        require("view",token)
    for token in ("Unsafe_filename_blocks_confirmation","Selected_destination_and_safe_name_are_frozen_for_confirm",
                  "Unavailable_default_requires_explicit_selection_and_receipt_path_must_match",
                  "Replaced_session_cannot_edit_destination_or_export"):
        require("destination_tests",token)
    for token in ("files.Count!=receipt.FileCount", "!sites.Add(row.SiteId)",
                  "!paths.Add(file)", "row.PersonCount!=receipt.PersonCount",
                  "Path.GetDirectoryName(file),folder", "Array.AsReadOnly(frozen.ToArray())"):
        require("receipt_evidence",token)
    for token in ('RequiredArray(data,"files")', "rows.Count!=fileCount",
                  "NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(receipt,filter)",
                  "info.Length<=0", "FileAttributes.ReparsePoint", "return receipt with{Files=verified}"):
        require("backend",token)
    require("excel_vm","Files=NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(result,request)")
    require("excel_vm","ResultFiles => _receipt?.Files ?? []")
    for token in ("Excel.ResultFiles","Excel.HasResultFiles",'Binding="{Binding SiteName}"',
                  'Binding="{Binding PersonCount}"','Binding="{Binding FilePath}"'):
        require("xaml",token)
    for token in ('page["total"]!.GetValue<long>()!=total', "rowNumber+items.Count>total",
                  "if(rowNumber!=total)", 'combinedData["personCount"]!.GetValue<long>()!=combined',
                  'entry["personCount"]!.GetValue<long>()!=plan.Count', "if(!success)foreach(var file in created)"):
        require("excel_service",token)
    for token in ("Complete_set_preserves_order_and_counts_without_summing_overlapping_memberships",
                  "Inconsistent_set_fails_closed","Single_site_receipt_requires_exact_site_and_one_file",
                  "Frozen_receipt_does_not_alias_mutable_backend_list"):
        require("receipt_tests",token)
    require("excel_sql_tests","ChangedRosterCount_RefusesSuccessAndCleansOnlyAttemptFiles")
    require("excel_sql_tests",'XDocument.Load(sheet).Descendants(ns+"row").Count()-3')
    require("backend","ExportWithProgressAsync(")
    require("backend","CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation)")
    require("backend",".ExportAsync(MonthlyExcelPayload(filter, year, month), lifetime.Token, progress)")
    require("backend","VerifyContinuationIdentityAsync(actor, principal.SessionRevocation)")
    require("vm","CanChangeMonthlyScope => !Correction.IsOpen && !RangeCorrection.IsOpen && !Excel.IsExporting")
    require("shell","!MonthlyAttendance.RangeCorrection.IsOpen && !MonthlyAttendance.Excel.IsExporting")
    for token in ("Excel.ProgressPercent", "Excel.IsProgressIndeterminate", "Excel.CancelExportCommand",
                  "Excel.ProgressMessage", "Excel.IsExporting"):
        require("xaml",token)
    for token in ("Progress_never_claims_completion_before_receipt",
                  "Cancellation_requests_stop_and_never_retries_unverified_result",
                  "Cancellation_after_publication_does_not_hide_verified_receipt",
                  "Revoked_session_ignores_late_progress_and_receipt",
                  "Monthly_scope_is_frozen_until_canonical_export_finishes"):
        require("progress_tests",token)
    for k in ("core", "release"):
        require(k, "python build/test-native-phase7-monthly-roster-contract.py")
    for token in ("RunPendingAsync(", "ExecuteInWriteTransactionAsync(",
                  "new AttendanceCommitService(", "new BridgePrincipal(",
                  "new SqliteConnection("):
        if token in content["vm"] or token in content["view"]:
            errors.append(f"Monthly presentation may not bypass canonical SQLCipher services: {token}")
    return errors


if __name__ == "__main__":
    errors = problems(TEXT)
    if errors:
        raise SystemExit("PHASE 7 MONTHLY NATIVE CONTRACT FAILED:\n" + "\n".join(errors))
    for key, token in (
        ("vm", "_boundActor != actor"),
        ("vm", "Correction.ForceClear();"),
        ("vm", "RangeCorrection.ForceClear();"),
        ("vm", "CanChangeMonthlyScope => !Correction.IsOpen && !RangeCorrection.IsOpen"),
        ("range_vm", "approved.MatchesReceipt(result)"),
        ("range_vm", "OpenForPeriod("),
        ("period_evidence", "report.AttendanceTotal != report.Attendance.Count"),
        ("backend", "services.SearchPersonsAsync(term,50,ct,activeOnly:false)"),
        ("catchup_contract", "receipt.AppliedCount <= PendingCount"),
        ("catchup_review", "approved.MatchesReceipt(result)"),
        ("policy_review", "result.Enabled != approved.Enabled"),
        ("shell", "screen == NativeScreen.Settings || !Settings.IsReviewOpen"),
        ("backend", "bool historical = filter.ExplicitArchive ||"),
        ("vm", "ExplicitArchive = IsArchiveMode"),
        ("xaml", "ShowCurrentArchiveCommand"),
        ("templates", "Policy.ConfirmCommand"),
        ("policy_review", "SetClosedDayPolicyAsync(_actor!,approved,_operationId"),
        ("policy_sql_tests", "JsonNode.DeepEquals(firstReceipt,replayedReceipt)"),
        ("shell", "Settings.Clear();"),
        ("templates", "CatchUp.ConfirmCommand"),
        ("catchup_adapter", "approved.MatchesReceipt(receipt)"),
        ("catchup_service", "ADMIN_ATTENDANCE_CATCHUP_APPROVED"),
        ("catchup_sql_tests", "JsonNode.DeepEquals(receipt,replayedReceipt)"),
        ("range_vm", "response.Matches(request, Today)"),
        ("xaml", "RangeCorrection.ConfirmCommand"),
        ("backend", "INativeHistoricalRangeBackend.CommitRangeAsync("),
        ("detail_tests", "Range_review_freezes_scope_and_refreshes_only_after_verified_receipt"),
        ("vm", "CanChangeMonthlyScope => !Correction.IsOpen"),
        ("shell", "screen == NativeScreen.MonthlyAttendance || !MonthlyAttendance.Correction.IsOpen"),
        ("backend", "INativeClosedDayCorrectionBackend.CommitAsync("),
        ("xaml", "Correction.ConfirmCommand"),
        ("detail_tests", "Correction_rejects_supervisor_and_discards_late_statuses_on_logout"),
        ("vm", "request != _generation"),
        ("vm", "row.DayCells.Select"),
        ("vm", "version != _detailGeneration"),
        ("backend", "MonthlyCellDetailsAsync(new JsonObject"),
        ("receipt_evidence", "files.Count!=receipt.FileCount"),
        ("receipt_evidence", "!sites.Add(row.SiteId)"),
        ("receipt_evidence", "!paths.Add(file)"),
        ("receipt_evidence", "row.PersonCount!=receipt.PersonCount"),
        ("backend", 'RequiredArray(data,"files")'),
        ("backend", "NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(receipt,filter)"),
        ("excel_vm", "Files=NativeMonthlyExcelReceiptEvidence.ValidateAndFreeze(result,request)"),
        ("excel_service", 'page["total"]!.GetValue<long>()!=total'),
        ("excel_service", "if(rowNumber!=total)"),
        ("excel_service", 'entry["personCount"]!.GetValue<long>()!=plan.Count'),
        ("xaml", "Excel.ResultFiles"),
        ("excel_vm", "request=filter with { OutputDirectory=_outputDirectory,OutputFileName=name }"),
        ("excel_vm", "Monthly Excel receipt destination mismatch."),
        ("backend", 'payload["directory"]=filter.OutputDirectory'),
        ("backend", 'payload["fileName"]=filter.OutputFileName'),
        ("backend", "Monthly Excel receipt does not match reviewed destination."),
        ("excel_service", "MonthlyExcelOutputNamePolicy.TryNormalize(input,out name)"),
        ("view", "ReferenceEquals(export.Preview,preview)"),
        ("xaml", 'Click="OnChooseExportFolder"'),
        ("backend", "return await ExportMonthlyExcelCoreAsync(actor,filter,(_,_)=>{},ct)"),
        ("backend", "return await ExportMonthlyExcelCoreAsync(actor,filter,progress,ct)"),
        ("backend", "private async Task<NativeMonthlyExcelReceipt> ExportMonthlyExcelCoreAsync("),
        ("excel_vm", "progressBackend.ExportWithProgressAsync(actor, request, Report, linked.Token)"),
        ("excel_vm", "Math.Min(99, (int)((long)processed * 100 / total))"),
        ("excel_vm", "_progressPercent = 100;"),
        ("excel_vm", "version != _generation || !_exporting"),
        ("backend", ".ExportAsync(MonthlyExcelPayload(filter, year, month), lifetime.Token, progress)"),
        ("vm", "CanChangeMonthlyScope => !Correction.IsOpen && !RangeCorrection.IsOpen && !Excel.IsExporting"),
        ("shell", "!MonthlyAttendance.RangeCorrection.IsOpen && !MonthlyAttendance.Excel.IsExporting"),
        ("xaml", "Excel.CancelExportCommand"),
        ("excel_vm", "ExportExcelAsync(actor, request, linked.Token)"),
        ("excel_vm", "_currentFilter?.Invoke() != filter"),
        ("backend", "new MonthlyExcelExportService(database)"),
        ("excel_tests", "Forged_preview_or_export_receipt_is_rejected"),
        ("xaml", "Excel.ConfirmCommand"),
        ("vm", "Excel.Close();"),
        ("view", "OnDayCellClick("),
        ("xaml", "SelectedCellDetails"),
        ("detail_tests", "Forged_old_or_out_of_range_rows_never_start_detail_reads"),
        ("backend", "VerifyContinuationIdentityAsync(actor, ct)"),
        ("backend", "MonthlyGridAsync(new JsonObject"),
        ("backend", "DateOnly.TryParseExact(saved.Key"),
        ("xaml", 'IsReadOnly="True"'),
        ("view", "Days[{i}].Description"),
        ("view", "Days[{i}].DisplaySymbol"),
        ("view", "Days[{i}].BackgroundColor"),
        ("contracts", "NativeMonthlyCellDisplay From("),
        ("shell", "MonthlyAttendance.Clear();"),
        ("core", "python build/test-native-phase7-monthly-roster-contract.py"),
        ("release", "python build/test-native-phase7-monthly-roster-contract.py"),
    ):
        changed = TEXT.copy()
        changed[key] = changed[key].replace(token, "REMOVED_BY_NEGATIVE_TEST")
        if not problems(changed):
            raise SystemExit(f"Unsafe missing monthly invariant passed: {key}: {token}")
    print("PHASE 7 NATIVE MONTHLY PASS: SQLCipher, day details, approved XLSX, nonce, Core+Release")
