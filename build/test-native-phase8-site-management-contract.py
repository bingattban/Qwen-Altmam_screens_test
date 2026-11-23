#!/usr/bin/env python3
"""Phase 8 sites, test-first negative-path guard for BOTH Windows workflows.

The actual xUnit tests, original canonical SQLCipher integration, 9 Release
shards, WPF/WebView2 GUI and four package acceptance gates remain mandatory.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contracts": "src/AlTamam.NativePresentation/NativeSiteCatalogContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeSiteCatalogViewModel.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/AdminConfigurationService.cs",
    "sitesread": "src/AlTamam.Application/Bridge/BridgeSharedServices.cs",
    "tests": "tests/AlTamam.UnitTests/NativeSiteManagementPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/AdminSettingsIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeSiteCatalogView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeSiteCatalogView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k: (ROOT / v).read_text(encoding="utf-8") for k, v in PATHS.items()}


def failures(data):
    errors = []

    def expect(file, token):
        if token not in data[file]:
            errors.append(f"{file}: required original-site guard missing: {token}")

    for key in ("xaml", "roster"):
        try:
            ET.fromstring(data[key])
        except ET.ParseError as exc:
            errors.append(f"{key}: invalid native WPF XML: {exc}")

    for token in (
        "INativeSiteCatalogBackend", "ReadSitesAsync(", "CreateSiteAsync(",
        "RenameSiteAsync(", "NativeSiteRow(", "NativeSiteMutationReceipt("
    ):
        expect("contracts", token)

    for token in (
        "ListSitesAsync(", "includeInactive: true",
        "new AdminConfigurationService(database)", ".CreateSiteAsync(new JsonObject",
        ".RenameSiteAsync(new JsonObject",
        '["operationId"] = operationId',
        '["expectedRevision"] = revision',
        '["name"] = name',
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSessionAsync(actor.SessionNonce, ct)",
        "RequiredBoolean(row, \"active\")",
        "RequiredNumber(row, \"rowVersion\")",
        'RequiredText(data, "auditEventId")',
        "RequiredNumber(data, \"rowVersion\") != revision + 1",
        "siteId == \"SITE-UNSPECIFIED\""
    ):
        expect("backend", token)

    for token in (
        "public async Task<BridgeResponse> CreateSiteAsync(",
        "public async Task<BridgeResponse> RenameSiteAsync(",
        "MutationIdempotency.StartAsync(",
        "MutationIdempotency.StoreAsync(",
        "SiteNameHistory", "RowVersion=RowVersion+1",
        "SITE_CREATE", "SITE_RENAME", "BridgeAuditWriter.WriteAsync("
    ):
        expect("canonical", token)
    for token in ("ListSitesAsync(", "VisibleName", "ActiveFlag", "RowVersion"):
        expect("sitesread", token)
    expect("integration", "CreateSiteAsync(")

    for token in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor",
        "ReadSitesAsync(actor, linked.Token)",
        "BeginRename(NativeSiteRow?",
        "ReferenceEquals(candidate, row)",
        'row.SiteId != "SITE-UNSPECIFIED"',
        "ValidName(_name)",
        "Review()",
        "InvalidateReview()",
        "_attempted = true",
        'Guid.NewGuid().ToString("N")',
        "await _backend.CreateSiteAsync(",
        "await _backend.RenameSiteAsync(",
        "receipt.SiteId != draft.SiteId",
        "receipt.Revision != (draft.IsCreate ? 1 : draft.Revision + 1)",
        "_uncertainActor = actor",
        "public void ForceCancel()",
        "Cancelled"
    ):
        if token == "Cancelled":
            expect("vm", "_pending?.Cancel();")
        else:
            expect("vm", token)

    for token in (
        "SiteCatalog = new NativeSiteCatalogViewModel()",
        "SiteCatalog.AttachBackend(",
        "OpenSiteCatalogCommand",
        "SiteCatalog.OpenAsync()",
        "SiteCatalog.ForceCancel()",
        "!SiteCatalog.IsOpen"
    ):
        expect("admin", token)
    expect("shell", "Administration.SiteCatalog.ForceCancel()")
    for token in (
        'Command="{Binding CreateCommand}"',
        'Command="{Binding RenameCommand}"',
        'Command="{Binding ReviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'Command="{Binding CloseCommand}"',
        'ItemsSource="{Binding Sites}"',
        'Text="{Binding DraftName, Mode=TwoWay',
        'IsReadOnly="True"',
        "ReviewNote", "CompletionNote"
    ):
        expect("xaml", token)
    expect("view", "InitializeComponent()")
    expect("roster", 'Command="{Binding OpenSiteCatalogCommand}"')
    expect("roster", 'DataContext="{Binding SiteCatalog}"')

    for token in (
        "No_session_or_supervisor_must_not_read_or_edit_site_catalogue",
        "Sites_are_canonical_including_retired_and_locked_system_rows",
        "Create_requires_valid_name_separate_review_and_exact_audited_receipt",
        "Rename_uses_selected_row_identity_and_expected_revision_only",
        "Changing_name_invalidates_prior_review_without_writing",
        "Async_page_result_cannot_leak_after_logout_or_close",
        "Forged_or_stale_confirmation_receipt_never_looks_successful",
        "Lost_write_reply_never_retries_and_locks_same_manager_session"
    ):
        expect("tests", token)
    if data["tests"].count("[Fact]") < 8:
        errors.append("tests: eight test-first xUnit regressions required")

    for key in ("core", "release"):
        lines = data[key].splitlines()
        statement = "python build/test-native-phase8-site-management-contract.py"
        matches = [i for i, line in enumerate(lines) if line.strip() == statement]
        if len(matches) != 1:
            errors.append(f"{key}: guard invocation must occur exactly once")
        elif matches[0]+1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[matches[0]+1]
        ):
            errors.append(f"{key}: guard MUST fail closed immediately")
    for key in ("contracts", "vm", "xaml", "view"):
        for forbidden in (
            "new SqliteConnection(", "UPDATE Sites SET",
            "DELETE FROM Sites", "INSERT INTO Sites",
            "new AdminConfigurationService(", "ExecuteInWriteTransactionAsync(",
            "RemoveSiteAndReassignCurrentPersonsAsync"
        ):
            if forbidden in data[key]:
                errors.append(f"{key}: forbidden direct site mutation: {forbidden}")
    return errors


if __name__ == "__main__":
    problems = failures(TEXT)
    if problems:
        raise SystemExit("PHASE8 SITE MANAGEMENT FAIL:\n" + "\n".join(problems))
    for key, token in (
        ("backend", "new AdminConfigurationService(database)"),
        ("backend", '["expectedRevision"] = revision'),
        ("vm", "_attempted = true"),
        ("vm", "_uncertainActor = actor"),
        ("admin", "SiteCatalog.AttachBackend("),
        ("shell", "Administration.SiteCatalog.ForceCancel()"),
        ("xaml", 'Command="{Binding ConfirmCommand}"'),
        ("roster", 'DataContext="{Binding SiteCatalog}"'),
        ("tests", "Lost_write_reply_never_retries_and_locks_same_manager_session"),
        ("core", "python build/test-native-phase8-site-management-contract.py"),
        ("release", "python build/test-native-phase8-site-management-contract.py")
    ):
        altered = dict(TEXT)
        altered[key] = altered[key].replace(token, "DELETED_NEGATIVE_CASE")
        if not failures(altered):
            raise SystemExit(f"Missing negative regression: {key} {token}")
    print("PHASE8 SITE MANAGEMENT PASS: original SQLCipher, audited create/rename, exact session and Core/Release")
