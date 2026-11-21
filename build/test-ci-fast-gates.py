#!/usr/bin/env python3
"""Test-first fail-closed contract for faster Core and exhaustive Release."""
import re
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

def job(workflow: str, name: str) -> str:
    marker = f'  {name}:\n'
    if marker not in workflow:
        return ''
    return re.split(r'(?m)^  [a-z][a-z0-9-]*:\s*$', workflow.split(marker, 1)[1], maxsplit=1)[0]

def check(core: str, release: str) -> list[str]:
    problems = []
    smoke = job(core, 'core-tests')
    full = job(release, 'build-test')
    package = job(release, 'package')
    preflight = job(release, 'preflight')
    if not preflight or not full or not smoke or not package:
        problems.append('all static, test, and package jobs required')
    if '    needs: manifest-contract' not in smoke:
        problems.append('Core tests must follow the manifest guard')
    if 'dotnet build AlTamam.slnx --configuration Release --no-restore' not in smoke:
        problems.append('Core must compile entire solution')
    if '--filter "FullyQualifiedName~BridgeRbacMatrixTests|' not in smoke:
        problems.append('Core must run positively selected critical smoke tests')
    for term in ('AttendanceCommitIntegrationTests', 'SqlCipherConnectionTests',
                 'R01AuthenticatedMutationBoundaryTests'):
        if term not in smoke:
            problems.append('Core smoke missing ' + term)
    if 'if ($count -lt 5)' not in smoke or 'ResultSummary.Counters.total' not in smoke:
        problems.append('Core smoke requires nonzero TRX and minimum count')
    if 'ALTAMAM_PHASE15_MULTIYEAR_PERF_REPORT' in smoke or 'Phase15TenThousandMultiYearDiagnostics' in smoke:
        problems.append('expensive Phase 15 benchmark cannot run in Core')
    if 'Capture measured ten-thousand-person search benchmark' in smoke:
        problems.append('standalone repeated synthetic benchmark cannot run in Core')
    if 'pwsh -NoProfile -File ./build/test-webview-ui-e2e.ps1' not in smoke:
        problems.append('real Core WPF/WebView2 E2E is mandatory')
    if '    needs: preflight' in full:
        problems.append('Release preflight and test jobs must start independently')
    if '    needs: [preflight, build-test, verify-full-test-shards]' not in package:
        problems.append('package must wait for preflight, all Release tests, and independent attestation')
    if re.search(r'(?m)^    if:\s*(?:always\(\)|true)\s*$', package):
        problems.append('package must never bypass failure')
    for path in ('tests/AlTamam.IntegrationTests/AlTamam.IntegrationTests.csproj',
                 'tests/AlTamam.UnitTests/AlTamam.UnitTests.csproj',
                 'tests/AlTamam.Tests/AlTamam.Tests.csproj'):
        if 'dotnet test ' + path not in full:
            problems.append('Release full coverage missing ' + path)
    full_suite = full.split('      - name: Full deterministic test suite', 1)[-1].split('      - name:', 1)[0]
    if '--filter "$filter"' not in full_suite or 'shard: [r01, bridge, performance, other]' not in full:
        problems.append('Release full xUnit suite must run in four exhaustive shards')
    for term in ('check-phase15-perf-evidence.py','check-r01-native-contract-evidence.py TestResults/full-suite.trx',
                 'pwsh -NoProfile -File ./build/test-webview-ui-e2e.ps1'):
        if term not in full:
            problems.append('Release exhaustive tests/evidence missing ' + term)
    for term in ('runtime: win-x86', 'runtime: win-x64', 'Build Portable ZIP',
                 'compile installer', 'SHA-256 checksums', 'test-package-appdata-preservation.ps1',
                 'verify-release-artifacts.py'):
        if term not in package:
            problems.append('required packaging gate/output missing ' + term)
    if core.count('cancel-in-progress: true') != 1 or release.count('cancel-in-progress: true') != 1:
        problems.append('cancel obsolete PR runs on both tracks')
    return problems

def self_test(core: str, release: str) -> None:
    assert not check(core, release)
    assert any('wait for preflight' in p for p in check(core, release.replace('needs: [preflight, build-test, verify-full-test-shards]', 'needs: build-test', 1)))
    assert any('full coverage missing' in p for p in check(core, release.replace(
        'dotnet test tests/AlTamam.Tests/AlTamam.Tests.csproj', 'dotnet test tests/Unknown.csproj', 1)))
    assert any('four exhaustive shards' in p for p in check(core, release.replace(
        '--filter "$filter"', '--filter "FullyQualifiedName~NoTests"', 1)))
    assert any('positively selected' in p for p in check(core.replace(
        '--filter "FullyQualifiedName~BridgeRbacMatrixTests|', '--filter "FullyQualifiedName~Unrelated|', 1), release))

if __name__ == '__main__':
    core = (ROOT / '.github/workflows/windows-ci.yml').read_text(encoding='utf-8')
    release = (ROOT / '.github/workflows/windows-release.yml').read_text(encoding='utf-8')
    violations = check(core, release)
    if violations:
        raise SystemExit('FAST CI CONTRACT FAILED:\n' + '\n'.join(violations))
    self_test(core, release)
    print('FAST CI CONTRACT PASS: Core smoke + four disjoint Release shards + triple package gates')
