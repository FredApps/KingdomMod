<#
.SYNOPSIS
  One-shot setup for KingdomMod. Run this first.

.DESCRIPTION
  Locates your Kingdom Two Crowns install, backs up saves, downloads the
  open-source tools, installs MelonLoader into the game folder, installs
  KingdomMod's patched Cpp2IL, and generates local SDK reference assemblies.

  Nothing here redistributes game content: MelonLoader and Il2CppDumper are
  third-party open-source tools, and the interop/dump are produced from YOUR copy.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/install.ps1
#>
param(
    [string]$GameDir,
    [switch]$SkipDump,
    [switch]$SkipLaunch
)
. "$PSScriptRoot\common.ps1"

$root = Get-RepoRoot
$tools = Join-Path $root 'build\_tools'
New-Item -ItemType Directory -Force -Path $tools | Out-Null

Write-Host "== KingdomMod setup ==" -ForegroundColor Cyan
$game = Find-GameDir -Override $GameDir
Write-Host "Game found: $game"

Write-Host "`n[1/4] Backing up saves..."
Backup-Saves | Out-Null

Write-Host "`n[2/4] Installing MelonLoader and patched Cpp2IL..."
if (-not (Test-Path (Join-Path $game 'MelonLoader'))) {
    $rel = Invoke-RestMethod 'https://api.github.com/repos/LavaGang/MelonLoader/releases/latest' -Headers @{'User-Agent'='kmod'}
    $asset = $rel.assets | Where-Object { $_.name -eq 'MelonLoader.x64.zip' } | Select-Object -First 1
    if (-not $asset) {
        $asset = $rel.assets | Where-Object { $_.name -match 'x64.*\.zip$' } | Select-Object -First 1
    }
    if (-not $asset) {
        throw "Could not find a MelonLoader x64 zip in the latest release."
    }

    $zip = Join-Path $tools $asset.name
    Write-Host "  Downloading $($asset.name) ($($rel.tag_name))..."
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -Headers @{'User-Agent'='kmod'}
    Write-Host "  Extracting into game folder..."
    Expand-Archive -Path $zip -DestinationPath $game -Force
    Write-Host "  MelonLoader installed."
}
else {
    Write-Host "  MelonLoader already present - skipping."
}

Write-Host "  Installing patched Cpp2IL..."
& "$PSScriptRoot\install-patched-cpp2il.ps1" -GameDir $game

if (-not $SkipDump) {
    Write-Host "`n[3/4] Dumping class surface (for docs/api-reference)..."
    $dumperDir = Join-Path $tools 'Il2CppDumper'
    if (-not (Test-Path (Join-Path $dumperDir 'Il2CppDumper.dll'))) {
        $rel = Invoke-RestMethod 'https://api.github.com/repos/Perfare/Il2CppDumper/releases/latest' -Headers @{'User-Agent'='kmod'}
        $asset = $rel.assets | Where-Object { $_.name -match 'net6|net8' } | Select-Object -First 1
        if (-not $asset) {
            throw "Could not find an Il2CppDumper net6/net8 release asset."
        }

        $zip = Join-Path $tools $asset.name
        Invoke-WebRequest $asset.browser_download_url -OutFile $zip -Headers @{'User-Agent'='kmod'}
        Expand-Archive -Path $zip -DestinationPath $dumperDir -Force
    }
    & "$PSScriptRoot\dump-classes.ps1" -GameDir $game
}
else {
    Write-Host "`n[3/4] Skipping dump (-SkipDump)."
}

Write-Host "`n[4/4] Generating SDK references..."
# The one-time generation launch must be ONLINE: MelonLoader downloads the
# matching UnityDependencies zip then. A leftover force_offline_generation=true
# from a previous deploy would block that download (the zip never lands and
# generation fails), so explicitly go online first.
Set-OfflineGeneration -GameDir $game -Enabled $false

$glArgs = @{ GameDir = $game }
if ($SkipLaunch) {
    $glArgs.SkipLaunch = $true
}
& "$PSScriptRoot\generate-sdk.ps1" @glArgs

# Generation done and the interop cache (+ UnityDependencies zip) is now on
# disk, so future launches don't need the network. Go offline to skip the
# flaky RemoteAPI call-home (502/526 spam). Only do this once the cache really
# exists, otherwise we'd re-create the very trap that blocks generation.
if (Test-InteropGenerated -GameDir $game) {
    Set-OfflineGeneration -GameDir $game -Enabled $true
    Write-Host "  Interop cache present - set force_offline_generation = true (skips RemoteAPI call-home)."
} else {
    Write-Host "  Interop cache not detected - left online generation enabled (re-run after a successful launch)." -ForegroundColor Yellow
}

Write-Host "`nDone. Next: dotnet build KingdomMod.sln -c Release" -ForegroundColor Green
