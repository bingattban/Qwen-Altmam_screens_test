#!/usr/bin/env python3
"""Fail-closed Phase 8 native movement security contract, Core + Release.

Static structural contract, not a replacement for real xUnit, canonical
SQLCipher integration, compiled WPF UI or four-package acceptance.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PATHS = {
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelMovementContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelTransferViewModel.cs",
    "tests": "tests/AlTamam.UnitTests/NativePersonnelTransfersPhase8Tests.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/PersonnelMovementService.cs",
    "canonicaltests": "tests/AlTamam.Tests/Bridge/PersonnelMovementIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativePersonnelTransferView.xaml",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelTransferView.xaml.cs",
    "policy": "src/AlTamam.Domain/Policies/RoleAuthorizationPolicy.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {key: (ROOT / path).read_text(encoding="utf-8")
        for key, path in PATHS.items()}


def problems(files):
    errors = []

    def require(key, needle):
        if needle not in files[key]:
            errors.append(f"{key} missing native transfer safeguard: {needle}")

    for key in ("xaml", "roster"):
        try:
            ET.fromstring(files[key])
        except ET.ParseError as err:
            errors.append(f"Malformed WPF {key}: {err}")

    for phrase in ("INativePersonnelMovementBackend",
                   "NativePersonnelMovementDraft(",
                   "NativePersonnelMovementPreview(",
                   "NativePersonnelMovementReceipt(",
                   "PreviewMovementAsync(", "CommitMovementAsync("):
        require("contracts", phrase)
    for phrase in (
        "INativePersonnelMovementBackend",
        "new PersonnelMovementService(database)",
        "PreviewAsync(new JsonObject",
        "CommitAsync(new JsonObject",
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSessionAsync(actor.SessionNonce, ct)",
        '["sourceSiteId"] = draft.SourceSiteId',
        '["businessDate"] = today',
        '["personIds"] = new JsonArray(JsonValue.Create(draft.PersonId))',
        '["operationId"] = operationId',
        '["previewToken"] = preview.PreviewToken',
        'RequiredNumber(data, "count") != 1',
        'RequiredText(person, "sourceAssignmentId")',
        "mapped.PersonId != draft.PersonId",
        "mapped.Revision != draft.Revision",
        "preview.BusinessDate != LocalPersonDay()",
        'RequiredBoolean(receipt, "external")',
        "new NativePersonnelMovementReceipt("
    ):
        require("backend", phrase)

    for phrase in ("BridgePreviewTokens.Issue(",
                   "BridgePreviewTokens.ConsumeAsync(",
                   "MutationIdempotency.StartAsync(",
                   "MutationIdempotency.StoreAsync(",
                   "AlreadyMovedToday(",
                   "PersonAssignments", "PersonnelMovements",
                   "AccountLifecycleService.SuspendAccountsForPersonnelExitAsync(",
                   "BridgeAuditWriter.WriteAsync(",
                   "external?2:1"):
        require("canonical", phrase)
    require("canonicaltests", "InternalBulkTransfer_EffectiveDatesAndAudit_OneTime")
    require("policy", "CanAccessUsersManagement(UserRole role)")
    for phrase in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "SourceSiteId", "PreviewMovementAsync(actor, draft, linked.Token)",
        "CommitMovementAsync(", "_onPage?.Invoke(row) == true",
        "_uncertainActor = actor", "_attempted = true",
        "_actor is { } actor", "ValidatePreview(result, draft)",
        "draft.Revision", "preview.Persons.Count != 1",
        "SourceAssignmentId", "Guid.NewGuid().ToString(\"N\")",
        "IsOriginalContextValid()", "ForceCancel()",
        "CommittedAndClosed"
    ):
        require("vm", phrase)
    if files["vm"].count("await _backend.CommitMovementAsync") != 1:
        errors.append("Movement view model must contain exactly one write call.")
    for phrase in (
        "Movement = new NativePersonnelTransferViewModel()",
        "Movement.AttachBackend(", "Movement.Open(person, _site, _sites, false)",
        "Movement.Open(person, _site, _sites, true)",
        "Movement.ForceCancel()", "Movement.CommittedAndClosed",
        "_status == 1 && _site.Length > 0",
        "site.Name == row.SiteName",
        "ReferenceEquals(current, row)",
        "OpenInternalTransferCommand", "OpenExternalTransferCommand"
    ):
        require("admin", phrase)
    require("shell", "Administration.Movement.ForceCancel()")
    for phrase in (
        "IsOpen, Converter=", "IsInternal, Converter=", "IsExternal, Converter=",
        "FormDestinationSiteId, Mode=TwoWay", "FormExternalName, Mode=TwoWay",
        'Command="{Binding PreviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'Visibility="{Binding HasPreview',
        "ReviewMessage", "CompletionMessage"
    ):
        require("xaml", phrase)
    for phrase in (
        'IsReadOnly="True"',
        'Command="{Binding OpenInternalTransferCommand}"',
        'Command="{Binding OpenExternalTransferCommand}"',
        "NativePersonnelTransferView"
    ):
        require("roster", phrase)
    require("view", "InitializeComponent()")
    for phrase in (
        "Missing_session_supervisor_or_unselected_source_cannot_preview_or_commit",
        "Internal_move_requires_real_preview_explicit_confirmation_and_one_write",
        "External_move_requires_named_destination_and_keeps_original_identity",
        "Changed_source_page_or_nonce_revokes_inflight_preview",
        "Forged_preview_snapshot_and_stale_row_cannot_be_approved",
        "Ambiguous_committed_reply_never_retries_with_new_operation_id"
    ):
        require("tests", phrase)
    if files["tests"].count("[Fact]") < 6:
        errors.append("Expected at least six test-first movement regressions.")
    for key in ("core", "release"):
        require(key, "python build/test-native-phase8-personnel-movement-contract.py")
    for token in (
        "new SqliteConnection(", "ExecuteInWriteTransactionAsync(",
        "new PersonnelMovementService(",
        "INSERT INTO PersonnelMovements",
        "UPDATE PersonAssignments"
    ):
        if any(token in files[key] for key in ("vm", "xaml", "view")):
            errors.append("Native presentation bypasses canonical movements: " + token)
    return errors


if __name__ == "__main__":
    errors = problems(TEXT)
    if errors:
        raise SystemExit("PHASE8 NATIVE TRANSFER FAIL:\n" + "\n".join(errors))
    for key, token in (
        ("backend", "new PersonnelMovementService(database)"),
        ("backend", '["operationId"] = operationId'),
        ("backend", "VerifyPersonnelManagerAsync(actor, ct)"),
        ("canonical", "AlreadyMovedToday("),
        ("vm", "_uncertainActor = actor"),
        ("vm", "ValidatePreview(result, draft)"),
        ("admin", "_status == 1 && _site.Length > 0"),
        ("shell", "Administration.Movement.ForceCancel()"),
        ("roster", 'Command="{Binding OpenExternalTransferCommand}"'),
        ("tests", "Ambiguous_committed_reply_never_retries_with_new_operation_id"),
        ("core", "python build/test-native-phase8-personnel-movement-contract.py"),
        ("release", "python build/test-native-phase8-personnel-movement-contract.py"),
    ):
        modified = TEXT.copy()
        modified[key] = modified[key].replace(token, "REMOVED_NEGATIVE_TEST")
        if not problems(modified):
            raise SystemExit(f"Removal passed movement contract unexpectedly: {key}: {token}")
    print("PHASE8 NATIVE TRANSFER PASS: canonical SQLCipher, one-use token, actor nonce, 1-person, Windows Core+Release")
