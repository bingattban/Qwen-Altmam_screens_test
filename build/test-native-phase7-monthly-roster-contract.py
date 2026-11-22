#!/usr/bin/env python3
"""Phase 7 monthly SQLCipher historical WPF — fail-closed static regression.

Both Windows workflows run this before C# compilation/testing and four-package
Release. This is additive to real xUnit, SQLCipher and E2E tests, NOT a substitute.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "contracts": "src/AlTamam.NativePresentation/NativeMonthlyAttendanceContracts.cs",
    "vm": "src/AlTamam.NativePresentation/MonthlyAttendancePageViewModel.cs",
    "tests": "tests/AlTamam.UnitTests/NativeMonthlyAttendancePhase7Tests.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/DailyMonthlyReadService.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "pages": "src/AlTamam.NativePresentation/NativePageViewModels.cs",
    "templates": "src/AlTamam.NativeDesktop/Views/NativeSectionTemplates.xaml",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeMonthlyAttendanceView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeMonthlyAttendanceView.xaml.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {k: (ROOT / p).read_text(encoding="utf-8") for k, p in FILES.items()}


def problems(content):
    errors = []

    def require(k, token):
        if token not in content[k]:
            errors.append(f"{k}: required invariant missing: {token}")

    for key in ("templates", "xaml"):
        try:
            ET.fromstring(content[key])
        except ET.ParseError as e:
            errors.append(f"Malformed native WPF {key}: {e}")

    for token in ("interface INativeMonthlyAttendanceReader",
                  "ReadCatalogueAsync(", "ReadMonthAsync(",
                  "NativeMonthlyFilter(", "NativeMonthlyCell(",
                  "NativeMonthlySnapshot("):
        require("contracts", token)
    for token in ("VerifyContinuationIdentityAsync(actor, ct)",
                  "ArchivePeriodsAsync(ct)", "MonthlyGridAsync(new JsonObject",
                  '["archiveMode"] = historical', '["pageSize"] = 100',
                  "DateOnly.TryParseExact(saved.Key", "savedDays",
                  "movementNote", "PersonId", "RequiredArray(data, \"customColumns\")",
                  "new NativeMonthlySnapshot("):
        require("backend", token)
    for token in ("ArchivePeriodsAsync(", "MonthlyGridAsync(",
                  "PersonIdentityHistory", "SiteNameHistory",
                  "MonthlyAttendanceCellPolicy.TemporalMembershipSql",
                  "movementNote", "RosterCustomFieldProjection.AttachAsync"):
        require("canonical", token)
    for token in ("ValidPeriod(", "_boundActor != actor",
                  "_liveActor?.Invoke() != actor", "request != _generation",
                  "_pending?.Cancel()", "HideRows(", "Validate(result, filter)",
                  "result.Rows.Select", "row.DayCells.Select",
                  "SelectPeriodAsync(", "SelectSiteAsync(", "SearchAsync(",
                  "NextPageAsync(", "PreviousPageAsync(", "CurrentMonth()"):
        require("vm", token)
    for token in ("MonthlyAttendance = new MonthlyAttendancePageViewModel()",
                  "MonthlyAttendance.AttachReader(archiveReader, () => _active)",
                  "MonthlyAttendance.RefreshAsync()", "MonthlyAttendance.Clear();"):
        require("shell", token)
    if "public sealed class MonthlyAttendancePageViewModel()" in content["pages"]:
        errors.append("Disconnected monthly placeholder must be removed.")
    require("templates", "<views:NativeMonthlyAttendanceView")
    for token in ('Header="الرقم"', 'Header="الرقم (ع)"',
                  'Header="الرتبة"', 'Header="الاسم"',
                  'Header="الموقع"', 'IsReadOnly="True"',
                  "SelectedPeriod", "SelectedSiteId", "MonthlyGrid",
                  "SearchCommand"):
        require("xaml", token)
    for token in ("RenderColumns()", "MonthlyGrid.Columns.Add(",
                  "Days[{i}].Symbol", "Days[{i}].Description",
                  "CustomCells[{i}]", "OnPeriodChanged(",
                  "OnSiteChanged("):
        require("view", token)
    for token in ("No_live_session_does_not_read_months_or_people",
                  "Native_monthly_page_uses_real_historical_cells",
                  "Site_search_archive_and_pagination",
                  "Future_or_unlisted_month",
                  "Logout_and_replaced_nonce",
                  "Malformed_service_response"):
        require("tests", token)
    if content["tests"].count("[Fact]") < 6:
        errors.append("Missing monthly test-first positive/negative regressions.")
    for k in ("core", "release"):
        require(k, "python build/test-native-phase7-monthly-roster-contract.py")
    for token in ("RunPendingAsync(", "ExecuteInWriteTransactionAsync(",
                  "new AttendanceCommitService(", "new BridgePrincipal(",
                  "new SqliteConnection("):
        if token in content["vm"] or token in content["view"]:
            errors.append(f"Monthly presentation may not bypass canonical SQLCipher services: {token}")
    return errors


if __name__ == "__main__":
    errors = problems(TEXT)
    if errors:
        raise SystemExit("PHASE 7 MONTHLY NATIVE CONTRACT FAILED:\n" + "\n".join(errors))
    for key, token in (
        ("vm", "_boundActor != actor"),
        ("vm", "request != _generation"),
        ("vm", "row.DayCells.Select"),
        ("backend", "VerifyContinuationIdentityAsync(actor, ct)"),
        ("backend", "MonthlyGridAsync(new JsonObject"),
        ("backend", "DateOnly.TryParseExact(saved.Key"),
        ("xaml", 'IsReadOnly="True"'),
        ("view", "Days[{i}].Description"),
        ("shell", "MonthlyAttendance.Clear();"),
        ("core", "python build/test-native-phase7-monthly-roster-contract.py"),
        ("release", "python build/test-native-phase7-monthly-roster-contract.py"),
    ):
        changed = TEXT.copy()
        changed[key] = changed[key].replace(token, "REMOVED_BY_NEGATIVE_TEST")
        if not problems(changed):
            raise SystemExit(f"Unsafe missing monthly invariant passed: {key}: {token}")
    print("PHASE 7 NATIVE MONTHLY PASS: archived SQLCipher, snapshot days, session, Core+Release")
