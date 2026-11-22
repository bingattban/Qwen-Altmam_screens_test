#!/usr/bin/env python3
"""Native WPF Phase 5 security/UX contract, enforced by BOTH Windows preflights.

Keeps the extra PR #12 negative-path CI coverage while retaining the PR #13
canonical bounded-range implementation. Complements, never replaces,
real Windows C#/SQLCipher/E2E/package acceptance.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "contracts": "src/AlTamam.NativePresentation/NativeAttendanceRevisionContracts.cs",
    "vm": "src/AlTamam.NativePresentation/NativeAttendanceReviewViewModel.cs",
    "backend": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherAttendanceReviewBackend.cs",
    "reader": "src/AlTamam.NativeDesktop/Dashboard/NativeSqlCipherDashboardReader.cs",
    "xaml": "src/AlTamam.NativeDesktop/Views/NativeAttendanceReviewView.xaml",
    "canonical": "src/AlTamam.Application/Bridge/AttendanceCommitService.cs",
    "policy": "src/AlTamam.Application/Attendance/AttendanceDateRangePolicy.cs",
    "phase4": "build/test-native-phase4-scheduler-approval-contract.py",
    "tests": "tests/AlTamam.UnitTests/NativeAttendancePhase5CrossBranchTests.cs",
    "core": ".github/workflows/windows-ci.yml",
    "release": ".github/workflows/windows-release.yml",
}
TEXT = {name: (ROOT / path).read_text(encoding="utf-8")
        for name, path in FILES.items()}


def problems(data):
    errors = []

    def require(part, snippet):
        if snippet not in data[part]:
            errors.append(f"Missing {part} invariant: {snippet}")

    try:
        ET.fromstring(data["xaml"])
    except ET.ParseError as error:
        errors.append(f"Invalid WPF date-picker XAML: {error}")

    for snippet in ("NativeAttendanceDateRange? range = null",
                    "string? RangeStartDate", "string? RangeEndDate"):
        require("contracts", snippet)
    for snippet in ("public bool RequiresDateRange",
                    "public bool CanEditSelection", "public bool CanEditRange",
                    "public DateTime? RangeStartDate", "public DateTime? RangeEndDate",
                    "NativeAttendanceDateRange.TryCreate",
                    "if (_committing || _unverifiedCommit)",
                    "draft.RangeStartDate != range?.StartDate",
                    "draft.RangeEndDate != range?.EndDate",
                    "InvalidatePreview();", "draft.PreviewToken, _operationId"):
        require("vm", snippet)
    for snippet in ("statuses.Add(new(id, name));",
                    "NativeAttendanceDateRange.TryCreate",
                    "AttendanceDateRangePolicy.TryParse",
                    'payload["rangeStartDate"]',
                    'payload["rangeEndDate"]',
                    "actualStart != validatedRange?.StartDate",
                    "actualEnd != validatedRange?.EndDate",
                    "new AttendanceCommitService(database)",
                    "principal.SessionRevocation"):
        require("backend", snippet)
    if "if (AttendanceDateRangePolicy.RequiresRange(id)) continue;" in data["backend"]:
        errors.append("Leave and permission statuses must not be hidden.")
    require("reader", "_review.PreviewAsync(actor, date, site, people, statusId, ct, range)")
    for snippet in ("RequiresDateRange", "CanEditSelection", "CanEditRange",
                    "RangeStartDate", "RangeEndDate", "<DatePicker",
                    "PreviewCommand", "ConfirmCommand"):
        require("xaml", snippet)
    for snippet in ("AttendanceDateRangePolicy.TryParse",
                    "AttendanceDateRanges", "MutationIdempotency"):
        require("canonical", snippet)
    require("policy", "to.DayNumber-from.DayNumber>365")
    require("phase4", "NativeAttendanceDateRange.TryCreate")
    for snippet in ("Controls_and_range_are_frozen_while_canonical_preview_is_pending",
                    "Dates_outside_canonical_bounds_never_reach_preview_or_commit",
                    "Swapped_live_session_after_timed_preview_cannot_approve"):
        require("tests", snippet)
    for part in ("core", "release"):
        require(part, "python build/test-native-phase5-bounded-ranges-contract.py")
        require(part, "python build/test-native-phase4-scheduler-approval-contract.py")
    for forbidden in ("new AttendanceCommitService", "ExecuteInWriteTransactionAsync(",
                      "new BridgePrincipal(", "RunPendingAsync("):
        if forbidden in data["vm"]:
            errors.append(f"Presentation may not bypass canonical services: {forbidden}")
    return errors


if __name__ == "__main__":
    failures = problems(TEXT)
    if failures:
        raise SystemExit("NATIVE WPF PHASE 5 CONTRACT FAILED:\n" + "\n".join(failures))
    for part, token in (
        ("vm", "if (_committing || _unverifiedCommit)"),
        ("vm", "draft.RangeEndDate != range?.EndDate"),
        ("backend", "AttendanceDateRangePolicy.TryParse"),
        ("backend", "actualStart != validatedRange?.StartDate"),
        ("reader", "_review.PreviewAsync(actor, date, site, people, statusId, ct, range)"),
        ("xaml", "RangeEndDate"),
        ("core", "python build/test-native-phase5-bounded-ranges-contract.py"),
        ("release", "python build/test-native-phase5-bounded-ranges-contract.py"),
    ):
        broken = TEXT.copy()
        broken[part] = broken[part].replace(token, "MISSING_BY_NEGATIVE_TEST")
        if not problems(broken):
            raise SystemExit(f"Unsafe omission incorrectly passed: {part}: {token}")
    print("NATIVE WPF PHASE 5 CONTRACT PASS: dates, identity, explicit review, no duplicate writer, BOTH Windows workflows")
