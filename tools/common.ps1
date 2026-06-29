# Shared helpers for KingdomMod tooling. Dot-source this: . "$PSScriptRoot\common.ps1"

$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

# Try hard to locate a Kingdom Two Crowns install (Steam libraries, common paths).
function Find-GameDir {
    param([string]$Override)
    if ($Override) {
        if (Test-Path (Join-Path $Override 'GameAssembly.dll')) { return (Resolve-Path $Override).Path }
        throw "No GameAssembly.dll under -GameDir '$Override'."
    }

    $candidates = New-Object System.Collections.Generic.List[string]

    # Steam: read libraryfolders.vdf for every library, then the known app folder name.
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = 'C:\Program Files (x86)\Steam' }
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    $libs = @($steam)
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s*"([^"]+)"')) {
            $libs += ($m.Groups[1].Value -replace '\\\\', '\')
        }
    }
    foreach ($lib in $libs | Select-Object -Unique) {
        $candidates.Add((Join-Path $lib 'steamapps\common\Kingdom Two Crowns'))
    }
    # Other storefronts (best-effort).
    $candidates.Add('C:\Program Files\Epic Games\KingdomTwoCrowns')
    $candidates.Add('C:\GOG Games\Kingdom Two Crowns')

    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'GameAssembly.dll'))) { return (Resolve-Path $c).Path }
    }
    throw "Could not locate Kingdom Two Crowns. Pass -GameDir 'C:\path\to\Kingdom Two Crowns'."
}

function Get-DataDir {
    param([string]$GameDir)
    return (Join-Path $GameDir 'KingdomTwoCrowns_Data')
}

# Back up local saves before any modded launch. KTC saves live under LocalLow.
function Backup-Saves {
    param([string]$Label = (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $save = Join-Path $env:USERPROFILE 'AppData\LocalLow\noio\KingdomTwoCrowns'
    if (-not (Test-Path $save)) { Write-Host "  (no save folder found at $save - skipping backup)"; return $null }
    $dest = Join-Path (Get-RepoRoot) "build\save-backups\$Label"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -Path (Join-Path $save '*') -Destination $dest -Recurse -Force
    Write-Host "  Saves backed up to $dest"
    return $dest
}

# True once MelonLoader has generated the Il2Cpp interop cache for this game
# (the marker assembly Il2Cppmscorlib.dll is the last reliable thing produced).
# Generating that cache requires a one-time ONLINE launch so MelonLoader can
# download the matching UnityDependencies zip; only after it exists is it safe
# to force offline generation.
function Test-InteropGenerated {
    param([Parameter(Mandatory)][string]$GameDir)
    return (Test-Path (Join-Path $GameDir 'MelonLoader\Il2CppAssemblies\Il2Cppmscorlib.dll'))
}

# Set MelonLoader's force_offline_generation flag in UserData\Loader.cfg.
# $Enabled=$false lets the generator reach the network (to fetch UnityDependencies
# and run); $Enabled=$true skips the flaky RemoteAPI call-home on every launch.
# Handles every cfg state: key=true/false present, [unityengine] section present
# but key absent, section absent, or the whole file missing (pre-first-launch).
function Set-OfflineGeneration {
    param(
        [Parameter(Mandatory)][string]$GameDir,
        [Parameter(Mandatory)][bool]$Enabled
    )
    $val = if ($Enabled) { 'true' } else { 'false' }
    $userDataDir = Join-Path $GameDir 'UserData'
    $loaderCfg   = Join-Path $userDataDir 'Loader.cfg'
    New-Item -ItemType Directory -Force -Path $userDataDir | Out-Null

    if (Test-Path $loaderCfg) {
        $cfg = Get-Content $loaderCfg -Raw
        if ($cfg -match "(?m)^\s*force_offline_generation\s*=\s*$val\s*$") {
            return  # already at desired value
        } elseif ($cfg -match '(?m)^\s*force_offline_generation\s*=') {
            $cfg = [regex]::Replace($cfg, '(?m)^(\s*force_offline_generation\s*=\s*).*$', "`${1}$val")
        } elseif ($cfg -match '(?m)^\[unityengine\]') {
            $cfg = [regex]::Replace($cfg, '(?m)^\[unityengine\]\s*$', "[unityengine]`r`nforce_offline_generation = $val", 1)
        } else {
            $cfg = $cfg.TrimEnd() + "`r`n`r`n[unityengine]`r`nforce_offline_generation = $val`r`n"
        }
        Set-Content -Path $loaderCfg -Value $cfg -Encoding UTF8 -NoNewline
    } else {
        Set-Content -Path $loaderCfg -Value "[unityengine]`r`nforce_offline_generation = $val`r`n" -Encoding UTF8 -NoNewline
    }
}

function Test-DotnetHasSdk {
    param([string]$Exe)
    if (-not $Exe -or -not (Test-Path $Exe)) { return $false }
    try { return [bool]((& $Exe --list-sdks 2>$null) | Where-Object { $_ -match '\S' }) }
    catch { return $false }
}

# Locate a usable dotnet. Build steps (dotnet build) need an SDK, not just a
# runtime - a runtime-only install (e.g. C:\Program Files\dotnet with only
# shared frameworks) will fail. So prefer the first candidate that actually has
# an SDK; fall back to any dotnet that exists for runtime-only uses (dotnet <dll>).
function Resolve-Dotnet {
    param([switch]$RequireSdk)
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($env:DOTNET_ROOT) { $candidates.Add((Join-Path $env:DOTNET_ROOT 'dotnet.exe')) }
    # Repo-local toolchain: a sibling '.tools\dotnet' next to the repo root.
    $candidates.Add((Join-Path (Split-Path (Get-RepoRoot) -Parent) '.tools\dotnet\dotnet.exe'))
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates.Add($cmd.Source) }
    $candidates.Add('C:\Program Files\dotnet\dotnet.exe')

    foreach ($c in $candidates) { if (Test-DotnetHasSdk $c) { return $c } }
    if ($RequireSdk) {
        throw "No .NET SDK found (checked DOTNET_ROOT, .tools\dotnet, PATH, Program Files). Install from https://dotnet.microsoft.com/download"
    }
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    throw "No .NET runtime or SDK found. Install from https://dotnet.microsoft.com/download"
}
