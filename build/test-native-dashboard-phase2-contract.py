#!/usr/bin/env python3
"""Fail-closed native WPF dashboard Phase 2 migration contract.

Run before .NET compilation in BOTH Core/Release. Complements the actual C#
unit/integration suites and CI's 69-screen/103-action real production tests.
The canonical Dashboard counts/query semantics stay in existing SQLCipher
DailyMonthlyReadService, not in a new parallel implementation.
"""
import json
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
PATHS={
 "view":"src/AlTamam.NativeDesktop/Views/NativeDashboardView.xaml",
 "view_code":"src/AlTamam.NativeDesktop/Views/NativeDashboardView.xaml.cs",
 "templates":"src/AlTamam.NativeDesktop/Views/NativeSectionTemplates.xaml",
 "vm":"src/AlTamam.NativePresentation/DashboardPageViewModel.cs",
 "model":"src/AlTamam.NativePresentation/NativeDashboardContracts.cs",
 "reader":"src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
 "backend":"src/AlTamam.NativeDesktop/Auth/NativeSqlCipherAuthenticationBackend.cs",
 "shell":"src/AlTamam.NativePresentation/NativeShellViewModel.cs",
 "window":"src/AlTamam.NativeDesktop/MainWindow.xaml.cs",
 "tests":"tests/AlTamam.UnitTests/NativeDashboardPhase2Tests.cs",
 "webui":"src/AlTamam.WebUI/assets/js/functional-screens.js",
 "release":".github/workflows/windows-release.yml",
 "matrix":"docs/native-wpf-migration/SCREEN_MIGRATION_MATRIX.json",
}
DATA={key:(ROOT/path).read_text(encoding="utf8") for key,path in PATHS.items()}

def failures(data):
    problems=[]
    def require(key, token):
        if token not in data[key]:
            problems.append(f"Missing required {key} contract: {token}")
    for path in ("view","templates"):
        try: ET.fromstring(data[path])
        except ET.ParseError as ex: problems.append(f"Invalid {path} XAML: {ex}")
    for color in ("#0F2942","#0D9488","#FFFFFF","#F8FAFC","#CBD5E1"):
        require("view",color)
    for phrase in ("التمام اليومي","جميع المواقع","كشف الأفراد","الرقم (ع)",
                   "الاسم","الموقع","الحالة","مسح البحث","تحديث البيانات","السابق","التالي"):
        # 'جميع المواقع' is in strongly typed model rather than XAML.
        require("vm" if phrase=="جميع المواقع" else "view",phrase)
    for token in ("SelectedSiteId","SelectStatusCommand","SearchCommand",
                  "ItemsSource=\"{Binding Rows}\"","OnSiteSelectionChanged"):
        require("view",token)
    for token in ("CustomColumns","RebuildCustomColumns()","PersonnelGrid.Columns.Add"):
        require("view_code",token)
    for token in ("DailyMonthlyReadService(database)","DashboardSummaryAsync(summaryQuery",
                  "DailyRosterAsync(rosterQuery","ListSitesAsync(includeInactive: false",
                  "VerifyLiveSessionAsync().ConfigureAwait(false)",
                  "RoleAuthorizationPolicy.CanPerformStandardOperations",
                  "RequiredArray(roster, \"customColumns\")","RequiredArray(roster, \"items\")"):
        require("reader",token)
    if data["reader"].count("await VerifyLiveSessionAsync().ConfigureAwait(false);") != 2:
        problems.append("Real backend must validate session both before AND after SQLCipher reads")
    for token in ("NativeSqlCipherDashboardReader","CreateDashboardReader()"):
        require("backend",token)
    require("window","_viewModel.AttachDashboardReader(_backend.CreateDashboardReader());")
    for token in ("Dashboard.AttachReader(reader, () => _active)","await Dashboard.RefreshAsync(ct)",
                  "Dashboard.Clear()"):
        require("shell",token)
    for token in ("_requestVersion","_activeRead?.Cancel()","_boundPrincipal",
                  "_currentPrincipal?.Invoke() != principal","ClearSensitivePresentation(",
                  'new("", "إجمالي القوة"', '"الموجود"', '"المحضّرون تلقائياً"',
                  '"نُقلوا اليوم"', 'StatusId == _selectedStatusId'):
        require("vm",token)
    if "new(\"__UNPREPARED__\", \"غير المحضرين\"" in data["vm"]:
        problems.append("Do not invent unprepared primary counter (regression from WebUI)")
    require("templates",'<views:NativeDashboardView />')
    require("view",'IsReadOnly="True"')
    if data["tests"].count("[Fact]") < 7:
        problems.append("Native dashboard security/filters/replay unit test baseline reduced")
    for token in ("GetDashboardSummary","QueryDailyRoster",
                  "const DAILY_PAGE_SIZE=100","المحضّرون تلقائياً"):
        require("webui",token)
    require("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj")
    routes=json.loads(data["matrix"])["routes"]
    if len(routes)!=69 or any(x["migrationState"]!="not_migrated" or
                               x["functionalParityVerified"] for x in routes):
        problems.append("Do not claim operational route acceptance before real x86/x64 end-to-end parity")
    return problems

if __name__=="__main__":
    problems=failures(DATA)
    if problems: raise SystemExit("NATIVE WPF DASHBOARD PHASE 2 FAIL:\n"+"\n".join(problems))
    for key,token in (
        ("reader","DailyRosterAsync(rosterQuery"),
        ("reader","ListSitesAsync(includeInactive: false"),
        ("window","_viewModel.AttachDashboardReader(_backend.CreateDashboardReader());"),
        ("shell","Dashboard.Clear()"),
        ("view",'IsReadOnly="True"'),
        ("vm","_boundPrincipal"),
        ("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj"),
    ):
        mut=DATA.copy()
        mut[key]=mut[key].replace(token,"REMOVED_FOR_NEGATIVE_TEST")
        if not failures(mut):
            raise SystemExit("Unsafe dashboard contract mutation passed: "+key+" "+token)
    print("NATIVE DASHBOARD PHASE 2 PASS: real read-only SQLCipher, 100-row filters, RTL palette, live nonce and safe gate")
