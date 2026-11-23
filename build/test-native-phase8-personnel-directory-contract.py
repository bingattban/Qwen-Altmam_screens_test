#!/usr/bin/env python3
"""Phase 8 native WPF personnel manager — read-only, fail-closed static guard.

Runs in BOTH Windows Core and Release, before C# build/real xUnit and
four-package build. Does not replace actual SQLCipher/Windows tests.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelDirectoryContracts.cs",
    "vm": "src/AlTamam.NativePresentation/AdministrationPageViewModel.cs",
    "tests": "tests/AlTamam.UnitTests/NativePersonnelDirectoryPhase8Tests.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/PersonRosterService.cs",
    "policy": "src/AlTamam.Domain/Policies/RoleAuthorizationPolicy.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "pages": "src/AlTamam.NativePresentation/NativePageViewModels.cs",
    "templates": "src/AlTamam.NativeDesktop/Views/NativeSectionTemplates.xaml",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelDirectoryView.xaml.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k: (ROOT / p).read_text(encoding="utf-8") for k, p in FILES.items()}


def problems(files):
    failures = []

    def require(key, token):
        if token not in files[key]:
            failures.append(f"{key}: required Phase8 invariant missing: {token}")

    for key in ("templates", "xaml"):
        try:
            ET.fromstring(files[key])
        except ET.ParseError as err:
            failures.append(f"Malformed native WPF {key}: {err}")
    for token in (
        "interface INativePersonnelDirectoryReader",
        "ReadPersonnelCatalogueAsync(", "ReadPageAsync(", "ReadProfileAsync(",
        "NativePersonnelFilter(", "NativePersonnelSnapshot(",
        "NativePersonnelProfile(", "NativePersonnelIdentityHistory(",
        "NativePersonnelDisplayRow("
    ):
        require("contracts", token)
    for token in (
        "VerifyPersonnelManagerAsync(actor, ct)",
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "new PersonRosterService(database)",
        "QueryPersonsAsync(new JsonObject", "GetPersonSummaryAsync(new JsonObject",
        '["includeArchived"] = filter.OrganizationalStatus == 0',
        '["pageSize"] = filter.PageSize',
        'RequiredText(row, "personId")', "RequiredBoolean(row, \"active\")",
        "RequiredArray(data, \"customColumns\")",
        "RequiredArray(data, \"identityHistory\")",
        "new NativePersonnelSnapshot(", "new NativePersonnelProfile("
    ):
        require("backend", token)
    for token in ("QueryPersonsAsync(", "GetPersonSummaryAsync(",
                  "p.PersonId", "NormalizedServiceNumberA",
                  "RosterCustomFieldProjection.AttachAsync",
                  '["identityHistory"] = history'):
        require("canonical", token)
    require("policy", "CanAccessUsersManagement(UserRole role)")
    for token in (
        "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)",
        "ReadPersonnelCatalogueAsync(actor, linked.Token)",
        "_reader.ReadPageAsync(actor, filter, linked.Token)",
        "_reader.ReadProfileAsync(actor, row.PersonId, linked.Token)",
        "_boundActor != actor", "_liveActor?.Invoke() != actor",
        "version != _generation", "version != _profileGeneration",
        "ReferenceEquals(current, row)", "profile.PersonId != row.PersonId",
        "person.CustomCells[rank]", "SelectSiteAsync(", "SelectStatusAsync(",
        "SearchAsync(", "NextPageAsync(", "PreviousPageAsync(",
        "CloseProfile()", "HideRows(", "Validate(page, filter)",
        "new NativePersonnelDisplayRow("
    ):
        require("vm", token)
    for token in (
        "Administration = new AdministrationPageViewModel()",
        "Administration.AttachReader(personnelReader, () => _active)",
        "Administration.RefreshAsync()", "Administration.CloseProfile()",
        "Administration.Clear();",
        "screen != NativeScreen.Administration",
        "RoleAuthorizationPolicy.CanAccessUsersManagement(identity.Role)"
    ):
        require("shell", token)
    if "public sealed class AdministrationPageViewModel()" in files["pages"]:
        failures.append("Disconnected Administration placeholder must be removed.")
    require("templates", "<views:NativePersonnelDirectoryView")
    for token in (
        'Header="الرقم"', 'Header="الرقم (ع)"',
        'Header="الرتبة"', 'Header="الاسم"',
        'Header="الموقع"', 'Header="الحالة"',
        'IsReadOnly="True"', "SelectedSiteId", "SelectedStatus",
        "ProfileError", "SelectedProfile.IdentityHistory",
        "OpenProfileCommand", "CloseProfileCommand"
    ):
        require("xaml", token)
    for token in ("RenderCustomColumns()", "PersonnelGrid.Columns.Add(",
                  "CustomCells[{i}]", "OnSiteChanged(", "OnStatusChanged("):
        require("view", token)
    for token in (
        "Missing_session_and_supervisor_cannot_read_admin_personnel",
        "Authenticated_personnel_show_original_number_rank_site_and_stable_identity",
        "Site_status_search_and_pagination_use_exact_canonical_filters",
        "Forged_old_row_and_nonce_swap_never_read_another_person_profile",
        "Canonical_profile_history_is_read_only_and_closes_on_refresh",
        "Logout_discards_inflight_read_and_malformed_page_fails_closed"
    ):
        require("tests", token)
    if files["tests"].count("[Fact]") < 6:
        failures.append("Missing Phase8 manager-only native C# regressions.")
    for key in ("core", "release"):
        require(key, "python build/test-native-phase8-personnel-directory-contract.py")
    for token in ("new SqliteConnection(", "ExecuteInWriteTransactionAsync(",
                  "CreatePersonAsync(", "UpdatePersonIdentityAsync(",
                  "ArchivePersonsAsync(", "RevokeSessionAsync("):
        if token in files["vm"] or token in files["view"]:
            failures.append(f"Native admin presentation may not bypass canonical bridge: {token}")
    return failures


if __name__ == "__main__":
    errors = problems(TEXT)
    if errors:
        raise SystemExit("PHASE8 NATIVE PERSONNEL FAIL:\n" + "\n".join(errors))
    for key, token in (
        ("vm", "RoleAuthorizationPolicy.CanAccessUsersManagement(actor.Role)"),
        ("vm", "_reader.ReadProfileAsync(actor, row.PersonId, linked.Token)"),
        ("vm", "ReferenceEquals(current, row)"),
        ("backend", "new PersonRosterService(database)"),
        ("backend", "VerifyPersonnelManagerAsync(actor, ct)"),
        ("shell", "Administration.Clear();"),
        ("templates", "<views:NativePersonnelDirectoryView"),
        ("xaml", 'IsReadOnly="True"'),
        ("view", "CustomCells[{i}]"),
        ("tests", "Missing_session_and_supervisor_cannot_read_admin_personnel"),
        ("core", "python build/test-native-phase8-personnel-directory-contract.py"),
        ("release", "python build/test-native-phase8-personnel-directory-contract.py"),
    ):
        modified = TEXT.copy()
        modified[key] = modified[key].replace(token, "REMOVED_BY_NEGATIVE_TEST")
        if not problems(modified):
            raise SystemExit(f"Unsafe native personnel removal passed: {key}:{token}")
    print("PHASE8 NATIVE PERSONNEL PASS: canonical SQLCipher, manager RBAC, PersonId, nonce, Core+Release")
