#!/usr/bin/env python3
"""Negative-path guard: the real recovery founder session is NEVER skipped.

The UI test-only read is given its own bounded timeout under concurrent
GitHub-hosted Windows load, without changing production Bridge defaults,
credentials, authorization, R01 SQLCipher or backup/restore assertions.
"""
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT/"src/AlTamam.DesktopHost/Bridge/Phase04RecoveryUiSelfCheck.cs"
SCRIPT=ROOT/"build/test-webview-ui-e2e.ps1"
WORKFLOW=ROOT/".github/workflows/windows-release.yml"


def check(source: str, driver: str, workflow: str) -> list[str]:
    bad=[]
    if source.count('api.invoke("GetCurrentSession",{}, {timeoutMs: 45000})')!=1:
        bad.append("Test-only founder session verification requires explicit bounded 45s bridge timeout")
    required=(
        'setStage("founder-session-check");',
        'authenticated.data?.username!=="phase04-owner"',
        '!authenticated.data?.sessionNonce',
        'throw Error("real founder session not active before settings navigation")',
        'setStage("configure-recovery-output");',
        'api.invoke("SetOutputDirectory"',
        'PASS R01 real backup restore-preview reset UI and independent encrypted rescue',
    )
    if not all(x in source for x in required):
        bad.append("Real founder authentication, session identity, backup or restore verification removed")
    start='setStage("founder-session-check");'
    request='api.invoke("GetCurrentSession",{}, {timeoutMs: 45000})'
    if start in source and request in source and source.index(start)>source.index(request):
        bad.append("Session verification must run after authenticated login")
    if 'TimeSpan.FromSeconds(115)' not in source:
        bad.append("Native recovery must remain bounded")
    if "Phase13Diagnostics" not in driver or 'throw "UI-E2E FAIL:' not in driver:
        bad.append("Failing E2E runs must retain independent non-success diagnostics")
    if "name: phase13-real-windows-e2e-release" not in workflow or "if-no-files-found: error" not in workflow:
        bad.append("Successful evidence must be mandatory and fail closed")
    if "name: phase13-failed-webview-diagnostics" not in workflow or "if: failure()" not in workflow:
        bad.append("Separate failure diagnostics upload must not be misrepresented as successful evidence")
    return bad


def test_mutations(src: str, driver: str, workflow: str):
    assert not check(src,driver,workflow)
    cases=[
        (src.replace('{timeoutMs: 45000}','{}',1),driver,workflow),
        (src.replace('authenticated.data?.username!=="phase04-owner"','false',1),driver,workflow),
        (src.replace('!authenticated.data?.sessionNonce','false',1),driver,workflow),
        (src.replace('setStage("configure-recovery-output");','',1),driver,workflow),
        (src,driver.replace('Phase13Diagnostics','NoFailureTrace',1),workflow),
        (src,driver,workflow.replace('name: phase13-failed-webview-diagnostics','name: unexpected',1))
    ]
    for a,b,c in cases:
        assert check(a,b,c),"Negative-path mutation silently bypassed required assertion"

if __name__=="__main__":
    a=SRC.read_text(encoding="utf-8")
    b=SCRIPT.read_text(encoding="utf-8")
    c=WORKFLOW.read_text(encoding="utf-8")
    problems=check(a,b,c)
    if problems: raise SystemExit("RECOVERY SESSION READINESS FAIL:\n"+"\n".join(problems))
    test_mutations(a,b,c)
    print("RECOVERY SESSION READINESS PASS: verified founder nonce + identity, bounded UI-only 45s, mandatory failure evidence")
