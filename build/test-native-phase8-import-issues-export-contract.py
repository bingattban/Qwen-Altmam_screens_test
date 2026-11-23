#!/usr/bin/env python3
"""Phase 8 slice 16: fail-closed canonical XLSX rejected-row export contract.

Supplementary structural guard: the real C# xUnit cases, SQLCipher tests,
WPF/WebView2 E2E and four packaged customer acceptance gates are mandatory too.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "canonical": "src/AlTamam.Application/Bridge/ExcelImportOutcomeService.cs",
    "contract": "src/AlTamam.NativePresentation/NativeImportOutcomeHistoryContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeImportOutcomeHistoryViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeImportOutcomeHistoryView.xaml",
    "test": "tests/AlTamam.UnitTests/NativeImportIssuesExportPhase8Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
FILES = {key: (ROOT / path).read_text(encoding="utf-8") for key, path in PATHS.items()}
REQUIRED = {
    "canonical": (
        "public async Task<BridgeResponse> ExportIssuesAsync(",
        "CreatedByUserId=$actor AND Status='COMMITTED'",
        "new OutputDirectorySettings(_db).GetAsync(ct)",
        "OutputDirectorySettings.IsSafeDirectory(",
        "FileMode.CreateNew", "ReportWorkbookCommit.VerifyFlushAndMove(",
        "issues.Count > 500", "ORDER BY RowNumber,IssueId LIMIT 501",
        "inlineStr",
    ),
    "contract": (
        "Task<NativeImportIssuesExportReceipt> ExportIssuesAsync(",
        "throw new NotSupportedException(",
        "record NativeImportIssuesExportReceipt(",
    ),
    "vm": (
        "public bool AcknowledgeSavedIssues", "public bool CanExport =>",
        "_approvedIssues &&", "_detail is { Issues.Count: > 0 }",
        "public async Task ExportIssuesAsync(",
        "_exportAttempted = true;",
        "_backend.ExportIssuesAsync(",
        "result.IssueCount != detail.Issues.Count",
        "Path.IsPathFullyQualified(result.FilePath)",
        "_uncertainActor = actor;",
        "private void RevokeExportState()",
        "_exportPending?.Cancel();",
        "public void CloseDetails()", "ExportIssuesCommand",
    ),
    "adapter": (
        "public async Task<NativeImportIssuesExportReceipt> ExportIssuesAsync(",
        "new ExcelImportOutcomeService(database)",
        ".ExportIssuesAsync(new JsonObject { [\"importBatchId\"] = batchId }",
        "actor.UserId, ct)",
        "await VerifyPersonnelManagerAsync(actor, ct)",
        "receipt.ImportBatchId != batchId", "receipt.Format != \"xlsx\"",
        "Path.IsPathFullyQualified(receipt.FilePath)",
    ),
    "xaml": (
        "Binding AcknowledgeSavedIssues, Mode=TwoWay",
        "Binding ExportIssuesCommand",
        "Binding ExportError",
        "Binding ExportStateSummary",
        "Binding IsExportBusy",
        "مجلد التقارير المحدد مسبقا",
    ),
    "test": (
        "Browsing_and_reading_do_not_export_without_explicit_issue_acknowledgement",
        "Approved_owner_batch_exports_exactly_once_from_original_service",
        "Zero_rejected_rows_cannot_create_an_empty_spreadsheet",
        "Forged_export_receipt_is_refused_and_no_second_write_is_sent",
        "Lost_response_cannot_be_replayed_after_closing_and_reopening_same_session",
        "Logout_during_export_discards_late_success_as_unverified",
        "Anonymous_and_supervisor_cannot_export_a_batch",
    ),
    "core": ("python build/test-native-phase8-import-issues-export-contract.py",),
    "release": ("python build/test-native-phase8-import-issues-export-contract.py",),
}
FORBIDDEN = {
    "contract": ("SaveFileDialog", "new SqliteConnection(", "INSERT INTO"),
    "vm": ("SaveFileDialog", "File.WriteAllBytes(", "new SqliteConnection("),
    "xaml": ("SaveFileDialog",),
}


def problems(src):
    errors = []
    for key, tokens in REQUIRED.items():
        for token in tokens:
            if token not in src[key]:
                errors.append(f"{key}: missing export invariant {token}")
    try:
        ElementTree.fromstring(src["xaml"])
    except ElementTree.ParseError as exc:
        errors.append(f"xaml: invalid WPF XML {exc}")
    if src["test"].count("[Fact]") < 7:
        errors.append("test: missing seven test-first cases")
    for key, tokens in FORBIDDEN.items():
        for token in tokens:
            if token in src[key]:
                errors.append(f"{key}: prohibited duplicate writer/path selection: {token}")
    for key in ("core", "release"):
        lines = src[key].splitlines()
        marker = "python build/test-native-phase8-import-issues-export-contract.py"
        positions = [i for i, line in enumerate(lines) if line.strip() == marker]
        if len(positions) != 1 or positions[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[positions[0] + 1]
        ):
            errors.append(f"{key}: independent fail-closed export guard missing")
    return errors


if __name__ == "__main__":
    failures = problems(FILES)
    if failures:
        raise SystemExit("NATIVE IMPORT ISSUES EXPORT FAIL:\n" + "\n".join(failures))
    for key, needle in (
        ("canonical", "CreatedByUserId=$actor AND Status='COMMITTED'"),
        ("canonical", "FileMode.CreateNew"),
        ("vm", "_approvedIssues &&"),
        ("vm", "_exportAttempted = true;"),
        ("vm", "_uncertainActor = actor;"),
        ("adapter", "await VerifyPersonnelManagerAsync(actor, ct)"),
        ("adapter", "Path.IsPathFullyQualified(receipt.FilePath)"),
        ("xaml", "Binding ExportIssuesCommand"),
        ("test", "Lost_response_cannot_be_replayed_after_closing_and_reopening_same_session"),
        ("core", "python build/test-native-phase8-import-issues-export-contract.py"),
        ("release", "python build/test-native-phase8-import-issues-export-contract.py"),
    ):
        broken = dict(FILES)
        broken[key] = broken[key].replace(needle, "REMOVED_IN_NEGATIVE_PATH")
        if not problems(broken):
            raise SystemExit(f"Broken negative guard: {key}: {needle}")
    print("PASS: canonical owner-scoped XLSX export, explicit review, no overwrite or retry")
