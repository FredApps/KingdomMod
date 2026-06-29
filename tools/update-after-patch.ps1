<#
.SYNOPSIS
  Re-runs SDK generation after a game update.

  When Kingdom Two Crowns ships a new patch, every IL2CPP method offset shifts
  and the interop DLLs we compile against drift away from the live binary.  This
  script (a) clears MelonLoader's interop cache and (b) regenerates everything
  so mods built against name-based Harmony patches keep working.
#>
param([string]$GameDir)
. "$PSScriptRoot\common.ps1"

$root  = Get-RepoRoot
$game  = Find-GameDir -Override $GameDir
$mlDep = Join-Path $game 'MelonLoader\Dependencies\Il2CppAssemblyGenerator'

Write-Host "Clearing interop cache..."
foreach ($d in @(
        (Join-Path $game 'MelonLoader\Il2CppAssemblies'),
        (Join-Path $mlDep 'Cpp2IL\cpp2il_out'),
        (Join-Path $mlDep 'Il2CppInteropAssemblies'),
        (Join-Path $root 'docs\_generated\dump'),
        (Join-Path $root 'refs')
    )) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force; Write-Host "  removed $d" }
}
Remove-Item (Join-Path $mlDep 'AssemblyGenerator.cfg') -Force -ErrorAction SilentlyContinue

Write-Host "Installing patched Cpp2IL..."
& "$PSScriptRoot\install-patched-cpp2il.ps1" -GameDir $game

Write-Host "Re-dumping class surface..."
& "$PSScriptRoot\dump-classes.ps1" -GameDir $game -Force

Write-Host "Re-generating SDK references..."
& "$PSScriptRoot\generate-sdk.ps1" -GameDir $game

Write-Host "`nDone. Rebuild mods with: dotnet build KingdomMod.sln -c Release" -ForegroundColor Green
