# Tests FIRST. No network needed: simulate HTTP 503, mirror success and
# tampered content while keeping the actual git-blob security verifier.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'fetch-verified-build-asset.ps1')

function Assert([bool]$Condition, [string]$Message) {
  if (-not $Condition) { throw "FONT FETCH TEST FAIL: $Message" }
}
function ExpectFailure([scriptblock]$Action, [string]$Description) {
  $failed = $false
  try { & $Action } catch { $failed = $true }
  Assert $failed $Description
}
$script:requests = [System.Collections.Generic.List[string]]::new()
$script:mode = ''
$script:counter = 0
function Invoke-WebRequest {
  [CmdletBinding()]
  param([string]$Uri, [string]$OutFile, [int]$TimeoutSec)
  $script:requests.Add($Uri)
  $script:counter++
  if ($script:mode -eq 'retry' -and $script:counter -eq 1) {
    throw 'HTTP 503 Backend.max_conn reached'
  }
  if ($script:mode -eq 'fallback' -and $Uri.Contains('primary')) {
    throw 'HTTP 503 Backend.max_conn reached'
  }
  if ($script:mode -eq 'down') { throw 'HTTP 503 Backend.max_conn reached' }
  $contents = if ($script:mode -eq 'tamper') { 'TAMPERED' } else { 'VERIFIED-ASSET' }
  if ($script:mode -eq 'license') { $contents = 'SIL OPEN FONT LICENSE Version 1.1' }
  [System.IO.File]::WriteAllText($OutFile, $contents)
}
$directory = Join-Path ([System.IO.Path]::GetTempPath()) (
  'altamam-pinned-font-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $directory | Out-Null
try {
  $fixture = Join-Path $directory 'source.bin'
  [System.IO.File]::WriteAllText($fixture, 'VERIFIED-ASSET')
  $sha = (& git hash-object -- $fixture).Trim()
  Assert ($LASTEXITCODE -eq 0 -and $sha.Length -eq 40) 'real git blob hash unavailable'
  Remove-Item $fixture -Force
  $urls = @('https://primary.invalid/pinned', 'https://mirror.invalid/pinned')

  $script:mode = 'retry'; $script:counter = 0; $script:requests.Clear()
  $target = Join-Path $directory 'retry.ttf'
  Save-VerifiedBuildAsset -Target $target -Urls $urls -ExpectedGitBlob $sha -MaxAttempts 2 -RetryDelaySeconds 0
  Assert (Test-Path $target) 'retry did not save expected font'
  Assert ($script:counter -eq 2) 'must retry primary after transient 503'
  Assert ($script:requests[0] -eq $urls[0] -and $script:requests[1] -eq $urls[0]) 'unexpected retry URL'
  Assert ((& git hash-object -- $target).Trim() -eq $sha) 'retry saved unverified bytes'

  $script:mode = 'fallback'; $script:counter = 0; $script:requests.Clear()
  $mirrorTarget = Join-Path $directory 'mirror.ttf'
  Save-VerifiedBuildAsset -Target $mirrorTarget -Urls $urls -ExpectedGitBlob $sha -MaxAttempts 2 -RetryDelaySeconds 0
  Assert ($script:counter -eq 3) 'must attempt mirror only after primary attempts exhausted'
  Assert ($script:requests[2] -eq $urls[1]) 'no independent fallback host'
  Assert ((& git hash-object -- $mirrorTarget).Trim() -eq $sha) 'fallback did not verify original blob'

  $script:mode = 'tamper'; $script:counter = 0; $script:requests.Clear()
  $tamper = Join-Path $directory 'tamper.ttf'
  ExpectFailure {
    Save-VerifiedBuildAsset -Target $tamper -Urls $urls -ExpectedGitBlob $sha -MaxAttempts 2 -RetryDelaySeconds 0
  } 'tampered mirror download must fail closed'
  Assert (!(Test-Path $tamper)) 'tampered asset must not become customer output'
  Assert ($script:counter -eq 1) 'hash mismatch must never silently try to trust another source'

  $script:mode = 'down'; $script:counter = 0; $script:requests.Clear()
  $absent = Join-Path $directory 'absent.ttf'
  ExpectFailure {
    Save-VerifiedBuildAsset -Target $absent -Urls $urls -ExpectedGitBlob $sha -MaxAttempts 2 -RetryDelaySeconds 0
  } 'all-sources-down condition must fail visibly'
  Assert ($script:counter -eq 4) 'retries and fallback must both be bounded'
  Assert (!(Test-Path $absent)) 'no empty file when both endpoints down'

  $script:counter = 0; $script:requests.Clear()
  Save-VerifiedBuildAsset -Target $target -Urls $urls -ExpectedGitBlob $sha -MaxAttempts 2 -RetryDelaySeconds 0
  Assert ($script:counter -eq 0) 'valid cached font must work offline without network'

  [System.IO.File]::WriteAllText($target, 'CHANGED')
  ExpectFailure {
    Save-VerifiedBuildAsset -Target $target -Urls $urls -ExpectedGitBlob $sha -MaxAttempts 2 -RetryDelaySeconds 0
  } 'corrupt preexisting font must fail closed without downloading'
  Assert ($script:counter -eq 0) 'corrupt cached file must not be quietly replaced'

  $script:mode = 'license'; $script:requests.Clear()
  $license = Join-Path $directory 'OFL.txt'
  Save-VerifiedBuildAsset -Target $license -Urls $urls -RequiredText 'SIL OPEN FONT LICENSE' -MaxAttempts 1 -RetryDelaySeconds 0
  Assert ([IO.File]::ReadAllText($license).Contains('SIL OPEN FONT LICENSE')) 'OFL must be present'

  $partials = @(Get-ChildItem -LiteralPath $directory -File -Filter '*.partial')
  Assert ($partials.Count -eq 0) 'failed attempts left partial customer files'
  Write-Host 'OFFLINE FONT FETCH PASS: HTTP 503 retry + alternate host + real Git blob + fail-closed tamper/empty/offline cache/license'
} finally {
  Remove-Item -LiteralPath $directory -Recurse -Force -ErrorAction SilentlyContinue
}
