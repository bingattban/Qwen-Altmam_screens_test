#!/usr/bin/env python3
"""Phase 8 native personnel create/edit approvals security contract.

Both Windows pipelines run this first; xUnit tests and real package acceptance
are still mandatory. Refuses regression to direct SQLite, missing canonical
previews, missing manager RBAC, or automatic re-sends after uncertain commits.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelMutationContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelMutationViewModel.cs",
    "tests": "tests/AlTamam.UnitTests/NativePersonnelMutationsPhase8Tests.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/PersonRosterService.cs",
    "archived": "src/AlTamam.Application/Bridge/ArchivedReturnService.cs",
    "directory": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativePersonnelMutationView.xaml",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelMutationView.xaml.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k: (ROOT / path).read_text(encoding="utf-8") for k, path in FILES.items()}


def problems(files):
    errors = []

    def require(key, phrase):
        if phrase not in files[key]:
            errors.append(f"{key}: required Phase8 identity invariant missing: {phrase}")

    for k in ("xaml", "roster"):
        try:
            ET.fromstring(files[k])
        except ET.ParseError as exc:
            errors.append(f"{k}: native WPF XAML invalid: {exc}")

    for token in (
        "INativePersonnelMutationBackend", "NativePersonnelMutationDraft(",
        "NativePersonnelMutationPreview(", "NativePersonnelMutationReceipt(",
        "PreviewAsync(", "CommitAsync(",
    ):
        require("contracts", token)

    for token in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "VerifyPersonnelManagerAsync(actor, ct)",
        "new ArchivedReturnService(database)", "PreviewAsync(new JsonObject",
        "new PersonRosterService(database)", "PreviewPersonIdentityUpdateAsync(",
        "CreatePersonAsync(", "UpdatePersonIdentityAsync(",
        '["operationId"] = operationId',
        '["expectedRevision"] = preview.Draft.BaseRevision',
        '["firstRecordedLocalDate"] = preview.EffectiveLocalDate',
        '["actorHint"] = actor.UserId', "ResolveSessionAsync(actor.SessionNonce, ct)",
        "await VerifyPersonnelManagerAsync(actor, ct).ConfigureAwait(false)",
        "RequiredNumber(result, \"revision\")", "new NativePersonnelMutationReceipt(",
        "preview.RequiresIdentityResolution",
    ):
        require("backend", token)

    for token in (
        "MutationIdempotency.StartAsync(", "MutationIdempotency.StoreAsync(",
        "PersonIdentityHistory", "DuplicateServiceNumber",
        "PERSON_IDENTITY_UPDATE", "PERSON_CREATE",
        "archivedNumber", "identityResolutionToken",
    ):
        require("canonical", token)
    for token in ("new JsonObject", '["requiresResolution"]=true',
                  "PreviewOperation"):
        require("archived", token)

    for token in (
        "OpenCreate(", "OpenEdit(", "PreviewAsync(", "ConfirmAsync(",
        "ForceCancel()", "HasUnverifiedCommit", "_writeAttempted",
        "_unverifiedActor = actor", "Guid.NewGuid().ToString(\"N\")",
        "_preview.RequiresIdentityResolution", "IsOriginalContextValid()",
        "ValidatePreview(authoritative, draft)", "receipt.IsCreate",
        "CommittedAndClosed", "CanConfirm",
    ):
        require("vm", token)
    if "await _backend.CommitAsync(actor, preview, operationId" not in files["vm"]:
        errors.append("Canonical write must use the immutable one-time operationId.")
    if files["vm"].count("await _backend.CommitAsync") != 1:
        errors.append("Identity UI must have exactly one write invocation.")

    for token in (
        "OpenCreateCommand", "OpenEditCommand", "Mutation.AttachBackend(",
        "Mutation.OpenCreate(_sites)", "Mutation.OpenEdit(person)",
        "Mutation.ForceCancel()", "Mutation.CommittedAndClosed",
        "ReferenceEquals(current, row)",
    ):
        require("directory", token)
    for token in ("Administration.Mutation.ForceCancel()", "Administration.Clear();"):
        require("shell", token)
    for token in (
        "Text=\"{Binding FormName", "Text=\"{Binding FormNumber",
        "Text=\"{Binding FormReason", "SelectedValue=\"{Binding FormSiteId",
        "Command=\"{Binding PreviewCommand}",
        "Command=\"{Binding ConfirmCommand}",
        "Visibility=\"{Binding HasPreview",
        "Text=\"{Binding ConfirmationNote",
    ):
        require("xaml", token)
    for token in (
        'IsReadOnly="True"', "Command=\"{Binding OpenCreateCommand}",
        "Command=\"{Binding OpenEditCommand}",
        "NativePersonnelMutationView",
    ):
        require("roster", token)
    require("view", "InitializeComponent()")

    for token in (
        "Missing_actor_and_supervisor_cannot_preview_or_write",
        "Create_requires_real_archive_preview_and_explicit_one_time_confirmation",
        "Archived_identity_collision_and_changed_preview_fields_never_commit",
        "Edit_uses_frozen_real_person_id_revision_reason_and_authoritative_before_after",
        "Replaced_row_and_nonce_logout_cancel_stale_preview_and_display",
        "Ambiguous_commit_error_locks_operation_instead_of_retrying_or_claiming_success",
    ):
        require("tests", token)
    if files["tests"].count("[Fact]") < 6:
        errors.append("Missing test-first personnel create/edit xUnit cases.")

    for key in ("core", "release"):
        require(key, "python build/test-native-phase8-personnel-write-contract.py")
    for token in ("new SqliteConnection(", "ExecuteInWriteTransactionAsync(",
                  "new PersonRosterService(", "new ArchivedReturnService(",
                  "CreatePersonAsync(", "UpdatePersonIdentityAsync("):
        if token in files["vm"] or token in files["xaml"] or token in files["view"]:
            errors.append("WPF layer must not bypass canonical personnel backend: " + token)

    return errors


if __name__ == "__main__":
    errors = problems(TEXT)
    if errors:
        raise SystemExit("PHASE8 PERSONNEL MUTATION FAIL:\n" + "\n".join(errors))
    for key, needle in (
        ("backend", "new ArchivedReturnService(database)"),
        ("backend", "PreviewPersonIdentityUpdateAsync("),
        ("backend", '["operationId"] = operationId'),
        ("backend", "VerifyPersonnelManagerAsync(actor, ct)"),
        ("vm", "HasUnverifiedCommit"),
        ("vm", "_unverifiedActor = actor"),
        ("vm", "ValidatePreview(authoritative, draft)"),
        ("directory", "Mutation.ForceCancel()"),
        ("xaml", 'Command="{Binding ConfirmCommand}"'),
        ("tests", "Ambiguous_commit_error_locks_operation_instead_of_retrying_or_claiming_success"),
        ("core", "python build/test-native-phase8-personnel-write-contract.py"),
        ("release", "python build/test-native-phase8-personnel-write-contract.py"),
    ):
        modified = TEXT.copy()
        modified[key] = modified[key].replace(needle, "REMOVED_BY_NEGATIVE_REGRESSION")
        if not problems(modified):
            raise SystemExit(f"Unsafe Phase8 identity mutation removal passed: {key}:{needle}")
    print("PHASE8 PERSONNEL WRITE PASS: actor+nonce, canonical preview, one-write idempotency, archive guard, Core+Release")
