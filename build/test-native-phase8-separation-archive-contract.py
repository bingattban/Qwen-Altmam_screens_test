#!/usr/bin/env python3
"""Fail-closed native dismissal archive safety guard for BOTH Windows pipelines.

Test-first C# xUnit regressions, full canonical SQLCipher integration and Windows
packaged acceptance remain mandatory; this is a supplemental early negative gate.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "contracts": "src/AlTamam.NativePresentation/NativeSeparationArchiveContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeSeparationArchiveViewModel.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/PersonnelSeparationService.cs",
    "tests": "tests/AlTamam.UnitTests/NativeSeparationArchivePhase8Tests.cs",
    "canonicaltests": "tests/AlTamam.Tests/Bridge/PersonnelSeparationIntegrationTests.cs",
    "admin": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeSeparationArchiveView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeSeparationArchiveView.xaml.cs",
    "roster": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml"
}
TEXT = {name: (ROOT / path).read_text(encoding="utf-8") for name, path in PATHS.items()}


def errors_for(files):
    issues = []

    def require(key, token):
        if token not in files[key]:
            issues.append(f"{key}: missing canonical separation archive control: {token}")

    for key in ("xaml", "roster"):
        try:
            ET.fromstring(files[key])
        except ET.ParseError as exc:
            issues.append(f"{key}: invalid WPF XML: {exc}")

    for token in (
        "INativeSeparationArchiveBackend", "NativeSeparationArchiveFilter(",
        "NativeSeparationArchivePage(", "NativeSeparationArchiveRow(",
        "NativeSeparationArchiveDetail(", "ReadSeparationsAsync(",
        "ReadSeparationDetailsAsync("
    ):
        require("contracts", token)

    for token in (
        "new PersonnelSeparationService(database)",
        ".ArchiveAsync(new JsonObject",
        ".DetailsAsync(new JsonObject",
        "VerifyPersonnelManagerAsync(actor, ct)",
        '["siteId"] = filter.SiteId',
        '["term"] = filter.SearchTerm',
        '["pageSize"] = filter.PageSize',
        'RequiredText(entry, "reason")',
        'RequiredText(entry, "actorUserId")',
        'RequiredText(entry, "actorDisplay")',
        'RequiredNumber(data, "total")',
        'RequiredBoolean(data, "hasNext")',
        'RequiredText(data, "movementId") != movementId',
        "filter.PageSize != 50",
        "filter.SearchTerm.Length > 100"
    ):
        require("backend", token)
    for token in (
        "m.MovementType=3", "m.SourceSiteId=$site",
        "m.PersonNameSnapshot LIKE $term",
        "m.ServiceNumberASnapshot LIKE $term",
        "m.SeparationReason", "m.EffectiveLocalDate",
        "m.ActorUserId", "ORDER BY m.EffectiveLocalDate DESC",
        "LIMIT $limit OFFSET $offset", "MovementType=3;"
    ):
        require("canonical", token)
    require("canonicaltests", "SeparationRequiresServerDelay_ThenKeepsHistory_AuditAndMonthlyCell")
    for token in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "_liveActor?.Invoke() == actor", "ReadSeparationsAsync(actor, filter, linked.Token)",
        "ReadSeparationDetailsAsync(actor, row.MovementId, linked.Token)",
        "ReferenceEquals(x, row)", "ValidatePage(page, filter)",
        "detail.Reason != row.Reason", "detail.PersonId != row.PersonId",
        "detail.ActorUserId != row.ActorUserId",
        "detail.CommittedAtUtc != row.CommittedAtUtc",
        "page.Rows.Count > filter.PageSize",
        "page.HasNext != ((long)filter.Page * filter.PageSize < page.Total)",
        "CloseDetails()", "public void Close()", "public async Task SearchAsync(",
        "public async Task SelectSiteAsync(", "Cancel"
    ):
        require("vm", token)

    for token in (
        "SeparationArchive = new NativeSeparationArchiveViewModel()",
        "SeparationArchive.AttachBackend(",
        "OpenSeparationArchiveCommand",
        "SeparationArchive.OpenAsync(_sites)",
        "SeparationArchive.Close()",
        "!SeparationArchive.IsOpen"
    ):
        require("admin", token)
    require("shell", "Administration.SeparationArchive.Close()")
    for token in (
        'Text="{Binding SearchDraft, Mode=TwoWay',
        'SelectedValue="{Binding SelectedSiteId, Mode=OneWay}"',
        'Command="{Binding SearchCommand}"',
        'Command="{Binding DetailsCommand}"',
        'Command="{Binding CloseCommand}"',
        'ItemsSource="{Binding Rows}"',
        'IsReadOnly="True"',
        'Text="{Binding SelectedDetail.Reason}"',
        "OnSiteChanged"
    ):
        require("xaml", token)
    require("view", "await vm.SelectSiteAsync(site)")
    require("roster", 'Command="{Binding OpenSeparationArchiveCommand}"')
    require("roster", 'DataContext="{Binding SeparationArchive}"')
    for name in (
        "Archive_is_denied_without_live_manager_or_unchanged_session",
        "Snapshot_reason_actor_and_date_survive_read_only_paging",
        "Search_and_site_filter_reset_page_without_forging_history",
        "Stale_inflight_page_and_details_are_hidden_after_close",
        "Forged_details_and_unselected_movement_ids_are_rejected",
        "Malformed_backend_page_fails_closed_without_reusing_cached_personnel"
    ):
        require("tests", name)
    if files["tests"].count("[Fact]") < 6:
        issues.append("tests: expected at least six test-first C# regressions")

    for key in ("core", "release"):
        lines = files[key].splitlines()
        invocation = "python build/test-native-phase8-separation-archive-contract.py"
        indices = [i for i, line in enumerate(lines) if line.strip() == invocation]
        if len(indices) != 1:
            issues.append(f"{key}: archive contract invocation missing or duplicated")
        elif indices[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[indices[0] + 1]
        ):
            issues.append(f"{key}: archive contract must fail closed immediately")

    for key in ("contracts", "vm", "xaml", "view"):
        for forbidden in (
            "new SqliteConnection(", "DELETE FROM Persons",
            "UPDATE Persons SET", "INSERT INTO PersonnelMovements",
            "ExecuteInWriteTransactionAsync(", "new PersonnelSeparationService(",
            "CommitSeparationAsync(", "CommitAsync(new JsonObject"
        ):
            if forbidden in files[key]:
                issues.append(f"{key}: archive must not mutate original SQLCipher: {forbidden}")
    return issues


if __name__ == "__main__":
    failures = errors_for(TEXT)
    if failures:
        raise SystemExit("PHASE8 SEPARATION ARCHIVE FAIL:\n" + "\n".join(failures))
    for key, token in (
        ("backend", "new PersonnelSeparationService(database)"),
        ("backend", 'RequiredText(entry, "reason")'),
        ("canonical", "m.MovementType=3"),
        ("vm", "detail.Reason != row.Reason"),
        ("vm", "_liveActor?.Invoke() == actor"),
        ("admin", "SeparationArchive.AttachBackend("),
        ("shell", "Administration.SeparationArchive.Close()"),
        ("roster", 'DataContext="{Binding SeparationArchive}"'),
        ("tests", "Forged_details_and_unselected_movement_ids_are_rejected"),
        ("core", "python build/test-native-phase8-separation-archive-contract.py"),
        ("release", "python build/test-native-phase8-separation-archive-contract.py")
    ):
        changed = TEXT.copy()
        changed[key] = changed[key].replace(token, "REMOVED_FOR_NEGATIVE_TEST")
        if not errors_for(changed):
            raise SystemExit(f"Missing negative-path regression for {key}:{token}")
    print("PHASE8 SEPARATION ARCHIVE PASS: original immutable SQLCipher, reason, actor nonce, Core+Release")
