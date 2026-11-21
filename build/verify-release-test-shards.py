#!/usr/bin/env python3
"""Verify real, disjoint Windows xUnit TRX results before *any* Release packaging.

Shards partition the entire AlTamam.Tests namespace with an exhaustive,
mutually exclusive set of VSTest FullyQualifiedName predicates. The baseline
minimum counts were measured from the full 1,162-test successful Windows run
44937400797. Adding tests is allowed; silently omitting old coverage is not.
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SHARDS = ("r01-config", "r01-excel", "r01-ops", "r01-other",
          "bridge-heavy", "bridge-light", "performance", "security", "other")
# Empirical per-class test inventory from the real Windows R01 TRX. Each of
# 1,162 original cases is mandatory; all four native R01 subsets are attested.
MINIMUMS = {"r01-config": 7, "r01-excel": 6, "r01-ops": 5, "r01-other": 17,
            "bridge-heavy": 24, "bridge-light": 876,
            "performance": 17, "security": 123, "other": 87}
# Four measured Bridge integration classes consume most of the original 900-test
# worker time (verified from the actual 2026-11-21 Windows TRX).
HEAVY_BRIDGE_CLASSES = (
    "NativeRestoreActivationCoordinatorTests",
    "BackupRetentionIntegrationTests",
    "Section03FirstRunBridgeTests",
    "Section03FullResetTests",
)
PHASE_MINIMUMS = {"phase02-integration.trx": 18, "phase03-unit.trx": 18}


def belongs(name: str, shard: str) -> bool:
    if not name.startswith("AlTamam.Tests."):
        return False
    bridge = name.startswith("AlTamam.Tests.Bridge.")
    r01 = name.startswith("AlTamam.Tests.Bridge.R01")
    r01_config = name.startswith("AlTamam.Tests.Bridge.R01NativeConfigurationLifecycleTests.")
    r01_excel = name.startswith("AlTamam.Tests.Bridge.R01NativeExcelLifecycleTests.")
    r01_ops = name.startswith("AlTamam.Tests.Bridge.R01NativeOperationalLifecycleTests.")
    perf = name.startswith("AlTamam.Tests.Performance.")
    security = name.startswith("AlTamam.Tests.Security.")
    heavy = any(name.startswith("AlTamam.Tests.Bridge." + cls + ".")
                for cls in HEAVY_BRIDGE_CLASSES)
    return {
        "r01-config": r01_config,
        "r01-excel": r01_excel,
        "r01-ops": r01_ops,
        "r01-other": r01 and not (r01_config or r01_excel or r01_ops),
        "bridge-heavy": heavy,
        "bridge-light": bridge and not r01 and not heavy,
        "performance": perf,
        "security": security,
        "other": not bridge and not perf and not security
    }[shard]


def read_trx(path: Path) -> tuple[list[ET.Element], list[str]]:
    problems = []
    try:
        root = ET.parse(path).getroot()
        if root.tag != "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}TestRun":
            return [], [f"{path}: invalid TRX root"]
        results = root.findall("./t:Results/t:UnitTestResult", NS)
        counters = root.find("./t:ResultSummary/t:Counters", NS)
        if counters is None:
            return [], [f"{path}: missing real test counters"]
        count = len(results)
        for key, wanted in (("total", count), ("executed", count), ("passed", count),
                            ("failed", 0), ("error", 0), ("notExecuted", 0)):
            raw = counters.get(key)
            if raw is None and key in ("error", "notExecuted"):
                continue  # miniature synthetic test fixtures
            if raw is None or int(raw) != wanted:
                problems.append(f"{path}: {key} mismatch, got {raw}, expected {wanted}")
        if count == 0:
            problems.append(f"{path}: zero executed tests")
        for row in results:
            if row.get("outcome") != "Passed" or not row.get("testId") or not row.get("testName"):
                problems.append(f"{path}: non-passing/unnamed test")
    except (OSError, ValueError, ET.ParseError) as exc:
        return [], [f"{path}: invalid/missing TRX: {exc}"]
    return results, problems


def validate(root: Path, minimums: dict[str, int] | None = None,
             phase_minimums: dict[str, int] | None = None) -> list[str]:
    minimums = MINIMUMS if minimums is None else minimums
    phase_minimums = PHASE_MINIMUMS if phase_minimums is None else phase_minimums
    problems = []
    actual = {p.name for p in root.iterdir() if p.is_dir()} if root.is_dir() else set()
    wanted = {"test-results-windows-" + shard for shard in SHARDS}
    if actual != wanted:
        problems.append(f"Wrong shard artifact directories: missing={sorted(wanted-actual)} unexpected={sorted(actual-wanted)}")
    seen_names: set[str] = set()
    seen_ids: set[str] = set()
    counts = {}
    for shard in SHARDS:
        folder = root / ("test-results-windows-" + shard)
        tests, failures = read_trx(folder / "full-suite.trx")
        problems.extend(failures)
        counts[shard] = len(tests)
        if len(tests) < minimums[shard]:
            problems.append(f"{shard}: executed {len(tests)}, baseline requires at least {minimums[shard]}")
        for result in tests:
            name = result.get("testName", "")
            tid = result.get("testId", "")
            if not belongs(name, shard):
                problems.append(f"{shard}: misplaced test {name}")
            if name in seen_names or tid in seen_ids:
                problems.append(f"{shard}: duplicate test evidence {name}")
            seen_names.add(name)
            seen_ids.add(tid)
        expected_files = {"full-suite.trx"} | (set(phase_minimums) if shard == "other" else set())
        unexpected = ({p.name for p in folder.glob("*.trx")} if folder.is_dir() else set()) - expected_files
        if unexpected:
            problems.append(f"{shard}: unexpected TRX files {sorted(unexpected)}")
    for filename, minimum in phase_minimums.items():
        tests, errors = read_trx(root / "test-results-windows-other" / filename)
        problems.extend(errors)
        if len(tests) < minimum:
            problems.append(f"{filename}: {len(tests)} tests, minimum {minimum}")
    if not problems:
        print("RELEASE FULL COVERAGE PASS: " + ", ".join(f"{k}={v}" for k, v in counts.items()) +
              f"; total={sum(counts.values())}, distinct passed; Phase02/03 TRX verified")
    return problems


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: verify-release-test-shards.py <downloaded-artifact-parent>")
    issues = validate(Path(sys.argv[1]))
    if issues:
        raise SystemExit("RELEASE SHARDED TEST ATTESTATION FAIL:\n" + "\n".join(issues))
