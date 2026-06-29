param(
    [Parameter(Mandatory=$true)]
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'

$game = $GameDir.Trim().Trim('"').TrimEnd('\')
$exe = Join-Path $game 'KingdomTwoCrowns.exe'
if (-not (Test-Path $exe)) {
    throw "The selected folder is not a Kingdom Two Crowns install: $game"
}

$support = Join-Path $game '.kingdommod-installer'
$zip = Join-Path $support 'MelonLoader.x64.zip'
if (-not (Test-Path $zip)) {
    throw "Missing bundled MelonLoader payload: $zip"
}

$mlDir = Join-Path $game 'MelonLoader'
$versionDll = Join-Path $game 'version.dll'
$ownsMarker = Join-Path $support 'owns-melonloader'

if ((Test-Path $mlDir) -or (Test-Path $versionDll)) {
    Write-Host 'MelonLoader already exists; KingdomMod will leave it owned by the user.'
    if (Test-Path $ownsMarker) {
        Remove-Item -LiteralPath $ownsMarker -Force -ErrorAction SilentlyContinue
    }
    exit 0
}

Write-Host 'Installing bundled MelonLoader...'
Expand-Archive -LiteralPath $zip -DestinationPath $game -Force
Set-Content -LiteralPath $ownsMarker -Value "KingdomMod MSI installed MelonLoader on $(Get-Date -Format o)" -Encoding UTF8
Write-Host 'MelonLoader installed by KingdomMod MSI.'
