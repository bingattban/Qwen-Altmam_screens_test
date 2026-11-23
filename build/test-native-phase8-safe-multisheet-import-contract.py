#!/usr/bin/env python3
"""Phase 8 XLSX all-sheets extension: fail-closed structural negative tests.

This is an early Windows Core AND Windows Release guard, additional to the
native C# xUnit tests, actual SQLCipher import integration, WPF/WebView2 E2E
before packaging, four package acceptances and independent SHA-256 audit.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contract": "src/AlTamam.NativePresentation/NativeExcelPersonnelImportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeExcelPersonnelImportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "original": "src/AlTamam.Application/Bridge/ExcelPersonnelImportService.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeExcelPersonnelImportView.xaml",
    "unit": "tests/AlTamam.UnitTests/NativeSafeMultisheetImportPhase8Tests.cs",
    "base_tests": "tests/AlTamam.UnitTests/NativeExcelPersonnelImportPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/ExcelPersonnelImportIntegrationTests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
FILES = {k: (ROOT / path).read_text(encoding="utf-8")
         for k, path in PATHS.items()}


def problems(src):
    errors = []

    def need(key, substring):
        if substring not in src[key]:
            errors.append(f"{key}: missing safe multisheet invariant {substring}")

    try:
        ET.fromstring(src["xaml"])
    except ET.ParseError as exc:
        errors.append(f"xaml: invalid markup: {exc}")

    for t in (
        "bool allSheets, CancellationToken ct = default)",
        "if (allSheets)",
        'throw new NotSupportedException(',
        "return StageAndPreviewAsync(actor, fileName, workbook, ct)"
    ):
        need("contract", t)
    for t in (
        "public bool ImportAllSheets",
        "if (!CanSelect || _allSheets == value) return",
        "_allSheets = value;",
        "++_generation;",
        "_pending?.Cancel();",
        "_review = null;",
        "_receipt = null;",
        "_reviewedPeople = false;",
        "_approvedSites = false;",
        "_reviewedSimilarNames = false;",
        "_allSheets = false;",
        "StageAndPreviewAsync(",
        "actor, fileName, workbook, _allSheets, linked.Token)",
        "preview is { NewCount: > 500 }",
        "review.NewCount is < 0 or > 500",
        "review.NewPeople.Count != review.NewCount",
        "review.CustomUpdateCount != 0",
        "nameof(ImportAllSheets)",
        "public void ForceCancel()"
    ):
        need("vm", t)
    for t in (
        "bool allSheets, CancellationToken ct = default)",
        "StageAndPreviewAsync(actor, fileName, workbook, false, ct)",
        '["allSheets"] = allSheets',
        '["updateExistingCustomFields"] = false',
        "selectedWorksheets",
        "permittedSheets.Length is < 1 or > 50",
        "(!allSheets && permittedSheets.Length != 1)",
        "permittedSheets.Distinct(StringComparer.Ordinal)",
        "permittedSheets.ToHashSet(StringComparer.Ordinal)",
        "people.Any(p => !allowed.Contains(p.Worksheet))",
        "errors.Any(e => !allowed.Contains(e.Worksheet))",
        "hints.Any(h => !allowed.Contains(h.Worksheet))",
        "fresh is < 0 or > 500",
        "accepted.Count != fresh",
        "customUpdated != 0",
        "RequiredNumber(preview, \"newCount\")"
    ):
        need("adapter", t)
    for t in (
        'options?["allSheets"]?.GetValue<bool>()==true',
        "ExcelWorksheetResolver.Resolve(zip,SafeXml)",
        "PreviewNames(accepted)",
        "accepted.Take(MaxRecords)",
        "selectedWorksheets",
        "WorkbookOperation",
        "PlanOperation"
    ):
        need("original", t)

    for t in (
        'IsChecked="{Binding ImportAllSheets, Mode=TwoWay',
        'IsEnabled="{Binding CanSelect}"',
        "الورقة الأولى فقط",
        "جميع أوراق",
        "500 اسم جديد"
    ):
        need("xaml", t)
    for t in (
        "Default_first_worksheet_mode_preserves_original_safe_preview",
        "Explicit_all_worksheets_uses_original_canonical_parser_and_shows_every_accepted_person",
        "Changing_sheet_mode_revokes_previous_signed_review_and_all_explicit_approvals",
        "Exactly_500_accepted_people_are_individually_visible_and_committed_once",
        "Truncated_500_person_preview_cannot_be_approved",
        "All_worksheets_above_500_accepted_people_are_rejected_even_if_first_500_are_visible",
        "Forged_multisheet_total_or_existing_person_update_fails_closed_without_write",
        "Logout_during_multiworksheet_preview_cancels_late_reply_and_future_write",
        "Unauthorized_and_supervisor_cannot_select_multiworksheet_mode_or_stage_file"
    ):
        need("unit", t)
    if src["unit"].count("[Fact]") < 9:
        errors.append("unit: nine tests must cover 500 accepted, 501 rejected, and truncated previews")
    for t in (
        "All_rows_must_be_visible_and_limit_500_in_native_import",
        "Existing_custom_field_updates_are_rejected_even_if_preview_was_forged"
    ):
        need("base_tests", t)
    for t in (
        "MultipleSheets_CustomHeaderMapping_RejectsOnlyInvalidRow_WithWorksheetEvidence",
        "WorkbookRelationshipOrder_OverridesPhysicalSheetNumbering"
    ):
        need("integration", t)

    for key in ("core", "release"):
        target = "python build/test-native-phase8-safe-multisheet-import-contract.py"
        lines = src[key].splitlines()
        sites = [i for i, l in enumerate(lines) if l.strip() == target]
        if len(sites) != 1:
            errors.append(f"{key}: dedicated guard must run exactly once")
        elif sites[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[sites[0] + 1]
        ):
            errors.append(f"{key}: dedicated guard must immediately fail closed")

    for key in ("vm", "contract", "xaml"):
        for unsafe in (
            "UPDATE Persons SET", "new SqliteConnection(",
            "DELETE FROM AttendanceRecords",
            "new ExcelPersonnelImportService(", "ExecuteInWriteTransactionAsync("
        ):
            if unsafe in src[key]:
                errors.append(f"{key}: duplicate native SQL writer: {unsafe}")

    return errors


if __name__ == "__main__":
    issues = problems(FILES)
    if issues:
        raise SystemExit("SAFE MULTISHEET XLSX FAIL:\n" + "\n".join(issues))
    for key, needle in (
        ("contract", "if (allSheets)"),
        ("vm", "preview is { NewCount: > 500 }"),
        ("vm", "_reviewedSimilarNames = false;"),
        ("adapter", '["allSheets"] = allSheets'),
        ("adapter", "people.Any(p => !allowed.Contains(p.Worksheet))"),
        ("original", 'options?["allSheets"]?.GetValue<bool>()==true'),
        ("xaml", 'IsChecked="{Binding ImportAllSheets, Mode=TwoWay'),
        ("unit", "All_worksheets_above_500_accepted_people_are_rejected_even_if_first_500_are_visible"),
        ("core", "python build/test-native-phase8-safe-multisheet-import-contract.py"),
        ("release", "python build/test-native-phase8-safe-multisheet-import-contract.py"),
    ):
        mutated = dict(FILES)
        mutated[key] = mutated[key].replace(needle, "DELETED_FOR_NEGATIVE_PATH")
        if not problems(mutated):
            raise SystemExit(f"Broken negative-path check: {key}: {needle}")
    print("PASS: safe all-worksheet native XLSX, all visible identities <=500, both Windows gates")
