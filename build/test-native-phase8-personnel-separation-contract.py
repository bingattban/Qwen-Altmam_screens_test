#!/usr/bin/env python3
"""Phase 8 native separation safety: fail-closed structural regression in BOTH Windows gates.

C# xUnit, original SQLCipher integration and real Windows package/UI tests remain mandatory.
This early guard protects the existing canonical service and prevents bypassing review.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelSeparationContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelSeparationViewModel.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/PersonnelSeparationService.cs",
    "removal": "src/AlTamam.NativePresentation/NativeRosterRemovalEvidence.cs",
    "removal_tests": "tests/AlTamam.UnitTests/NativeRosterRemovalPreviewPhase12Tests.cs",
    "roster_service": "src/AlTamam.Application/Bridge/PersonRosterService.cs",
    "removal_sql_tests": "tests/AlTamam.Tests/Bridge/Section02RosterLifecycleTests.cs",
    "removal_gateway": "src/AlTamam.Application/Roster/RosterAuthorizedCommands.cs",
    "tests": "tests/AlTamam.UnitTests/NativePersonnelSeparationPhase8Tests.cs",
    "canonicaltests": "tests/AlTamam.Tests/Bridge/PersonnelSeparationIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativePersonnelSeparationView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelSeparationView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml"
}
TEXT = {key: (ROOT / path).read_text(encoding="utf-8") for key, path in PATHS.items()}


def problems(files):
    errors = []

    def require(key, value):
        if value not in files[key]:
            errors.append(f"{key}: missing native separation safeguard: {value}")

    for key in ("xaml", "roster"):
        try:
            ET.fromstring(files[key])
        except ET.ParseError as error:
            errors.append(f"{key}: invalid WPF XAML: {error}")

    for t in ("INativePersonnelSeparationBackend", "NativePersonnelSeparationDraft(",
              "NativePersonnelSeparationPreview(", "NativePersonnelSeparationReceipt(",
              "PreviewSeparationAsync(", "CommitSeparationAsync("):
        require("contracts", t)

    for t in (
        "new PersonnelSeparationService(database)",
        "PreviewSeparationAsync(", "CommitSeparationAsync(",
        ".PreviewAsync(new JsonObject", ".CommitAsync(new JsonObject",
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSessionAsync(actor.SessionNonce, ct)",
        '["reason"] = draft.Reason', '["businessDate"] = today',
        '["previewToken"] = preview.PreviewToken',
        '["operationId"] = operationId',
        'RequiredText(data, "assignmentId")',
        'RequiredNumber(data, "minimumConfirmationSeconds")',
        'RequiredNumber(data, "rowVersion")',
        'RequiredText(data, "siteId")',
        'RequiredText(data, "siteName")',
        'RequiredText(data, "reason")',
        'RequiredBoolean(data, "separated")',
        'RequiredText(data, "personId") != preview.Draft.PersonId',
        'RequiredText(data, "date") != preview.BusinessDate',
        "minimumSeconds != 5"
    ):
        require("backend", t)
    if files["backend"].count("await VerifyPersonnelManagerAsync(actor, ct)") < 7:
        errors.append("backend: actor checks must survive before and after separation service")

    for t in (
        "BridgePreviewTokens.Issue(", "BridgePreviewTokens.ConsumeAsync(",
        "MutationIdempotency.StartAsync(", "MutationIdempotency.StoreAsync(",
        "_time.GetUtcNow()-issued<TimeSpan.FromSeconds(5)",
        "AccountLifecycleService.SuspendAccountsForPersonnelExitAsync(",
        "BridgeAuditWriter.WriteAsync(", "PERSON_SEPARATE",
        "ExecuteInWriteTransactionAsync(", "PersonAssignments",
        "PersonnelMovements", "OrganizationalStatus=3"
    ):
        require("canonical", t)
    require("canonicaltests", "SeparationRequiresServerDelay_ThenKeepsHistory_AuditAndMonthlyCell")
    require("canonicaltests", "SeparationExactRetry_IsRecordedOnceAfterEnforcedFiveSecondDelay")

    for t in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor", "_onPage?.Invoke(row) == true",
        "FormReason", "CurrentDraft()", "InvalidatePreview()",
        "ValidatePreview(result, draft)", "MinimumConfirmationSeconds != 5",
        "Task.Delay(TimeSpan.FromSeconds(expected.MinimumConfirmationSeconds)",
        "WaitingForConfirmation", "_delayElapsed", "CancelDelay()",
        "PreviewSeparationAsync(actor, draft, linked.Token)",
        "CommitSeparationAsync(actor, preview, operationId, linked.Token)",
        "Guid.NewGuid().ToString(\"N\")", "_attempted = true",
        "_uncertainActor = actor", "public void ForceCancel()",
        "receipt.PersonId != preview.Draft.PersonId",
        "receipt.BusinessDate != preview.BusinessDate"
    ):
        require("vm", t)
    if files["vm"].count("await _backend.CommitSeparationAsync") != 1:
        errors.append("vm: exactly one explicit write call is permitted")

    for t in (
        "Separation = new NativePersonnelSeparationViewModel()",
        "Separation.AttachBackend(", "Separation.ForceCancel()",
        "OpenSeparationCommand", "CanOpenSeparation(",
        "_status == 1 && _site.Length > 0",
        "_rows.Any(current => ReferenceEquals(current, row))",
        "Separation.CommittedAndClosed"
    ):
        require("admin", t)
    require("shell", "Administration.Separation.ForceCancel()")
    for t in (
        'Text="{Binding FormReason, Mode=TwoWay',
        'Command="{Binding PreviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'Command="{Binding CloseCommand}"',
        "WaitingForConfirmation", "ReviewMessage",
        "CompletionMessage", "IsEnabled=\"{Binding CanEdit}\"",
        "MaxLength=\"1000\""
    ):
        require("xaml", t)
    require("view", "InitializeComponent()")
    for t in (
        'Command="{Binding OpenSeparationCommand}"',
        'DataContext="{Binding Separation}"',
        "NativePersonnelSeparationView"
    ):
        require("roster", t)

    for t in (
        "Supervisor_missing_session_and_unselected_source_are_denied",
        "Reason_and_exact_source_must_precede_canonical_nonwriting_preview",
        "Mandatory_five_second_review_blocks_early_confirmation",
        "Forged_person_assignment_or_revision_preview_cannot_be_approved",
        "Navigation_or_nonce_change_revokes_pending_preview_and_delay",
        "Lost_commit_receipt_never_automatically_retries_or_reuses_approval"
    ):
        require("tests", t)
    if files["tests"].count("[Fact]") < 6:
        errors.append("tests: six test-first regressions are mandatory")

    # Removal from force is archival, not the independent separation action.
    begin=files["backend"].find("public async Task<NativeRosterRemovalPreview> PreviewRosterRemovalAsync(")
    end=files["backend"].find("public async Task<NativePersonnelSeparationPreview> PreviewSeparationAsync(",begin)
    if not (0<=begin<end):
        errors.append("Native removal preview boundary missing.")
    else:
        entry=files["backend"][begin:end]
        for token in ("using var nativeOperation = operations.Enter();",
                      "RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce(actor.Role)",
                      "Array.AsReadOnly(members.ToArray())",
                      "ResolveSitesPrincipalAsync(actor,ct)",
                      "principal.SessionRevocation",
                      ".PreviewRemovalAsync(principal,",
                      'RequiredArray(data,"expectedVersions")',
                      'RequiredText(version,"personId")!=id',
                      "NativeRosterRemovalEvidence.ValidateAndFreeze(selection,"):
            if token not in entry:
                errors.append("Native removal preview missing: "+token)
        if any(token in entry for token in (".RemoveAsync(", ".ArchivePersonsAsync(", ".CommitAsync(", "PersonnelSeparationService")):
            errors.append("Removal preview must not commit, separate or bypass canonical gateway.")
    for token in ("CanApprove=>Candidates.Count>0", "x.Blocker is null",
                  "preview.BaseRevision!=members.Sum(x=>x.Revision)", "row.Member!=members[i]",
                  "Array.AsReadOnly(preview.Candidates.ToArray())"):
        require("removal",token)
    require("roster_service",'row.Org != 1 ? "الفرد ليس على القوة الفعالة"')
    require("roster_service","!row.Eligible ?")
    require("removal_gateway","CanArchivePersonsFromActiveForce(actor.Role)")
    require("removal_sql_tests","ArchivePreview_ReportsCanonicalCommitBlockersWithoutRosterWrites")
    for token in ("Exact_order_identity_version_and_aggregate_are_required",
                  "Any_blocker_disables_entire_batch_and_freezes_evidence",
                  "Reject_duplicate_or_unbounded_selection_and_invalid_receipt_fields"):
        require("removal_tests",token)
    for key in ("core", "release"):
        lines = files[key].splitlines()
        call = "python build/test-native-phase8-personnel-separation-contract.py"
        indices = [i for i, line in enumerate(lines) if line.strip() == call]
        if len(indices) != 1:
            errors.append(f"{key}: expected exactly one native separation contract invocation")
        elif indices[0]+1 >= len(lines) or (
            'if ($LASTEXITCODE -ne 0) { throw ' not in lines[indices[0]+1]
        ):
            errors.append(f"{key}: no immediate fail-closed exit check after separation contract")

    for key in ("contracts", "vm", "xaml", "view"):
        for forbidden in ("new SqliteConnection(", "INSERT INTO PersonnelMovements",
                          "UPDATE Persons SET", "ExecuteInWriteTransactionAsync(",
                          "new PersonnelSeparationService(", "DELETE FROM Persons"):
            if forbidden in files[key]:
                errors.append(f"{key}: bypasses canonical separation service: {forbidden}")
    return errors


if __name__ == "__main__":
    issues = problems(TEXT)
    if issues:
        raise SystemExit("PHASE8 PERSONNEL SEPARATION FAIL:\n" + "\n".join(issues))
    for key, token in (
        ("backend", ".PreviewRemovalAsync(principal,"),
        ("backend", 'RequiredArray(data,"expectedVersions")'),
        ("backend", "NativeRosterRemovalEvidence.ValidateAndFreeze(selection,"),
        ("removal", "row.Member!=members[i]"),
        ("removal", "x.Blocker is null"),
        ("roster_service", "!row.Eligible ?"),
        ("backend", "new PersonnelSeparationService(database)"),
        ("backend", '["operationId"] = operationId'),
        ("backend", "minimumSeconds != 5"),
        ("canonical", "_time.GetUtcNow()-issued<TimeSpan.FromSeconds(5)"),
        ("vm", "ValidatePreview(result, draft)"),
        ("vm", "_uncertainActor = actor"),
        ("vm", "_delayElapsed"),
        ("admin", "CanOpenSeparation("),
        ("shell", "Administration.Separation.ForceCancel()"),
        ("roster", 'DataContext="{Binding Separation}"'),
        ("tests", "Mandatory_five_second_review_blocks_early_confirmation"),
        ("core", "python build/test-native-phase8-personnel-separation-contract.py"),
        ("release", "python build/test-native-phase8-personnel-separation-contract.py")
    ):
        modified = TEXT.copy()
        modified[key] = modified[key].replace(token, "REMOVED_FOR_NEGATIVE_TEST")
        if not problems(modified):
            raise SystemExit(f"Missing negative-path detection for {key}:{token}")
    print("PHASE8 NATIVE SEPARATION PASS: original SQLCipher audit, reason, 5s, actor nonce, one write, Core/Release")
