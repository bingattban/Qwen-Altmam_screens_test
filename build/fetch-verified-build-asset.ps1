# Reusable build-only integrity verifier. NEVER bypass the pinned Git blob
# checksum to work around network failures. No runtime font downloads.
$ErrorActionPreference = 'Stop'

function Assert-VerifiedBuildAsset {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string]$File,
    [string]$ExpectedGitBlob = '',
    [string]$RequiredText = ''
  )
  if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
    throw [System.Security.SecurityException]::new("Required offline asset is missing: $(Split-Path -Leaf $File)")
  }
  if ($ExpectedGitBlob) {
    $actual = (& git hash-object -- $File).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -cne $ExpectedGitBlob) {
      throw [System.Security.SecurityException]::new("Pinned Git blob integrity failure: $(Split-Path -Leaf $File)")
    }
  }
  if ($RequiredText) {
    $text = [System.IO.File]::ReadAllText($File)
    if (-not $text.Contains($RequiredText, [System.StringComparison]::Ordinal)) {
      throw [System.Security.SecurityException]::new("OFL license missing or invalid: $(Split-Path -Leaf $File)")
    }
  }
}

function Save-VerifiedBuildAsset {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)][string]$Target,
    [Parameter(Mandatory)][string[]]$Urls,
    [string]$ExpectedGitBlob = '',
    [string]$RequiredText = '',
    [ValidateRange(1,5)][int]$MaxAttempts = 3,
    [ValidateRange(0,30)][int]$RetryDelaySeconds = 2
  )
  if ($Urls.Count -lt 1 -or (-not $ExpectedGitBlob -and -not $RequiredText)) {
    throw 'Pinned asset requires source URL and an immutable blob or license guard.'
  }
  if ($ExpectedGitBlob -and $ExpectedGitBlob -cnotmatch '^[0-9a-f]{40}$') {
    throw 'Invalid pinned Git blob SHA-1.'
  }
  $parent = Split-Path -Parent $Target
  New-Item -ItemType Directory -Force -Path $parent | Out-Null
  if (Test-Path -LiteralPath $Target) {
    Assert-VerifiedBuildAsset -File $Target -ExpectedGitBlob $ExpectedGitBlob -RequiredText $RequiredText
    Write-Host "CACHED ASSET VERIFIED: $(Split-Path -Leaf $Target)"
    return
  }

  $attempts = 0
  foreach ($url in $Urls) {
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
      $attempts++
      $part = "$Target.$([guid]::NewGuid().ToString('N')).partial"
      try {
        # A 503 or exhausted connection pool must not leave a usable output.
        Invoke-WebRequest -Uri $url -OutFile $part -TimeoutSec 60 -ErrorAction Stop
        # Integrity errors are fatal and must never fall through to mirrors.
        Assert-VerifiedBuildAsset -File $part -ExpectedGitBlob $ExpectedGitBlob -RequiredText $RequiredText
        Move-Item -LiteralPath $part -Destination $Target -ErrorAction Stop
        Write-Host "PINNED ASSET VERIFIED: $(Split-Path -Leaf $Target)"
        return
      }
      catch [System.Security.SecurityException] {
        throw
      }
      catch {
        Write-Warning "Build asset delivery unavailable ($(Split-Path -Leaf $Target)), source attempt $attempt/$MaxAttempts; retrying/fallback without relaxing integrity checks."
        if ($attempt -lt $MaxAttempts -and $RetryDelaySeconds -gt 0) {
          Start-Sleep -Seconds ([Math]::Min(30, $RetryDelaySeconds * [Math]::Pow(2, $attempt - 1)))
        }
      }
      finally {
        Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
      }
    }
  }
  throw "Failed to fetch verified build asset $(Split-Path -Leaf $Target) after $attempts bounded source attempts."
}
