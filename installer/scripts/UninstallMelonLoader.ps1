param(
    [Parameter(Mandatory=$true)]
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'

$logPath = Join-Path ([System.IO.Path]::GetTempPath()) 'KingdomModMsi-Uninstall.log'
try { Start-Transcript -Path $logPath -Append | Out-Null } catch { }
trap {
    Write-Host "KingdomMod MSI uninstall cleanup failed: $($_.Exception.Message)"
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

function Resolve-GameDir {
    param([Parameter(Mandatory=$true)][string]$Path)

    $trimmed = $Path.Trim().Trim('"')
    $resolved = (Resolve-Path -LiteralPath $trimmed).Path
    return $resolved.TrimEnd('\')
}

$game = Resolve-GameDir -Path $GameDir
$support = Join-Path $game '.kingdommod-installer'
$cache = Join-Path $game '.kingdommod-cache'
$ownsMarker = Join-Path $support 'owns-melonloader'
$modsDir = Join-Path $game 'Mods'

function Remove-Tree {
    param([Parameter(Mandatory=$true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        return
    } catch {
        Write-Host "Retrying cleanup for '$Path': $($_.Exception.Message)"
    }

    Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $Path -Force -Recurse -ErrorAction SilentlyContinue

    if (Test-Path -LiteralPath $Path) {
        throw "Failed to remove '$Path'. Close the game and rerun uninstall."
    }
}

function Remove-InstallerBuildCache {
    Remove-Tree -Path (Join-Path $support 'dotnet')
    Remove-Tree -Path (Join-Path $support 'source')
    Remove-Tree -Path $cache
}

if (Test-Path $modsDir) {
    Get-ChildItem -LiteralPath $modsDir -File -Filter 'KingdomMod*.dll' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

Remove-InstallerBuildCache

if (-not (Test-Path $ownsMarker)) {
    Write-Host 'KingdomMod MSI did not install MelonLoader; leaving MelonLoader in place.'
    Remove-Tree -Path $support
    try { Stop-Transcript | Out-Null } catch { }
    exit 0
}

$foreignMods = @()
if (Test-Path $modsDir) {
    $foreignMods = @(Get-ChildItem -LiteralPath $modsDir -File -Filter '*.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike 'KingdomMod*.dll' })
}

if ($foreignMods.Count -gt 0) {
    Write-Host 'Other mod DLLs were found; leaving MelonLoader in place.'
    Remove-Item -LiteralPath $ownsMarker -Force -ErrorAction SilentlyContinue
    Remove-Tree -Path $support
    try { Stop-Transcript | Out-Null } catch { }
    exit 0
}

Write-Host 'Removing MelonLoader installed by KingdomMod MSI...'
Remove-Item -LiteralPath (Join-Path $game 'version.dll') -Force -ErrorAction SilentlyContinue
Remove-Tree -Path (Join-Path $game 'MelonLoader')
Remove-Item -LiteralPath $ownsMarker -Force -ErrorAction SilentlyContinue
Remove-Tree -Path $support
Write-Host 'Owned MelonLoader files removed.'
try { Stop-Transcript | Out-Null } catch { }
