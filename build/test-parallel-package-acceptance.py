#!/usr/bin/env python3
"""Fail-closed tests for four independent packaged Portable/Installer checks.

The four actual packaged binaries must each execute the native Windows
WebView2/SQLCipher acceptance once, on separate Windows workers, while
per-mode AppData preservation and the independent SHA256 audit stay mandatory.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def validate(release: str, package: str) -> list[str]:
    issues=[]
    if "  package-acceptance:\n" not in release:
        return ["Missing post-build parallel package acceptance job"]
    build = release.split("  package:\n",1)[1].split("  package-acceptance:\n",1)[0]
    acceptance = release.split("  package-acceptance:\n",1)[1].split("  handover-evidence:\n",1)[0]
    audit = release.split("  handover-evidence:\n",1)[1].split("  clean-windows-acceptance:\n",1)[0]
    if "needs: [preflight, build-test, verify-full-test-shards, webview-e2e]" not in build:
        issues.append("Packages must still be built only after all Release test gates")
    if "test-package-appdata-preservation.ps1" in build:
        issues.append("Slow acceptance cannot serialize the x86 build job")
    for name in ("AlTamam-Portable-", "AlTamam-Installer-", "AlTamam-Checksums-"):
        if name not in build:
            issues.append("Missing candidate package output " + name)
    for required in (
        "needs: package", "arch: [Windows-32bit, Windows-64bit]",
        "RUNTIME: ${{ matrix.arch == 'Windows-32bit' && 'win-x86' || 'win-x64' }}",
        "mode: [Portable, Installer]", "actions/download-artifact@v4",
        "test-package-appdata-preservation.ps1",
        "-AcceptanceMode", "if ($LASTEXITCODE -ne 0) { throw",
        "Preserve packaged acceptance diagnostics even after failure"
    ):
        if required not in acceptance:
            issues.append("Missing full real Windows package acceptance: " + required)
    if "continue-on-error: true" in acceptance:
        issues.append("Package acceptance cannot bypass Windows failures")
    if "needs: [package, package-acceptance]" not in audit:
        issues.append("Final independent SHA256 audit MUST await all four real acceptance jobs")
    if "verify-phase15-handover.py" not in audit:
        issues.append("Independent byte-for-byte four-package SHA256 verifier missing")
    if "ValidateSet(\"All\", \"Portable\", \"Installer\")" not in package:
        issues.append("Package script must select mutually exclusive acceptance modes")
    for text in ("Run-PackagedWebViewChecked $portableExe \"Portable ZIP\" $true",
                 "Run-PackagedWebViewChecked $installedExe \"Installer\" $true",
                 "Assert-DataPreserved \"Portable direct run\"",
                 "Assert-DataPreserved \"Installer run\"",
                 "Assert-DataPreserved \"Reinstall\"",
                 "if ($AcceptanceMode -ne \"Installer\")",
                 "if ($AcceptanceMode -ne \"Portable\")"):
        if text not in package:
            issues.append("Portable or Installer real validation removed: " + text)
    return issues


def self_test(release: str, package: str) -> None:
    assert not validate(release,package)
    for old,new in (
        ("needs: [package, package-acceptance]","needs: package"),
        ("mode: [Portable, Installer]","mode: [Portable]"),
        ("-AcceptanceMode", "-OmittedMode"),
        ("test-package-appdata-preservation.ps1","fake-acceptance.ps1")
    ):
        assert validate(release.replace(old,new,1),package),old
    assert validate(release,package.replace('if ($AcceptanceMode -ne "Portable")','if ($AcceptanceMode -eq "Portable")',1))


if __name__ == "__main__":
    release=(ROOT/".github/workflows/windows-release.yml").read_text(encoding="utf-8")
    package=(ROOT/"build/test-package-appdata-preservation.ps1").read_text(encoding="utf-8")
    errors=validate(release,package)
    if errors:
        raise SystemExit("PARALLEL PACKAGE ACCEPTANCE FAIL:\n"+"\n".join(errors))
    self_test(release,package)
    print("PARALLEL PACKAGE ACCEPTANCE PASS: 4 real Windows x86/x64 Portable/Installer tests before SHA256 audit")
