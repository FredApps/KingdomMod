param(
    [Parameter(Mandatory=$true)]
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'

$game = $GameDir.Trim().Trim('"').TrimEnd('\')
$support = Join-Path $game '.kingdommod-installer'
$ownsMarker = Join-Path $support 'owns-melonloader'

if (-not (Test-Path $ownsMarker)) {
    Write-Host 'KingdomMod MSI did not install MelonLoader; leaving MelonLoader in place.'
    exit 0
}

$modsDir = Join-Path $game 'Mods'
$foreignMods = @()
if (Test-Path $modsDir) {
    $foreignMods = @(Get-ChildItem -LiteralPath $modsDir -File -Filter '*.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notlike 'KingdomMod*.dll' })
}

if ($foreignMods.Count -gt 0) {
    Write-Host 'Other mod DLLs were found; leaving MelonLoader in place.'
    Remove-Item -LiteralPath $ownsMarker -Force -ErrorAction SilentlyContinue
    exit 0
}

Write-Host 'Removing MelonLoader installed by KingdomMod MSI...'
Remove-Item -LiteralPath (Join-Path $game 'version.dll') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $game 'MelonLoader') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $ownsMarker -Force -ErrorAction SilentlyContinue
Write-Host 'Owned MelonLoader files removed.'
