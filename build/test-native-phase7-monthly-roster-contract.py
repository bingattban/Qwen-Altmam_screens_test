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
    "range_vm": "src/AlTamam.NativePresentation/NativeHistoricalRangeReview.cs",
    "range_tests": "tests/AlTamam.UnitTests/NativeHistoricalRangeReviewPhase10Tests.cs",
    "catchup_contract": "src/AlTamam.NativePresentation/NativeAdministrativeCatchUpContracts.cs",
    "catchup_review": "src/AlTamam.NativePresentation/NativeAdministrativeCatchUpReview.cs",
    "catchup_review_tests": "tests/AlTamam.UnitTests/NativeAdministrativeCatchUpReviewPhase10Tests.cs",
    "catchup_tests": "tests/AlTamam.UnitTests/NativeAdministrativeCatchUpPhase10Tests.cs",
    "catchup_adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherAttendanceReviewBackend.cs",
    "catchup_service": "src/AlTamam.Application/Bridge/AdministrativeAttendanceCatchUpService.cs",
    "catchup_sql_tests": "tests/AlTamam.Tests/Bridge/AdministrativeAttendanceCatchUpTests.cs",
    "excel_tests": "tests/AlTamam.UnitTests/NativeMonthlyExcelExportPhase7Tests.cs",
    "excel_vm": "src/AlTamam.NativePresentation/NativeMonthlyExcelExportViewModel.cs",
    "excel_service": "src/AlTamam.Application/Bridge/MonthlyExcelExportService.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/DailyMonthlyReadService.cs",
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

    for key in ("templates", "xaml"):
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
                  '["archiveMode"] = historical', '["pageSize"] = 100',
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
    for token in ("answer.Matches(request,Today)", "approved.MatchesReceipt(result)",
                  "_busy = _uncertain = true;", "_pending?.Cancel()", "Guid.NewGuid().ToString",
                  "CanExecuteClosedDayEmergencyOverride(actor.Role)", "_liveActor?.Invoke() == actor"):
        require("catchup_review",token)
    for token in ("Settings.AttachCatchUp(catchUpBackend, () => _active)", "Settings.Clear();",
                  "screen == NativeScreen.Settings || !Settings.CatchUp.IsOpen"):
        require("shell",token)
    for token in ("INativeAdministrativeCatchUpBackend.PreviewCatchUpAsync(", "INativeAdministrativeCatchUpBackend.CommitCatchUpAsync("):
        require("backend",token)
    for token in ("CatchUp.PreviewCommand","CatchUp.ConfirmCommand","CatchUp.CloseCommand",
                  "CatchUp.Preview.PendingCount","CatchUp.Preview.EligibleCount","OpenCatchUpCommand"):
        require("templates",token)
    for token in ("Catch_up_requires_admin_valid_date_reason_and_separate_confirmation",
                  "Catch_up_rejects_mismatched_preview_and_retries_exact_uncertain_operation",
                  "Catch_up_discards_late_preview_on_logout_and_never_commits_as_replaced_actor",
                  "Settings_catch_up_is_disabled_for_supervisor_and_cleared_on_session_reset"):
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
                  "Days[{i}].Symbol", "Days[{i}].Description",
                  "CustomCells[{i}]", "OnPeriodChanged(",
                  "OnSiteChanged(",
                  "OnDayCellClick(", "Button.ClickEvent",
                  "OpenCellDetailsAsync(row, day)"):

        require("view", token)
    for token in ("No_live_session_does_not_read_months_or_people",
                  "Native_monthly_page_uses_real_historical_cells",
                  "Site_search_archive_and_pagination",
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
                  "ExportExcelAsync(actor, filter, linked.Token)",
                  "ValidatePreview(result, filter)", "ValidateReceipt(result, filter)",
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
        ("catchup_contract", "receipt.AppliedCount <= PendingCount"),
        ("catchup_review", "approved.MatchesReceipt(result)"),
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
        ("excel_vm", "ExportExcelAsync(actor, filter, linked.Token)"),
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
        ("shell", "MonthlyAttendance.Clear();"),
        ("core", "python build/test-native-phase7-monthly-roster-contract.py"),
        ("release", "python build/test-native-phase7-monthly-roster-contract.py"),
    ):
        changed = TEXT.copy()
        changed[key] = changed[key].replace(token, "REMOVED_BY_NEGATIVE_TEST")
        if not problems(changed):
            raise SystemExit(f"Unsafe missing monthly invariant passed: {key}: {token}")
    print("PHASE 7 NATIVE MONTHLY PASS: SQLCipher, day details, approved XLSX, nonce, Core+Release")
