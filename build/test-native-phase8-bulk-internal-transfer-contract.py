#!/usr/bin/env python3
"""Test-first fail-closed native WPF batch internal transfer check.

Runs on BOTH Windows Core + Release before packaging. C# xUnit, actual
SQLCipher integrations, Windows WPF/WebView2 E2E, four package acceptances,
and independent SHA-256 audit are separate mandatory gates.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contract": "src/AlTamam.NativePresentation/NativeBulkInternalTransferContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeBulkInternalTransferViewModel.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/PersonnelMovementService.cs",
    "tests": "tests/AlTamam.UnitTests/NativeBulkInternalTransferPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/PersonnelMovementIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeBulkInternalTransferView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeBulkInternalTransferView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k: (ROOT / p).read_text(encoding="utf-8") for k, p in PATHS.items()}


def validate(data):
    errors = []

    def require(key, needle):
        if needle not in data[key]:
            errors.append(f"{key}: missing bulk-transfer invariant: {needle}")

    for name in ("xaml", "roster"):
        try:
            ET.fromstring(data[name])
        except ET.ParseError as exc:
            errors.append(f"{name}: invalid WPF XAML: {exc}")

    for token in (
        "INativeBulkInternalTransferBackend", "PreviewInternalBatchAsync(",
        "CommitInternalBatchAsync(", "NativeBulkInternalPerson(",
        "NativeBulkInternalTransferDraft(", "NativeBulkInternalTransferPreview(",
        "NativeBulkInternalTransferReceipt("
    ):
        require("contract", token)

    for token in (
        "Selected(JsonObject? data)",
        "ids.Length is >=1 and <=100",
        "ids.Distinct(StringComparer.Ordinal).Count()==ids.Length",
        "PreviewAsync(JsonObject? data,BridgePrincipal actor,",
        "CommitAsync(JsonObject? data,BridgePrincipal actor,",
        "BridgePreviewTokens.Issue(",
        "BridgePreviewTokens.ConsumeAsync(",
        "MutationIdempotency.StartAsync(",
        "ExecuteInWriteTransactionAsync(",
        "AlreadyMovedToday(",
        "p.AssignmentId",
        "p.Revision",
        "PersonnelMovements", "PersonAssignments",
        "BridgeAuditWriter.WriteAsync("
    ):
        require("canonical", token)

    for token in (
        "INativeBulkInternalTransferBackend",
        "new PersonnelMovementService(database)",
        ".PreviewAsync(new JsonObject",
        ".CommitAsync(new JsonObject",
        '["personIds"] = new JsonArray(draft.Persons.Select',
        '["businessDate"] = today',
        '["sourceSiteId"] = draft.SourceSiteId',
        '["destinationSiteId"] = draft.DestinationSiteId',
        '["previewToken"] = preview.PreviewToken',
        '["operationId"] = operationId',
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSitesPrincipalAsync(actor, ct)",
        "ValidateBulkInternalDraft(draft)",
        "ValidateBulkCanonicalPersons(draft, people)",
        'RequiredBoolean(data, "external")',
        'RequiredNumber(data, "count") != draft.Persons.Count',
        'count != preview.Draft.Persons.Count',
        'RequiredText(x, "personId")',
        'RequiredText(x, "sourceAssignmentId")'
    ):
        require("backend", token)

    for token in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor",
        "_onPage?.Invoke(x.Row) == true",
        "pageRows.Count is < 1 or > 100",
        "_onPage?.Invoke(r) != true",
        "pageRows.Select(r => r.PersonId).Distinct(StringComparer.Ordinal)",
        "_options.Where(x => x.IsSelected)",
        "_destinationSiteId == _sourceSiteId",
        "OnSelectionChanged()",
        "InvalidatePreview()",
        "_approvedPeople",
        "CurrentPeople().SequenceEqual(_approvedPeople)",
        "ReviewAcknowledged",
        "_acknowledged",
        "ValidatePreview(preview, draft)",
        "PreviewInternalBatchAsync(",
        "_attempted = true",
        'Guid.NewGuid().ToString("N")',
        "await _backend.CommitInternalBatchAsync(",
        "result.Count != preview.Persons.Count",
        "_uncertainActor = actor",
        "ForceCancel()"
    ):
        require("vm", token)

    if data["vm"].count("await _backend.CommitInternalBatchAsync") != 1:
        errors.append("vm: one explicit atomic batch write ONLY")

    for token in (
        "BulkMovement = new NativeBulkInternalTransferViewModel()",
        "BulkMovement.AttachBackend(",
        "BulkMovement.CommittedAndClosed",
        "OpenBulkInternalTransferCommand",
        "BulkMovement.Open(_rows, _site, _sites)",
        "BulkMovement.ForceCancel()",
        "!BulkMovement.IsOpen",
        "_rows.Count is >= 1 and <= 100",
        "_status == 1 && _site.Length > 0"
    ):
        require("admin", token)
    require("shell", "Administration.BulkMovement.ForceCancel()")
    for token in (
        'ItemsSource="{Binding Options}"',
        'Binding="{Binding IsSelected, Mode=TwoWay',
        'ItemsSource="{Binding SiteOptions}"',
        'SelectedValue="{Binding DestinationSiteId, Mode=TwoWay',
        'IsChecked="{Binding ReviewAcknowledged, Mode=TwoWay',
        'Command="{Binding PreviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'Command="{Binding CloseCommand}"',
        'Text="{Binding SelectedCount, StringFormat=',
        "ReviewMessage", "CompletionMessage",
        "الصفحة الحالية"
    ):
        require("xaml", token)
    require("view", "InitializeComponent()")
    require("roster", 'Command="{Binding OpenBulkInternalTransferCommand}"')
    require("roster", 'DataContext="{Binding BulkMovement}"')

    for token in (
        "Non_admin_or_missing_session_never_opens_or_writes",
        "Wrong_source_stale_row_or_duplicate_ids_cannot_open",
        "Explicit_page_selection_1_to_100_only_and_preview_never_writes",
        "Forged_member_or_revision_invalidates_whole_batch",
        "Changing_selection_or_destination_invalidates_original_preview_and_consent",
        "Explicit_two_person_review_uses_one_canonical_atomic_commit",
        "Logout_or_replaced_page_drops_delayed_preview_and_prevents_write",
        "Commit_lost_reply_or_bad_receipt_never_resends_in_same_session",
        "Replaced_roster_member_after_preview_prevents_any_commit"
    ):
        require("tests", token)
    if data["tests"].count("[Fact]") < 9:
        errors.append("tests: nine test-first C# xUnit cases are required")
    for token in (
        "InternalBulkTransfer_EffectiveDatesAndAudit_OneTime",
        "IdenticalInternalTransferRetry_ReturnsRecordedResultWithoutExtraMovementsOrAudit",
        "PreparedPeopleCanTransfer_WithoutLosingAttendanceOrMonthlyHistory",
        "ApprovedTransferRule_MovementOnDayAndAttendanceEligibleNextDay"
    ):
        require("integration", token)

    for key in ("core", "release"):
        lines = data[key].splitlines()
        command = "python build/test-native-phase8-bulk-internal-transfer-contract.py"
        positions = [n for n, l in enumerate(lines) if l.strip() == command]
        if len(positions) != 1:
            errors.append(f"{key}: batch guard must run once")
        elif positions[0]+1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[positions[0]+1]
        ):
            errors.append(f"{key}: batch guard MUST fail closed immediately")

    for key in ("contract", "vm", "xaml", "view"):
        for forbidden in (
            "new SqliteConnection(", "UPDATE Persons SET",
            "UPDATE PersonAssignments SET", "INSERT INTO PersonnelMovements",
            "new PersonnelMovementService(", "ExecuteInWriteTransactionAsync("
        ):
            if forbidden in data[key]:
                errors.append(f"{key}: alternate native SQL writer forbidden: {forbidden}")
    return errors


if __name__ == "__main__":
    errors = validate(TEXT)
    if errors:
        raise SystemExit("PHASE8 BULK TRANSFER FAIL:\n" + "\n".join(errors))
    for key, needle in (
        ("backend", '["personIds"] = new JsonArray(draft.Persons.Select'),
        ("backend", "ValidateBulkCanonicalPersons(draft, people)"),
        ("canonical", "BridgePreviewTokens.ConsumeAsync("),
        ("vm", "_acknowledged"),
        ("vm", "_attempted = true"),
        ("admin", "BulkMovement.AttachBackend("),
        ("shell", "Administration.BulkMovement.ForceCancel()"),
        ("xaml", 'Binding="{Binding IsSelected, Mode=TwoWay'),
        ("roster", 'DataContext="{Binding BulkMovement}"'),
        ("tests", "Commit_lost_reply_or_bad_receipt_never_resends_in_same_session"),
        ("core", "python build/test-native-phase8-bulk-internal-transfer-contract.py"),
        ("release", "python build/test-native-phase8-bulk-internal-transfer-contract.py")
    ):
        copy = dict(TEXT)
        copy[key] = copy[key].replace(needle, "NEGATIVE_CASE_REMOVED")
        if not validate(copy):
            raise SystemExit(f"Negative regression missing: {key}: {needle}")
    print("PHASE8 BULK INTERNAL PASS: explicit selected-page 1-100, canonical one-time SQLCipher, Core+Release")
