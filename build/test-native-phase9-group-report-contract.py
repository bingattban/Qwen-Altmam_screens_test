#!/usr/bin/env python3
"""Phase 9 group report contract: canonical encrypted source or fail closed.

In addition to the native xUnit tests, verify WPF wiring and original
read-only SQLCipher authority. Negative mutations prove every gate is active.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
FILES = {
    "canonical": "src/AlTamam.Application/Bridge/PersonnelReportService.cs",
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelGroupReportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelGroupReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelGroupReportView.xaml",
    "viewcode": "src/AlTamam.NativeDesktop/Views/NativePersonnelGroupReportView.xaml.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "adminview": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "tests": "tests/AlTamam.UnitTests/NativePersonnelGroupReportPhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
DATA = {k: (ROOT / v).read_text(encoding="utf-8") for k,v in FILES.items()}
COMMAND = "python build/test-native-phase9-group-report-contract.py"
NEEDED = {
    "canonical": (
        "private async Task<BridgeResponse> GroupReportAsync(",
        'selected.Count is <1 or >100',
        "if(people.Count!=ids.Count)return NotFound();",
        '["mode"]="group",["personCount"]=ids.Count',
        '["attendanceTotal"]=allAttendance',
        '["movementTotal"]=allMovements',
        '["dailyStatusTotals"]=dailySummary',
        '["dailyStatusPage"]=summaryPage',
        '["dailyStatusHasNext"]=summaryHasNext',
        '["dateFrom"]=from,["dateTo"]=to',
        "WHERE PersonId IN ({placeholders})",
        "($moveType=0 OR MovementType=$moveType)",
    ),
    "contracts": (
        "INativePersonnelGroupReportReader", "ReadGroupAsync(",
        "NativePersonnelGroupReportRequest(",
        "NativePersonnelGroupReportSnapshot(",
        "NativePersonnelGroupPerson(",
        "NativePersonnelGroupDailyStatus(",
        "IReadOnlyList<string> PersonIds",
    ),
    "vm": (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "ReferenceEquals(_currentRows?.Invoke(), rows)",
        "rows.Any(current => ReferenceEquals(current, row))",
        "Open(IReadOnlyList<NativePersonnelDisplayRow> rows)",
        "SelectRows(IReadOnlyList<NativePersonnelDisplayRow> selected)",
        "selected.Count > 100",
        "candidates.Any(c => ReferenceEquals(c, r))",
        "RunAsync(CancellationToken ct = default)",
        "ClearFiltersAsync(CancellationToken ct = default)",
        "NextSummaryAsync(CancellationToken ct = default)",
        "PreviousSummaryAsync(CancellationToken ct = default)",
        "version != _generation || linked.IsCancellationRequested",
        "report.PersonIds.SequenceEqual(request.PersonIds, StringComparer.Ordinal)",
        "report.StatusTotals.Sum(s => s.Count) != report.AttendanceTotal",
        "_report = null;",
    ),
    "adapter": (
        "INativePersonnelGroupReportReader",
        "ReadGroupAsync(",
        "await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false)",
        "new PersonnelReportService(database)",
        '["personIds"] = new JsonArray(',
        'RequiredText(data, "mode") != "group"',
        'RequiredNumber(data, "personCount") != request.PersonIds.Count',
        'RequiredNumber(data, "dailyStatusPage") != request.SummaryPage',
        "id != request.PersonIds[index]",
        'RequiredArray(data, "dailyStatusTotals")',
        "report.Persons.Sum(x => x.AttendanceTotal) != report.AttendanceTotal",
        "report.StatusTotals.Sum(x => x.Count) != report.AttendanceTotal",
    ),
    "view": (
        'FlowDirection="RightToLeft"',
        'SelectionMode="Multiple"',
        'SelectionChanged="OnGroupSelectionChanged"',
        "Binding CandidateRows",
        "Binding RunCommand",
        "Binding Report.Persons",
        "Binding Report.StatusTotals",
        "Binding Report.DailyStatuses",
        "Binding NextSummaryCommand",
        "Binding PreviousSummaryCommand",
        "Binding CloseCommand",
        "قراءة فقط",
    ),
    "viewcode": (
        "OnGroupSelectionChanged(", "vm.SelectRows(",
        "OfType<NativePersonnelDisplayRow>()",
    ),
    "admin": (
        "new NativePersonnelGroupReportViewModel()",
        "GroupReport.Open(_rows)",
        "GroupReport.AttachBackend(groupBackend",
        "private bool CanOpenGroupReport()",
        "GroupReport.Close();",
        "OpenGroupReportCommand",
    ),
    "adminview": (
        "Binding OpenGroupReportCommand",
        "<views:NativePersonnelGroupReportView",
        'DataContext="{Binding GroupReport}"',
    ),
    "shell": ("Administration.GroupReport.Close();",),
    "tests": (
        "Explicit_selected_current_roster_rows_keep_identity_and_source_order",
        "Missing_explicit_selection_and_outside_page_row_cannot_be_read",
        "More_than_100_rows_or_duplicate_person_ids_fail_closed",
        "Inclusive_dates_and_movement_type_survive_source_paging",
        "Invalid_date_or_movement_filter_does_not_touch_sqlcipher",
        "Forged_person_order_or_totals_are_rejected_without_showing_any_data",
        "Session_revocation_and_roster_refresh_invalidate_original_selection",
        "Superseded_async_read_cannot_overwrite_new_selection",
        "Closing_dialog_clears_sensitive_person_data_and_ignores_late_result",
    ),
    "core": (COMMAND,),
    "release": (COMMAND,),
}
FORBIDDEN = (
    "new SqliteConnection(", "INSERT INTO ", "DELETE FROM ",
    "UPDATE Persons", "File.WriteAllBytes(", "SaveFileDialog(",
    "new PersonnelReportService("
)

def check(data):
    errors = []
    for k, terms in NEEDED.items():
        for term in terms:
            if term not in data[k]:
                errors.append(f"{k}: missing group source invariant: {term}")
    for k in ("contracts", "vm", "view", "viewcode"):
        for term in FORBIDDEN:
            if term in data[k]:
                errors.append(f"{k}: forbidden group reporter/writer: {term}")
    for k in ("view", "adminview"):
        try:
            ElementTree.fromstring(data[k])
        except ElementTree.ParseError as exc:
            errors.append(f"{k}: invalid WPF XAML: {exc}")
    if data["tests"].count("[Fact]") < 8 or data["tests"].count("[Theory]") < 1:
        errors.append("tests: missing original read, pagination, role and stale-response red cases")
    for k in ("core", "release"):
        lines = data[k].splitlines()
        positions = [i for i,line in enumerate(lines) if line.strip() == COMMAND]
        if len(positions) != 1 or positions[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[positions[0] + 1]
        ):
            errors.append(f"{k}: missing fatal group-report preflight")
    return errors

if __name__ == "__main__":
    problems = check(DATA)
    if problems:
        raise SystemExit("NATIVE GROUP REPORT CONTRACT FAIL:\n" + "\n".join(problems))
    for key, term in (
        ("canonical", "if(people.Count!=ids.Count)return NotFound();"),
        ("contracts", "IReadOnlyList<string> PersonIds"),
        ("vm", "ReferenceEquals(_currentRows?.Invoke(), rows)"),
        ("vm", "report.PersonIds.SequenceEqual(request.PersonIds, StringComparer.Ordinal)"),
        ("adapter", '["personIds"] = new JsonArray('),
        ("adapter", "id != request.PersonIds[index]"),
        ("view", 'SelectionMode="Multiple"'),
        ("viewcode", "vm.SelectRows("),
        ("admin", "GroupReport.Close();"),
        ("adminview", 'DataContext="{Binding GroupReport}"'),
        ("shell", "Administration.GroupReport.Close();"),
        ("tests", "Superseded_async_read_cannot_overwrite_new_selection"),
        ("core", COMMAND), ("release", COMMAND),
    ):
        altered = dict(DATA)
        altered[key] = altered[key].replace(term, "MUTATED_SOURCE_GUARD")
        if not check(altered):
            raise SystemExit(f"Negative group report mutation was accepted: {key}/{term}")
    print("PASS: native group PersonId identity, original SQLCipher, role, WPF selection and Core/Release gates")
