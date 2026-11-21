#!/usr/bin/env python3
"""Fail-closed test-first contracts for parallel Release Windows WebView2 acceptance.

Full C# / R01 / 10k SQLCipher shards, separate real Windows UI gate and the
four actual Portable/Installer checks must ALL pass before audit and shipping.
"""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
W=ROOT/".github/workflows/windows-release.yml"
PACKAGE_NEEDS="needs: [preflight, build-test, verify-full-test-shards, webview-e2e]"


def validate(workflow: str) -> list[str]:
    problems=[]
    for stage in ("build-test", "webview-e2e", "verify-full-test-shards",
                  "package", "package-acceptance", "handover-evidence"):
        if f"  {stage}:\n" not in workflow:
            problems.append(f"Release stage missing: {stage}")
    if problems:
        return problems
    full=workflow.split("  build-test:\n",1)[1].split("  webview-e2e:\n",1)[0]
    ui=workflow.split("  webview-e2e:\n",1)[1].split("  verify-full-test-shards:\n",1)[0]
    package=workflow.split("  package:\n",1)[1].split("  package-acceptance:\n",1)[0]
    if "pwsh -NoProfile -File ./build/test-webview-ui-e2e.ps1" in full:
        problems.append("Expensive real UI suite duplicated in full xUnit shard")
    if any(line.startswith("    needs:") or line.startswith("    if: always()") for line in ui.splitlines()):
        problems.append("Windows UI acceptance must start independently, and never bypass failures")
    for needed in (
        "runs-on: windows-latest",
        "actions/checkout@v4",
        "actions/setup-dotnet@v4",
        "dotnet restore AlTamam.slnx",
        "dotnet build AlTamam.slnx",
        "pwsh -NoProfile -File ./build/test-webview-ui-e2e.ps1",
        "ALTAMAM_PHASE13_EVIDENCE_PATH:",
        "ALTAMAM_SOURCE_HEAD_SHA: ",
        "Collect-Phase13WindowsFacts.ps1",
        "Validate-Phase13Evidence.ps1",
        "phase13-real-windows-e2e-release",
        "if-no-files-found: error",
    ):
        if needed not in ui:
            problems.append("Real Windows UI gate weakened: "+needed)
    if ("dotnet build AlTamam.slnx" in ui and
        "pwsh -NoProfile -File ./build/test-webview-ui-e2e.ps1" in ui and
        ui.index("dotnet build AlTamam.slnx") > ui.index(
            "pwsh -NoProfile -File ./build/test-webview-ui-e2e.ps1"
        )):
        problems.append("Real Windows UI testing must use built source")
    if PACKAGE_NEEDS not in package:
        problems.append("Packaging must depend on full tests, real UI, preflight, TRX verification")
    if "needs: package" not in workflow.split("  package-acceptance:\n",1)[1]:
        problems.append("Four real packaged customer binaries must still be checked")
    if "needs: [package, package-acceptance]" not in workflow:
        problems.append("SHA256 audit must wait for all real acceptance jobs")
    if "shard: [r01-config, r01-excel, r01-ops, r01-other, bridge-heavy, bridge-light, performance, security, other]" not in full:
        problems.append("All nine exhaustive xUnit test shards mandatory")
    return problems


def self_test(workflow: str) -> None:
    assert not validate(workflow)
    for original,replacement in (
        (PACKAGE_NEEDS,"needs: [preflight, build-test, verify-full-test-shards]"),
        ("pwsh -NoProfile -File ./build/test-webview-ui-e2e.ps1", "echo SKIPPED"),
        ("Validate-Phase13Evidence.ps1","echo SKIPPED"),
        ("if-no-files-found: error", "if-no-files-found: warn"),
    ):
        # Some tokens occur elsewhere in workflow, so target the independent
        # Windows UI section specifically when testing the mutation.
        where=workflow.index("  webview-e2e:\n")
        end=workflow.index("  verify-full-test-shards:\n")
        new=workflow[:where]+workflow[where:end].replace(original,replacement,1)+workflow[end:]
        if original==PACKAGE_NEEDS:
            new=workflow.replace(original,replacement,1)
        assert validate(new),original

if __name__=="__main__":
    source=W.read_text(encoding="utf-8")
    defects=validate(source)
    if defects:
        raise SystemExit("PARALLEL WINDOWS UI CONTRACT FAIL:\n"+"\n".join(defects))
    self_test(source)
    print("PARALLEL WINDOWS UI CONTRACT PASS: independent real E2E and exhaustive tests block all four packages")
