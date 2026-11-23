#!/usr/bin/env python3
"""Phase 9 slice 2: immutable PersonId site and correction evidence guard.

Run separately in both Windows CI paths. Tests enforce precise native
bindings, canonical data ownership, independent 100-row paging and no writes.
"""
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
PATHS = {
    "service": "src/AlTamam.Application/Bridge/PersonnelReportService.cs",
    "contract": "src/AlTamam.NativePresentation/NativePersonnelReportContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativePersonnelReportViewModel.cs",
    "adapter": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "view": "src/AlTamam.NativeDesktop/Views/NativePersonnelReportView.xaml",
    "tests": "tests/AlTamam.UnitTests/NativeHistoricSitesAndRevisionsPhase9Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
CONTENTS = {key: (ROOT / path).read_text(encoding="utf-8") for key, path in PATHS.items()}
GUARD = "python build/test-native-phase9-site-revision-report-contract.py"
REQUIRED = {
    "service": (
        "FROM PersonAssignments a WHERE a.PersonId=$id",
        "FROM AttendanceRevisions rev WHERE rev.PersonId=$id",
        '["siteHistory"]=sites', '["siteTotal"]=totalSites',
        '["revisions"]=revisionHistory', '["revisionTotal"]=totalRevisions',
        '["sitePage"]=sitePage', '["revisionPage"]=revisionPage',
        '["siteHasNext"]=', '["revisionHasNext"]='),
    "contract": ("NativePersonnelReportSite(", "NativePersonnelReportRevision(",
                 "int SitePage = 1", "int RevisionPage = 1",
                 "IReadOnlyList<NativePersonnelReportSite> Sites",
                 "IReadOnlyList<NativePersonnelReportRevision> Revisions"),
    "vm": ("NextSitesAsync(", "PreviousSitesAsync(", "NextRevisionsAsync(",
           "PreviousRevisionsAsync(", "SitesPageLabel", "RevisionsPageLabel",
           "report.SitePage != requested.SitePage",
           "report.RevisionPage != requested.RevisionPage",
           "report.SiteHasNext !=", "report.RevisionHasNext !=",
           "ValidPageCount(report.Sites.Count",
           "ValidPageCount(report.Revisions.Count",
           "_pending?.Cancel();", "if (!CanRead) { Close(); return; }"),
    "adapter": (
        "new PersonnelReportService(database)",
        '["sitePage"] = pages.SitePage',
        '["revisionPage"] = pages.RevisionPage',
        'RequiredNumber(data, "sitePage") != pages.SitePage',
        'RequiredNumber(data, "revisionPage") != pages.RevisionPage',
        'RequiredArray(data, "siteHistory")',
        'RequiredArray(data, "revisions")',
        'RequiredNumber(data, "siteTotal")',
        'RequiredNumber(data, "revisionTotal")',
        "await VerifyPersonnelManagerAsync(actor, ct)"),
    "view": ('FlowDirection="RightToLeft"', "Binding Report.Sites",
             "Binding Report.Revisions", "Binding NextSitesCommand",
             "Binding PreviousSitesCommand", "Binding NextRevisionsCommand",
             "Binding PreviousRevisionsCommand", 'IsReadOnly="True"'),
    "tests": ("Sites_have_independent_real_100_row_pagination",
              "Corrections_have_independent_real_100_row_pagination",
              "Historical_site_name_and_correction_actor_are_never_derived_from_current_roster",
              "Forged_history_pages_or_counts_must_be_hidden",
              "New_history_panels_reject_stale_selected_row_or_non_manager",
              "Closing_during_history_request_discards_the_late_response"),
    "core": (GUARD,),
    "release": (GUARD,),
}
FORBIDDEN = ("new SqliteConnection(", "INSERT INTO", "DELETE FROM",
             "UPDATE Persons SET", "File.WriteAllBytes(", "SaveFileDialog(")


def problems(parts):
    result = []
    for key, needles in REQUIRED.items():
        for needle in needles:
            if needle not in parts[key]:
                result.append(f"{key}: missing required report evidence: {needle}")
    for key in ("contract", "vm", "view"):
        for bad in FORBIDDEN:
            if bad in parts[key]:
                result.append(f"{key}: forbidden writer: {bad}")
    try:
        ElementTree.fromstring(parts["view"])
    except ElementTree.ParseError as exc:
        result.append(f"view: malformed WPF XML: {exc}")
    if parts["tests"].count("[Fact]") < 5 or "[Theory]" not in parts["tests"]:
        result.append("tests: missing original xUnit test-first cases")
    for key in ("core", "release"):
        lines = parts[key].splitlines()
        positions = [i for i, ln in enumerate(lines) if ln.strip() == GUARD]
        if len(positions) != 1 or positions[0] + 1 >= len(lines) or (
            "if ($LASTEXITCODE -ne 0) { throw " not in lines[positions[0] + 1]
        ):
            result.append(f"{key}: missing fail-closed guard wiring")
    return result


if __name__ == "__main__":
    issues = problems(CONTENTS)
    if issues:
        raise SystemExit("FAIL: native history report guard\n" + "\n".join(issues))
    for key, needle in (
        ("service", "FROM AttendanceRevisions rev WHERE rev.PersonId=$id"),
        ("service", "FROM PersonAssignments a WHERE a.PersonId=$id"),
        ("contract", "NativePersonnelReportRevision("),
        ("vm", "report.SitePage != requested.SitePage"),
        ("vm", "ValidPageCount(report.Revisions.Count"),
        ("adapter", '["sitePage"] = pages.SitePage'),
        ("adapter", "await VerifyPersonnelManagerAsync(actor, ct)"),
        ("view", "Binding Report.Sites"),
        ("view", "Binding Report.Revisions"),
        ("tests", "Closing_during_history_request_discards_the_late_response"),
        ("core", GUARD), ("release", GUARD)
    ):
        forged = dict(CONTENTS)
        forged[key] = forged[key].replace(needle, "FORGED_BY_NEGATIVE_TEST")
        if not problems(forged):
            raise SystemExit(f"FAIL: ineffective negative check {key} {needle}")
    print("PASS: native history site+revision pages retain source SQLCipher PersonId and RBAC")
