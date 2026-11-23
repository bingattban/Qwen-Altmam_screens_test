#!/usr/bin/env python3
"""Fail-closed structural contract for Phase 9 native read-only individual report.

The original 69-route WPF parity matrix is NOT marked migrated by this slice.
This contract supplements C# xUnit, actual SQLCipher and WPF E2E gates.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
SOURCES = {
    "original": "src/AlTamam.Application/Bridge/PersonnelReportService.cs",
    "contract": "src/AlTamam.NativePresentation/NativePersonnelReportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelReportView.xaml",
    "viewcode": "src/AlTamam.NativeDesktop/Views/NativePersonnelReportView.xaml.cs",
    "host": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeIndividualPersonnelReportPhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
FILES = {key: (ROOT / filename).read_text(encoding="utf-8") for key, filename in SOURCES.items()}
REQUIRED = {
    "original": ("public async Task<BridgeResponse> ReportAsync(",
                 "FROM Persons WHERE PersonId=$id", "FROM PersonIdentityHistory WHERE PersonId=$id",
                 "FROM PersonnelMovements m WHERE m.PersonId=$id",
                 "FROM AttendanceRecords ar WHERE ar.PersonId=$id",
                 "pageSize", "attendanceHasNext", "movementHasNext", "identityHasNext"),
    "contract": ("interface INativePersonnelReportReader",
                 "NativeIndividualReportPages(", "NativePersonnelReportSnapshot(",
                 "NativePersonnelReportAttendance(", "HistoricalNameDisplay",
                 "غير موثق تاريخيا"),
    "vm": ("CanAccessUsersManagement", "ReferenceEquals",
           "_liveActor?.Invoke() == actor", "version != _generation || linked.IsCancellationRequested",
           "if (!CanRead) { Close(); return; }", "_pending?.Cancel();",
           "report.PersonId != requested.PersonId", "ValidPageCount(",
           "AttendanceHasNext", "MovementHasNext", "IdentityHasNext",
           "public async Task OpenAsync(", "NextAttendanceAsync(",
           "NextMovementsAsync(", "NextIdentitiesAsync(", "public void Close()"),
    "adapter": ("INativePersonnelReportReader", "new PersonnelReportService(database)",
                ".ReportAsync(new JsonObject", '["personId"] = pages.PersonId',
                '["attendancePage"] = pages.AttendancePage',
                '["movementPage"] = pages.MovementPage',
                '["identityPage"] = pages.IdentityPage',
                "await VerifyPersonnelManagerAsync(actor, ct)",
                "returnedId != pages.PersonId", "RequiredNumber(data, \"pageSize\") != 100"),
    "admin": ("ReportHistory = new NativePersonnelReportViewModel();",
              "OpenPersonnelReportCommand", "ReportHistory.AttachBackend(",
              "_rows.Any(current => ReferenceEquals(current, row))",
              "ReportHistory.Close();"),
    "view": ('FlowDirection="RightToLeft"', "Binding Report.Attendance",
             "Binding Report.Movements", "Binding Report.Identities",
             "Binding Report.StatusTotals", "Binding NextAttendanceCommand",
             "Binding NextMovementsCommand", "Binding NextIdentitiesCommand",
             "Binding CloseCommand", "غير الموثقين لا يستبدلان"),
    "viewcode": ("InitializeComponent();",),
    "host": ("NativePersonnelReportView", "OpenPersonnelReportCommand",
             "Binding ReportHistory", "SelectedItem, ElementName=PersonnelGrid"),
    "tests": ("Anonymous_and_supervisor_cannot_read_any_report_data",
              "Only_the_current_selected_row_can_resolve_a_person_id_report",
              "Historical_identity_must_not_be_invented_from_current_identity",
              "Forged_person_or_pagination_is_hidden_instead_of_rendered",
              "Attendance_page_two_uses_original_100_row_paging",
              "Logout_during_report_read_discards_late_response",
              "Changed_selected_person_invalidates_the_previous_report"),
    "core": ("python build/test-native-phase9-individual-person-report-contract.py",),
    "release": ("python build/test-native-phase9-individual-person-report-contract.py",),
}
FORBIDDEN = ("INSERT INTO", "DELETE FROM", "UPDATE Persons SET",
             "new SqliteConnection(", "File.WriteAllBytes(",
             "new PersonnelReportService(", "ExportAsync(")


def problems(files):
    errors = []
    for key, required in REQUIRED.items():
        for token in required:
            if token not in files[key]:
                errors.append(f"{key}: required historical report invariant missing: {token}")
    for key in ("vm", "contract", "view", "viewcode"):
        for token in FORBIDDEN:
            if token in files[key]:
                errors.append(f"{key}: forbidden native report writer: {token}")
    for key in ("view", "host"):
        try:
            ElementTree.fromstring(files[key])
        except ElementTree.ParseError as exc:
            errors.append(f"{key}: invalid WPF XML: {exc}")
    if files["tests"].count("[Fact]") < 7:
        errors.append("tests: test-first security cases missing")
    for key in ("core", "release"):
        lines = files[key].splitlines()
        marker = "python build/test-native-phase9-individual-person-report-contract.py"
        index = [i for i, line in enumerate(lines) if line.strip() == marker]
        if len(index) != 1 or index[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[index[0] + 1]
        ):
            errors.append(f"{key}: guard must run exactly once and fail on nonzero exit")
    return errors


if __name__ == "__main__":
    bad = problems(FILES)
    if bad:
        raise SystemExit("NATIVE PERSONNEL REPORT CONTRACT FAIL:\n" + "\n".join(bad))
    for key, needle in (
        ("original", "FROM Persons WHERE PersonId=$id"),
        ("contract", "غير موثق تاريخيا"),
        ("vm", "_pending?.Cancel();"),
        ("vm", "report.PersonId != requested.PersonId"),
        ("adapter", "await VerifyPersonnelManagerAsync(actor, ct)"),
        ("adapter", "new PersonnelReportService(database)"),
        ("admin", "ReportHistory.Close();"),
        ("view", "Binding Report.Attendance"),
        ("tests", "Logout_during_report_read_discards_late_response"),
        ("core", "python build/test-native-phase9-individual-person-report-contract.py"),
        ("release", "python build/test-native-phase9-individual-person-report-contract.py"),
    ):
        mutated = dict(FILES)
        mutated[key] = mutated[key].replace(needle, "REMOVED_IN_NEGATIVE_CASE")
        if not problems(mutated):
            raise SystemExit(f"Missing negative test for {key}: {needle}")
    print("PASS: existing PersonId SQLCipher report, WPF no writes, strict pages and session revocation")
