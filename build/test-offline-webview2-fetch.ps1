$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'fetch-offline-webview2.ps1')

function Assert([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw "WEBVIEW2 FETCH TEST FAIL: $Message" }
}
function ExpectFailure([scriptblock]$Action) {
    $failed=$false
    try { & $Action } catch { $failed=$true }
    Assert $failed 'expected failure'
}
$script:count=0
$script:mode=''
$script:urls=[System.Collections.Generic.List[string]]::new()
function Invoke-WebRequest {
    [CmdletBinding()]
    param([string]$Uri,[string]$OutFile,[int]$MaximumRedirection,[int]$TimeoutSec)
    $script:count++
    $script:urls.Add($Uri)
    Assert (!(Test-Path -LiteralPath $OutFile)) 'attempt reused a partial file'
    $length=if ($script:mode -eq 'short' -or
        ($script:mode -eq 'retry' -and $script:count -eq 1)) { 100 } else { 50000000 }
    $stream=[IO.File]::Create($OutFile)
    try { $stream.SetLength($length) } finally { $stream.Dispose() }
    if ($script:mode -eq 'down' -or ($script:mode -eq 'retry' -and $script:count -eq 1)) {
        throw 'The response ended prematurely. (ResponseEnded)'
    }
}
function Get-AuthenticodeSignature {
    [CmdletBinding()]
    param([string]$FilePath)
    $status=if ($script:mode -eq 'tamper') { 'HashMismatch' } else { 'Valid' }
    $subject=if ($script:mode -eq 'wrong-publisher') { 'O=Other Corporation' } else { 'CN=Microsoft Corporation, O=Microsoft Corporation' }
    [pscustomobject]@{Status=$status;SignerCertificate=[pscustomobject]@{Subject=$subject}}
}
$dir=Join-Path ([IO.Path]::GetTempPath()) ('altamam-webview2-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $target=Join-Path $dir 'runtime.exe'
    $script:mode='retry'
    Save-OfflineWebView2 -Runtime win-x86 -Target $target -MaxAttempts 3 -RetryDelaySeconds 0
    Assert ($script:count -eq 2) 'premature response was not retried'
    Assert ((Get-Item -LiteralPath $target).Length -eq 50000000) 'partial file was published'
    Assert ($script:urls[0] -eq 'https://go.microsoft.com/fwlink/?linkid=2099617' -and $script:urls[1] -eq $script:urls[0]) 'x86 official source changed'
    Remove-Item -LiteralPath $target
    $script:count=0; $script:urls.Clear(); $script:mode='ok'
    Save-OfflineWebView2 -Runtime win-x64 -Target $target -MaxAttempts 3 -RetryDelaySeconds 0
    Assert ($script:count -eq 1 -and $script:urls[0] -eq 'https://go.microsoft.com/fwlink/?linkid=2124701') 'wrong x64 source'
    Remove-Item -LiteralPath $target
    foreach ($mode in @('down','short','tamper','wrong-publisher')) {
        $script:mode=$mode; $script:count=0
        ExpectFailure { Save-OfflineWebView2 -Runtime win-x86 -Target $target -MaxAttempts 3 -RetryDelaySeconds 0 }
        $expected=if ($mode -in @('down','short')) { 3 } else { 1 }
        Assert ($script:count -eq $expected) "unexpected attempts for $mode"
        Assert (!(Test-Path -LiteralPath $target)) "unverified output for $mode"
        Assert (@(Get-ChildItem -LiteralPath $dir -File).Count -eq 0) "partial output for $mode"
    }
    Write-Host 'OFFLINE WEBVIEW2 FETCH PASS: ResponseEnded, bounded retries, architecture source, signature/publisher, cleanup'
} finally {
    Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
}
