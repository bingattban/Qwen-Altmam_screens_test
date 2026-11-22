#!/usr/bin/env python3
"""Phase 6 first slice: live daily roster and fail-closed native WPF contract.

This static negative-path check runs in BOTH Core and Release alongside real
xUnit, SQLCipher Windows integration, WebView2 E2E and four package gates.
It does not certify native WPF production or the unmigrated daily write route.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "vm": "src/AlTamam.NativePresentation/DailyAttendancePageViewModel.cs",
    "tests": "tests/AlTamam.UnitTests/NativeDailyAttendancePhase6Tests.cs",
    "review_tests": "tests/AlTamam.UnitTests/NativeDailyReviewedStatusPhase6Tests.cs",
    "review": "src/AlTamam.NativePresentation/NativeAttendanceReviewViewModel.cs",
    "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
    "pages": "src/AlTamam.NativePresentation/NativePageViewModels.cs",
    "templates": "src/AlTamam.NativeDesktop/Views/NativeSectionTemplates.xaml",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeDailyAttendanceView.xaml",
    "view": "src/AlTamam.NativeDesktop/Views/NativeDailyAttendanceView.xaml.cs",
    "reader": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "canonical": "src/AlTamam.Application/Bridge/DailyMonthlyReadService.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {key: (ROOT / name).read_text(encoding="utf-8")
        for key, name in FILES.items()}


def check(content):
    issues = []

    def needs(key, text):
        if text not in content[key]:
            issues.append(f"Missing {key} safety invariant: {text}")

    for key in ("templates", "xaml"):
        try:
            ET.fromstring(content[key])
        except ET.ParseError as error:
            issues.append(f"Invalid native XAML {key}: {error}")
    for token in ("INativeDashboardReader", "DateOnly.FromDateTime(DateTime.Now)",
                  "new NativeDashboardFilter(", "100);",
                  "ClearSensitive(", "_activeRead?.Cancel()",
                  "request != _version", "_currentPrincipal?.Invoke() != principal",
                  "_boundPrincipal != principal", "Validate(snapshot, filter)",
                  "snapshot.BusinessDate != filter.BusinessDate",
                  "snapshot.SiteId != filter.SiteId",
                  "snapshot.HasNext !=", "SelectSiteAsync(", "SelectStatusAsync(",
                  "SearchAsync(", "NextPageAsync(", "PreviousPageAsync(",
                  "snapshot.CustomColumns[i].Caption.Trim() is \"الرتبة\" or \"رتبة\"",
                  "CustomCells.Where((_, i) => i != rankIndex)"):
        needs("vm", token)
    for token in ("CanReviewPerson(", "ReferenceEquals(item, row)",
                  "Review.OpenPersonsAsync(", "Review.ForceCancel();",
                  "person.PersonId, person.RowNumber", "_loadedDate = snapshot.BusinessDate"):
        needs("vm", token)
    for token in ("OpenPersonsAsync(", "CommitAsync(",
                  "ReadChoicesAsync(", "PreviewAsync("):
        needs("review", token)
    for token in ("Live_daily_row_reuses_server_review_and_writes_only_after_confirmation",
                  "Forged_or_expired_daily_selection_cannot_open_review",
                  "Selection_from_previous_page_or_filter_is_not_authorized"):
        needs("review_tests", token)
    if content["review_tests"].count("[Fact]") < 3:
        issues.append("Missing daily-review positive and negative path xUnit coverage.")
    for token in ("DailyAttendance = new DailyAttendancePageViewModel()",
                  "DailyAttendance.AttachReader(reader, () => _active)",
                  "_ = DailyAttendance.RefreshAsync()",
                  "DailyAttendance.Clear();"):
        needs("shell", token)
    if "public sealed class DailyAttendancePageViewModel()" in content["pages"]:
        issues.append("The disconnected daily route placeholder remains.")
    needs("templates", "<views:NativeDailyAttendanceView")
    for token in ('Header="الرقم"', 'Header="الرقم (ع)"',
                  'Header="الرتبة"', 'Header="الاسم"',
                  'Header="الموقع"', 'Header="الحالة"',
                  'IsReadOnly="True"', "SearchCommand",
                  "SelectedSiteId", "SelectedStatusId", "DailyGrid",
                  "NativeAttendanceReviewView", "ReviewPersonCommand",
                  'CommandParameter="{Binding SelectedItem, ElementName=DailyGrid}"'):
        needs("xaml", token)
    for token in ("IsDataConnected", "CustomColumns",
                  "DailyGrid.Columns.Add(", "OnSiteChanged(",
                  "OnStatusChanged(", "IsKeyboardFocusWithin"):
        needs("view", token)
    for token in ("ResolveSessionAsync(identity.SessionNonce",
                  "DailyRosterAsync(rosterQuery, ct)",
                  "new DailyMonthlyReadService(database)",
                  "personId", "customColumns"):
        needs("reader", token)
    needs("canonical", "RosterCustomFieldProjection.AttachAsync")
    if content["tests"].count("[Fact]") < 6:
        issues.append("Missing Phase 6 xUnit regression coverage.")
    for token in ("No_live_session_means_no_query_and_no_personnel",
                  "Native_daily_screen_uses_existing_typed_roster_with_actual_rank",
                  "Logout_or_replaced_nonce_revokes_all_daily_personnel"):
        needs("tests", token)
    for name in ("core", "release"):
        needs(name, "python build/test-native-phase6-daily-roster-contract.py")
    for forbidden in ("new AttendanceCommitService(", "RunPendingAsync(",
                      "ExecuteInWriteTransactionAsync(", "new BridgePrincipal("):
        if forbidden in content["vm"] or forbidden in content["view"]:
            issues.append(f"No write or forged authority in daily WPF presentation: {forbidden}")
    return issues


if __name__ == "__main__":
    errors = check(TEXT)
    if errors:
        raise SystemExit("NATIVE PHASE 6 DAILY CONTRACT FAILED:\n" + "\n".join(errors))
    for key, text in (
        ("vm", "request != _version"),
        ("vm", "_boundPrincipal != principal"),
        ("vm", "snapshot.HasNext !="),
        ("shell", "DailyAttendance.Clear();"),
        ("shell", "DailyAttendance.AttachReader(reader, () => _active)"),
        ("xaml", 'Header="الرتبة"'),
        ("xaml", "NativeAttendanceReviewView"),
        ("vm", "ReferenceEquals(item, row)"),
        ("review", "OpenPersonsAsync("),
        ("review_tests", "Forged_or_expired_daily_selection_cannot_open_review"),
        ("reader", "DailyRosterAsync(rosterQuery, ct)"),
        ("core", "python build/test-native-phase6-daily-roster-contract.py"),
        ("release", "python build/test-native-phase6-daily-roster-contract.py"),
    ):
        altered = TEXT.copy()
        altered[key] = altered[key].replace(text, "REMOVED_BY_NEGATIVE_TEST")
        if not check(altered):
            raise SystemExit(f"Unsafe change incorrectly passed: {key}: {text}")
    print("NATIVE PHASE 6 DAILY PASS: SQLCipher, daily filters, rank, nonce, reviewed status, Core/Release")
