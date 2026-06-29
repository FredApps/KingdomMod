param(
    [Parameter(Mandatory=$true)]
    [string]$GameDir,

    [string]$DefenderExclusionAccepted,

    [string]$DotNetSdkVersion = '8.0.421',

    [int]$LaunchTimeoutSec = 900
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:CleanupGameDir = $null
$script:CleanupSupportDir = $null
$script:CleanupAddedDefenderExclusion = $false
$script:CleanupInstalledMelonLoader = $false
$script:CleanupCopiedDlls = [System.Collections.Generic.List[string]]::new()
$script:InstallStage = 'starting install'

$logPath = Join-Path ([System.IO.Path]::GetTempPath()) 'KingdomModMsi-BuildAndInstall.log'
try { Start-Transcript -Path $logPath -Append | Out-Null } catch { }
trap {
    Write-Host "KingdomMod MSI install failed during $script:InstallStage: $($_.Exception.Message)"
    if ($script:CleanupSupportDir) { try { Stop-DotnetBuildServers -SupportDir $script:CleanupSupportDir } catch { } }
    try { Invoke-InstallRollback } catch { Write-Host "KingdomMod rollback failed: $($_.Exception.Message)" }
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

function Invoke-InstallRollback {
    Write-Host 'Rolling back KingdomMod install changes...'

    foreach ($dll in @($script:CleanupCopiedDlls)) {
        if ($dll -and (Test-Path -LiteralPath $dll)) {
            Remove-Item -LiteralPath $dll -Force -ErrorAction SilentlyContinue
            Write-Host "  Removed copied DLL: $dll"
        }
    }

    if ($script:CleanupInstalledMelonLoader -and $script:CleanupGameDir) {
        foreach ($path in @(
            (Join-Path $script:CleanupGameDir 'MelonLoader'),
            (Join-Path $script:CleanupGameDir 'version.dll'),
            (Join-Path $script:CleanupGameDir 'dobby.dll'),
            (Join-Path $script:CleanupGameDir 'NOTICE.txt')
        )) {
            if (Test-Path -LiteralPath $path) {
                Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "  Removed installer-owned MelonLoader path: $path"
            }
        }
    }

    if ($script:CleanupSupportDir -and (Test-Path -LiteralPath $script:CleanupSupportDir)) {
        Remove-Item -LiteralPath $script:CleanupSupportDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  Removed installer support folder: $script:CleanupSupportDir"
    }

    if ($script:CleanupAddedDefenderExclusion -and $script:CleanupGameDir) {
        try {
            Remove-MpPreference -ExclusionPath $script:CleanupGameDir -ErrorAction Stop
            Write-Host "  Removed Windows Defender exclusion: $script:CleanupGameDir"
        } catch {
            Write-Host "  Could not remove Windows Defender exclusion automatically: $($_.Exception.Message)"
        }
    }
}

function Resolve-GameDir {
    param([Parameter(Mandatory=$true)][string]$Path)

    $trimmed = $Path.Trim().Trim('"')
    $resolved = (Resolve-Path -LiteralPath $trimmed).Path
    return $resolved.TrimEnd('\')
}

function Resolve-Dotnet {
    param(
        [Parameter(Mandatory=$true)][string]$SupportDir,
        [switch]$AllowMissing
    )

    $candidates = [System.Collections.Generic.List[string]]::new()
    $candidates.Add((Join-Path $SupportDir 'dotnet\dotnet.exe'))
    if ($env:DOTNET_ROOT) { $candidates.Add((Join-Path $env:DOTNET_ROOT 'dotnet.exe')) }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates.Add($cmd.Source) }
    $candidates.Add('C:\Program Files\dotnet\dotnet.exe')

    foreach ($candidate in $candidates) {
        if (-not $candidate -or -not (Test-Path $candidate)) { continue }
        try {
            $sdks = & $candidate --list-sdks 2>$null
            if ($sdks | Where-Object { $_ -match '\S' }) { return $candidate }
        } catch { }
    }

    if ($AllowMissing) { return $null }
    throw 'No .NET SDK found. The setup-time SDK could not be used; rerun the MSI or check the KingdomMod installer log.'
}

function Invoke-DownloadFile {
    param(
        [Parameter(Mandatory=$true)][string]$Uri,
        [Parameter(Mandatory=$true)][string]$OutFile,
        [Parameter(Mandatory=$true)][string]$Stage
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Write-Host "$Stage (attempt $attempt/3)..."
            Invoke-WebRequest -Uri $Uri -OutFile $OutFile -Headers @{ 'User-Agent' = 'kingdommod-msi-installer' }
            if ((Test-Path -LiteralPath $OutFile) -and ((Get-Item -LiteralPath $OutFile).Length -gt 0)) { return }
            throw "Downloaded file is missing or empty: $OutFile"
        } catch {
            $lastError = $_.Exception.Message
            Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds ([Math]::Min(10, $attempt * 2))
        }
    }

    throw "$Stage failed after 3 attempts: $lastError"
}

function Install-SetupDotnetSdk {
    param(
        [Parameter(Mandatory=$true)][string]$SupportDir,
        [Parameter(Mandatory=$true)][string]$DotNetSdkVersion
    )

    $dotnetExe = Join-Path $SupportDir 'dotnet\dotnet.exe'
    if (Test-Path $dotnetExe) { return }

    $assetName = "dotnet-sdk-$DotNetSdkVersion-win-x64.zip"
    $sdkZip = Join-Path $SupportDir $assetName
    if (-not (Test-Path $sdkZip)) {
        $downloadUrl = "https://builds.dotnet.microsoft.com/dotnet/Sdk/$DotNetSdkVersion/$assetName"
        Invoke-DownloadFile -Uri $downloadUrl -OutFile $sdkZip -Stage "Downloading pinned setup-time .NET SDK: $assetName"
    }

    Write-Host "Extracting setup-time .NET SDK: $assetName..."
    $target = Join-Path $SupportDir 'dotnet'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Expand-Archive -LiteralPath $sdkZip -DestinationPath $target -Force
}

function Enable-BundledDotnetSdk {
    param([Parameter(Mandatory=$true)][string]$SupportDir)

    $dotnetRoot = Join-Path $SupportDir 'dotnet'
    $dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
    if (-not (Test-Path $dotnetExe)) {
        throw "Setup-time .NET SDK was not extracted to '$dotnetRoot'."
    }

    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot;$env:PATH"
}

function Stop-DotnetBuildServers {
    param([Parameter(Mandatory=$true)][string]$SupportDir)

    try {
        $dotnet = Resolve-Dotnet -SupportDir $SupportDir -AllowMissing
        if ($dotnet) {
            Write-Host 'Stopping setup-time .NET build servers...'
            & $dotnet build-server shutdown | Out-Host
        }
    } catch {
        Write-Host "Could not stop .NET build servers: $($_.Exception.Message)"
    }
}

function Stop-GameProcesses {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    $exe = Join-Path $GameDir 'KingdomTwoCrowns.exe'
    Get-Process -Name KingdomTwoCrowns -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ($_.Path -ieq $exe) } |
        ForEach-Object {
            try {
                $_.CloseMainWindow() | Out-Null
                Start-Sleep -Seconds 2
                if (-not $_.HasExited) { $_.Kill() }
            } catch {
                Write-Host "Could not stop Kingdom Two Crowns process $($_.Id): $($_.Exception.Message)"
            }
        }
}

function Add-DefenderExclusion {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    $gamePath = (Resolve-Path -LiteralPath $GameDir).Path.TrimEnd('\')

    try {
        $existing = (Get-MpPreference -ErrorAction Stop).ExclusionPath
        if ($existing) {
            $normalized = @($existing | ForEach-Object { $_.TrimEnd('\') })
            if ($normalized -contains $gamePath) {
                Write-Host "Windows Defender exclusion already present: $gamePath"
                return
            }
        }
    } catch {
        Write-Host "Could not read current Windows Defender exclusions: $($_.Exception.Message)"
    }

    Write-Host "Adding required Windows Defender exclusion: $gamePath"
    Write-Host 'KingdomMod builds unsigned mod DLLs locally. Without this exclusion, Defender can quarantine them before MelonLoader can run them.'

    $escapedGamePath = $gamePath.Replace("'", "''")
    $command = "Add-MpPreference -ExclusionPath '$escapedGamePath'"
    $isAdmin = ([System.Security.Principal.WindowsPrincipal]([System.Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($isAdmin) {
        Invoke-Expression $command
        $script:CleanupAddedDefenderExclusion = $true
    } else {
        $tmpPs1 = Join-Path ([System.IO.Path]::GetTempPath()) "kingdommod-defender-$([guid]::NewGuid()).ps1"
        Set-Content -LiteralPath $tmpPs1 -Value $command -Encoding UTF8
        try {
            $proc = Start-Process -FilePath 'powershell.exe' `
                -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File', $tmpPs1) `
                -Verb RunAs -Wait -PassThru
            if ($proc.ExitCode -ne 0) {
                throw "The elevated Defender exclusion command exited with code $($proc.ExitCode)."
            }
            $script:CleanupAddedDefenderExclusion = $true
        } finally {
            Remove-Item -LiteralPath $tmpPs1 -Force -ErrorAction SilentlyContinue
        }
    }

    try {
        $existing = (Get-MpPreference -ErrorAction Stop).ExclusionPath
        $normalized = @($existing | ForEach-Object { $_.TrimEnd('\') })
        if ($normalized -contains $gamePath) {
            Write-Host "Windows Defender exclusion added: $gamePath"
            return
        }
    } catch { }

    throw 'Windows Defender exclusion was not added. KingdomMod installation cannot continue without it.'
}

function Set-OfflineGeneration {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][bool]$Enabled
    )

    $value = if ($Enabled) { 'true' } else { 'false' }
    $userDataDir = Join-Path $GameDir 'UserData'
    $loaderCfg = Join-Path $userDataDir 'Loader.cfg'
    New-Item -ItemType Directory -Force -Path $userDataDir | Out-Null

    if (Test-Path $loaderCfg) {
        $cfg = Get-Content $loaderCfg -Raw
        if ($cfg -match '(?m)^\s*force_offline_generation\s*=') {
            $cfg = [regex]::Replace($cfg, '(?m)^(\s*force_offline_generation\s*=\s*).*$', "`${1}$value")
        } elseif ($cfg -match '(?m)^\[unityengine\]') {
            $cfg = [regex]::Replace($cfg, '(?m)^\[unityengine\]\s*$', "[unityengine]`r`nforce_offline_generation = $value", 1)
        } else {
            $cfg = $cfg.TrimEnd() + "`r`n`r`n[unityengine]`r`nforce_offline_generation = $value`r`n"
        }
        Set-Content -LiteralPath $loaderCfg -Value $cfg -Encoding UTF8 -NoNewline
    } else {
        Set-Content -LiteralPath $loaderCfg -Value "[unityengine]`r`nforce_offline_generation = $value`r`n" -Encoding UTF8 -NoNewline
    }
}

function Install-PatchedCpp2IL {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][string]$SourceRoot
    )

    $script = Join-Path $SourceRoot 'tools\install-patched-cpp2il.ps1'
    if (-not (Test-Path $script)) {
        throw "Missing patched Cpp2IL source installer script: $script"
    }

    Write-Host 'Building and installing patched Cpp2IL from source on this machine...'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -GameDir $GameDir
    if ($LASTEXITCODE -ne 0) { throw 'Patched Cpp2IL source build/install failed.' }
}

function Generate-Refs {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][string]$SourceRoot,
        [Parameter(Mandatory=$true)][int]$LaunchTimeoutSec
    )

    $gen = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'
    $refs = Join-Path $SourceRoot 'refs'
    $marker = Join-Path $gen 'Il2Cppmscorlib.dll'

    Set-OfflineGeneration -GameDir $GameDir -Enabled $false

    if (-not (Test-Path $marker)) {
        $exe = Join-Path $GameDir 'KingdomTwoCrowns.exe'
        Write-Host 'Running Kingdom Two Crowns setup pass to generate IL2CPP references...'
        $oldSteamAppId = $env:SteamAppId
        $oldSteamGameId = $env:SteamGameId
        $env:SteamAppId = '701160'
        $env:SteamGameId = '701160'
        try {
            $proc = Start-Process -FilePath $exe -PassThru
        } finally {
            if ($null -eq $oldSteamAppId) { Remove-Item Env:\SteamAppId -ErrorAction SilentlyContinue } else { $env:SteamAppId = $oldSteamAppId }
            if ($null -eq $oldSteamGameId) { Remove-Item Env:\SteamGameId -ErrorAction SilentlyContinue } else { $env:SteamGameId = $oldSteamGameId }
        }
        $deadline = (Get-Date).AddSeconds($LaunchTimeoutSec)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 3
            if (Test-Path $marker) { break }
        }

        Start-Sleep -Seconds 5
        if ($proc -and -not $proc.HasExited) {
            $proc.CloseMainWindow() | Out-Null
            Start-Sleep -Seconds 2
            if (-not $proc.HasExited) { $proc.Kill() }
        }

        if (-not (Test-Path $marker)) {
            throw "Interop assemblies were not generated within $LaunchTimeoutSec seconds. Launch the game once manually, reach the main menu, then rerun the MSI."
        }
    }

    New-Item -ItemType Directory -Force -Path $refs | Out-Null
    $patterns = @(
        (Join-Path $gen '*.dll'),
        (Join-Path $GameDir 'MelonLoader\net6\MelonLoader.dll'),
        (Join-Path $GameDir 'MelonLoader\net6\Il2CppInterop.Runtime.dll'),
        (Join-Path $GameDir 'MelonLoader\net6\Il2CppInterop.Common.dll'),
        (Join-Path $GameDir 'MelonLoader\net6\0Harmony.dll')
    )

    foreach ($pattern in $patterns) {
        Get-ChildItem $pattern -ErrorAction SilentlyContinue |
            Copy-Item -Destination $refs -Force
    }

    if (-not (Test-Path (Join-Path $refs 'Il2Cppmscorlib.dll'))) {
        throw 'Failed to collect generated references into the installer source tree.'
    }

    Set-OfflineGeneration -GameDir $GameDir -Enabled $true
}

$game = Resolve-GameDir -Path $GameDir
$script:CleanupGameDir = $game
$exe = Join-Path $game 'KingdomTwoCrowns.exe'
if (-not (Test-Path $exe)) {
    throw "The selected folder is not a Kingdom Two Crowns install: $game"
}

if ($DefenderExclusionAccepted -ne '1') {
    throw 'Windows Defender exclusion consent was not provided. KingdomMod installation cannot continue.'
}
$script:InstallStage = 'adding Windows Defender exclusion'
Add-DefenderExclusion -GameDir $game

$script:InstallStage = 'validating MSI support payload'
$support = Join-Path $game '.kingdommod-installer'
$script:CleanupSupportDir = $support
$sourceRoot = Join-Path $support 'source'
$zip = Join-Path $support 'MelonLoader.x64.zip'
if (-not (Test-Path $zip)) {
    throw "Missing bundled MelonLoader payload: $zip"
}
if (-not (Test-Path (Join-Path $sourceRoot 'KingdomMod.sln'))) {
    throw "Missing bundled KingdomMod source payload: $sourceRoot"
}

$script:InstallStage = 'resolving .NET SDK'
$dotnet = Resolve-Dotnet -SupportDir $support -AllowMissing
if (-not $dotnet) {
    $script:InstallStage = 'downloading setup-time .NET SDK'
    Install-SetupDotnetSdk -SupportDir $support -DotNetSdkVersion $DotNetSdkVersion
    Enable-BundledDotnetSdk -SupportDir $support
} elseif (Test-Path (Join-Path $support 'dotnet\dotnet.exe')) {
    Enable-BundledDotnetSdk -SupportDir $support
}

$mlDir = Join-Path $game 'MelonLoader'
$versionDll = Join-Path $game 'version.dll'
$ownsMarker = Join-Path $support 'owns-melonloader'

if ((Test-Path $mlDir) -or (Test-Path $versionDll)) {
    Write-Host 'MelonLoader already exists; KingdomMod will leave it owned by the user.'
    Remove-Item -LiteralPath $ownsMarker -Force -ErrorAction SilentlyContinue
} else {
    Write-Host 'Installing bundled MelonLoader...'
    Expand-Archive -LiteralPath $zip -DestinationPath $game -Force
    $script:CleanupInstalledMelonLoader = $true
    Set-Content -LiteralPath $ownsMarker -Value "KingdomMod MSI installed MelonLoader on $(Get-Date -Format o)" -Encoding UTF8
}

$script:InstallStage = 'building and installing patched Cpp2IL'
Install-PatchedCpp2IL -GameDir $game -SourceRoot $sourceRoot
$script:InstallStage = 'generating IL2CPP references'
Generate-Refs -GameDir $game -SourceRoot $sourceRoot -LaunchTimeoutSec $LaunchTimeoutSec

$script:InstallStage = 'building KingdomMod DLLs'
$dotnet = Resolve-Dotnet -SupportDir $support
Write-Host 'Building KingdomMod DLLs on this machine...'
& $dotnet build (Join-Path $sourceRoot 'KingdomMod.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'KingdomMod build failed.' }

$script:InstallStage = 'copying KingdomMod DLLs'
$modsDir = Join-Path $game 'Mods'
New-Item -ItemType Directory -Force -Path $modsDir | Out-Null

$dlls = @()
$dlls += Join-Path $sourceRoot 'src\KingdomMod.Loader\bin\Release\KingdomMod.Loader.dll'
$dlls += Join-Path $sourceRoot 'src\KingdomMod.Api\bin\Release\KingdomMod.Api.dll'
$dlls += Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'examples') -Directory |
    ForEach-Object {
        Get-ChildItem -LiteralPath (Join-Path $_.FullName 'bin\Release') -File -Filter 'KingdomMod.Examples.*.dll' -ErrorAction SilentlyContinue
    } |
    ForEach-Object { $_.FullName }

foreach ($dll in $dlls) {
    if (-not (Test-Path $dll)) { throw "Expected build output missing: $dll" }
    $targetDll = Join-Path $modsDir (Split-Path $dll -Leaf)
    Copy-Item -LiteralPath $dll -Destination $targetDll -Force
    $script:CleanupCopiedDlls.Add($targetDll) | Out-Null
}

Write-Host "KingdomMod built and installed. DLLs copied: $($dlls.Count)"
Stop-DotnetBuildServers -SupportDir $support
$script:CleanupCopiedDlls.Clear()
$script:CleanupInstalledMelonLoader = $false
$script:CleanupAddedDefenderExclusion = $false
$script:CleanupSupportDir = $null
try { Stop-Transcript | Out-Null } catch { }
