#!/usr/bin/env python3
"""Test-first native WPF status catalogue negative-path guard, BOTH Windows gates.

This early structural guard is NOT a substitute for C# xUnit, canonical SQLCipher
tests, WPF/WebView2 E2E, x86/x64 package acceptance and independent SHA256 audit.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
FILES = {
    "contract": "src/AlTamam.NativePresentation/NativeStatusCatalogContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeStatusCatalogViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/AdminConfigurationService.cs",
    "catalogue": "src/AlTamam.Application/Bridge/BridgeSharedServices.cs",
    "tests": "tests/AlTamam.UnitTests/NativeStatusCatalogPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/StatusSymbolUniquenessTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeStatusCatalogView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeStatusCatalogView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {key: (ROOT / val).read_text(encoding="utf-8") for key, val in FILES.items()}


def check_all(files):
    errors = []

    def need(key, fragment):
        if fragment not in files[key]:
            errors.append(f"{key}: missing original status guard: {fragment}")

    for key in ("xaml", "roster"):
        try:
            ET.fromstring(files[key])
        except ET.ParseError as exc:
            errors.append(f"{key}: invalid WPF markup: {exc}")

    for token in (
        "INativeStatusCatalogBackend", "ReadActiveStatusesAsync(",
        "CreateStatusAsync(", "UpdateStatusAsync(", "DeactivateStatusAsync(",
        "NativeStatusRow(", "NativeStatusMutationReceipt("
    ):
        need("contract", token)

    for token in (
        "new AdminConfigurationService(database)",
        "ListActiveStatusesAsync(ct)",
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSitesPrincipalAsync(actor, ct)",
        ".CreateStatusAsync(new JsonObject",
        ".UpdateStatusAsync(new JsonObject",
        ".DeactivateStatusAsync(new JsonObject",
        '["statusId"] = statusId',
        '["expectedRevision"] = revision',
        '["operationId"] = operationId',
        '["symbol"] = symbol',
        '["color"] = color',
        'RequiredText(status, "shortSymbol")',
        'RequiredText(status, "color")',
        'RequiredNumber(status, "version")',
        'RequiredNumber(status, "todayCount")',
        'RequiredBoolean(status, "active")',
        'RequiredText(data, "auditEventId")',
        'RequiredNumber(data, "rowVersion") != revision + 1',
        'RequiredBoolean(data, "active")',
        'Guid.TryParseExact(operationId, "N"'
    ):
        need("adapter", token)

    for token in (
        "public async Task<BridgeResponse> CreateStatusAsync(",
        "public async Task<BridgeResponse> UpdateStatusAsync(",
        "public async Task<BridgeResponse> DeactivateStatusAsync(",
        "MutationIdempotency.StartAsync(", "MutationIdempotency.StoreAsync(",
        "ActiveStatusSymbolExists", "AttendanceStatusNameHistory",
        "AutoAttendancePolicyService.DefaultStatusKey",
        "SELECT COUNT(*) FROM AttendanceRecords WHERE StatusId=$id",
        "STATUS_CREATE", "STATUS_UPDATE", "STATUS_DEACTIVATE",
        "ExecuteInWriteTransactionAsync(", "BridgeAuditWriter.WriteAsync("
    ):
        need("canonical", token)

    for token in ("ListActiveStatusesAsync(", "AttendanceStatusDefinitions",
                  "WHERE s.ActiveFlag=1", "TodayCount"):
        need("catalogue", token)
    for token in (
        "CreateAndUpdate_RejectEquivalentActiveSymbols_WhilePreservingRecords",
        "ArabicDiacriticsAreEquivalent_AndInactiveSymbolsAreReusable",
        "DeactivateStatusAsync("
    ):
        need("integration", token)

    for token in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor", "ReferenceEquals(current, row)",
        "ReadActiveStatusesAsync(actor, linked.Token)",
        "_attempted = true", 'Guid.NewGuid().ToString("N")',
        "CurrentDraft() == _reviewDraft",
        "!IsDeactivating || _deactivateConfirmed",
        "_deactivateConfirmed = false",
        "InvalidateReview()", "DeactivationConfirmed",
        "await _backend.CreateStatusAsync(", "await _backend.UpdateStatusAsync(",
        "await _backend.DeactivateStatusAsync(",
        "receipt.StatusId != draft.StatusId",
        "receipt.Revision != (create ? 1 : draft.BaseRevision + 1)",
        "_uncertainActor = actor", "ForceCancel()"
    ):
        need("vm", token)
    for token in (
        "StatusCatalog = new NativeStatusCatalogViewModel()",
        "OpenStatusCatalogCommand", "StatusCatalog.OpenAsync()",
        "StatusCatalog.AttachBackend(", "StatusCatalog.ForceCancel()",
        "!StatusCatalog.IsOpen", "StatusCatalog.CommittedAndClosed"
    ):
        need("admin", token)
    need("shell", "Administration.StatusCatalog.ForceCancel()")

    for token in (
        'ItemsSource="{Binding Statuses}"',
        'Command="{Binding CreateCommand}"',
        'Command="{Binding EditCommand}"',
        'Command="{Binding DeactivateCommand}"',
        'Command="{Binding ReviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'IsChecked="{Binding DeactivationConfirmed, Mode=TwoWay',
        'Text="{Binding DraftName, Mode=TwoWay',
        'Text="{Binding DraftSymbol, Mode=TwoWay',
        'Text="{Binding DraftColor, Mode=TwoWay',
        'IsReadOnly="True"', "ReviewNote", "CompletionNote"
    ):
        need("xaml", token)
    need("view", "InitializeComponent()")
    need("roster", 'Command="{Binding OpenStatusCatalogCommand}"')
    need("roster", 'DataContext="{Binding StatusCatalog}"')

    for token in (
        "Logged_out_or_supervisor_cannot_read_or_mutate_attendance_statuses",
        "Original_active_statuses_have_color_symbol_revision_and_today_count",
        "Create_is_nonwriting_until_separate_validated_human_confirmation",
        "Update_keeps_original_status_id_expected_revision_and_historical_snapshots",
        "Deactivation_requires_separate_explicit_checkbox_and_never_deletes_history",
        "Changed_name_symbol_or_color_revokes_preview_and_disables_commit",
        "Forged_or_duplicate_canonical_status_rows_fail_closed",
        "Pending_read_after_logout_cannot_repopulate_closed_dialogue",
        "Ambiguous_or_forged_status_write_receipt_never_autoretries"
    ):
        need("tests", token)
    if files["tests"].count("[Fact]") < 9:
        errors.append("tests: nine test-first C# status regressions required")

    for key in ("core", "release"):
        lines = files[key].splitlines()
        invocation = "python build/test-native-phase8-status-catalog-contract.py"
        found = [i for i, line in enumerate(lines) if line.strip() == invocation]
        if len(found) != 1:
            errors.append(f"{key}: status contract must appear exactly once")
        elif found[0]+1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[found[0]+1]
        ):
            errors.append(f"{key}: status guard must fail closed immediately")

    for key in ("contract", "vm", "xaml", "view"):
        for forbidden in (
            "new SqliteConnection(", "UPDATE AttendanceStatusDefinitions",
            "DELETE FROM AttendanceRecords", "INSERT INTO AttendanceStatusDefinitions",
            "new AdminConfigurationService(", "ExecuteInWriteTransactionAsync("
        ):
            if forbidden in files[key]:
                errors.append(f"{key}: forbidden alternate SQL status writer: {forbidden}")
    return errors


if __name__ == "__main__":
    failures = check_all(TEXT)
    if failures:
        raise SystemExit("PHASE8 STATUS CATALOG FAIL:\n" + "\n".join(failures))
    for key, token in (
        ("adapter", "new AdminConfigurationService(database)"),
        ("adapter", '["expectedRevision"] = revision'),
        ("canonical", "AutoAttendancePolicyService.DefaultStatusKey"),
        ("vm", "_attempted = true"),
        ("vm", "!IsDeactivating || _deactivateConfirmed"),
        ("admin", "StatusCatalog.AttachBackend("),
        ("shell", "Administration.StatusCatalog.ForceCancel()"),
        ("xaml", 'Command="{Binding ConfirmCommand}"'),
        ("roster", 'DataContext="{Binding StatusCatalog}"'),
        ("tests", "Ambiguous_or_forged_status_write_receipt_never_autoretries"),
        ("core", "python build/test-native-phase8-status-catalog-contract.py"),
        ("release", "python build/test-native-phase8-status-catalog-contract.py")
    ):
        test = dict(TEXT)
        test[key] = test[key].replace(token, "REMOVED_NEGATIVE_CASE")
        if not check_all(test):
            raise SystemExit(f"Broken negative-path regression: {key}: {token}")
    print("PHASE8 STATUS CATALOG PASS: canonical SQLCipher, explicit soft-disable, role+nonce, both Windows workflows")
