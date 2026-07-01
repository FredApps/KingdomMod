<#
.SYNOPSIS
  Launch the local KingdomMod mount asset designer.

.DESCRIPTION
  Creates a private workspace under build\asset-designer, installs pinned
  Python design/extraction dependencies into build\_tools\asset-designer-venv,
  extracts private local game references on first run when possible, and opens
  a browser UI for editing/exporting custom mount sprites.
#>
param(
    [string]$GameDir,
    [int]$Port = 8787,
    [switch]$NoOpen,
    [switch]$NoExtract,
    [switch]$SkipDependencyInstall
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\common.ps1"

function Resolve-Python {
    $cmd = Get-Command python -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($py) { return $py.Source }
    throw "Python 3 was not found on PATH."
}

function Ensure-DesignerVenv {
    param([string]$Python)
    $venv = Join-Path $RepoRoot 'build\_tools\asset-designer-venv'
    $venvPython = Join-Path $venv 'Scripts\python.exe'
    if (-not (Test-Path $venvPython)) {
        Write-Host "Creating asset-designer Python environment..."
        & $Python -m venv $venv
        if ($LASTEXITCODE -ne 0) { throw "Failed to create Python venv." }
    }
    if (-not $SkipDependencyInstall) {
        Write-Host "Installing pinned asset-designer dependencies..."
        & $venvPython -m pip install --disable-pip-version-check --upgrade pip | Out-Host
        & $venvPython -m pip install --disable-pip-version-check "Pillow==12.2.0" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Failed to install Pillow, which is required by the asset designer." }
        & $venvPython -m pip install --disable-pip-version-check "UnityPy==1.25.0" | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "UnityPy install failed. The designer will still run, but private game reference extraction will be unavailable."
        }
    }
    return $venvPython
}

$python = Resolve-Python
$designerPython = Ensure-DesignerVenv -Python $python

$workspace = Join-Path $RepoRoot 'build\asset-designer'
New-Item -ItemType Directory -Force -Path $workspace | Out-Null

$extractFlag = @()
if (-not $NoExtract) {
    if (-not $GameDir) {
        try { $GameDir = Find-GameDir } catch { Write-Warning $_.Exception.Message }
    }
    if ($GameDir) {
        Write-Host "Game references will be extracted privately from: $GameDir"
        $extractFlag = @('--extract')
    } else {
        Write-Warning "Game folder not found; designer will start with generated examples only."
    }
}

$openFlag = if ($NoOpen) { @('--no-open') } else { @() }
$gameArg = if ($GameDir) { @('--game-dir', $GameDir) } else { @() }
& $designerPython (Join-Path $RepoRoot 'tools\asset-designer\server.py') --port $Port @gameArg @openFlag @extractFlag
