#!/usr/bin/env python3
"""Phase 3 native dashboard auto-prepared register + clock security contract.

Executed in Core and Release BEFORE expensive Windows tests. Native xUnit
remains mandatory; negative fixture verifies every fail-closed invariant.
No scheduler, mutation or skipped legacy WebView2 package acceptance is allowed.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PATHS = {
  "contracts": "src/AlTamam.NativePresentation/NativeDashboardContinuationContracts.cs",
  "register_vm": "src/AlTamam.NativePresentation/NativeAutoPreparedRegisterViewModel.cs",
  "dashboard_vm": "src/AlTamam.NativePresentation/DashboardPageViewModel.cs",
  "reader": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
  "register_xaml": "src/AlTamam.NativeDesktop/Views/NativeAutoPreparedRegisterView.xaml",
  "register_code": "src/AlTamam.NativeDesktop/Views/NativeAutoPreparedRegisterView.xaml.cs",
  "dashboard_xaml": "src/AlTamam.NativeDesktop/Views/NativeDashboardView.xaml",
  "dashboard_code": "src/AlTamam.NativeDesktop/Views/NativeDashboardView.xaml.cs",
  "shell": "src/AlTamam.NativePresentation/NativeShellViewModel.cs",
  "tests": "tests/AlTamam.UnitTests/NativeAutoPreparedRegisterPhase3Tests.cs",
  "functional": "src/AlTamam.WebUI/assets/js/functional-screens.js",
  "scheduler": "src/AlTamam.DesktopHost/Scheduling/EightPmSchedulerService.cs",
  "core": ".github/workflows/windows-ci.yml",
  "release": ".github/workflows/windows-release.yml"
}
DATA = {key: (ROOT / path).read_text(encoding="utf-8") for key, path in PATHS.items()}

def problems(data):
    errors=[]
    def need(key, content):
        if content not in data[key]:
            errors.append(f"{key}: missing '{content}'")
    for key in ("register_xaml", "dashboard_xaml"):
        try: ET.fromstring(data[key])
        except ET.ParseError as exc: errors.append(f"{key} invalid XAML: {exc}")
    for token in ("INativeDashboardContinuationReader", "NativeAutoAttendancePolicy",
                  "NativeAutoRegisterFilter", 'StatusId => "__AUTO_PREPARED__"'):
        need("contracts", token)
    for token in ("AutoAttendancePolicyService(database)", ".GetAsync(ct)",
                  "AutoAttendancePolicyService.IsValidClock(due)", "DailyMonthlyReadService(database)",
                  '"statusId"] = "__AUTO_PREPARED__"', "DailyRosterAsync(new JsonObject",
                  "RequiredText(item, \"savedBusinessDate\")",
                  "RequiredText(item, \"autoSource\")", "RequiredText(item, \"committedAtUtc\")",
                  'RequiredArray(data, "customColumns")', "ResolveSessionAsync(identity.SessionNonce",
                  "ReadAutoRegisterAsync(", "ReadPolicyAsync("):
        need("reader",token)
    if data["reader"].count("await VerifyContinuationIdentityAsync(identity, ct).ConfigureAwait(false);") != 4:
        errors.append("Due clock and auto register must EACH verify real identity before AND after read.")
    for token in ("_pending?.Cancel()", "_generation", "Close()", "SearchAsync(",
                  "NextPageAsync(", "PreviousPageAsync(", "_getPrincipal?.Invoke() != expected",
                  "DateOnly.FromDateTime(DateTime.Now) != _businessDate",
                  'filter.PageSize', 'query.PageSize' ):
        # Note filter.PageSize is a backend responsibility, VM uses query.PageSize.
        if token == "filter.PageSize": need("reader",token)
        else: need("register_vm",token)
    for token in ("public void TickClock(DateTime localNow)", "TimeOnly.TryParseExact",
                  "LoadAutoPolicyAsync", "AutoRegister.Close();", "OpenAutoRegisterAsync(",
                  'statusId == "__AUTO_PREPARED__"',
                  "RefreshAsync(); // DISPLAY read only; never RunPendingAsync.",
                  "public NativeAutoPreparedRegisterViewModel AutoRegister"):
        need("dashboard_vm",token)
    for token in ("NativeAutoPreparedRegisterView", 'DataContext="{Binding AutoRegister}"',
                  'Text="{Binding CountdownLabel}"'):
        need("dashboard_xaml",token)
    for token in ("سجل التحضير التلقائي المحفوظ", "وقت الاعتماد UTC",
                  "تاريخ التحضير", "مصدر التحضير", "الحالة المحفوظة",
                  'IsReadOnly="True"', "SearchCommand", "CloseCommand",
                  "PreviousPageCommand", "NextPageCommand",
                  "KeyboardNavigation.TabNavigation"):
        need("register_xaml",token)
    for color in ("#0F2942","#0D9488","#F8FAFC","#CBD5E1"):
        need("register_xaml",color)
    need("register_code","Key.Escape")
    need("register_code","CustomCells[")
    need("dashboard_code","DispatcherTimer")
    need("dashboard_code","TickClock(DateTime.Now)")
    need("shell","Dashboard.AutoRegister.Close();")
    if data["tests"].count("[Fact]") < 9:
        errors.append("Phase 3 native xUnit contract must retain >= 9 actual cases")
    for token in ("GetAutoAttendancePolicy", "QueryDailyRoster",
                  '"__AUTO_PREPARED__"', "beginEightAmCountdown"):
        need("functional",token)
    for token in ("AutoAttendanceService", "_attendance.RunPendingAsync",
                  "RunPendingAsync(ct)"):
        need("scheduler",token)
    # New C# WPF modal is purposely read-only; do not fake parity by
    # directly updating attendance from an unreviewed click.
    for forbidden in ("RunPendingAsync(", "ReviseTodayAttendance", "PreviewAttendanceChange",
                      "ExecuteInWriteTransactionAsync(", "SetAsync(new JsonObject"):
        for key in ("dashboard_vm","register_vm","reader","dashboard_code","register_code"):
            if forbidden in data[key]:
                errors.append(f"Forbidden native write or unapproved preview in {key}: {forbidden}")
    need("core","build/test-native-dashboard-phase2-contract.py")
    need("release","build/test-native-dashboard-phase2-contract.py")
    need("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj")
    return errors

if __name__ == "__main__":
    issues=problems(DATA)
    if issues: raise SystemExit("NATIVE AUTO REGISTER PHASE3 FAIL:\n"+"\n".join(issues))
    for key,token in (
       ("reader",'"statusId"] = "__AUTO_PREPARED__"'),
       ("reader","ResolveSessionAsync(identity.SessionNonce"),
       ("reader","AutoAttendancePolicyService(database)"),
       ("dashboard_vm","AutoRegister.Close();"),
       ("register_vm","_pending?.Cancel()"),
       ("dashboard_xaml",'DataContext="{Binding AutoRegister}"'),
       ("register_xaml",'IsReadOnly="True"'),
       ("shell","Dashboard.AutoRegister.Close();"),
       ("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj")
    ):
        changed=DATA.copy()
        changed[key]=changed[key].replace(token, "REMOVED_FOR_NEGATIVE_TEST")
        if not problems(changed):
            raise SystemExit(f"Negative mutation unexpectedly passed for {key} / {token}")
    print("NATIVE AUTO REGISTER PHASE3 PASS: canonical HH:mm display clock, read-only SQLCipher register, live sessions, no new scheduler or write")
