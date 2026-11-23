#!/usr/bin/env python3
"""Fail-closed phase8 native XLSX import guard, BOTH Windows Core and Release.

Supplements required xUnit SQLCipher integration, real WPF/WebView2 E2E,
test-before-build release order, four packaged acceptances and SHA-256 audit.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contract": "src/AlTamam.NativePresentation/NativePersonnelExcelImportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelExcelImportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/ExcelPersonnelImportService.cs",
    "outcome": "src/AlTamam.Application/Bridge/ExcelImportOutcomeService.cs",
    "test": "tests/AlTamam.UnitTests/NativePersonnelExcelImportPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/ExcelPersonnelImportIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativePersonnelExcelImportView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelExcelImportView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k: (ROOT / path).read_text(encoding="utf-8") for k, path in PATHS.items()}


def validate(data):
    issues = []

    def require(key, snippet):
        if snippet not in data[key]:
            issues.append(f"{key}: missing original XLSX safeguard: {snippet}")

    for key in ("xaml", "roster"):
        try:
            ET.fromstring(data[key])
        except ET.ParseError as e:
            issues.append(f"{key}: invalid RTL WPF XML: {e}")

    for snippet in (
        "INativePersonnelExcelImportBackend",
        "StageWorkbookAsync(", "PreviewWorkbookAsync(", "CommitWorkbookAsync(",
        "NativePersonnelExcelWorkbookSelection(", "NativePersonnelExcelImportPreview(",
        "NativePersonnelExcelCandidate(", "NativePersonnelExcelIssue(",
        "NativePersonnelExcelSite(", "NativePersonnelExcelImportReceipt("
    ):
        require("contract", snippet)

    for snippet in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor",
        "160 * 1024",
        "StageWorkbookAsync(", "PreviewWorkbookAsync(", "CommitWorkbookAsync(",
        "_attempted = true",
        'Guid.NewGuid().ToString("N")',
        "preview.CustomUpdateCount != 0",
        "_preview.IssueCount == 0 || _rejectedReviewed",
        "_preview.NewSites.Count == 0 || _newSitesApproved",
        "_preview.SimilarNameCount == 0 || _similarNamesReviewed",
        "_preview.WorkbookToken == _selection.WorkbookToken",
        "result.CreatedCount != preview.NewCount",
        "result.RejectedCount != preview.IssueCount",
        "result.CustomUpdatedCount != 0",
        "result.CreatedSitesCount != preview.NewSites.Count",
        "_uncertainActor = actor",
        "ForceCancel()", "DiscardSelection()"
    ):
        require("vm", snippet)

    for snippet in (
        "INativePersonnelExcelImportBackend",
        "new ExcelPersonnelImportService(database)",
        ".SelectAsync(new JsonObject", ".PreviewAsync(new JsonObject",
        ".CommitAsync(new JsonObject",
        '["fileName"] = filename',
        '["base64"] = base64',
        '["allSheets"] = allSheets',
        '["updateExistingCustomFields"] = false',
        '["workbookToken"] = selection.WorkbookToken',
        '["previewToken"] = preview.PreviewToken',
        '["operationId"] = operationId',
        '["approveCustomUpdates"] = false',
        "actor.UserId, actor.Username, ct",
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSitesPrincipalAsync(actor, ct)",
        'RequiredNumber(data, "rowCount")',
        'RequiredNumber(data, "newCount")',
        'RequiredNumber(data, "issueCount")',
        'RequiredNumber(data, "customUpdateCount")',
        'RequiredNumber(data, "similarNameCount")',
        'RequiredArray(data, "acceptedPreview")',
        'RequiredArray(data, "issues")',
        'RequiredArray(data, "newSites")',
        "createdSitesCount",
        "createdCount",
        "rejectedCount"
    ):
        require("adapter", snippet)

    for snippet in (
        "public async Task<BridgeResponse> SelectAsync(",
        "public async Task<BridgeResponse> AnalyzeAsync(",
        "public async Task<BridgeResponse> PreviewAsync(",
        "public async Task<BridgeResponse> CommitAsync(",
        "private const int MaxWorkbookBytes = 160 * 1024",
        "private const int MaxRecords = 500",
        "updateExistingCustomFields",
        "BridgePreviewTokens.Issue(",
        "BridgePreviewTokens.ConsumeAsync(",
        "MutationIdempotency.StartAsync(",
        "MutationIdempotency.StoreAsync(",
        "ExecuteInWriteTransactionAsync(",
        "ImportIssues", "ImportBatches",
        "PERSONNEL_IMPORT",
        "PersonIdentityHistory",
        "NewSitesAsync(",
        "ImportNameCollisionHints.QueryAsync("
    ):
        require("canonical", snippet)

    for snippet in (
        "QueryAsync(", "CreatedByUserId=$actor",
        "ImportIssues", "ExportIssuesAsync("
    ):
        require("outcome", snippet)

    for snippet in (
        "OpenExcelImportCommand", "ExcelImport = new NativePersonnelExcelImportViewModel()",
        "ExcelImport.AttachBackend(", "ExcelImport.CommittedAndClosed",
        "ExcelImport.ForceCancel()",
        "!ExcelImport.IsOpen",
        "CanOpenExcelImport()"
    ):
        require("admin", snippet)
    require("shell", "Administration.ExcelImport.ForceCancel()")

    for snippet in (
        'IsChecked="{Binding ImportAllSheets, Mode=TwoWay',
        'Click="PickWorkbook_OnClick"',
        'Command="{Binding PreviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'IsChecked="{Binding PersonnelApproved, Mode=TwoWay',
        'IsChecked="{Binding RejectedRowsReviewed, Mode=TwoWay',
        'IsChecked="{Binding NewSitesApproved, Mode=TwoWay',
        'IsChecked="{Binding SimilarNamesReviewed, Mode=TwoWay',
        'ItemsSource="{Binding Preview.AcceptedRows}"',
        'ItemsSource="{Binding Preview.Issues}"',
        'ItemsSource="{Binding Preview.NewSites}"',
        "إضافة آمنة للأفراد الجدد فقط",
        "160 كيلوبايت", "500 صف"
    ):
        require("xaml", snippet)
    for snippet in (
        "OpenFileDialog", "ShowDialog()",
        "CheckFileExists = true",
        "new FileInfo(dialog.FileName)",
        "file.Length is < 1 or > 160 * 1024",
        "File.ReadAllBytesAsync(file.FullName)",
        "CryptographicOperations.ZeroMemory(workbookBytes)"
    ):
        require("view", snippet)
    require("roster", 'Command="{Binding OpenExcelImportCommand}"')
    require("roster", 'DataContext="{Binding ExcelImport}"')

    for snippet in (
        "Only_original_authenticated_manager_can_open_or_stage_workbook",
        "Non_xlsx_empty_or_oversize_files_fail_before_canonical_staging",
        "Staging_and_preview_never_commit_existing_people",
        "Rejected_rows_new_sites_and_similar_names_each_need_explicit_consent",
        "Forged_or_inconsistent_preview_counts_or_tokens_fail_closed",
        "Changing_sheet_selection_clears_previous_signed_import_plan",
        "Logout_or_navigation_drops_outstanding_file_preview",
        "Unknown_commit_or_forged_receipt_never_auto_retries_or_reopens",
        "Import_without_custom_update_approval_never_overwrites_existing_identity"
    ):
        require("test", snippet)
    if data["test"].count("[Fact]") < 9:
        issues.append("test: nine test-first C# regressions are required")

    for snippet in (
        "Import_AddsNewPeopleAndIdentityHistoryOnly_Audited_WithoutAttendance",
        "DuplicateWithinFileAndPreviouslyImportedNumber_AreReportedButNeverOverwritten",
        "StalePlanAndWrongUserAreDeniedBeforeWriting",
        "ArchivedDismissedNumber_NeverSilentlyReturnsOrCreatesNewIdentityViaExcel",
        "NewSiteCreatedAutomaticallyOnConfirmedImport_AndAuditedOnce"
    ):
        require("integration", snippet)

    for key in ("core", "release"):
        cmd = "python build/test-native-phase8-personnel-excel-import-contract.py"
        lines = data[key].splitlines()
        positions = [i for i, line in enumerate(lines) if line.strip() == cmd]
        if len(positions) != 1:
            issues.append(f"{key}: original XLSX gate must run exactly once")
        elif positions[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[positions[0] + 1]
        ):
            issues.append(f"{key}: XLSX gate must abort on failure")

    for key in ("contract", "vm", "xaml", "view"):
        for forbidden in (
            "new SqliteConnection(", "UPDATE Persons SET", "DELETE FROM Persons",
            "INSERT INTO Persons", "INSERT INTO ImportBatches",
            "new ExcelPersonnelImportService(",
            "ExecuteInWriteTransactionAsync("
        ):
            if forbidden in data[key]:
                issues.append(f"{key}: forbidden second import writer: {forbidden}")
    return issues


if __name__ == "__main__":
    issues = validate(TEXT)
    if issues:
        raise SystemExit("PHASE8 EXCEL IMPORT FAIL:\n" + "\n".join(issues))
    for key, snippet in (
        ("vm", "_attempted = true"),
        ("vm", "_preview.NewSites.Count == 0 || _newSitesApproved"),
        ("vm", "result.CustomUpdatedCount != 0"),
        ("adapter", '["updateExistingCustomFields"] = false'),
        ("adapter", '["approveCustomUpdates"] = false'),
        ("canonical", "MutationIdempotency.StartAsync("),
        ("admin", "ExcelImport.AttachBackend("),
        ("shell", "Administration.ExcelImport.ForceCancel()"),
        ("xaml", 'IsChecked="{Binding NewSitesApproved, Mode=TwoWay'),
        ("view", "CryptographicOperations.ZeroMemory(workbookBytes)"),
        ("test", "Unknown_commit_or_forged_receipt_never_auto_retries_or_reopens"),
        ("core", "python build/test-native-phase8-personnel-excel-import-contract.py"),
        ("release", "python build/test-native-phase8-personnel-excel-import-contract.py")
    ):
        damaged = dict(TEXT)
        damaged[key] = damaged[key].replace(snippet, "BROKEN_NEGATIVE_CASE")
        if not validate(damaged):
            raise SystemExit(f"Missing negative-path regression: {key}: {snippet}")
    print("PHASE8 EXCEL IMPORT PASS: original SQLCipher, no identity overwrite, multi-consent, both Windows gates")
