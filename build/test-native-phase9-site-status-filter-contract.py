#!/usr/bin/env python3
"""Phase 9 slice 6: immutable PersonId site/status filter fail-closed contract.

The real C# xUnit suite covers sessions, navigation, stale responses and source
echoes. This additional script checks wiring, canonical SQL and workflow
gates, and deliberately breaks safety invariants to ensure failures are fatal.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
SOURCES = {
    "canonical": "src/AlTamam.Application/Bridge/PersonnelReportService.cs",
    "contracts": "src/AlTamam.NativePresentation/NativePersonnelReportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelReportView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeReportSiteStatusPhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
DATA = {k: (ROOT / p).read_text(encoding="utf-8") for k,p in SOURCES.items()}
COMMAND = "python build/test-native-phase9-site-status-filter-contract.py"
REQUIRED = {
    "canonical": (
        '["siteId"]=filters.SiteId', '["statusId"]=filters.StatusId',
        '["filterSites"]=filterSites,["filterStatuses"]=filterStatuses',
        "FROM PersonAssignments a WHERE a.PersonId=$id",
        "FROM PersonnelMovements WHERE PersonId=$id",
        "SELECT SiteId, MAX(SiteName) FROM (",
        "SELECT StatusId,MAX(StatusName) FROM (",
        "FROM AttendanceRevisions WHERE PersonId=$id",
        "GROUP BY SiteId ORDER BY SiteId LIMIT 501",
        "GROUP BY StatusId ORDER BY StatusId LIMIT 501",
        "if(filterSites.Count==500)", "if(filterStatuses.Count==500)",
        "choices.Parameters.AddWithValue(\"$id\",personId);",
        "($site='' OR SiteIdAtEffectiveDate=$site)",
        "($status='' OR StatusId=$status)",
        "($site='' OR SourceSiteId=$site OR DestinationSiteId=$site)",
        "($status='' OR rev.BeforeStatusId=$status OR rev.AfterStatusId=$status)",
    ),
    "contracts": (
        'int MovementType = 0, string SiteId = "", string StatusId = ""',
        "NativeReportFilterOption(string Id, string Title)",
        "public string SiteId { get; init; }",
        "public string StatusId { get; init; }",
        "AvailableSites", "AvailableStatuses",
    ),
    "vm": (
        "SiteFilterOptions", "StatusFilterOptions",
        "SiteIdDraft", "StatusIdDraft",
        "ApplySiteStatusFiltersAsync(", "ClearSiteStatusFiltersAsync(",
        "_siteFilterOptions.Any(x => x.Id == siteId)",
        "_statusFilterOptions.Any(x => x.Id == statusId)",
        "SiteId = siteId, StatusId = statusId",
        "AttendancePage = 1, MovementPage = 1, RevisionPage = 1",
        "report.SiteId != requested.SiteId",
        "report.StatusId != requested.StatusId",
        "report.AvailableSites.Count > 500",
        "report.AvailableStatuses.Count > 500",
        "Distinct(StringComparer.Ordinal)",
        "AppliedSiteStatusLabel",
        "version != _generation || linked.IsCancellationRequested",
        "if (!CanRead) { Close(); return; }",
    ),
    "adapter": (
        "new PersonnelReportService(database)",
        '["siteId"] = pages.SiteId',
        '["statusId"] = pages.StatusId',
        'RequiredText(data, "siteId") != pages.SiteId',
        'RequiredText(data, "statusId") != pages.StatusId',
        'RequiredArray(data, "filterSites")',
        'RequiredArray(data, "filterStatuses")',
        "Requested historical filter is not owned by this PersonId",
        "Duplicate canonical ",
        "AvailableSites = filterSites",
        "AvailableStatuses = filterStatuses",
        "await VerifyPersonnelManagerAsync(actor, ct)",
    ),
    "view": (
        'FlowDirection="RightToLeft"',
        "Binding SiteFilterOptions", "Binding StatusFilterOptions",
        "Binding SiteIdDraft", "Binding StatusIdDraft",
        "Binding ApplySiteStatusFiltersCommand",
        "Binding ClearSiteStatusFiltersCommand",
        "Binding AppliedSiteStatusLabel",
        "Binding Report.Attendance", "Binding Report.Movements",
        "المعرفات ثابتة", "الموقع يقيّد التحضير والحركات والتصحيحات",
    ),
    "tests": (
        "Site_and_status_selections_are_person_scoped_and_independent_of_pages",
        "Site_and_status_parameters_are_sent_exactly_to_canonical_service",
        "Unknown_or_dangerous_catalog_keys_fail_before_any_read",
        "Empty_selection_is_a_genuine_original_unfiltered_read",
        "Other_filters_are_preserved_and_attendance_movement_revision_pages_reset",
        "Date_and_movement_changes_do_not_silently_clear_site_or_status",
        "Forged_filter_echo_is_rejected_before_showing_any_rows",
        "Duplicate_source_catalog_keys_are_refused_as_corrupted_evidence",
        "Old_response_cannot_replace_new_site_or_status_query",
        "Revoked_role_or_stale_selection_never_reads_site_status_history",
    ),
    "core": (COMMAND,),
    "release": (COMMAND,),
}
FORBIDDEN = ("new SqliteConnection(", "INSERT INTO", "DELETE FROM",
             "UPDATE Persons SET", "File.WriteAllBytes(",
             "SaveFileDialog(", "new PersonnelReportService(")

def check(data):
    problems = []
    for key, tokens in REQUIRED.items():
        for token in tokens:
            if token not in data[key]:
                problems.append(f"{key}: missing invariant: {token}")
    for key in ("contracts", "vm", "view"):
        for bad in FORBIDDEN:
            if bad in data[key]:
                problems.append(f"{key}: forbidden duplicate source/writer: {bad}")
    try:
        ElementTree.fromstring(data["view"])
    except ElementTree.ParseError as err:
        problems.append(f"view: invalid WPF XML: {err}")
    if data["tests"].count("[Fact]") < 7 or data["tests"].count("[Theory]") < 3:
        problems.append("tests: insufficient test-first adversarial coverage")
    for key in ("core", "release"):
        lines = data[key].splitlines()
        indexes = [i for i, line in enumerate(lines) if line.strip() == COMMAND]
        if len(indexes) != 1 or indexes[0]+1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[indexes[0]+1]
        ):
            problems.append(f"{key}: fail-closed workflow gate missing or not fatal")
    return problems

if __name__ == "__main__":
    errors = check(DATA)
    if errors:
        raise SystemExit("NATIVE PERSON SITE/STATUS FILTER FAIL:\n" + "\n".join(errors))
    for key, token in (
        ("canonical", '["filterSites"]=filterSites,["filterStatuses"]=filterStatuses'),
        ("canonical", "GROUP BY SiteId ORDER BY SiteId LIMIT 501"),
        ("canonical", "FROM AttendanceRevisions WHERE PersonId=$id"),
        ("contracts", 'int MovementType = 0, string SiteId = "", string StatusId = ""'),
        ("vm", "_siteFilterOptions.Any(x => x.Id == siteId)"),
        ("vm", "report.StatusId != requested.StatusId"),
        ("adapter", '["statusId"] = pages.StatusId'),
        ("adapter", 'RequiredArray(data, "filterSites")'),
        ("view", "Binding SiteIdDraft"),
        ("tests", "Old_response_cannot_replace_new_site_or_status_query"),
        ("core", COMMAND), ("release", COMMAND),
    ):
        forged = dict(DATA)
        forged[key] = forged[key].replace(token, "REMOVED_SECURITY_INVARIANT")
        if not check(forged):
            raise SystemExit(f"Negative mutation unexpectedly passed: {key}/{token}")
    print("PASS: canonical PersonId full-lifetime sites/statuses, source echoes, paging, Core+Release")
