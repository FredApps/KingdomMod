<#
.SYNOPSIS
  Generates the local reference assemblies that KingdomMod mods compile against.

  These come from MelonLoader's Il2CppInterop output, produced when the game is
  launched once after MelonLoader is installed. They are game-derived, so they are
  copied into refs/ (which is .gitignored) and must never be redistributed.

.DESCRIPTION
  1. Ensures MelonLoader is installed in the game folder (see install.ps1).
  2. Launches the game once so MelonLoader emits Il2Cpp*.dll proxies, then exits.
  3. Copies MelonLoader + Il2CppInterop + UnityEngine + the game interop into refs/.
#>
param(
    [string]$GameDir,
    [int]$LaunchTimeoutSec = 900,
    [switch]$SkipLaunch
)
. "$PSScriptRoot\common.ps1"

$root = Get-RepoRoot
$game = Find-GameDir -Override $GameDir
$ml   = Join-Path $game 'MelonLoader'
$gen  = Join-Path $game 'MelonLoader\Il2CppAssemblies'
$refs = Join-Path $root 'refs'

if (-not (Test-Path $ml)) {
    throw "MelonLoader is not installed in '$game'. Run tools/install.ps1 first."
}

# --- 1. Launch once to generate Il2Cpp interop assemblies ---------------------
if (-not $SkipLaunch -and -not (Test-Path (Join-Path $gen 'Il2Cppmscorlib.dll'))) {
    Write-Host "Launching game once so MelonLoader can generate interop assemblies..."
    Write-Host "  (A game/console window will open; it is closed automatically once ready.)"
    $exe = Join-Path $game 'KingdomTwoCrowns.exe'
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = (Get-Date).AddSeconds($LaunchTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        if (Test-Path (Join-Path $gen 'Il2Cppmscorlib.dll')) { break }
    }
    Start-Sleep -Seconds 5  # let the last assemblies flush
    if ($proc -and -not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep 2; if (-not $proc.HasExited) { $proc.Kill() } }
    if (-not (Test-Path (Join-Path $gen 'Il2Cppmscorlib.dll'))) {
        throw "Interop assemblies were not generated within $LaunchTimeoutSec s. Launch the game once manually, reach the main menu, then re-run with -SkipLaunch."
    }
}

# --- 2. Collect references into refs/ -----------------------------------------
New-Item -ItemType Directory -Force -Path $refs | Out-Null
$sources = @(
    (Join-Path $gen '*.dll'),                                  # Il2Cpp* + UnityEngine*
    (Join-Path $ml 'net6\MelonLoader.dll'),
    (Join-Path $ml 'net6\Il2CppInterop.Runtime.dll'),
    (Join-Path $ml 'net6\Il2CppInterop.Common.dll'),
    (Join-Path $ml 'net6\0Harmony.dll')
)
$count = 0
foreach ($s in $sources) {
    foreach ($f in (Get-ChildItem $s -ErrorAction SilentlyContinue)) {
        Copy-Item $f.FullName -Destination $refs -Force; $count++
    }
}
Write-Host ""
Write-Host "Copied $count reference assemblies into $refs"
Write-Host "These are game-derived and .gitignored. You can now: dotnet build -c Release"
