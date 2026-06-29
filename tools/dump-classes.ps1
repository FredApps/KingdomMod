<#
.SYNOPSIS
  Runs Il2CppDumper against YOUR Kingdom Two Crowns install to produce a
  human-readable class-surface dump (dump.cs) and DummyDll proxy assemblies.

  The output is game-derived (it describes the game's code), so it is written
  under docs/_generated/ which is .gitignored and never redistributed.

.NOTES
  Il2CppDumper is the net6 build; we run it with roll-forward so a newer .NET
  runtime (e.g. 8) can host it without requiring the .NET 6 runtime specifically.
#>
param(
    [string]$GameDir,
    [switch]$Force
)
. "$PSScriptRoot\common.ps1"

$root    = Get-RepoRoot
$game    = Find-GameDir -Override $GameDir
$data    = Get-DataDir -GameDir $game
$asm     = Join-Path $game 'GameAssembly.dll'
$meta    = Join-Path $data 'il2cpp_data\Metadata\global-metadata.dat'
$outDir  = Join-Path $root 'docs\_generated\dump'
$dumper  = Join-Path $root 'build\_tools\Il2CppDumper\Il2CppDumper.dll'

if (-not (Test-Path $asm))  { throw "Missing $asm" }
if (-not (Test-Path $meta)) { throw "Missing $meta" }
if (-not (Test-Path $dumper)) {
    throw "Il2CppDumper not found at $dumper. Run tools/install.ps1 first (it downloads tools)."
}
if ((Test-Path (Join-Path $outDir 'dump.cs')) -and -not $Force) {
    Write-Host "Dump already exists at $outDir (use -Force to regenerate)."; return
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dotnet = Resolve-Dotnet

Write-Host "Dumping class surface (this reads the game, never modifies it)..."
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
& $dotnet $dumper $asm $meta $outDir
if ($LASTEXITCODE -ne 0) { throw "Il2CppDumper exited with $LASTEXITCODE" }

Write-Host ""
Write-Host "Dump written to $outDir"
Write-Host "  dump.cs        - full class/method/field surface (search this!)"
Write-Host "  DummyDll/      - proxy assemblies (reference for exploration only)"
