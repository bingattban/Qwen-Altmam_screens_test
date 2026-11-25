#!/usr/bin/env python3
"""Both workflows must preserve full tests; development is never release evidence."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FULL = "(github.event_name == 'workflow_dispatch' && inputs.build_mode == 'full') || startsWith(github.ref, 'refs/tags/v')"
ARCHS = [{"arch":"Windows-32bit","runtime":"win-x86"}, {"arch":"Windows-64bit","runtime":"win-x64"}]
ACCEPTANCE = [{"arch":a["arch"],"mode":m} for a in ARCHS for m in ("Portable","Installer")]
COMMIT = "a" * 40
HEAD = "b" * 40
VERSION = "0.0.999-ci"

def job(workflow, name):
    import re
    return re.split(r"(?m)^  [a-z][a-z0-9-]*:\s*$", workflow.split(f"  {name}:\n",1)[1],maxsplit=1)[0]

def matrix_expression(full, dev):
    return "include: ${{ fromJSON((" + FULL + ") && '" + json.dumps(full,separators=(",",":")) + "' || '" + json.dumps(dev,separators=(",",":")) + "') }}"

def step(body, name):
    return body.split("      - name: " + name, 1)[1].split("      - ",1)[0]

class BuildModes(unittest.TestCase):
    def test_both_modes_share_required_tests_and_use_exact_matrices(self):
        release=(ROOT/".github/workflows/windows-release.yml").read_text(encoding="utf-8")
        core=(ROOT/".github/workflows/windows-ci.yml").read_text(encoding="utf-8")
        self.assertIn("build_mode:",release)
        self.assertIn("default: development",release)
        self.assertIn("options: [development, full]",release)
        self.assertIn("BUILD_MODE: ${{ ("+FULL+") && 'full' || 'development' }}",release)
        self.assertIn(matrix_expression(ARCHS,ARCHS[:1]),job(release,"package"))
        self.assertIn(matrix_expression(ACCEPTANCE,ACCEPTANCE[:1]),job(release,"package-acceptance"))
        self.assertIn("needs: [preflight, build-test, verify-full-test-shards, webview-e2e]",job(release,"package"))
        self.assertIn("needs: package",job(release,"package-acceptance"))
        self.assertIn("needs: [package, package-acceptance]",job(release,"handover-evidence"))
        for name in ("preflight","build-test","webview-e2e","verify-full-test-shards","package","package-acceptance","handover-evidence"):
            self.assertNotIn("\n    if:",job(release,name),name+" cannot skip a mandatory job")
            self.assertNotIn("continue-on-error: true",job(release,name))
        for wf in (core,release):
            self.assertIn("python build/test-ci-build-modes.py",wf)
        package=job(release,"package")
        for name in ("Install Inno Setup 6","Generate SHA-256 checksums","Verify bytes, checksums","Stage byte-identical","Upload standalone Portable","Upload standalone Installer","Upload checksums and build metadata","Retain raw publish dir","Optional signing"):
            self.assertIn("if: env.BUILD_MODE == 'full'",step(package,name),name)
        compile_step=step(job(release,"preflight"),"Compile x64 target without packaging")
        self.assertIn("if: env.BUILD_MODE == 'development'",compile_step)
        self.assertIn("-r win-x64 --self-contained true",compile_step)
        self.assertIn("-r win-x86 --self-contained true",compile_step)
        self.assertNotIn("dotnet publish",compile_step)
        installer=(ROOT/"build/AlTamam-installer.iss").read_text(encoding="utf-8")
        for token in ("#ifdef BuildX86","ArchitecturesAllowed=x86compatible",
                      "ArchitecturesAllowed=x64compatible","ArchitecturesInstallIn64BitMode=x64compatible",
                      "[UninstallDelete]",'Name: "{app}\\logs"'):
            self.assertIn(token,installer)
        self.assertIn("'/DBuildX64'",step(package,"Install Inno Setup 6"))
        self.assertIn("'/DBuildX86'",step(package,"Install Inno Setup 6"))
        self.assertIn("if: env.BUILD_MODE == 'development'",step(package,"Generate development-only SHA-256 evidence"))
        self.assertIn("if: env.BUILD_MODE == 'development'",step(package,"Upload development Portable x86"))
        audit=job(release,"handover-evidence")
        self.assertIn("if: env.BUILD_MODE == 'full'",step(audit,"Independently hash four actual binaries"))
        self.assertIn("if: env.BUILD_MODE == 'development'",step(audit,"Independently verify development ZIP"))
        self.assertIn("if: "+FULL,job(release,"clean-windows-acceptance"))
        self.assertIn("BLOCKED: no Windows-UI acceptance tests",job(release,"clean-windows-acceptance"))
        acceptance=job(release,"package-acceptance")
        self.assertIn("if: env.BUILD_MODE == 'full'",step(acceptance,"Download full Installer"))
        self.assertIn("if: env.BUILD_MODE == 'development'",step(acceptance,"Download development Portable"))
        self.assertIn("-AcceptanceMode \"Portable\"",acceptance)
        self.assertIn("test-package-appdata-preservation.ps1",acceptance)
        script=(ROOT/"build/test-package-appdata-preservation.ps1").read_text(encoding="utf-8")
        self.assertIn('if ($AcceptanceMode -ne "Portable" -or -not [string]::IsNullOrWhiteSpace($InstallerPath))',script)
        self.assertIn('Assert-True (-not [string]::IsNullOrWhiteSpace($InstallerPath))',script)

    def test_development_metadata_is_exact_and_rejects_extra_or_tampered_packages(self):
        spec=importlib.util.spec_from_file_location("dev_artifact",ROOT/"build/development-artifact.py")
        mod=importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            archive=root/f"AlTamamDaily_{VERSION}_Portable_win-x86.zip"
            archive.write_bytes(b"synthetic zip bytes for hash validation only")
            mod.create(root,VERSION,COMMIT,HEAD)
            self.assertEqual([],mod.verify(root,VERSION,COMMIT,HEAD))
            meta=json.loads((root/"DEVELOPMENT_METADATA.json").read_text(encoding="utf-8"))
            self.assertEqual("development",meta["buildMode"])
            self.assertFalse(meta["finalRelease"])
            self.assertEqual("win-x86",meta["runtimeIdentifier"])
            self.assertTrue(mod.verify(root,VERSION,"c"*40,HEAD))
            self.assertTrue(mod.verify(root,VERSION,COMMIT,"d"*40))
            extra=root/f"AlTamamDaily_{VERSION}_Setup_win-x86.exe"
            extra.write_bytes(b"unexpected installer")
            self.assertTrue(mod.verify(root,VERSION,COMMIT,HEAD))
            extra.unlink()
            archive.write_bytes(b"corrupt")
            self.assertTrue(mod.verify(root,VERSION,COMMIT,HEAD))
            archive.unlink()
            self.assertTrue(mod.verify(root,VERSION,COMMIT,HEAD))

if __name__ == "__main__":
    unittest.main()
