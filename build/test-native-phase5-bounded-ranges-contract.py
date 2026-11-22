#!/usr/bin/env python3
"""WPF phase 5 bounded leave/permission guard, in BOTH Core and Release.

Static negative-path contract complements real xUnit, SQLCipher and Windows
integration/packaging tests; it does not prove clean-machine WPF acceptance.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PATHS = {
    "contracts": "src/AlTamam.NativePresentation/NativeAttendanceRevisionContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeAttendanceReviewViewModel.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherAttendanceReviewBackend.cs",
    "reader": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeAttendanceReviewView.xaml",
    "canonical": "src/AlTamam.Application/Bridge/AttendanceCommitService.cs",
    "policy": "src/AlTamam.Application/Attendance/AttendanceDateRangePolicy.cs",
    "tests": "tests/AlTamam.UnitTests/NativeAttendanceDateRangePhase5Tests.cs",
    "oldtests": "tests/AlTamam.UnitTests/NativeAttendanceReviewPhase4Tests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml"
}
TEXT = {key: (ROOT / filename).read_text(encoding="utf-8")
        for key, filename in PATHS.items()}

def check(d):
    faults = []
    def need(key, token):
        if token not in d[key]:
            faults.append("Missing " + key + " invariant: " + token)
    try:
        ET.fromstring(d["xaml"])
    except ET.ParseError as err:
        faults.append("Invalid WPF XAML: " + str(err))
    for token in ("NativeAttendanceDateRange", "RequiresDateRange = false",
                  "NativeAttendanceDateRange? range = null",
                  "NativeAttendanceDateRange? DateRange = null"):
        need("contracts", token)
    for token in ("public bool RequiresDateRange", "public DateTime? RangeStartDate",
                  "public DateTime? RangeEndDate", "ChangeRangeDate(",
                  "InvalidatePreview();", "_unverifiedCommit || _completed",
                  "start != _businessDate", "end < start",
                  "end.DayNumber - start.DayNumber > 365",
                  "linked.Token, selectedRange", "draft.DateRange != selectedRange",
                  "draft.PreviewToken, _operationId", "if (!HasValidActor(actor))"):
        need("vm", token)
    for token in ("statuses.Add(new(id, name, AttendanceDateRangePolicy.RequiresRange(id)))",
                  "AttendanceDateRangePolicy.TryParse(payload, statusId, today,",
                  'payload["rangeStartDate"]', 'payload["rangeEndDate"]',
                  'RequiredText(data, "rangeStartDate") != expectedStart',
                  'RequiredText(data, "rangeEndDate") != expectedEnd',
                  "new AttendanceCommitService(database)",
                  "principal.SessionRevocation", "return new NativeAttendanceRevisionPreview(",
                  "CommitAsync(new JsonObject"):
        need("backend", token)
    if "if (AttendanceDateRangePolicy.RequiresRange(id)) continue;" in d["backend"]:
        faults.append("Timed statuses are still hidden.")
    need("reader", "ct, range")
    for token in ("RequiresDateRange", "RangeStartDate", "RangeEndDate",
                  "<DatePicker", "PreviewCommand", "ConfirmCommand",
                  'KeyboardNavigation.TabNavigation="Cycle"'):
        need("xaml", token)
    for token in ("rangeStartDate", "rangeEndDate",
                  "AttendanceDateRangePolicy.TryParse", "AttendanceDateRanges"):
        need("canonical", token)
    need("policy", "start!=today")
    need("policy", "to.DayNumber-from.DayNumber>365")
    if d["tests"].count("[Fact]") < 6:
        faults.append("Fewer than 6 phase 5 unit tests.")
    need("oldtests", "NativeAttendanceDateRange? range = null")
    for key in ("core", "release"):
        need(key, "python build/test-native-phase5-bounded-ranges-contract.py")
        need(key, "python build/test-native-phase4-scheduler-approval-contract.py")
    for forbidden in ("new AttendanceCommitService", "ExecuteInWriteTransactionAsync(",
                      "new BridgePrincipal(", "RunPendingAsync("):
        if forbidden in d["vm"]:
            faults.append("Unsafe presentation-side mutation: " + forbidden)
    return faults

if __name__ == "__main__":
    errors = check(TEXT)
    if errors:
        raise SystemExit("WPF PHASE 5 FAILED:" + chr(10) + chr(10).join(errors))
    for key, token in (
        ("vm", "draft.DateRange != selectedRange"),
        ("vm", "end.DayNumber - start.DayNumber > 365"),
        ("backend", "AttendanceDateRangePolicy.TryParse(payload, statusId, today,"),
        ("backend", 'RequiredText(data, "rangeEndDate") != expectedEnd'),
        ("xaml", "RangeEndDate"),
        ("core", "python build/test-native-phase5-bounded-ranges-contract.py"),
        ("release", "python build/test-native-phase5-bounded-ranges-contract.py")
    ):
        damaged = TEXT.copy()
        damaged[key] = damaged[key].replace(token, "UNSAFE_REMOVED")
        if not check(damaged):
            raise SystemExit("Mutation incorrectly passed " + key + ": " + token)
    print("WPF PHASE 5 PASS: date range, exact server echo, explicit review, Core/Release enforcement")
