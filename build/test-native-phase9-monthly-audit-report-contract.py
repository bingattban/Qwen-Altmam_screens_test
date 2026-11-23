#!/usr/bin/env python3
"""Native screen_025 slice 3: original monthly totals and immutable audit guard.

Extends tests-first C# behavior regression; both Windows workflows run this
before packaging. It does not assert customer readiness or route parity.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "original": "src/AlTamam.Application/Bridge/PersonnelReportService.cs",
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelReportContracts.cs",
    "viewmodel": "src/AlTamam.NativePresentation/NativePersonnelReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelReportView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeMonthlyHistoryAndAuditPhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
SOURCE = {key: (ROOT / path).read_text(encoding="utf-8") for key, path in PATHS.items()}
COMMAND = "python build/test-native-phase9-monthly-audit-report-contract.py"
REQUIRED = {
    "original": ("FROM AttendanceRecords WHERE PersonId=$id",
                 "GROUP BY SUBSTR(AttendanceLocalDate,1,7),StatusId,StatusNameSnapshot",
                 "FROM AuditEvents", "TargetEntityId=$id",
                 '["monthlyTotals"]=monthly', '["audit"]=events',
                 '["auditPage"]=auditPage', '["auditTotal"]=audits',
                 '["auditHasNext"]=' ),
    "contracts": ("NativePersonnelReportMonthSummary(",
                  "NativePersonnelReportAuditRecord(",
                  "int AuditPage = 1", "MonthlyTotals { get; init; }",
                  "AuditTotal { get; init; }",
                  "AuditHasNext { get; init; }"),
    "viewmodel": ("NextAuditAsync(", "PreviousAuditAsync(",
                  "AuditPageLabel", "report.AuditPage != requested.AuditPage",
                  "report.AuditHasNext !=",
                  "ValidPageCount(report.Audit.Count, report.AuditTotal, requested.AuditPage)",
                  "report.MonthlyTotals.Sum(month => month.Count) != report.AttendanceTotal",
                  "month.Period + \"-01\"", "month.Count <= 0",
                  "item.UsernameSnapshot", "version != _generation",
                  "_pending?.Cancel();"),
    "adapter": ("new PersonnelReportService(database)",
                '["auditPage"] = pages.AuditPage',
                'RequiredNumber(data, "auditPage") != pages.AuditPage',
                'RequiredArray(data, "monthlyTotals")',
                'RequiredArray(data, "audit")',
                'RequiredNumber(data, "auditTotal")',
                'RequiredBoolean(data, "auditHasNext")',
                "await VerifyPersonnelManagerAsync(actor, ct)"),
    "view": ('FlowDirection="RightToLeft"', "Binding Report.MonthlyTotals",
             "Binding Report.Audit", "Binding NextAuditCommand",
             "Binding PreviousAuditCommand", 'IsReadOnly="True"',
             "Binding UsernameSnapshot", "Binding FullNameSnapshot"),
    "tests": ("Monthly_totals_preserve_source_periods_and_status_names",
              "Audit_has_own_100_row_pagination_without_mutating_other_pages",
              "Historical_executor_never_substitutes_current_actor_or_person",
              "Forged_monthly_or_audit_evidence_is_never_displayed",
              "Stale_row_or_unauthorized_actor_cannot_open_audit",
              "Logout_discards_late_audit_page_results"),
    "core": (COMMAND,),
    "release": (COMMAND,),
}
FORBIDDEN = ("new SqliteConnection(", "INSERT INTO", "DELETE FROM",
             "File.WriteAllBytes(", "SaveFileDialog(", "UPDATE Persons SET")


def validate(files):
    errors = []
    for key, tokens in REQUIRED.items():
        for token in tokens:
            if token not in files[key]:
                errors.append(f"{key}: missing original report invariant: {token}")
    for key in ("contracts", "viewmodel", "view"):
        for token in FORBIDDEN:
            if token in files[key]:
                errors.append(f"{key}: native report may not write: {token}")
    try:
        ElementTree.fromstring(files["view"])
    except ElementTree.ParseError as exc:
        errors.append(f"view: malformed XML: {exc}")
    if files["tests"].count("[Fact]") < 5 or "[Theory]" not in files["tests"]:
        errors.append("tests: test-first monthly/audit cases absent")
    for key in ("core", "release"):
        lines = files[key].splitlines()
        indexes = [i for i, line in enumerate(lines) if line.strip() == COMMAND]
        if len(indexes) != 1 or indexes[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[indexes[0] + 1]
        ):
            errors.append(f"{key}: guard must run exactly once with nonzero-exit failure")
    return errors


if __name__ == "__main__":
    errors = validate(SOURCE)
    if errors:
        raise SystemExit("NATIVE MONTHLY/AUDIT REPORT FAIL:\n" + "\n".join(errors))
    for key, needle in (
        ("original", "GROUP BY SUBSTR(AttendanceLocalDate,1,7),StatusId,StatusNameSnapshot"),
        ("original", "FROM AuditEvents"),
        ("contracts", "NativePersonnelReportAuditRecord("),
        ("viewmodel", "report.AuditPage != requested.AuditPage"),
        ("viewmodel", "report.MonthlyTotals.Sum(month => month.Count) != report.AttendanceTotal"),
        ("adapter", '["auditPage"] = pages.AuditPage'),
        ("adapter", "await VerifyPersonnelManagerAsync(actor, ct)"),
        ("view", "Binding Report.MonthlyTotals"),
        ("view", "Binding Report.Audit"),
        ("tests", "Logout_discards_late_audit_page_results"),
        ("core", COMMAND),
        ("release", COMMAND),
    ):
        replaced = dict(SOURCE)
        replaced[key] = replaced[key].replace(needle, "FORGED_BY_NEGATIVE_PATH_TEST")
        if not validate(replaced):
            raise SystemExit(f"Missing negative-path enforcement: {key} {needle}")
    print("PASS: original immutable monthly aggregates and paged actor audit evidence")
