#!/usr/bin/env python3
"""Phase 3 native dashboard auto-prepared register + clock security contract.

Executed in Core and Release BEFORE expensive Windows tests. Native xUnit
remains mandatory; negative fixture verifies every fail-closed invariant.
No scheduler or mutation is allowed in the dashboard/register read paths; the
separately approved settings mutation is narrowly scoped. Legacy acceptance remains mandatory.
"""
from pathlib import Path
from native_theme_contract import check_visual_resources
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PATHS = {
    "theme_light":"src/AlTamam.NativeDesktop/Themes/Light.xaml",
    "theme_dark":"src/AlTamam.NativeDesktop/Themes/Dark.xaml",
    "theme_app":"src/AlTamam.NativeDesktop/App.xaml",
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
    errors.extend(check_visual_resources(data["register_xaml"],data["theme_light"],data["theme_dark"],data["theme_app"],(
        ("Foreground","NativeText","#0F2942"),
        ("Background","NativeAccentFill","#0F766E"),
        ("Background","NativeSubtle","#F8FAFC"),
        ("BorderBrush","NativeBorder","#CBD5E1"),
    )))
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
    # The shared adapter also owns the approved Phase 12 default-status writer.
    # Exempt only its exact canonical call, inside that one method and only
    # when the authorization, session, receipt and read-back guards are present.
    reader_scan=data["reader"]
    writer_start="    public async Task<NativeAttendanceSettingsState> SaveDefaultStatusAsync("
    writer_end="    public async Task<NativeAttendanceSettingsState> SaveAttendanceClockAsync("
    approved_call = """        var response=await service.SetAsync(new JsonObject
        {
            ["statusId"]=intent.Choice.Id,["expectedDefaultStatusId"]=intent.Before.DefaultStatusId,
            ["expectedStatusName"]=intent.Choice.Name,["operationId"]=intent.OperationId
        },principal,lifetime.Token).ConfigureAwait(false);"""
    writer_guards = ["using var nativeOperation = operations.Enter();","if(intent is null || !intent.IsValid(actor))","ResolveBackupPrincipalAsync(actor,ct)","RoleAuthorizationPolicy.CanAccessUsersManagement(principal.Role)","CancellationTokenSource.CreateLinkedTokenSource(ct,principal.SessionRevocation)","var service=new AutoAttendancePolicyService(database);","RequiredObject(response,\"SetAutoAttendanceDefaultStatus\")","response.AuditEventId","if(!intent.Matches(current))"]
    before, marker, remainder=reader_scan.partition(writer_start)
    writer, boundary, after=remainder.partition(writer_end)
    if (not marker or not boundary or reader_scan.count(writer_start)!=1 or
            reader_scan.count(writer_end)!=1 or writer.count(approved_call)!=1 or
            not all(token in writer for token in writer_guards)):
        errors.append("Approved default-status writer scope or security evidence is invalid.")
    else:
        # Leave all other source, even the rest of the writer, subject to the ban.
        reader_scan=before+marker+writer.replace(approved_call,"APPROVED_DEFAULT_STATUS_CALL",1)+boundary+after
    # The dashboard and auto-register remain read-only; no unreviewed write,
    # direct attendance mutation or scheduler invocation is permitted.
    for forbidden in ("RunPendingAsync(", "ReviseTodayAttendance", "PreviewAttendanceChange",
                      "ExecuteInWriteTransactionAsync(", "SetAsync(new JsonObject"):
        for key in ("dashboard_vm","register_vm","reader","dashboard_code","register_code"):
            if forbidden in (reader_scan if key=="reader" else data[key]):
                errors.append(f"Forbidden native write or unapproved preview in {key}: {forbidden}")
    need("core","build/test-native-dashboard-phase2-contract.py")
    need("release","build/test-native-dashboard-phase2-contract.py")
    need("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj")
    return errors

if __name__ == "__main__":
    issues=problems(DATA)
    if issues: raise SystemExit("NATIVE AUTO REGISTER PHASE3 FAIL:\n"+"\n".join(issues))
    for key,token in (
        ("register_xaml","{DynamicResource NativeAccentFill}"),
        ("theme_light",'x:Key="NativeAccentFill" Color="#0F766E"'),
        ("theme_dark",'x:Key="NativeAccentFill"'),
        ("theme_app",'Source="Themes/Light.xaml"'),
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
    # Adding an identical call anywhere else must still fail. Removing security
    # evidence or changing the exact approved payload must also fail closed.
    for old,new in (
        ("ReadPolicyAsync(", "SetAsync(new JsonObject\nReadPolicyAsync("),
        ("ReadAutoRegisterAsync(", "SetAsync(new JsonObject\nReadAutoRegisterAsync("),
        ('["expectedStatusName"]=intent.Choice.Name', '["expectedStatusName"]="unreviewed"'),
        ("if(intent is null || !intent.IsValid(actor))", "if(false)"),
        ("var service=new AutoAttendancePolicyService(database);", "var service=otherService;")
    ):
        changed=DATA.copy()
        changed["reader"]=changed["reader"].replace(old,new)
        if changed["reader"]==DATA["reader"] or not problems(changed):
            raise SystemExit(f"Default-status scope negative mutation unexpectedly passed: {old}")
    print("NATIVE AUTO REGISTER PHASE3 PASS: canonical HH:mm display clock, read-only SQLCipher register, live sessions, scoped reviewed settings exception")
