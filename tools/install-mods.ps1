<#
.SYNOPSIS
  Developer deploy script for KingdomMod. Copies the current loader + example
  mods into a Kingdom Two Crowns install, installing MelonLoader if needed.

.DESCRIPTION
  - Auto-detects Kingdom Two Crowns via Steam library folders and the common
    GOG/Epic paths. Prompts for a path if detection fails.
  - Downloads and installs the latest MelonLoader x64 release if the game
    folder doesn't already have one.
  - Copies KingdomMod.Loader.dll, KingdomMod.Api.dll, and every built
    KingdomMod.Examples.*.dll into <game>\Mods.
  - If the DLLs aren't built yet and 'dotnet' is on PATH, runs a Release build.

.PARAMETER GameDir
  Override the auto-detected Kingdom Two Crowns folder.

.PARAMETER SkipExamples
  Install only the loader + API. Skip the example mods.

.PARAMETER NoBuild
  Don't run 'dotnet build' even when DLLs are missing - fail instead.

.PARAMETER NoDefenderExclusion
  Skip adding a Windows Defender exclusion for the game folder. By default
  the installer prompts to add one - this is the upstream MelonLoader-
  recommended remediation for AV false-positives on the generated Il2Cpp
  interop DLLs (see github.com/LavaGang/MelonLoader/issues/692). Self-signing
  is no longer attempted; it never bypassed Smart App Control anyway, and an
  exclusion is a stronger fix for Defender.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\install-mods.ps1

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\install-mods.ps1 -GameDir 'D:\Games\Kingdom Two Crowns'
#>
param(
    [string]$GameDir,
    [switch]$SkipExamples,
    [switch]$NoBuild,
    [switch]$NoDefenderExclusion
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot

# True once MelonLoader has generated the Il2Cpp interop cache (Il2Cppmscorlib.dll
# is the reliable marker). Generating it needs a one-time ONLINE launch so
# MelonLoader can download the matching UnityDependencies zip.
function Test-InteropGenerated {
    param([Parameter(Mandatory)][string]$GameDir)
    return (Test-Path (Join-Path $GameDir 'MelonLoader\Il2CppAssemblies\Il2Cppmscorlib.dll'))
}

# Write MelonLoader's force_offline_generation flag into UserData\Loader.cfg,
# handling every cfg state (key present true/false, [unityengine] present but
# key absent, section absent, or whole file missing pre-first-launch).
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
            return
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

function Find-GameDir {
    $candidates = [System.Collections.Generic.List[string]]::new()

    $steam = $null
    try { $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath } catch {}
    if (-not $steam) {
        try { $steam = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath } catch {}
    }
    if (-not $steam) { $steam = 'C:\Program Files (x86)\Steam' }

    $libs = [System.Collections.Generic.List[string]]::new()
    $libs.Add($steam) | Out-Null
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object {
            $libs.Add(($_.Matches[0].Groups[1].Value -replace '\\\\','\')) | Out-Null
        }
    }
    foreach ($l in $libs) {
        $candidates.Add((Join-Path $l 'steamapps\common\Kingdom Two Crowns')) | Out-Null
    }

    $candidates.Add('C:\GOG Games\Kingdom Two Crowns') | Out-Null
    $candidates.Add('D:\GOG Games\Kingdom Two Crowns') | Out-Null
    if ($env:ProgramFiles)        { $candidates.Add((Join-Path $env:ProgramFiles        'Epic Games\KingdomTwoCrowns')) | Out-Null }
    if (${env:ProgramFiles(x86)}) { $candidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Epic Games\KingdomTwoCrowns')) | Out-Null }

    foreach ($c in $candidates) {
        if ((Test-Path $c) -and (Test-Path (Join-Path $c 'KingdomTwoCrowns.exe'))) {
            return (Get-Item $c).FullName
        }
    }
    return $null
}

function Test-GameDir([string]$dir) {
    if (-not $dir) { return $false }
    if (-not (Test-Path $dir)) { return $false }
    return (Test-Path (Join-Path $dir 'KingdomTwoCrowns.exe'))
}

function Resolve-Dll([string]$relProject, [string]$dllName) {
    $candidates = @(
        (Join-Path $RepoRoot "dist\$dllName"),
        (Join-Path $RepoRoot "$relProject\bin\Release\$dllName"),
        (Join-Path $RepoRoot "$relProject\bin\Debug\$dllName")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return (Get-Item $c).FullName }
    }
    return $null
}

function Get-SmartAppControlState {
    # 0 = Off, 1 = Evaluation, 2 = Enforce. Key may be absent on older Windows.
    try {
        $v = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name VerifiedAndReputablePolicyState -ErrorAction Stop).VerifiedAndReputablePolicyState
        switch ($v) { 0 { 'Off' } 1 { 'Evaluation' } 2 { 'Enforce' } default { "Unknown ($v)" } }
    } catch { 'Off' }
}

Write-Host '== KingdomMod installer ==' -ForegroundColor Cyan

$sac = Get-SmartAppControlState
if ($sac -eq 'Enforce') {
    Write-Host ''
    Write-Warning 'Smart App Control (SAC) is ON in Enforce mode.'
    Write-Warning 'SAC blocks unrecognized DLLs with'
    Write-Warning '  "An Application Control policy has blocked this file" (0x800711C7),'
    Write-Warning 'regardless of signing - only Microsoft-vetted publishers or files with'
    Write-Warning 'positive ISG cloud reputation are allowed.'
    Write-Warning ''
    Write-Warning 'To use KingdomMod you must turn SAC off:'
    Write-Warning '  Settings -> Privacy & security -> Windows Security ->'
    Write-Warning '    App & browser control -> Smart App Control settings -> Off'
    Write-Warning '  WARNING: re-enabling SAC later requires a Windows reinstall.'
    Write-Warning ''
    $ans = Read-Host 'Continue installing anyway? (y/N)'
    if ($ans -notmatch '^(y|yes)$') { throw 'Aborted by user (SAC enforce).' }
}

# ---- 1. Locate the game ---------------------------------------------------

if (-not $GameDir) { $GameDir = Find-GameDir }

while (-not (Test-GameDir $GameDir)) {
    if ($GameDir) {
        Write-Warning "Not a Kingdom Two Crowns install (no KingdomTwoCrowns.exe): $GameDir"
    } else {
        Write-Host 'Could not auto-detect Kingdom Two Crowns.' -ForegroundColor Yellow
    }
    $GameDir = Read-Host 'Enter the path to your Kingdom Two Crowns folder'
    if (-not $GameDir) { throw 'Aborted: no game folder provided.' }
    $GameDir = $GameDir.Trim().Trim('"')
}

$GameDir = (Get-Item $GameDir).FullName
Write-Host "Game folder: $GameDir" -ForegroundColor Green

# ---- 2. MelonLoader -------------------------------------------------------

$mlDir  = Join-Path $GameDir 'MelonLoader'
$verDll = Join-Path $GameDir 'version.dll'

if ((Test-Path $mlDir) -and (Test-Path $verDll)) {
    Write-Host '[1/2] MelonLoader already installed.'
} else {
    Write-Host '[1/2] Installing MelonLoader...'
    $tmp = Join-Path $env:TEMP "kingdommod-ml-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    try {
        $hdr = @{ 'User-Agent' = 'kingdommod-installer' }
        $rel = Invoke-RestMethod 'https://api.github.com/repos/LavaGang/MelonLoader/releases/latest' -Headers $hdr
        $asset = $rel.assets | Where-Object { $_.name -eq 'MelonLoader.x64.zip' } | Select-Object -First 1
        if (-not $asset) { $asset = $rel.assets | Where-Object { $_.name -match 'x64.*\.zip$' } | Select-Object -First 1 }
        if (-not $asset) { throw 'Could not find a MelonLoader x64 zip in the latest release.' }

        $zip = Join-Path $tmp $asset.name
        Write-Host "  Downloading $($asset.name) ($($rel.tag_name))..."
        Invoke-WebRequest $asset.browser_download_url -OutFile $zip -Headers $hdr
        Write-Host '  Extracting into game folder...'
        Expand-Archive -Path $zip -DestinationPath $GameDir -Force
        Write-Host '  MelonLoader installed.' -ForegroundColor Green
    } finally {
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    }
}

# ---- 2b. Disable MelonLoader RemoteAPI lookup -----------------------------
# MelonLoader pings api.melonloader.com on startup to fetch metadata for the
# Il2Cpp assembly generator. KTC isn't in their registry (and the service is
# frequently down - 502/526), so every online launch logs errors and waits on
# timeouts. Forcing offline mode skips the calls entirely.
#
# IMPORTANT: only force offline if the interop cache already exists. Generating
# that cache needs a one-time ONLINE launch (MelonLoader downloads the matching
# UnityDependencies zip then); setting offline before that download has happened
# is the exact trap that makes generation fail with
# "UnityDependencies_<ver>.zip does not Exist!". tools/install.ps1 performs the
# online generation; this deploy-only script must not pre-empt it.

if (Test-InteropGenerated -GameDir $GameDir) {
    Set-OfflineGeneration -GameDir $GameDir -Enabled $true
    Write-Host '  Set force_offline_generation = true (interop cache present; skips RemoteAPI call-home).'
} else {
    Write-Host '  Interop cache not generated yet - leaving online generation enabled.' -ForegroundColor Yellow
    Write-Host '    Run tools/install.ps1 (or launch the game once online) to generate it first.'
}

# ---- 3. KingdomMod DLLs ---------------------------------------------------

$loaderDll = Resolve-Dll 'src\KingdomMod.Loader' 'KingdomMod.Loader.dll'
$apiDll    = Resolve-Dll 'src\KingdomMod.Api'    'KingdomMod.Api.dll'

if ((-not $loaderDll) -or (-not $apiDll)) {
    if ($NoBuild) {
        throw 'KingdomMod DLLs not found and -NoBuild was set. Build the solution first or drop prebuilt DLLs into <repo>\dist.'
    }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "KingdomMod DLLs not found and 'dotnet' is not on PATH. Install .NET SDK 8.0+ or drop prebuilt DLLs into <repo>\dist."
    }
    Write-Host 'KingdomMod DLLs not found - running dotnet build...'
    & dotnet build (Join-Path $RepoRoot 'KingdomMod.sln') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    $loaderDll = Resolve-Dll 'src\KingdomMod.Loader' 'KingdomMod.Loader.dll'
    $apiDll    = Resolve-Dll 'src\KingdomMod.Api'    'KingdomMod.Api.dll'
}

if (-not $loaderDll) { throw 'KingdomMod.Loader.dll still not found after build.' }
if (-not $apiDll)    { throw 'KingdomMod.Api.dll still not found after build.' }

$modsDir = Join-Path $GameDir 'Mods'
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null

Write-Host '[2/2] Copying KingdomMod into Mods\...'
Copy-Item $loaderDll $modsDir -Force
Copy-Item $apiDll    $modsDir -Force
Write-Host "  + $(Split-Path $loaderDll -Leaf)"
Write-Host "  + $(Split-Path $apiDll    -Leaf)"

$exampleCount = 0
if (-not $SkipExamples) {
    $examplesRoot = Join-Path $RepoRoot 'examples'
    if (Test-Path $examplesRoot) {
        Get-ChildItem $examplesRoot -Directory | ForEach-Object {
            $relBin = Join-Path $_.FullName 'bin\Release'
            if (Test-Path $relBin) {
                Get-ChildItem $relBin -Filter 'KingdomMod.Examples.*.dll' -ErrorAction SilentlyContinue | ForEach-Object {
                    Copy-Item $_.FullName $modsDir -Force
                    Write-Host "  + $($_.Name)"
                    $script:exampleCount++
                }
            }
        }
    }
    $dist = Join-Path $RepoRoot 'dist'
    if (Test-Path $dist) {
        Get-ChildItem $dist -Filter 'KingdomMod.Examples.*.dll' -ErrorAction SilentlyContinue | ForEach-Object {
            $target = Join-Path $modsDir $_.Name
            if (-not (Test-Path $target)) {
                Copy-Item $_.FullName $modsDir -Force
                Write-Host "  + $($_.Name) (from dist\)"
                $script:exampleCount++
            }
        }
    }
}

Write-Host ''
Write-Host 'KingdomMod installed.' -ForegroundColor Green
Write-Host "  Game folder : $GameDir"
Write-Host "  Loader + API: 2 files"
if (-not $SkipExamples) { Write-Host "  Example mods: $exampleCount files" }

if (-not $NoDefenderExclusion) {
    # Idempotent: if the folder is already excluded, skip the prompt + elevation
    # entirely. Re-running the installer to redeploy DLLs shouldn't pay the UAC /
    # Add-MpPreference cost (several seconds) every time once it's set.
    $alreadyExcluded = $false
    try {
        $existing = (Get-MpPreference -ErrorAction Stop).ExclusionPath
        if ($existing) {
            $gd = $GameDir.TrimEnd('\')
            $alreadyExcluded = @($existing | ForEach-Object { $_.TrimEnd('\') }) -contains $gd
        }
    } catch { }

    if ($alreadyExcluded) {
        Write-Host ''
        Write-Host "[Defender] Exclusion already present for $GameDir - skipping." -ForegroundColor Green
    } else {
    Write-Host ''
    Write-Host '[Defender] Adding Windows Defender exclusion for the game folder.'
    Write-Host '  This is upstream MelonLoader''s recommended fix for AV false-positives'
    Write-Host '  on the IL2CPP interop DLLs the loader generates on first launch.'
    Write-Host '  Trade-off: Defender stops scanning files under the game folder.'
    $ans = Read-Host '  Add exclusion now? Needs admin elevation. (Y/n)'
    if ($ans -match '^(n|no)$') {
        Write-Host '  Skipped. Re-run with the default to add later.' -ForegroundColor Yellow
    } else {
        $isAdmin = ([System.Security.Principal.WindowsPrincipal]([System.Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
        $cmd = "Add-MpPreference -ExclusionPath '$GameDir'"
        try {
            if ($isAdmin) {
                Invoke-Expression $cmd
                Write-Host '  Exclusion added.' -ForegroundColor Green
            } else {
                $tmpPs1 = Join-Path $env:TEMP "kingdommod-defender-$([guid]::NewGuid()).ps1"
                # Single-quoted heredoc so $GameDir is interpolated at write time, not in the elevated child.
                Set-Content -Path $tmpPs1 -Value $cmd -Encoding UTF8
                $proc = Start-Process -FilePath 'powershell.exe' `
                    -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File', $tmpPs1) `
                    -Verb RunAs -Wait -PassThru -WindowStyle Hidden
                Remove-Item $tmpPs1 -ErrorAction SilentlyContinue
                if ($proc.ExitCode -eq 0) {
                    Write-Host '  Exclusion added.' -ForegroundColor Green
                } else {
                    Write-Warning "  Elevation declined or failed (exit $($proc.ExitCode))."
                    Write-Warning "  Add it manually:  Add-MpPreference -ExclusionPath '$GameDir'"
                }
            }
        } catch {
            Write-Warning "  Failed to add exclusion: $($_.Exception.Message)"
            Write-Warning "  Add it manually from an admin PowerShell:  Add-MpPreference -ExclusionPath '$GameDir'"
        }
    }
    }
}

Write-Host ''
Write-Host 'Launch Kingdom Two Crowns. To confirm load, look for'
Write-Host '  "KingdomMod platform initialised"'
Write-Host "in  $GameDir\MelonLoader\Latest.log"
Write-Host 'or press F1 in-game for the KingdomMod console.'
