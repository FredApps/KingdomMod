<#
.SYNOPSIS
  Builds KingdomMod and refreshes the committable dist/ release payload.

.DESCRIPTION
  GitHub Actions cannot generate the game-derived refs/ assemblies required to
  compile KingdomMod. Run this locally after tools/install.ps1 has generated
  refs/. The script copies only KingdomMod-built DLLs into dist/.
#>
param(
    [switch]$SkipBuild
)

. "$PSScriptRoot\common.ps1"

$root = Get-RepoRoot
$dotnet = Resolve-Dotnet -RequireSdk
$dist = Join-Path $root 'dist'

if (-not (Test-Path (Join-Path $root 'refs'))) {
    throw 'refs/ is missing. Run tools/install.ps1 before preparing a release payload.'
}

if (-not $SkipBuild) {
    Write-Host 'Building KingdomMod release payload...'
    & $dotnet build (Join-Path $root 'KingdomMod.sln') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Get-ChildItem -LiteralPath $dist -File -Filter 'KingdomMod*.dll' -ErrorAction SilentlyContinue |
    Remove-Item -Force

$files = @()
$files += Join-Path $root 'src\KingdomMod.Loader\bin\Release\KingdomMod.Loader.dll'
$files += Join-Path $root 'src\KingdomMod.Api\bin\Release\KingdomMod.Api.dll'
$files += Get-ChildItem -LiteralPath (Join-Path $root 'examples') -Directory |
    ForEach-Object {
        Get-ChildItem -LiteralPath (Join-Path $_.FullName 'bin\Release') -File -Filter 'KingdomMod.Examples.*.dll' -ErrorAction SilentlyContinue
    } |
    ForEach-Object { $_.FullName }

foreach ($file in $files) {
    if (-not (Test-Path $file)) { throw "Missing release DLL: $file" }
    Copy-Item -LiteralPath $file -Destination $dist -Force
}

$bad = Get-ChildItem -LiteralPath $dist -File -Filter '*.dll' |
    Where-Object {
        $_.Name -notlike 'KingdomMod.Loader.dll' -and
        $_.Name -notlike 'KingdomMod.Api.dll' -and
        $_.Name -notlike 'KingdomMod.Examples.*.dll'
    }
if ($bad) {
    throw "dist/ contains non-KingdomMod DLLs: $($bad.Name -join ', ')"
}

Write-Host "Release payload refreshed in $dist"
Get-ChildItem -LiteralPath $dist -File -Filter 'KingdomMod*.dll' |
    Sort-Object Name |
    ForEach-Object { Write-Host "  + $($_.Name)" }
