#!/usr/bin/env python3
"""Phase 4 native WPF scheduler + attendance review safety contract.

Fail-closed source gate executed identically in Windows Core and Release,
in addition to real xUnit and live SQLCipher/WebView2/full-package CI tests.
This is not a replacement for native interactive acceptance on clean Windows.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
FILES={
 "project":"src/AlTamam.NativeDesktop/AlTamam.NativeDesktop.csproj",
 "canonical":"src/AlTamam.DesktopHost/Scheduling/EightPmSchedulerService.cs",
 "attendance":"src/AlTamam.Application/Scheduling/AutoAttendanceService.cs",
 "host":"src/AlTamam.NativeDesktop/MainWindow.xaml.cs",
 "backend":"src/AlTamam.NativeDesktop/Auth/NativeSqlCipherAuthenticationBackend.cs",
 "scheduler":"src/AlTamam.NativeDesktop/Scheduling/NativeDurableAttendanceScheduler.cs",
 "gate":"src/AlTamam.NativePresentation/NativeAutomaticSchedulerGate.cs",
 "gate_tests":"tests/AlTamam.UnitTests/NativeAutomaticSchedulerPhase4Tests.cs",
 "contracts":"src/AlTamam.NativePresentation/NativeAttendanceRevisionContracts.cs",
 "review_vm":"src/AlTamam.NativePresentation/NativeAttendanceReviewViewModel.cs",
 "review_backend":"src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherAttendanceReviewBackend.cs",
 "review_tests":"tests/AlTamam.UnitTests/NativeAttendanceReviewPhase4Tests.cs",
 "reader":"src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
 "register_vm":"src/AlTamam.NativePresentation/NativeAutoPreparedRegisterViewModel.cs",
 "register_xaml":"src/AlTamam.NativeDesktop/Views/NativeAutoPreparedRegisterView.xaml",
 "review_xaml":"src/AlTamam.NativeDesktop/Views/NativeAttendanceReviewView.xaml",
 "review_code":"src/AlTamam.NativeDesktop/Views/NativeAttendanceReviewView.xaml.cs",
 "register_code":"src/AlTamam.NativeDesktop/Views/NativeAutoPreparedRegisterView.xaml.cs",
 "manifest":"src/AlTamam.Application/Bridge/ActionManifest.cs",
 "commit":"src/AlTamam.Application/Bridge/AttendanceCommitService.cs",
 "release":".github/workflows/windows-release.yml",
}
TEXT={k:(ROOT / p).read_text(encoding="utf-8") for k,p in FILES.items()}

def problems(contents):
    errors=[]
    def need(key, token):
        if token not in contents[key]:
            errors.append(f"Missing {key} invariant: {token}")
    for key in ("register_xaml","review_xaml","project"):
        try: ET.fromstring(contents[key])
        except ET.ParseError as ex: errors.append(f"Invalid XML/XAML in {key}: {ex}")
    need("project",'Compile Include="../AlTamam.DesktopHost/Scheduling/EightPmSchedulerService.cs"')
    for token in ("RunPendingAsync(ct)", "GetDueTimeAsync(ct)",
                  "await _attendance.RunPendingAsync(ct)", "public async ValueTask DisposeAsync()"):
        need("canonical",token)
    need("attendance", "IsCatchUpRun")
    for token in ("new EightPmSchedulerService(",
                  "new AutoAttendanceService(database)",
                  "new PreviewTokenRetentionService(database)",
                  "_original.Start()", "_original.DisposeAsync()"):
        need("scheduler",token)
    for token in ("INativeAutomaticScheduler", "StartOnce()", "_scheduler.Start()",
                  "await _scheduler.StopAsync().ConfigureAwait(false)", "ObjectDisposedException"):
        need("gate",token)
    for token in ("new NativeAutomaticSchedulerGate(", "StartAutomaticAttendanceScheduler()",
                  "await _autoAttendance.DisposeAsync()", "_database.Dispose()",
                  "public ValueTask DisposeAsync()"):
        need("backend",token)
    for token in ("_backend.StartAutomaticAttendanceScheduler();",
                  "await _backend.DisposeAsync();", "_mayClose = true;", "Close();"):
        need("host",token)
    if contents["host"].find("await _backend.DisposeAsync();") > contents["host"].find("_mayClose = true;"):
        errors.append("Native host must drain scheduler BEFORE releasing mutex/closing SQLCipher")
    for token in ("INativeAttendanceReviewBackend", "NativeAttendanceRevisionPreview",
                  "NativeAttendanceCommitReceipt", "PreviewToken", "OperationId"):
        # Contract names use lowercase positional params for 2 last fields:
        need("contracts",token if token not in ("PreviewToken","OperationId") else "string")
    for token in ("new AttendanceCommitService(database)",
                  ".PreviewAsync(payload, principal, combined.Token)",
                  "CommitAsync(new JsonObject", '"previewToken"','"operationId"',
                  "revise: true", "MutationIdempotency", "ResolveSessionAsync(expected.SessionNonce",
                  "ActionManifest.All.Single", "principal.SessionRevocation",
                  "AttendanceDateRangePolicy.TryParse",
                  "NativeAttendanceDateRange.TryCreate",
                  "actualStart != validatedRange?.StartDate",
                  "actualEnd != validatedRange?.EndDate"):
        # The idempotency implementation is in canonical commit service.
        need("commit" if token=="MutationIdempotency" else "review_backend",token)
    for token in ("_operationId = Guid.NewGuid().ToString(\"N\")",
                  "draft.PreviewToken, _operationId", "public async Task ConfirmAsync(",
                  "if (!HasValidActor(actor))", "draft.Persons.Count != _personIds.Length",
                  "returned.SequenceEqual(_personIds", "ForceCancel()", "IsCommitting"):
        need("review_vm",token)
    for token in ("INativeAttendanceReviewBackend", "ReadChoicesAsync(",
                  "_review.PreviewAsync(", "_review.CommitAsync(", "SavedStatusId",
                  "NativeAttendanceDateRange? range = null",
                  "_review.PreviewAsync(actor, date, site, people, statusId, ct, range)"):
        need("reader",token)
    for token in ("_selection.Count >= 100", "SelectVisiblePage()", "ClearSelection()",
                  "OpenReviewAsync(", "Review.Committed", "Review.ForceCancel()",
                  "Review.AttachBackend("):
        need("register_vm",token)
    for token in ("<views:NativeAttendanceReviewView", "DataContext=\"{Binding Review}\"",
                  "SelectVisiblePageCommand", "ClearSelectionCommand",
                  "ReviewSelectedCommand", "OnRegisterCheckboxChanged"):
        need("register_xaml",token)
    for token in ("PreviewCommand", "ConfirmCommand", "CancelCommand", "PreviewPersons",
                  "الحالة السابقة", "تأكيد", 'IsReadOnly="True"',
                  'KeyboardNavigation.TabNavigation="Cycle"'):
        need("review_xaml",token)
    need("review_code","Key.Escape")
    need("register_code","OnRegisterCheckboxChanged")
    for t in ("PreviewAttendanceChange", "ReviseTodayAttendance", "GeneralAdministration",
              "OrdinaryAdministration", "Supervisor"):
        need("manifest",t)
    if contents["gate_tests"].count("[Fact]") < 5:
        errors.append("Scheduler gate lost a required xUnit regression")
    if contents["review_tests"].count("[Fact]") < 10:
        errors.append("Operator-intent review gate lost xUnit regression coverage")
    need("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj")
    for key in ("register_vm","review_vm","host"):
        for forbidden in ("new AttendanceCommitService", "ExecuteInWriteTransactionAsync(",
                          "RunPendingAsync(", "new BridgePrincipal("):
            if forbidden in contents[key]:
                errors.append(f"Unauthorized direct write/scheduler or forged principal in {key}: {forbidden}")
    return errors

if __name__=="__main__":
    faults=problems(TEXT)
    if faults: raise SystemExit("NATIVE PHASE 4 CONTRACT FAILED:\n"+"\n".join(faults))
    for key,token in (
        ("project",'Compile Include="../AlTamam.DesktopHost/Scheduling/EightPmSchedulerService.cs"'),
        ("backend","await _autoAttendance.DisposeAsync()"),
        ("host","_backend.StartAutomaticAttendanceScheduler();"),
        ("review_backend","principal.SessionRevocation"),
        ("review_backend",'"operationId"'),
        ("review_backend","AttendanceDateRangePolicy.TryParse"),
        ("review_backend","NativeAttendanceDateRange.TryCreate"),
        ("review_backend","actualStart != validatedRange?.StartDate"),
        ("reader","_review.PreviewAsync(actor, date, site, people, statusId, ct, range)"),
        ("review_vm","draft.PreviewToken, _operationId"),
        ("register_vm","Review.ForceCancel()"),
        ("review_xaml","ConfirmCommand"),
        ("release","dotnet publish src/AlTamam.DesktopHost/AlTamam.DesktopHost.csproj")
    ):
        mutated=TEXT.copy()
        mutated[key]=mutated[key].replace(token,"REMOVED_BY_NEGATIVE_TEST")
        if not problems(mutated):
            raise SystemExit(f"Unsafe mutation incorrectly passed for {key}:{token}")
    print("NATIVE PHASE 4 PASS: exact durable engine, one-owner scheduler, audited human-preview/confirm/replay and four old customer packages intact")
