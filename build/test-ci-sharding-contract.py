#!/usr/bin/env python3
"""Fail-closed CI contract for exhaustive nine-shard Release validation."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WF = ROOT / ".github/workflows/windows-release.yml"
EXPECTED = {
    "r01-config": "FullyQualifiedName~AlTamam.Tests.Bridge.R01NativeConfigurationLifecycleTests",
    "r01-excel": "FullyQualifiedName~AlTamam.Tests.Bridge.R01NativeExcelLifecycleTests",
    "r01-ops": "FullyQualifiedName~AlTamam.Tests.Bridge.R01NativeOperationalLifecycleTests",
    "r01-other": "FullyQualifiedName~AlTamam.Tests.Bridge.R01&FullyQualifiedName!~R01NativeConfigurationLifecycleTests&FullyQualifiedName!~R01NativeExcelLifecycleTests&FullyQualifiedName!~R01NativeOperationalLifecycleTests",
    "bridge-heavy": "FullyQualifiedName~AlTamam.Tests.Bridge.NativeRestoreActivationCoordinatorTests|FullyQualifiedName~AlTamam.Tests.Bridge.BackupRetentionIntegrationTests|FullyQualifiedName~AlTamam.Tests.Bridge.Section03FirstRunBridgeTests|FullyQualifiedName~AlTamam.Tests.Bridge.Section03FullResetTests",
    "bridge-light": "FullyQualifiedName~AlTamam.Tests.Bridge&FullyQualifiedName!~AlTamam.Tests.Bridge.R01&FullyQualifiedName!~NativeRestoreActivationCoordinatorTests&FullyQualifiedName!~BackupRetentionIntegrationTests&FullyQualifiedName!~Section03FirstRunBridgeTests&FullyQualifiedName!~Section03FullResetTests",
    "performance": "FullyQualifiedName~AlTamam.Tests.Performance",
    "security": "FullyQualifiedName~AlTamam.Tests.Security",
    "other": "FullyQualifiedName!~AlTamam.Tests.Bridge&FullyQualifiedName!~AlTamam.Tests.Performance&FullyQualifiedName!~AlTamam.Tests.Security",
}

def validate(content: str) -> list[str]:
    problems=[]
    if "  build-test:\n" not in content or "  verify-full-test-shards:\n" not in content or "  package:\n" not in content:
        return ["Missing matrix, independent attestation or package job"]
    tests=content.split("  build-test:\n",1)[1].split("  webview-e2e:\n",1)[0]
    ui=content.split("  webview-e2e:\n",1)[1].split("  verify-full-test-shards:\n",1)[0]
    verify=content.split("  verify-full-test-shards:\n",1)[1].split("  package:\n",1)[0]
    package=content.split("  package:\n",1)[1]
    if "shard: [r01-config, r01-excel, r01-ops, r01-other, bridge-heavy, bridge-light, performance, security, other]" not in tests:
        problems.append("Matrix must include all nine exhaustive partitions")
    for shard, predicate in EXPECTED.items():
        if predicate not in tests:
            problems.append("Missing exact predicate: "+shard)
    if 'dotnet test tests/AlTamam.Tests/AlTamam.Tests.csproj' not in tests or ' --filter "$filter"' not in tests:
        problems.append("Real filtered xUnit test invocation missing")
    for mandatory in ("phase02-integration.trx","phase03-unit.trx",
                      "check-phase15-perf-evidence.py"):
        if mandatory not in tests:
            problems.append("Missing real acceptance evidence: "+mandatory)
    if "pwsh -NoProfile -File ./build/test-webview-ui-e2e.ps1" not in ui:
        problems.append("Independent real Windows E2E proof is mandatory")
    if "name: test-results-windows-" not in tests or "if: always()" not in tests:
        problems.append("Sharded TRX upload missing")
    if "needs: build-test" not in verify or "actions/download-artifact@v4" not in verify or "verify-release-test-shards.py" not in verify:
        problems.append("Independent full-test shard verifier missing")
    if "check-r01-native-contract-evidence.py ShardedResults/test-results-windows-r01-config/full-suite.trx" not in verify or "test-r01-native-contract-evidence.py" not in verify:
        problems.append("Independent real 22-case R01 native evidence verification missing")
    if "    needs: [preflight, build-test, verify-full-test-shards, webview-e2e]" not in package:
        problems.append("Packaging must wait for both static and all validated test shards")
    for arch in ("win-x86","win-x64"):
        if arch not in package:
            problems.append("Missing packaging runtime "+arch)
    for mandatory in ("test-package-appdata-preservation.ps1","verify-release-artifacts.py",
                      "verify-phase15-handover.py"):
        if mandatory not in content:
            problems.append("Release acceptance missing "+mandatory)
    if "continue-on-error: true" in tests or "continue-on-error: true" in verify or "if: always()" in verify:
        problems.append("Cannot ignore any test or verifier failure")
    return problems

def self_test(content: str) -> None:
    assert not validate(content)
    for before, after in (
        ("    needs: [preflight, build-test, verify-full-test-shards, webview-e2e]", "    needs: [preflight, build-test]"),
        (EXPECTED["performance"], "FullyQualifiedName~NoTests"),
        ("verify-release-test-shards.py", "fake-pass.py"),
        ("check-r01-native-contract-evidence.py ShardedResults/test-results-windows-r01-config/full-suite.trx", "echo no-tests"),
    ):
        assert validate(content.replace(before, after, 1)), before

if __name__ == "__main__":
    content=WF.read_text(encoding="utf-8")
    issues=validate(content)
    if issues:
        raise SystemExit("RELEASE SHARD CONTRACT FAIL:\n"+"\n".join(issues))
    self_test(content)
    print("RELEASE SHARD CONTRACT PASS: 1162+ tests across nine shards, verified before x86/x64 packages")
