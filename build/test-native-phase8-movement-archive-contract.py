#!/usr/bin/env python3
"""Phase 8 movement archive: structural fail-closed regression, Core and Release.

The canonical xUnit/SQLCipher and Windows UI runs remain authoritative;
this cheap preflight rejects common unsafe implementation substitutions.
"""
from __future__ import annotations

from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PATHS = {
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/PersonnelMovementService.cs",
    "contracts": "src/AlTamam.NativePresentation/NativeMovementArchiveContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeMovementArchiveViewModel.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeMovementArchiveView.xaml",
    "codebehind": "src/AlTamam.NativeDesktop/Views/NativeMovementArchiveView.xaml.cs",
    "directory": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeMovementArchivePhase8Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {key: (ROOT / path).read_text(encoding="utf-8")
        for key, path in PATHS.items()}


def problems(files: dict[str, str]) -> list[str]:
    errors = []

    def must(key: str, needle: str) -> None:
        if needle not in files[key]:
            errors.append(f"{key}: missing movement archive gate: {needle}")

    for key in ("xaml", "directory"):
        try:
            ET.fromstring(files[key])
        except ET.ParseError as err:
            errors.append(f"{key}: malformed native WPF XAML: {err}")

    for token in (
        "INativeMovementArchiveBackend",
        "NativeMovementArchiveFilter(", "NativeMovementArchivePage(",
        "NativeMovementArchiveDetail(", "ReadMovementsAsync(",
        "ReadMovementDetailsAsync("
    ):
        must("contracts", token)
    for token in (
        "INativeMovementArchiveBackend", "new PersonnelMovementService(database)",
        ".ArchiveAsync(new JsonObject", ".DetailsAsync(new JsonObject",
        "VerifyPersonnelManagerAsync(actor, ct)", '["page"] = filter.Page',
        '["pageSize"] = filter.PageSize', '["type"] = filter.Type',
        "RequiredText(entry, \"movementId\")",
        'RequiredText(data, "actorUsername")',
        'RequiredText(data, "movementId") != movementId',
        "NativeMovementArchivePage(", "NativeMovementArchiveDetail("
    ):
        must("backend", token)
    if files["backend"].count("await VerifyPersonnelManagerAsync(actor, ct)") < 4:
        errors.append("backend: archive list and detail reads must both verify exact SQLCipher actor twice")
    for token in (
        "public async Task<BridgeResponse> ArchiveAsync(",
        "public async Task<BridgeResponse> DetailsAsync(",
        "m.PersonNameSnapshot", "m.ServiceNumberASnapshot",
        "ORDER BY m.EffectiveLocalDate DESC", "MovementArchivePolicy.TryFilter("
    ):
        must("canonical", token)
    for token in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor", "CloseDetails();", "HideRows(",
        "ValidatePage(page, filter)", "_rows.Any(x => ReferenceEquals(x, row))",
        "detail.PersonId != row.PersonId", "detail.Name != row.Name",
        "detail.EffectiveDate != row.EffectiveDate",
        "detail.CommittedAtUtc != row.CommittedAtUtc",
        "CancellationTokenSource.CreateLinkedTokenSource(ct)",
        "public async Task OpenAsync(", "public void Close()"
    ):
        must("vm", token)
    for token in (
        "Archive = new NativeMovementArchiveViewModel()",
        "Archive.AttachBackend(archiveBackend",
        "Archive.Close()", "OpenMovementArchiveCommand"
    ):
        must("admin", token)
    must("shell", "Administration.Archive.Close()")
    for token in (
        'Command="{Binding DetailsCommand}"', 'Command="{Binding CloseCommand}"',
        'IsReadOnly="True"', "OnTypeChanged",
        'Visibility="{Binding IsOpen, Converter=',
        'Visibility="{Binding HasDetail, Converter='
    ):
        must("xaml", token)
    must("codebehind", "SelectTypeAsync(type)")
    for token in (
        "NativeMovementArchiveView", "OpenMovementArchiveCommand",
        'DataContext="{Binding Archive}"'
    ):
        must("directory", token)
    for token in (
        "Unauthenticated_or_supervisor_cannot_open_or_read_archive",
        "Archive_reads_historical_snapshots_in_bounded_canonical_pages",
        "Type_and_search_changes_invalidate_prior_page_and_result",
        "Invalid_filters_fail_closed_without_reusing_cached_personnel",
        "Logout_or_close_cancels_late_archive_results",
        "Forged_or_stale_details_never_replace_immutable_archive_identity"
    ):
        must("tests", token)
    if files["tests"].count("[Fact]") < 6:
        errors.append("tests: missing test-first archive regressions")
    for key in ("core", "release"):
        lines = files[key].splitlines()
        command = "python build/test-native-phase8-movement-archive-contract.py"
        matches = [i for i, line in enumerate(lines) if line.strip() == command]
        if len(matches) != 1:
            errors.append(f"{key}: missing or duplicate archive contract invocation")
        elif matches[0] + 1 >= len(lines) or (
            'if ($LASTEXITCODE -ne 0) { throw ' not in lines[matches[0] + 1]
        ):
            errors.append(f"{key}: archive contract failure could be masked")
    for key in ("vm", "xaml", "codebehind"):
        for forbidden in ("new SqliteConnection(", "ExecuteInWriteTransactionAsync(",
                          "INSERT INTO PersonnelMovements",
                          "new PersonnelMovementService(", "CommitMovementAsync("):
            if forbidden in files[key]:
                errors.append(f"{key}: native presentation bypasses immutable archive: {forbidden}")
    return errors


if __name__ == "__main__":
    defects = problems(TEXT)
    if defects:
        raise SystemExit("PHASE8 MOVEMENT ARCHIVE FAIL:\n" + "\n".join(defects))
    for key, token in (
        ("backend", ".ArchiveAsync(new JsonObject"),
        ("backend", "VerifyPersonnelManagerAsync(actor, ct)"),
        ("vm", "detail.PersonId != row.PersonId"),
        ("vm", "ValidatePage(page, filter)"),
        ("admin", "Archive.Close()"),
        ("shell", "Administration.Archive.Close()"),
        ("directory", 'DataContext="{Binding Archive}"'),
        ("tests", "Logout_or_close_cancels_late_archive_results"),
        ("core", "python build/test-native-phase8-movement-archive-contract.py"),
        ("release", "python build/test-native-phase8-movement-archive-contract.py"),
    ):
        changed = TEXT.copy()
        changed[key] = changed[key].replace(token, "NEGATIVE_REMOVED")
        if not problems(changed):
            raise SystemExit(f"Contract did not detect removal of {key}: {token}")
    print("PHASE8 MOVEMENT ARCHIVE PASS: immutable SQLCipher history, role/nonce, no writes, both Windows gates")
