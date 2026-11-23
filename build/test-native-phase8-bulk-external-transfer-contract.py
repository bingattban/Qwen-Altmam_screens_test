#!/usr/bin/env python3
"""Fail-closed external batch transfer guard for Windows Core AND Release.

Supplement, not substitute, for the nine C# Release test shards, SQLCipher
integration tests, WPF/WebView2 E2E, four package acceptances and SHA256 audit.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contract": "src/AlTamam.NativePresentation/NativeBulkExternalTransferContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeBulkExternalTransferViewModel.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/PersonnelMovementService.cs",
    "lifecycle": "src/AlTamam.Application/Bridge/AccountLifecycleService.cs",
    "tests": "tests/AlTamam.UnitTests/NativeBulkExternalTransferPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/PersonnelMovementIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeBulkExternalTransferView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeBulkExternalTransferView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
FILES = {k: (ROOT / v).read_text(encoding="utf-8") for k, v in PATHS.items()}


def errors_in(files):
    errors = []

    def check(key, snippet):
        if snippet not in files[key]:
            errors.append(f"{key}: missing original external batch safeguard: {snippet}")

    for key in ("xaml", "roster"):
        try:
            ET.fromstring(files[key])
        except ET.ParseError as exc:
            errors.append(f"{key}: invalid WPF XML: {exc}")

    for snippet in (
        "INativeBulkExternalTransferBackend", "PreviewExternalBatchAsync(",
        "CommitExternalBatchAsync(", "NativeBulkExternalTransferDraft(",
        "NativeBulkExternalTransferPreview(", "NativeBulkExternalTransferReceipt(",
        "ExternalDestinationName"
    ):
        check("contract", snippet)

    for snippet in (
        "new PersonnelMovementService(database)",
        "ValidateBulkExternalDraft(draft)",
        "ValidateBulkExternalPersons(draft, people)",
        "ValidateBulkExternalPersons(preview.Draft, preview.Persons)",
        '["personIds"] = new JsonArray(draft.Persons.Select',
        '["destinationSiteId"] = null',
        '["destinationExternalName"] = draft.ExternalDestinationName',
        "}, principal, true, ct)",
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSitesPrincipalAsync(actor, ct)",
        ".PreviewAsync(new JsonObject",
        ".CommitAsync(new JsonObject",
        '["previewToken"] = preview.PreviewToken',
        '["operationId"] = operationId',
        'RequiredBoolean(data, "external")',
        'RequiredNumber(data, "count") != draft.Persons.Count',
        'count != preview.Persons.Count',
        'RequiredText(row, "sourceAssignmentId")',
        "checked((int)count)"
    ):
        check("backend", snippet)

    for snippet in (
        "ids.Length is >=1 and <=100",
        "ids.Distinct(StringComparer.Ordinal).Count()==ids.Length",
        "PreviewAsync(JsonObject? data,BridgePrincipal actor,",
        "CommitAsync(JsonObject? data,BridgePrincipal actor,",
        "BridgePreviewTokens.Issue(", "BridgePreviewTokens.ConsumeAsync(",
        "MutationIdempotency.StartAsync(",
        "MutationIdempotency.StoreAsync(",
        "ExecuteInWriteTransactionAsync(",
        "AlreadyMovedToday(", "p.AssignmentId", "p.Revision",
        "AccountLifecycleService.SuspendAccountsForPersonnelExitAsync(",
        "BridgeAuditWriter.WriteAsync(",
        'external?"TRANSFER_EXTERNAL":"TRANSFER_INTERNAL"',
        "UPDATE Persons SET OrganizationalStatus=2,ActiveFlag=0",
        "PersonAssignments", "PersonnelMovements"
    ):
        check("canonical", snippet)
    for snippet in (
        "SuspendAccountsForPersonnelExitAsync(", "UserSessions",
        "RevokedFlag", "USER_LIFECYCLE_SUSPEND"
    ):
        check("lifecycle", snippet)

    for snippet in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor", "_onPage?.Invoke(o.Row) == true",
        "rows.Count is < 1 or > 100",
        "rows.Select(x => x.PersonId)",
        "CurrentPeople()", "_options.Where(x => x.IsSelected)",
        "destination.Length is < 2 or > 150",
        "_identityReviewed", "_accountAcknowledged",
        "CurrentPeople().SequenceEqual(_approvedPeople)",
        "PreviewExternalBatchAsync(",
        "ValidatePreview(preview, draft)",
        "_attempted = true",
        'Guid.NewGuid().ToString("N")',
        "await _backend.CommitExternalBatchAsync(",
        "result.Count != preview.Persons.Count",
        "result.DestinationName != preview.DestinationName",
        "_uncertainActor = actor", "InvalidatePreview()",
        "public void ForceCancel()"
    ):
        check("vm", snippet)
    if files["vm"].count("await _backend.CommitExternalBatchAsync(") != 1:
        errors.append("vm: there must be exactly ONE atomic original commit")

    for snippet in (
        "BulkExternalMovement = new NativeBulkExternalTransferViewModel()",
        "OpenBulkExternalTransferCommand",
        "BulkExternalMovement.Open(_rows, _site, _sites)",
        "BulkExternalMovement.AttachBackend(",
        "BulkExternalMovement.CommittedAndClosed",
        "BulkExternalMovement.ForceCancel()",
        "!BulkExternalMovement.IsOpen", "_status == 1 && _site.Length > 0"
    ):
        check("admin", snippet)
    check("shell", "Administration.BulkExternalMovement.ForceCancel()")
    for snippet in (
        'ItemsSource="{Binding Options}"',
        'Binding="{Binding IsSelected, Mode=TwoWay',
        'Text="{Binding ExternalDestinationName, Mode=TwoWay',
        'IsChecked="{Binding IdentityReviewed, Mode=TwoWay',
        'IsChecked="{Binding AccountSuspensionAcknowledged, Mode=TwoWay',
        'Command="{Binding PreviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'Command="{Binding CloseCommand}"',
        'ItemsSource="{Binding Preview.Persons}"',
        "تعليق حسابات", "إلغاء جلسات", "الصفحة الحالية"
    ):
        check("xaml", snippet)
    check("view", "InitializeComponent()")
    check("roster", 'Command="{Binding OpenBulkExternalTransferCommand}"')
    check("roster", 'DataContext="{Binding BulkExternalMovement}"')

    for snippet in (
        "Unauthorized_or_no_session_cannot_open_external_batch",
        "Duplicate_stale_or_mixed_source_rows_are_never_eligible",
        "No_default_selection_and_no_preview_before_explicit_valid_external_destination",
        "Full_100_member_limit_and_exact_one_to_one_canonical_preview",
        "Altered_member_revision_or_envelope_fails_the_entire_batch",
        "Editing_destination_or_selected_people_revokes_both_approvals",
        "Linked_accounts_revocation_needs_separate_explicit_acknowledgment",
        "Logout_during_delayed_preview_never_reopens_or_commits",
        "Unknown_write_or_forged_receipt_locks_same_manager_session"
    ):
        check("tests", snippet)
    if files["tests"].count("[Fact]") < 9:
        errors.append("tests: nine C# unit tests committed first required")
    for snippet in (
        "ExternalBulkTransfer_TwoLinkedAccountsSessionsAndHistory_AreSuspendedAtomically",
        "ExternalBulkTransfer_StaleSecondMember_RejectsEntireBatchAndDoesNotSuspendAnyLinkedAccount",
        "ExternalTransfer_DisablesLinkedAccountRevokesSessionsAndKeepsImmutableAudit",
        "IndependentFounder_RemainsActiveWhenDifferentRosterPersonTransfersExternally"
    ):
        check("integration", snippet)

    for key in ("core", "release"):
        cmd = "python build/test-native-phase8-bulk-external-transfer-contract.py"
        lines = files[key].splitlines()
        indexes = [i for i, line in enumerate(lines) if line.strip() == cmd]
        if len(indexes) != 1:
            errors.append(f"{key}: external batch check must appear exactly once")
        elif indexes[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[indexes[0] + 1]
        ):
            errors.append(f"{key}: external check must fail closed")

    for key in ("contract", "vm", "xaml", "view"):
        for unsafe in (
            "new SqliteConnection(", "UPDATE Persons SET",
            "UPDATE Users SET", "UPDATE UserSessions SET",
            "new PersonnelMovementService(", "ExecuteInWriteTransactionAsync(",
            "SuspendAccountsForPersonnelExitAsync("
        ):
            if unsafe in files[key]:
                errors.append(f"{key}: second unauthorized SQL writer: {unsafe}")
    return errors


if __name__ == "__main__":
    issues = errors_in(FILES)
    if issues:
        raise SystemExit("PHASE8 BULK EXTERNAL FAIL:\n" + "\n".join(issues))

    for key, token in (
        ("backend", '["destinationExternalName"] = draft.ExternalDestinationName'),
        ("backend", "}, principal, true, ct)"),
        ("canonical", "AccountLifecycleService.SuspendAccountsForPersonnelExitAsync("),
        ("lifecycle", "USER_LIFECYCLE_SUSPEND"),
        ("vm", "_accountAcknowledged"),
        ("vm", "_attempted = true"),
        ("admin", "BulkExternalMovement.AttachBackend("),
        ("shell", "Administration.BulkExternalMovement.ForceCancel()"),
        ("xaml", 'IsChecked="{Binding AccountSuspensionAcknowledged, Mode=TwoWay'),
        ("tests", "Unknown_write_or_forged_receipt_locks_same_manager_session"),
        ("integration", "ExternalBulkTransfer_StaleSecondMember_RejectsEntireBatchAndDoesNotSuspendAnyLinkedAccount"),
        ("core", "python build/test-native-phase8-bulk-external-transfer-contract.py"),
        ("release", "python build/test-native-phase8-bulk-external-transfer-contract.py")
    ):
        damaged = dict(FILES)
        damaged[key] = damaged[key].replace(token, "REMOVED_NEGATIVE_PATH")
        if not errors_in(damaged):
            raise SystemExit(f"Missing negative regression: {key}: {token}")

    print("PHASE8 BULK EXTERNAL PASS: 1–100 explicit people, linked-account atomic suspension, anti-retry, Core+Release")
