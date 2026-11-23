#!/usr/bin/env python3
"""Test-first, fail-closed native archived identity return guard.

Full C# xUnit, original SQLCipher integration and real Windows Release
shards/package acceptance remain mandatory in both Windows workflows.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
FILES = {
    "contracts": "src/AlTamam.NativePresentation/NativeArchivedReturnContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeArchivedReturnViewModel.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/ArchivedReturnService.cs",
    "tests": "tests/AlTamam.UnitTests/NativeArchivedReturnPhase8Tests.cs",
    "integration": "tests/AlTamam.Tests/Bridge/ArchivedIdentityReturnIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeArchivedReturnView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeArchivedReturnView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml"
}
TEXT = {k: (ROOT / p).read_text(encoding="utf-8") for k, p in FILES.items()}


def verify(data):
    problems = []

    def requires(key, snippet):
        if snippet not in data[key]:
            problems.append(f"{key}: missing archived-return safeguard: {snippet}")

    for key in ("xaml", "roster"):
        try:
            ET.fromstring(data[key])
        except ET.ParseError as exc:
            problems.append(f"{key}: malformed XAML: {exc}")

    for token in (
        "INativeArchivedReturnBackend", "NativeArchivedReturnDraft(",
        "NativeArchivedReturnCandidate(", "NativeArchivedReturnPreview(",
        "NativeArchivedReturnReceipt(", "PreviewReturnAsync(", "CommitReturnAsync("
    ):
        requires("contracts", token)

    for token in (
        "new ArchivedReturnService(database)",
        ".PreviewAsync(new JsonObject",
        ".CommitReturnAsync(new JsonObject",
        '["visibleName"] = draft.OriginalName',
        '["serviceNumberA"] = draft.ServiceNumberA',
        '["siteId"] = draft.DestinationSiteId',
        '["previewToken"] = preview.PreviewToken',
        '["personId"] = preview.Draft.PersonId',
        '["decision"] = "same"',
        '["operationId"] = operationId',
        'RequiredBoolean(data, "requiresResolution")',
        'RequiredBoolean(data, "numberContested")',
        'RequiredText(x, "personId")',
        'RequiredText(x, "name")',
        'RequiredText(x, "serviceNumberA")',
        'RequiredNumber(x, "revision")',
        'RequiredNumber(x, "organizationalStatus")',
        'RequiredBoolean(x, "numberMatch")',
        'RequiredBoolean(x, "nameMatch")',
        "VerifyPersonnelManagerAsync(actor, ct)",
        "ResolveSessionAsync(actor.SessionNonce, ct)",
        'RequiredBoolean(data, "returned")',
        'RequiredText(data, "siteId") != preview.Draft.DestinationSiteId',
        'RequiredNumber(data, "revision") != preview.Draft.Revision + 1'
    ):
        requires("backend", token)

    for token in (
        "BridgePreviewTokens.Issue(", "BridgePreviewTokens.ConsumeAsync(",
        "MutationIdempotency.StartAsync(", "MutationIdempotency.StoreAsync(",
        'Text(data,"decision")!="same"',
        'p.ActiveFlag=0 AND p.OrganizationalStatus IN(2,3,4)',
        'p.NormalizedServiceNumberA=$number OR p.NormalizedName=$name',
        "SELECT PayloadCanonicalJson FROM PreviewTokens",
        "PeriodClosures", "PersonnelMovements", "PersonIdentityHistory",
        "PersonAssignments", "PERSON_RETURN", "ExecuteInWriteTransactionAsync("
    ):
        requires("canonical", token)
    requires("integration", "ExternalTransferReturn_ReusesOriginalIdAndAddsDatedReturnMovement")

    for token in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor", "_onPage?.Invoke(row) == true",
        "row.StatusName is \"منقول خارجيا\" or \"مفصول\" or \"مؤرشف\"",
        "CurrentDraft()", "InvalidatePreview()",
        "PreviewReturnAsync(actor, draft, linked.Token)",
        "ValidatePreview(result, draft)",
        "SelectedCandidate(preview, draft)",
        "IdentityConfirmed", "_identityConfirmed",
        "_attempted = true", "Guid.NewGuid().ToString(\"N\")",
        "CommitReturnAsync(", "_uncertainActor = actor",
        "receipt.PersonId != preview.Draft.PersonId",
        "receipt.SiteId != preview.Draft.DestinationSiteId",
        "receipt.Revision != preview.Draft.Revision + 1",
        "public void ForceCancel()"
    ):
        requires("vm", token)
    if data["vm"].count("await _backend.CommitReturnAsync") != 1:
        problems.append("vm: exactly ONE explicit audited return write is allowed")

    for token in (
        "Return = new NativeArchivedReturnViewModel()",
        "Return.AttachBackend(", "OpenArchivedReturnCommand",
        "Return.Open(row, _sites)", "Return.ForceCancel()",
        "!Return.IsOpen", "CanOpenArchivedReturn(",
        "_rows.Any(current => ReferenceEquals(current, row))"
    ):
        requires("admin", token)
    requires("shell", "Administration.Return.ForceCancel()")
    for token in (
        'Command="{Binding PreviewCommand}"',
        'Command="{Binding ConfirmCommand}"',
        'Command="{Binding CloseCommand}"',
        'IsChecked="{Binding IdentityConfirmed, Mode=TwoWay',
        'SelectedValue="{Binding SelectedSiteId, Mode=TwoWay',
        'ItemsSource="{Binding Preview.Candidates}"',
        'Text="{Binding OriginalPersonId}"',
        "ConfirmationNote", "CompletionNote"
    ):
        requires("xaml", token)
    requires("view", "InitializeComponent()")
    requires("roster", 'Command="{Binding OpenArchivedReturnCommand}"')
    requires("roster", 'DataContext="{Binding Return}"')

    for test in (
        "Only_current_archived_row_and_manager_can_start_return",
        "Exact_destination_and_current_identity_precede_nonwriting_preview",
        "Identity_confirmation_is_explicit_even_when_multiple_candidates_match",
        "Stale_candidate_revision_or_wrong_person_never_enables_confirm",
        "Logout_or_source_page_change_drops_late_preview",
        "Explicit_same_human_confirmation_commits_once_without_new_identity",
        "Lost_write_receipt_never_resends_and_blocks_same_session_retry"
    ):
        requires("tests", test)
    if data["tests"].count("[Fact]") < 7:
        problems.append("tests: seven C# regressions are required")

    for key in ("core", "release"):
        lines = data[key].splitlines()
        command = "python build/test-native-phase8-archived-return-contract.py"
        indices = [i for i, line in enumerate(lines) if line.strip() == command]
        if len(indices) != 1:
            problems.append(f"{key}: expected exactly one return contract invocation")
        elif indices[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[indices[0]+1]
        ):
            problems.append(f"{key}: return guard must fail closed immediately")

    for key in ("contracts", "vm", "xaml", "view"):
        for bad in (
            "new SqliteConnection(", "UPDATE Persons SET",
            "DELETE FROM Persons", "INSERT INTO PersonAssignments",
            "new ArchivedReturnService(", "ExecuteInWriteTransactionAsync("
        ):
            if bad in data[key]:
                problems.append(f"{key}: bypassed original identity return SQLCipher: {bad}")
    return problems


if __name__ == "__main__":
    errors = verify(TEXT)
    if errors:
        raise SystemExit("PHASE8 ARCHIVED RETURN FAIL:\n" + "\n".join(errors))
    for key, token in (
        ("backend", '["decision"] = "same"'),
        ("backend", "ResolveSessionAsync(actor.SessionNonce, ct)"),
        ("canonical", "BridgePreviewTokens.ConsumeAsync("),
        ("vm", "_identityConfirmed"),
        ("vm", "_uncertainActor = actor"),
        ("admin", "Return.AttachBackend("),
        ("shell", "Administration.Return.ForceCancel()"),
        ("xaml", 'ItemsSource="{Binding Preview.Candidates}"'),
        ("roster", 'DataContext="{Binding Return}"'),
        ("tests", "Lost_write_receipt_never_resends_and_blocks_same_session_retry"),
        ("core", "python build/test-native-phase8-archived-return-contract.py"),
        ("release", "python build/test-native-phase8-archived-return-contract.py")
    ):
        modified = TEXT.copy()
        modified[key] = modified[key].replace(token, "REMOVED_FOR_NEGATIVE_TEST")
        if not verify(modified):
            raise SystemExit(f"Missing negative regression: {key}: {token}")
    print("PHASE8 ARCHIVED RETURN PASS: original PersonId, explicit sameness, nonce, one-write, Windows Core+Release")
