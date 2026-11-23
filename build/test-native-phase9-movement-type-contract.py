#!/usr/bin/env python3
"""Phase 9 slice 5: canonical historical movement-type filter negative guard.

The original SQLCipher PersonId report is the sole data authority. This guard
is additive to native C# xUnit and real Windows release E2E acceptance.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
SOURCES = {
    "original": "src/AlTamam.Application/Bridge/PersonnelReportService.cs",
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelReportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelReportView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeReportMovementTypePhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
FILES = {k: (ROOT / p).read_text(encoding="utf-8") for k, p in SOURCES.items()}
COMMAND = "python build/test-native-phase9-movement-type-contract.py"
REQUIRED = {
    "original": (
        "movementType is <0 or >4",
        '["movementType"]=filters.MovementType',
        "($moveType=0 OR MovementType=$moveType)",
        'Add("$moveType",MovementType);',
        "FROM PersonnelMovements WHERE PersonId=$id",
        "FROM AttendanceRecords WHERE PersonId=$id",
        '["movementTotal"]=totalMovements',
    ),
    "contracts": (
        "NativeReportMovementTypeOption(",
        "int MovementType = 0",
        "public int MovementType { get; init; }",
    ),
    "vm": (
        "MovementTypeOptions", 'new(0, "جميع الحركات")',
        'new(1, "نقل داخلي")', 'new(2, "نقل خارجي")',
        'new(3, "فصل")', 'new(4, "عودة للخدمة")',
        "ApplyMovementTypeFilterAsync(", "ClearMovementTypeFilterAsync(",
        "movementType is < 0 or > 4",
        "pages with { MovementType = movementType, MovementPage = 1 }",
        "report.MovementType != requested.MovementType",
        "AppliedMovementTypeLabel",
        "DateFrom = from, DateTo = to", "_pending?.Cancel();",
        "version != _generation || linked.IsCancellationRequested",
    ),
    "adapter": (
        "new PersonnelReportService(database)",
        "pages.MovementType is < 0 or > 4",
        '["movementType"] = pages.MovementType',
        'RequiredNumber(data, "movementType") != pages.MovementType',
        'MovementType = checked((int)RequiredNumber(data, "movementType"))',
        "await VerifyPersonnelManagerAsync(actor, ct)",
    ),
    "view": (
        'FlowDirection="RightToLeft"', "Binding MovementTypeOptions",
        "Binding MovementTypeDraft",
        "Binding ApplyMovementTypeFilterCommand",
        "Binding ClearMovementTypeFilterCommand",
        "Binding AppliedMovementTypeLabel",
        "Binding Report.Movements",
        "مرشح نوع الحركة يغير سجل الحركات فقط",
    ),
    "tests": (
        "Movement_type_filters_original_person_report_and_only_resets_movement_page",
        "Only_real_router_compatible_movement_codes_pass",
        "Invalid_movement_types_never_reach_canonical_service",
        "Source_echo_mismatch_fails_closed",
        "Applying_new_dates_keeps_existing_type",
        "Clearing_type_keeps_dates",
        "Unauthorized_or_stale_person_cannot_read_filtered_movement_history",
        "Old_filter_responses_cannot_replace_latest_results",
        "Movement_filter_choices_are_complete_unambiguous_and_bounded",
    ),
    "core": (COMMAND,),
    "release": (COMMAND,),
}
FORBIDDEN = (
    "new SqliteConnection(", "INSERT INTO", "DELETE FROM",
    "UPDATE Persons SET", "File.WriteAllBytes(", "SaveFileDialog(",
    "new PersonnelReportService("
)


def problems(data):
    issues = []
    for key, needles in REQUIRED.items():
        for needle in needles:
            if needle not in data[key]:
                issues.append(f"{key}: missing safety invariant: {needle}")
    for key in ("contracts", "vm", "view"):
        for needle in FORBIDDEN:
            if needle in data[key]:
                issues.append(f"{key}: forbidden independent reporter/writer: {needle}")
    try:
        ElementTree.fromstring(data["view"])
    except ElementTree.ParseError as error:
        issues.append(f"view: malformed WPF XAML: {error}")
    if data["tests"].count("[Fact]") < 6 or data["tests"].count("[Theory]") < 2:
        issues.append("tests: expected test-first role, pagination, replay and bad-input cases")
    for key in ("core", "release"):
        lines = data[key].splitlines()
        positions = [i for i, line in enumerate(lines) if line.strip() == COMMAND]
        if len(positions) != 1 or positions[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[positions[0] + 1]
        ):
            issues.append(f"{key}: contract guard must run once with fatal exit")
    return issues


if __name__ == "__main__":
    invalid = problems(FILES)
    if invalid:
        raise SystemExit("NATIVE MOVEMENT TYPE GUARD FAIL:\n" + "\n".join(invalid))
    for key, needle in (
        ("original", "($moveType=0 OR MovementType=$moveType)"),
        ("original", '["movementType"]=filters.MovementType'),
        ("contracts", "int MovementType = 0"),
        ("vm", "report.MovementType != requested.MovementType"),
        ("vm", "pages with { MovementType = movementType, MovementPage = 1 }"),
        ("adapter", '["movementType"] = pages.MovementType'),
        ("adapter", 'RequiredNumber(data, "movementType") != pages.MovementType'),
        ("view", "Binding MovementTypeDraft"),
        ("view", "Binding AppliedMovementTypeLabel"),
        ("tests", "Old_filter_responses_cannot_replace_latest_results"),
        ("core", COMMAND), ("release", COMMAND),
    ):
        forged = dict(FILES)
        forged[key] = forged[key].replace(needle, "INJECTED_BAD_CONTRACT")
        if not problems(forged):
            raise SystemExit(f"Negative regression failed: {key}, {needle}")
    print("PASS: movement-only filter uses canonical PersonId SQLCipher and rejects bad/late response")
