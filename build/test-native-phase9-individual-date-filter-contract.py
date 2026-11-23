#!/usr/bin/env python3
"""Phase 9 slice 4 fail-closed negative-path contract: individual date ranges.

Live C# unit tests independently exercise inclusive range apply/clear,
forged source echoes, stale reads and denial of unauthorized operations.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "original": "src/AlTamam.Application/Bridge/PersonnelReportService.cs",
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelReportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelReportView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeReportDateFiltersPhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml"
}
DATA = {k: (ROOT / name).read_text(encoding="utf-8") for k, name in PATHS.items()}
COMMAND = "python build/test-native-phase9-individual-date-filter-contract.py"
REQUIRED = {
    "original": ('TryReadFilters(payload,out var filters',
                 '["dateFrom"]=filters.From', '["dateTo"]=filters.To',
                 '"yyyy-MM-dd"', 'string.CompareOrdinal(from,to)>0',
                 "Add(\"$from\",From);", "Add(\"$to\",To);"),
    "contracts": ('string DateFrom = "", string DateTo = ""',
                  "public string DateFrom { get; init; }",
                  "public string DateTo { get; init; }"),
    "vm": ("DateFromDraft", "DateToDraft",
           "ApplyDateFiltersAsync(", "ClearDateFiltersAsync(",
           "ValidDateRange(string from, string to)",
           "DateOnly.TryParseExact(", "string.CompareOrdinal(from, to) <= 0",
           "new NativeIndividualReportPages(",
           "DateFrom: from, DateTo: to",
           "report.DateFrom != requested.DateFrom",
           "report.DateTo != requested.DateTo",
           "AppliedDateRangeLabel", "_pending?.Cancel();",
           "version != _generation || linked.IsCancellationRequested",
           "if (!CanRead) { Close(); return; }"),
    "adapter": ('new PersonnelReportService(database)',
                '["dateFrom"] = pages.DateFrom',
                '["dateTo"] = pages.DateTo',
                'RequiredText(data, "dateFrom") != pages.DateFrom',
                'RequiredText(data, "dateTo") != pages.DateTo',
                'DateFrom = RequiredText(data, "dateFrom")',
                'DateTo = RequiredText(data, "dateTo")',
                'await VerifyPersonnelManagerAsync(actor, ct)'),
    "view": ('FlowDirection="RightToLeft"', "Binding DateFromDraft",
             "Binding DateToDraft", "Binding AppliedDateRangeLabel",
             "Binding ApplyDateFiltersCommand",
             "Binding ClearDateFiltersCommand",
             'UpdateSourceTrigger=PropertyChanged',
             "Binding Report.Attendance"),
    "tests": ("Applying_valid_inclusive_dates_resets_all_pages_and_echoes_source_dates",
              "Open_ended_and_equal_boundaries_keep_canonical_inclusive_contract",
              "Invalid_or_reversed_ranges_cannot_query_original_data",
              "Clearing_filters_restores_unbounded_query_not_a_fake_local_subset",
              "Mismatched_date_filter_echo_is_rejected_before_render",
              "Changed_dates_discard_pending_response_from_old_filter",
              "Session_revocation_and_stale_roster_selection_block_date_requests"),
    "core": (COMMAND,),
    "release": (COMMAND,)
}
FORBIDDEN = ("new SqliteConnection(", "INSERT INTO", "DELETE FROM",
             "UPDATE Persons SET", "File.WriteAllBytes(",
             "SaveFileDialog(", "new PersonnelReportService(")


def validate(items):
    problems = []
    for key, tokens in REQUIRED.items():
        for needle in tokens:
            if needle not in items[key]:
                problems.append(f"{key}: missing date filter guarantee: {needle}")
    for key in ("contracts", "vm", "view"):
        for needle in FORBIDDEN:
            if needle in items[key]:
                problems.append(f"{key}: forbidden independent reporter/writer: {needle}")
    try:
        ElementTree.fromstring(items["view"])
    except ElementTree.ParseError as err:
        problems.append(f"view: malformed XAML XML: {err}")
    if items["tests"].count("[Fact]") < 5 or items["tests"].count("[Theory]") < 2:
        problems.append("tests: required red-first xUnit cases absent")
    for key in ("core", "release"):
        lines = items[key].splitlines()
        loc = [i for i, line in enumerate(lines) if line.strip() == COMMAND]
        if len(loc) != 1 or loc[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[loc[0] + 1]
        ):
            problems.append(f"{key}: negative-path guard must fail-closed")
    return problems


if __name__ == "__main__":
    errors = validate(DATA)
    if errors:
        raise SystemExit("NATIVE HISTORIC DATE FILTER CONTRACT FAIL:\n" + "\n".join(errors))
    for key, needle in (
        ("original", '["dateFrom"]=filters.From'),
        ("original", "string.CompareOrdinal(from,to)>0"),
        ("contracts", 'string DateFrom = "", string DateTo = ""'),
        ("vm", "report.DateFrom != requested.DateFrom"),
        ("vm", "DateFrom: from, DateTo: to"),
        ("adapter", '["dateTo"] = pages.DateTo'),
        ("adapter", "await VerifyPersonnelManagerAsync(actor, ct)"),
        ("view", "Binding DateFromDraft"),
        ("view", "Binding ApplyDateFiltersCommand"),
        ("tests", "Changed_dates_discard_pending_response_from_old_filter"),
        ("core", COMMAND), ("release", COMMAND),
    ):
        forged = dict(DATA)
        forged[key] = forged[key].replace(needle, "MUTATED_NEGATIVE_PATH")
        if not validate(forged):
            raise SystemExit(f"Missing regression for mutated {key}: {needle}")
    print("PASS: source-echoed inclusive date filters, original SQLCipher, role and stale response guard")
