#!/usr/bin/env python3
"""Test-first, fail-closed native additive-only XLSX import guard.

The original ExcelPersonnelImportService owns parsing, duplicate detection,
immutable identity snapshots, one-use tokens, sites and SQLCipher writes.
This structural guard is supplemental to xUnit, WPF real E2E, package
acceptance and SHA-256; mandatory in BOTH Windows workflows.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contract": "src/AlTamam.NativePresentation/NativeExcelPersonnelImportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeExcelPersonnelImportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "original": "src/AlTamam.Application/Bridge/ExcelPersonnelImportService.cs",
    "tests": "tests/AlTamam.UnitTests/NativeExcelPersonnelImportPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/ExcelPersonnelImportIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "ui": "src/AlTamam.NativeDesktop/Views/NativeExcelPersonnelImportView.xaml",
    "filepicker": "src/AlTamam.NativeDesktop/Views/NativeExcelPersonnelImportView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k: (ROOT / p).read_text(encoding="utf-8") for k, p in PATHS.items()}


def inspect(files):
    errs = []

    def require(key, token):
        if token not in files[key]:
            errs.append(f"{key}: missing import safety constraint: {token}")

    for key in ("ui", "roster"):
        try:
            ET.fromstring(files[key])
        except ET.ParseError as exc:
            errs.append(f"{key}: invalid WPF XAML: {exc}")

    for snippet in (
        "INativeExcelPersonnelImportBackend", "StageAndPreviewAsync(",
        "CommitAsync(", "NativeExcelImportReview(", "NativeExcelImportReceipt(",
        "NativeExcelImportPerson(", "NativeExcelImportIssue(",
        "NativeExcelImportSite(", "NativeExcelImportCollision(",
        "NativeExcelImportMatch("
    ):
        require("contract", snippet)

    for snippet in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor",
        "workbook is not { Length: >= 1 and <= 160 * 1024 }",
        "version != _generation || linked.IsCancellationRequested",
        "_review = null", "_reviewedPeople = false",
        "_approvedSites = false", "_reviewedSimilarNames = false",
        "_review.NewPeople.Count == _review.NewCount",
        "_review.NewSites.Count == 0 || _approvedSites",
        "_review.SimilarNameCount == 0 || _reviewedSimilarNames",
        "review.Collisions.Count != review.SimilarNameCount",
        "review.NewCount is < 0 or > 500",
        "review.CustomUpdateCount != 0",
        "await _backend.StageAndPreviewAsync(",
        "_attempted = true",
        'Guid.NewGuid().ToString("N")',
        "await _backend.CommitAsync(",
        "outcome.CustomUpdatedCount != 0",
        "_uncertainActor = actor", "ForceCancel()"
    ):
        require("vm", snippet)

    for snippet in (
        "INativeExcelPersonnelImportBackend",
        "new ExcelPersonnelImportService(database)",
        "original.SelectAsync(new JsonObject",
        "original.AnalyzeAsync(",
        "original.PreviewAsync(",
        ".CommitAsync(new JsonObject",
        '["base64"] = base64',
        '["updateExistingCustomFields"] = false',
        '["approveCustomUpdates"] = false',
        "VerifyPersonnelManagerAsync(actor, ct)",
        '["workbookToken"] = workbookToken',
        '["previewToken"] = reviewed.PreviewToken',
        '["operationId"] = operationId',
        "JsonNode.DeepEquals(analysis[field], preview[field])",
        'RequiredBoolean(preview, "requiresExplicitConfirmation")',
        "fresh is < 0 or > 500",
        "similarCount != collisions.Count",
        "new NativeExcelImportCollision(",
        "checked((int)RequiredNumber(result, \"createdCount\"))",
        "receipt.CustomUpdatedCount != 0"
    ):
        require("adapter", snippet)

    for snippet in (
        "private const int MaxWorkbookBytes = 160 * 1024;",
        "private const int MaxRecords = 500;",
        "public async Task<BridgeResponse> SelectAsync(",
        "public async Task<BridgeResponse> AnalyzeAsync(",
        "public async Task<BridgeResponse> PreviewAsync(",
        "public async Task<BridgeResponse> CommitAsync(",
        "WorkbookOperation", "PlanOperation",
        "ReadStagedAsync(", "BridgePreviewTokens.Issue(",
        "BridgePreviewTokens.ConsumeAsync(",
        "MutationIdempotency.StartAsync(",
        "MutationIdempotency.StoreAsync(",
        "ExecuteInWriteTransactionAsync(",
        "NormalizedServiceNumberA=$number",
        "reason=\"«الرقم (ع)» مكرر داخل الملف.\"",
        "ImportNameCollisionHints.QueryAsync(",
        "newSites.DeepClone()", "CreatedSitesCount",
        "PERSONNEL_IMPORT"
    ):
        require("original", snippet)

    for snippet in (
        "Missing_actor_and_supervisor_cannot_open_or_import",
        "Only_small_local_xlsx_and_exact_canonical_preview_are_accepted",
        "New_sites_and_similar_name_warnings_need_independent_consent",
        "Duplicate_service_number_and_archived_identity_remain_issues_not_automatic_merges",
        "All_rows_must_be_visible_and_limit_500_in_native_import",
        "Existing_custom_field_updates_are_rejected_even_if_preview_was_forged",
        "New_file_invalidates_all_old_consent_and_original_preview",
        "Logout_during_very_late_import_preview_drops_results",
        "Missing_or_forged_commit_receipt_never_retries_in_same_session"
    ):
        require("tests", snippet)
    if files["tests"].count("[Fact]") < 9:
        errs.append("tests: nine test-first native import cases required")

    for snippet in (
        "Import_AddsNewPeopleAndIdentityHistoryOnly_Audited_WithoutAttendance",
        "DuplicateWithinFileAndPreviouslyImportedNumber_AreReportedButNeverOverwritten",
        "NewSiteCreatedAutomaticallyOnConfirmedImport_AndAuditedOnce",
        "NameCollisionAdvisory_IsNotAnIdentityDecisionOrAutomaticMerge",
        "ArchivedDismissedNumber_NeverSilentlyReturnsOrCreatesNewIdentityViaExcel",
        "CommittedExcelImport_LostResponseRetry_ReturnsSameBatchAndOneAudit"
    ):
        require("integration", snippet)

    for snippet in (
        "ExcelImport = new NativeExcelPersonnelImportViewModel()",
        "OpenExcelImportCommand",
        "ExcelImport.Open()", "ExcelImport.AttachBackend(",
        "ExcelImport.CommittedAndClosed",
        "ExcelImport.ForceCancel()", "!ExcelImport.IsOpen"
    ):
        require("admin", snippet)
    require("shell", "Administration.ExcelImport.ForceCancel()")
    for snippet in (
        'Click="PickWorkbook_Click"',
        'IsEnabled="{Binding CanSelect}"',
        'ItemsSource="{Binding Review.NewPeople}"',
        'ItemsSource="{Binding Review.Issues}"',
        'ItemsSource="{Binding Review.NewSites}"',
        'ItemsSource="{Binding Review.Collisions}"',
        'IsChecked="{Binding ReviewedPeopleAndIssues, Mode=TwoWay',
        'IsChecked="{Binding ApproveCreatingSites, Mode=TwoWay',
        'IsChecked="{Binding AcknowledgeSimilarNames, Mode=TwoWay',
        'Command="{Binding CommitCommand}"',
        "الرقم (ع)", "الصفوف المرفوضة"
    ):
        require("ui", snippet)
    for snippet in (
        "OpenFileDialog", 'Filter = "كشوف Excel (*.xlsx)|*.xlsx"',
        "await File.ReadAllBytesAsync(",
        "vm.LoadWorkbookAsync(file.Name, bytes)",
        "CryptographicOperations.ZeroMemory(bytes)"
    ):
        require("filepicker", snippet)
    require("roster", 'Command="{Binding OpenExcelImportCommand}"')
    require("roster", 'DataContext="{Binding ExcelImport}"')

    for key in ("core", "release"):
        cmd = "python build/test-native-phase8-excel-personnel-import-contract.py"
        lines = files[key].splitlines()
        matches = [i for i, text in enumerate(lines) if text.strip() == cmd]
        if len(matches) != 1:
            errs.append(f"{key}: import integrity guard should occur exactly once")
        elif matches[0]+1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[matches[0]+1]
        ):
            errs.append(f"{key}: XLSX import guard must fail immediately")

    for key in ("contract", "vm", "ui", "filepicker"):
        for unsafe in (
            "new SqliteConnection(", "UPDATE Persons SET",
            "UPDATE PersonAssignments", "INSERT INTO Persons",
            "new ExcelPersonnelImportService(", "ExecuteInWriteTransactionAsync("
        ):
            if unsafe in files[key]:
                errs.append(f"{key}: second native SQL writer forbidden: {unsafe}")
    return errs


if __name__ == "__main__":
    errors = inspect(TEXT)
    if errors:
        raise SystemExit("PHASE8 XLSX IMPORT FAIL:\n" + "\n".join(errors))
    for key, needle in (
        ("adapter", '["updateExistingCustomFields"] = false'),
        ("adapter", '["approveCustomUpdates"] = false'),
        ("adapter", "JsonNode.DeepEquals(analysis[field], preview[field])"),
        ("original", "BridgePreviewTokens.ConsumeAsync("),
        ("vm", "_attempted = true"),
        ("vm", "review.Collisions.Count != review.SimilarNameCount"),
        ("admin", "ExcelImport.AttachBackend("),
        ("shell", "Administration.ExcelImport.ForceCancel()"),
        ("ui", 'IsChecked="{Binding ApproveCreatingSites, Mode=TwoWay'),
        ("filepicker", "CryptographicOperations.ZeroMemory(bytes)"),
        ("tests", "Missing_or_forged_commit_receipt_never_retries_in_same_session"),
        ("core", "python build/test-native-phase8-excel-personnel-import-contract.py"),
        ("release", "python build/test-native-phase8-excel-personnel-import-contract.py")
    ):
        damaged = dict(TEXT)
        damaged[key] = damaged[key].replace(needle, "REMOVED_NEGATIVE_GUARD")
        if not inspect(damaged):
            raise SystemExit(f"No negative regression for {key}: {needle}")
    print("PHASE8 XLSX IMPORT PASS: original additive SQLCipher, full 500-person review, no overwrite, both Windows gates")
