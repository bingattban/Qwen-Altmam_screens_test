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
    if content["detail_tests"].count("[Fact]") < 4:
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
