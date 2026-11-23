# Build-time only. The customer app NEVER downloads fonts at runtime.
# Both sources point to the SAME immutable OFL-licensed Google Fonts commit.
# Download may fail transiently (HTTP 503); content verification NEVER weakens.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'fetch-verified-build-asset.ps1')

$commit = 'e7db6ca603fd4862010383ff17ffd6cc4ffac1c0'
$primary = "https://raw.githubusercontent.com/google/fonts/$commit/ofl"
$mirror = "https://cdn.jsdelivr.net/gh/google/fonts@$commit/ofl"
$destination = Join-Path $PSScriptRoot '..\src\AlTamam.WebUI\assets\fonts'
New-Item -ItemType Directory -Force -Path $destination | Out-Null

$fonts = @(
  @{ file='Cairo-Variable.ttf'; relative='cairo/Cairo%5Bslnt%2Cwght%5D.ttf'; blob='25641b1c828857698457bcd9f5a102616b4e8033' },
  @{ file='Tajawal-Regular.ttf'; relative='tajawal/Tajawal-Regular.ttf'; blob='d23d25b8b68db86d5b727d181c2008fd874216eb' },
  @{ file='Tajawal-Bold.ttf'; relative='tajawal/Tajawal-Bold.ttf'; blob='71e1488e90215abcc8aedc541d94a2bcbec574f3' }
)
foreach ($font in $fonts) {
  $target = Join-Path $destination $font.file
  Save-VerifiedBuildAsset -Target $target -Urls @(
    "$primary/$($font.relative)",
    "$mirror/$($font.relative)"
  ) -ExpectedGitBlob $font.blob -MaxAttempts 3 -RetryDelaySeconds 2
}
foreach ($family in @('cairo','tajawal')) {
  $target = Join-Path $destination ("OFL-" + $family + ".txt")
  Save-VerifiedBuildAsset -Target $target -Urls @(
    "$primary/$family/OFL.txt",
    "$mirror/$family/OFL.txt"
  ) -RequiredText 'SIL OPEN FONT LICENSE' -MaxAttempts 3 -RetryDelaySeconds 2
}
Write-Host 'OFFLINE ARABIC FONTS READY: all three pinned Git blobs and both OFL licenses verified'
