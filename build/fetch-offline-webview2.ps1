# Build-time only. Never publish incomplete or unverified runtime installers.
function Save-OfflineWebView2 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('win-x86','win-x64')][string]$Runtime,
        [Parameter(Mandatory)][string]$Target,
        [ValidateRange(1,5)][int]$MaxAttempts=4,
        [ValidateRange(0,30)][int]$RetryDelaySeconds=3
    )
    $ErrorActionPreference='Stop'
    $uri=if ($Runtime -eq 'win-x86') {
        'https://go.microsoft.com/fwlink/?linkid=2099617'
    } else {
        'https://go.microsoft.com/fwlink/?linkid=2124701'
    }
    $Target=[IO.Path]::GetFullPath($Target)
    $dir=[IO.Path]::GetDirectoryName($Target)
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    if (Test-Path -LiteralPath $Target) {
        throw 'Unexpected existing WebView2 installer; refusing to overwrite it.'
    }
    for ($attempt=1; $attempt -le $MaxAttempts; $attempt++) {
        $partial=Join-Path $dir ('.webview2-'+[guid]::NewGuid().ToString('N')+'.partial.exe')
        try {
            try {
                Invoke-WebRequest -Uri $uri -OutFile $partial -MaximumRedirection 10 -TimeoutSec 300
                if (!(Test-Path -LiteralPath $partial -PathType Leaf) -or
                    (Get-Item -LiteralPath $partial).Length -lt 50000000) {
                    throw 'Offline WebView2 installer is missing or unexpectedly small'
                }
            } catch {
                if ($attempt -eq $MaxAttempts) { throw }
                Write-Warning "Microsoft WebView2 $Runtime download attempt $attempt failed: $($_.Exception.Message)"
                if ($RetryDelaySeconds -gt 0) {
                    Start-Sleep -Seconds ([Math]::Min(30,$RetryDelaySeconds * [Math]::Pow(2,$attempt-1)))
                }
                continue
            }
            # Authenticity failures are never retried or bypassed.
            $sig=Get-AuthenticodeSignature -FilePath $partial
            if ($sig.Status -ne 'Valid' -or
                $null -eq $sig.SignerCertificate -or
                $sig.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
                throw "WebView2 Microsoft Authenticode verification failed: $($sig.Status)"
            }
            $digest=(Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash
            # Same-volume rename, no overwrite; only verified content becomes output.
            [IO.File]::Move($partial,$Target)
            Write-Host "MICROSOFT OFFLINE WEBVIEW2: verified $Runtime SHA256=$digest"
            return
        } finally {
            if (Test-Path -LiteralPath $partial) {
                Remove-Item -LiteralPath $partial -Force
            }
        }
    }
    throw 'Offline WebView2 download exhausted all attempts.'
}
