#!/usr/bin/env python3
"""Fail-closed six-way partition regression using real 1,162-case TRX baseline.

The measured 2026-11-21 run 44949180998 contained:
  R01 35, Bridge-heavy 24, Bridge-light 876, Performance 17,
  Security 123, all remaining 87. The independent TRX verifier must
  reject missing/duplicate/wrong-shard evidence, never substitute counts.
"""
import importlib.util
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
p=ROOT/"build/verify-release-test-shards.py"
spec=importlib.util.spec_from_file_location("release_shards",p)
mod=importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
expected={"r01":35,"bridge-heavy":24,"bridge-light":876,"performance":17,"security":123,"other":87}
assert mod.MINIMUMS==expected, f"Shard count baseline drift: {mod.MINIMUMS}"
assert tuple(mod.SHARDS)==tuple(expected), f"Shard inventory drift: {mod.SHARDS}"
assert sum(mod.MINIMUMS.values())==1162, "Original 1162-test coverage must not shrink"

cases={
    "AlTamam.Tests.Bridge.R01NativeExcelLifecycleTests.Real":"r01",
    "AlTamam.Tests.Bridge.NativeRestoreActivationCoordinatorTests.Restore":"bridge-heavy",
    "AlTamam.Tests.Bridge.BackupRetentionIntegrationTests.Preserve":"bridge-heavy",
    "AlTamam.Tests.Bridge.Section03FirstRunBridgeTests.First":"bridge-heavy",
    "AlTamam.Tests.Bridge.Section03FullResetTests.Reset":"bridge-heavy",
    "AlTamam.Tests.Bridge.AttendanceCommitIntegrationTests.Commit":"bridge-light",
    "AlTamam.Tests.Bridge.BridgeRbacMatrixTests.EveryManifestAction":"bridge-light",
    "AlTamam.Tests.Performance.Phase15TenThousandMultiYearDiagnostics.Measured":"performance",
    "AlTamam.Tests.Security.BridgeFuzzPayloadTests.Fuzz":"security",
    "AlTamam.Tests.Security.WindowsDatabaseKeyStoreTests.Protect":"security",
    "AlTamam.Tests.Persistence.OfflineRestoreSwitchTests.Safe":"other",
    "AlTamam.Tests.R01BridgeSqlCipherDispatchTests.ActiveStatuses":"other",
}
for name,wanted in cases.items():
    actual=[shard for shard in mod.SHARDS if mod.belongs(name,shard)]
    assert actual==[wanted], f"Case {name} must belong exactly to {wanted}, got {actual}"

workflow=(ROOT/".github/workflows/windows-release.yml").read_text(encoding="utf-8")
matrix="shard: [r01, bridge-heavy, bridge-light, performance, security, other]"
assert matrix in workflow,"All six real xUnit runners required"
assert "python3 build/verify-release-test-shards.py ShardedResults" in workflow
assert "needs: [preflight, build-test, verify-full-test-shards, webview-e2e]" in workflow
assert "test-package-appdata-preservation.ps1" in workflow
print("SIX-WAY RELEASE SHARDS PASS: 1,162+ disjoint tests, full coverage, strict package gates")
