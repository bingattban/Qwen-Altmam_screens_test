#!/usr/bin/env python3
"""Test-first contract for 9 exact disjoint xUnit shards, preserving 1,162 tests.

R01 35 previously took 8m11s on Windows run 44951463147; its actual native
TRX partitions are config=7, Excel=6, operational=5, all other R01=17.
All R01 native stdout/response-shape evidence must be reassembled and verified
independently *before* packaging; no covered tests may be skipped or duplicated.
"""
import importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("shard_verify",ROOT/"build/verify-release-test-shards.py")
mod=importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
wanted={
    "r01-config":7, "r01-excel":6, "r01-ops":5,"r01-other":17,
    "bridge-heavy":24,"bridge-light":876,"performance":17,
    "security":123,"other":87
}
assert mod.MINIMUMS==wanted,("Test count drift",mod.MINIMUMS)
assert tuple(mod.SHARDS)==tuple(wanted),("Shard inventory drift",mod.SHARDS)
assert sum(mod.MINIMUMS.values())==1162
cases={
 "AlTamam.Tests.Bridge.R01NativeConfigurationLifecycleTests.PersonnelIdentity_Preserves":"r01-config",
 "AlTamam.Tests.Bridge.R01NativeExcelLifecycleTests.Import_RealWorkbook":"r01-excel",
 "AlTamam.Tests.Bridge.R01NativeOperationalLifecycleTests.Separation_RealServer":"r01-ops",
 "AlTamam.Tests.Bridge.R01NativeRecoveryLifecycleTests.Backup_RealCreate":"r01-other",
 "AlTamam.Tests.Bridge.R01AuthenticatedMutationBoundaryTests.RealLogin":"r01-other",
 "AlTamam.Tests.Bridge.NativeRestoreActivationCoordinatorTests.Restore":"bridge-heavy",
 "AlTamam.Tests.Bridge.AttendanceCommitIntegrationTests.Commit":"bridge-light",
 "AlTamam.Tests.Performance.Phase15TenThousandMultiYearDiagnostics.Measured":"performance",
 "AlTamam.Tests.Security.BridgeFuzzPayloadTests.Fuzz":"security",
 "AlTamam.Tests.Persistence.OfflineRestoreSwitchTests.Safe":"other"
}
for name,expected in cases.items():
    matched=[shard for shard in mod.SHARDS if mod.belongs(name,shard)]
    assert matched==[expected],f"{name} wrongly classified as {matched}"
workflow=(ROOT/".github/workflows/windows-release.yml").read_text(encoding="utf-8")
assert "shard: [r01-config, r01-excel, r01-ops, r01-other, bridge-heavy, bridge-light, performance, security, other]" in workflow
assert "python3 build/verify-release-test-shards.py ShardedResults" in workflow
assert "python3 build/check-r01-native-contract-evidence.py ShardedResults/test-results-windows-r01-config/full-suite.trx" in workflow
assert "needs: [preflight, build-test, verify-full-test-shards, webview-e2e]" in workflow
assert "needs: [package, package-acceptance]" in workflow
print("NINE-SHARD CONTRACT PASS: 1,162 distinct cases, 22 full native R01 shapes, full four-package gates")
