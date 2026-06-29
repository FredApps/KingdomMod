param(
    [Parameter(Mandatory=$true)]
    [string]$GameDir,

    [string]$DefenderExclusionAccepted,

    [string]$PreviousInstallFolder,

    [int]$MsiUiLevel = 2,

    [string]$DotNetSdkVersion = '8.0.421',

    [int]$LaunchTimeoutSec = 900
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$UnityDependenciesVersion = '6000.0.61'
$UnityDependenciesSha512 = '8AA951234926A3E0471FBF0C951A362DB310C4823867A11C81861C9887A00BCE68312974F01B6B021BA46E68F618BD0390C12FAAD6E3680E61F05D2E085CB421'

$script:CleanupGameDir = $null
$script:CleanupSupportDir = $null
$script:CleanupAddedDefenderExclusion = $false
$script:CleanupInstalledMelonLoader = $false
$script:CleanupCopiedDlls = [System.Collections.Generic.List[string]]::new()
$script:InstallStage = 'starting install'
$script:SetupLaunchNoticeShown = $false
$script:IsUpgradeInstall = -not [string]::IsNullOrWhiteSpace($PreviousInstallFolder)

$logPath = Join-Path ([System.IO.Path]::GetTempPath()) 'KingdomModMsi-BuildAndInstall.log'
try { Start-Transcript -Path $logPath -Append | Out-Null } catch { }
trap {
    Write-Host "KingdomMod MSI install failed during $script:InstallStage: $($_.Exception.Message)"
    if ($script:CleanupSupportDir) { try { Stop-DotnetBuildServers -SupportDir $script:CleanupSupportDir } catch { } }
    if ($script:CleanupGameDir) { try { Stop-GameProcesses -GameDir $script:CleanupGameDir } catch { } }
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
        [Parameter(Mandatory=$true)][string]$Stage,
        [string]$ExpectedSha512
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Write-Host "$Stage (attempt $attempt/3)..."
            Invoke-WebRequest -Uri $Uri -OutFile $OutFile -Headers @{ 'User-Agent' = 'kingdommod-msi-installer' }
            if (-not ((Test-Path -LiteralPath $OutFile) -and ((Get-Item -LiteralPath $OutFile).Length -gt 0))) {
                throw "Downloaded file is missing or empty: $OutFile"
            }
            if ($ExpectedSha512) {
                $actualHash = (Get-FileHash -LiteralPath $OutFile -Algorithm SHA512).Hash
                if ($actualHash -ine $ExpectedSha512) {
                    throw "Downloaded file hash mismatch for $OutFile. Expected $ExpectedSha512, got $actualHash."
                }
            }
            return
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

    $isAdmin = ([System.Security.Principal.WindowsPrincipal]([System.Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($isAdmin) {
        if (Test-DefenderExclusionPresent -GameDir $gamePath) {
            Write-Host "Windows Defender exclusion already present: $gamePath"
            return
        }
        try {
            Add-MpPreference -ExclusionPath $gamePath -ErrorAction Stop
        } catch {
            if (Test-DefenderExclusionPresent -GameDir $gamePath) {
                Write-Host "Windows Defender exclusion already present: $gamePath"
                return
            }
            throw
        }
        Assert-DefenderExclusionPresent -GameDir $gamePath
        $script:CleanupAddedDefenderExclusion = $true
    } else {
        $tmpPs1 = Join-Path ([System.IO.Path]::GetTempPath()) "kingdommod-defender-$([guid]::NewGuid()).ps1"
        $resultPath = Join-Path ([System.IO.Path]::GetTempPath()) "kingdommod-defender-result-$([guid]::NewGuid()).txt"
        $escapedGamePath = $gamePath.Replace("'", "''")
        $escapedResultPath = $resultPath.Replace("'", "''")
        Set-Content -LiteralPath $tmpPs1 -Encoding UTF8 -Value @"
`$ErrorActionPreference = 'Stop'
try {
    `$gamePath = '$escapedGamePath'
    `$resultPath = '$escapedResultPath'
    `$existing = (Get-MpPreference -ErrorAction Stop).ExclusionPath
    `$normalized = @(`$existing | ForEach-Object { `$_.TrimEnd('\') })
    if (`$normalized -contains `$gamePath.TrimEnd('\')) {
        Set-Content -LiteralPath `$resultPath -Value 'OK_ALREADY' -Encoding ASCII
        exit 0
    }
    try {
        Add-MpPreference -ExclusionPath `$gamePath -ErrorAction Stop
    } catch {
        `$existing = (Get-MpPreference -ErrorAction Stop).ExclusionPath
        `$normalized = @(`$existing | ForEach-Object { `$_.TrimEnd('\') })
        if (`$normalized -contains `$gamePath.TrimEnd('\')) {
            Set-Content -LiteralPath `$resultPath -Value 'OK_ALREADY' -Encoding ASCII
            exit 0
        }
        throw
    }
    `$existing = (Get-MpPreference -ErrorAction Stop).ExclusionPath
    `$normalized = @(`$existing | ForEach-Object { `$_.TrimEnd('\') })
    if (`$normalized -notcontains `$gamePath.TrimEnd('\')) {
        throw "Windows Defender did not report the exclusion after adding it: `$gamePath"
    }
    Set-Content -LiteralPath `$resultPath -Value 'OK_ADDED' -Encoding ASCII
} catch {
    Set-Content -LiteralPath '$escapedResultPath' -Value `$_.Exception.Message -Encoding UTF8
    exit 1
}
"@
        try {
            $proc = Start-Process -FilePath 'powershell.exe' `
                -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File', $tmpPs1) `
                -Verb RunAs -Wait -PassThru
            if ($proc.ExitCode -ne 0) {
                $detail = ''
                if (Test-Path -LiteralPath $resultPath) {
                    $detail = (Get-Content -LiteralPath $resultPath -Raw -ErrorAction SilentlyContinue).Trim()
                }
                if (-not $detail) { $detail = "exit code $($proc.ExitCode)" }
                throw "The elevated Defender exclusion command failed: $detail"
            }
            if (-not (Test-Path -LiteralPath $resultPath)) {
                throw 'The elevated Defender exclusion command did not return a verification result.'
            }
            $result = (Get-Content -LiteralPath $resultPath -Raw -ErrorAction Stop).Trim()
            if ($result -ne 'OK_ADDED' -and $result -ne 'OK_ALREADY') {
                throw "The elevated Defender exclusion command did not verify successfully: $result"
            }
            $script:CleanupAddedDefenderExclusion = ($result -eq 'OK_ADDED')
        } finally {
            Remove-Item -LiteralPath $tmpPs1 -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not $isAdmin) {
        Write-Host "Windows Defender exclusion added and verified by elevated helper: $gamePath"
        return
    }

    Write-Host "Windows Defender exclusion added: $gamePath"
}

function Assert-DefenderExclusionPresent {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    $gamePath = (Resolve-Path -LiteralPath $GameDir).Path.TrimEnd('\')
    $existing = (Get-MpPreference -ErrorAction Stop).ExclusionPath
    $normalized = @($existing | ForEach-Object { $_.TrimEnd('\') })
    if ($normalized -notcontains $gamePath) {
        throw "Windows Defender exclusion was not added or could not be verified: $gamePath"
    }
}

function Test-DefenderExclusionPresent {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    try {
        $gamePath = (Resolve-Path -LiteralPath $GameDir).Path.TrimEnd('\')
        $existing = (Get-MpPreference -ErrorAction Stop).ExclusionPath
        $normalized = @($existing | ForEach-Object { $_.TrimEnd('\') })
        return ($normalized -contains $gamePath)
    } catch {
        return $false
    }
}

function Show-DefenderExclusionManualWarning {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][string]$Reason
    )

    $message = @"
KingdomMod could not automatically add or verify the Windows Defender exclusion for:

$GameDir

Reason:
$Reason

The install will continue. If KingdomMod or MelonLoader DLLs are later quarantined, add this folder manually in Windows Security:

Virus & threat protection -> Manage settings -> Exclusions -> Add or remove exclusions -> Add an exclusion -> Folder
"@

    Write-Warning $message
    if ($MsiUiLevel -lt 4) { return }

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [System.Windows.Forms.MessageBox]::Show(
            $message,
            'KingdomMod Defender exclusion',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        ) | Out-Null
    } catch {
        try {
            $shell = New-Object -ComObject WScript.Shell
            $shell.Popup($message, 0, 'KingdomMod Defender exclusion', 48) | Out-Null
        } catch {
            Write-Host "Could not show Defender exclusion warning: $($_.Exception.Message)"
        }
    }
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

    $staleCpp2IlSource = Join-Path $SourceRoot 'build\_tools\Cpp2IL-src'
    if (Test-Path -LiteralPath $staleCpp2IlSource) {
        Write-Host "Removing stale Cpp2IL source cache: $staleCpp2IlSource"
        Remove-Item -LiteralPath $staleCpp2IlSource -Recurse -Force -ErrorAction Stop
    }

    $script = Join-Path $SourceRoot 'tools\install-patched-cpp2il.ps1'
    if (-not (Test-Path $script)) {
        throw "Missing patched Cpp2IL source installer script: $script"
    }

    Write-Host 'Building and installing patched Cpp2IL from source on this machine...'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -GameDir $GameDir
    if ($LASTEXITCODE -ne 0) { throw 'Patched Cpp2IL source build/install failed.' }
}

function Test-PatchedCpp2ILReady {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    $target = Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL'
    if (-not (Test-Path -LiteralPath (Join-Path $target 'Cpp2IL.exe'))) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $target 'Cpp2IL.original.exe'))) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $target 'Plugins\Cpp2IL.Plugin.StrippedCodeRegSupport.dll'))) { return $false }
    return $true
}

function Test-GeneratedInteropReady {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    $gen = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'
    if (-not (Test-Path -LiteralPath $gen)) { return $false }

    if (Test-Path -LiteralPath (Join-Path $gen 'Il2Cppmscorlib.dll')) { return $true }
    if (Test-Path -LiteralPath (Join-Path $gen 'Assembly-CSharp.dll')) { return $true }

    return (@(Get-ChildItem -LiteralPath $gen -File -Filter '*.dll' -ErrorAction SilentlyContinue).Count -ge 10)
}

function Clear-InteropGenerationState {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    foreach ($cache in @(
        (Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'),
        (Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\cpp2il_out'),
        (Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\AssemblyGenerator.cfg'),
        (Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\Config.cfg')
    )) {
        if (Test-Path -LiteralPath $cache) {
            Write-Host "Removing stale interop generation state: $cache"
            Remove-Item -LiteralPath $cache -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Test-UnityDependenciesReady {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    $dir = Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\UnityDependencies'
    if (-not (Test-Path -LiteralPath $dir)) { return $false }
    if (-not (Test-Path -LiteralPath (Join-Path $dir 'UnityEngine.CoreModule.dll'))) { return $false }
    return (@(Get-ChildItem -LiteralPath $dir -File -Filter '*.dll' -ErrorAction SilentlyContinue).Count -ge 50)
}

function Install-PinnedUnityDependencies {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    if (Test-UnityDependenciesReady -GameDir $GameDir) {
        Write-Host 'Pinned UnityDependencies are already present.'
        return $true
    }

    $generatorDir = Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator'
    New-Item -ItemType Directory -Force -Path $generatorDir | Out-Null

    $zipName = "UnityDependencies_$UnityDependenciesVersion.zip"
    $zip = Join-Path $generatorDir $zipName
    $url = "https://github.com/LavaGang/MelonLoader.UnityDependencies/releases/download/$UnityDependenciesVersion/Managed.zip"
    Invoke-DownloadFile -Uri $url -OutFile $zip -Stage "Downloading pinned UnityDependencies: $UnityDependenciesVersion" -ExpectedSha512 $UnityDependenciesSha512

    $target = Join-Path $generatorDir 'UnityDependencies'
    if (Test-Path -LiteralPath $target) {
        Write-Host "Removing stale UnityDependencies folder: $target"
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
    }

    Write-Host "Extracting pinned UnityDependencies: $UnityDependenciesVersion..."
    Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
    if (-not (Test-UnityDependenciesReady -GameDir $GameDir)) {
        throw "Pinned UnityDependencies extraction did not produce a usable dependency folder: $target"
    }

    return $true
}

function Show-SetupLaunchNotice {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    if ($script:SetupLaunchNoticeShown) { return }
    $script:SetupLaunchNoticeShown = $true
    if ($MsiUiLevel -lt 4) { return }

    $message = @"
KingdomMod must briefly start Kingdom Two Crowns to let MelonLoader generate game references.

The installer will close the game automatically when that setup pass is finished.

Please do not close the game while setup is running.
"@

    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [System.Windows.Forms.MessageBox]::Show(
            $message,
            'KingdomMod setup',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information
        ) | Out-Null
    } catch {
        try {
            $shell = New-Object -ComObject WScript.Shell
            $shell.Popup($message, 0, 'KingdomMod setup', 64) | Out-Null
        } catch {
            Write-Host "Could not show setup launch notice: $($_.Exception.Message)"
        }
    }
}

function Invoke-InteropSetupLaunch {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][int]$LaunchTimeoutSec,
        [Parameter(Mandatory=$true)][bool]$OfflineGeneration
    )

    Set-OfflineGeneration -GameDir $GameDir -Enabled $OfflineGeneration

    $exe = Join-Path $GameDir 'KingdomTwoCrowns.exe'
    $log = Join-Path $GameDir 'MelonLoader\Latest.log'
    $mode = if ($OfflineGeneration) { 'offline' } else { 'online' }
    Write-Host "Running controlled Kingdom Two Crowns setup pass to generate IL2CPP references ($mode)..."
    Show-SetupLaunchNotice -GameDir $GameDir

    $oldSteamAppId = $env:SteamAppId
    $oldSteamGameId = $env:SteamGameId
    $env:SteamAppId = '701160'
    $env:SteamGameId = '701160'
    try {
        $proc = Start-Process -FilePath $exe -WindowStyle Minimized -PassThru
    } finally {
        if ($null -eq $oldSteamAppId) { Remove-Item Env:\SteamAppId -ErrorAction SilentlyContinue } else { $env:SteamAppId = $oldSteamAppId }
        if ($null -eq $oldSteamGameId) { Remove-Item Env:\SteamGameId -ErrorAction SilentlyContinue } else { $env:SteamGameId = $oldSteamGameId }
    }

    try {
        $deadline = (Get-Date).AddSeconds($LaunchTimeoutSec)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 2
            if (Test-GeneratedInteropReady -GameDir $GameDir) { return $true }

            if ($log -and (Test-Path -LiteralPath $log)) {
                $tail = (Get-Content -LiteralPath $log -Tail 200 -ErrorAction SilentlyContinue) -join "`n"
                if ($tail -match 'No Support Module Loaded!' -or
                    $tail -match 'Assembly is up to date\. No Generation Needed\.' -or
                    $tail -match 'Loading Mods\.\.\.\s+0 Mods loaded\.') {
                    Write-Host 'MelonLoader setup pass reached a terminal state without usable generated refs; stopping it for a clean retry.'
                    break
                }
            }

            if ($proc -and $proc.HasExited) { break }
        }
    } finally {
        if ($proc -and -not $proc.HasExited) {
            try { $proc.CloseMainWindow() | Out-Null } catch { }
            Start-Sleep -Seconds 2
            if (-not $proc.HasExited) {
                try { $proc.Kill() } catch { }
            }
        }
        Stop-GameProcesses -GameDir $GameDir
    }

    return (Test-GeneratedInteropReady -GameDir $GameDir)
}

function Generate-Refs {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][string]$SourceRoot,
        [Parameter(Mandatory=$true)][int]$LaunchTimeoutSec
    )

    $gen = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'
    $refs = Join-Path $SourceRoot 'refs'

    if (-not (Test-GeneratedInteropReady -GameDir $GameDir)) {
        $offlineReady = $false
        try {
            $offlineReady = Install-PinnedUnityDependencies -GameDir $GameDir
        } catch {
            Write-Host "Could not prepare pinned UnityDependencies for offline generation: $($_.Exception.Message)"
            Write-Host 'Falling back to MelonLoader online dependency resolution for this install.'
        }

        Clear-InteropGenerationState -GameDir $GameDir
        $generated = Invoke-InteropSetupLaunch -GameDir $GameDir -LaunchTimeoutSec $LaunchTimeoutSec -OfflineGeneration $offlineReady

        if (-not $generated) {
            Write-Host 'Setup pass did not produce refs; clearing stale state and retrying once with online dependency resolution.'
            Clear-InteropGenerationState -GameDir $GameDir
            $generated = Invoke-InteropSetupLaunch -GameDir $GameDir -LaunchTimeoutSec $LaunchTimeoutSec -OfflineGeneration $false
        }

        if (-not $generated) {
            throw "Interop assemblies were not generated within $LaunchTimeoutSec seconds. The setup pass was stopped automatically; see $logPath and the MelonLoader Latest.log for details."
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

if ($script:IsUpgradeInstall) {
    Write-Host "Existing KingdomMod install detected at '$PreviousInstallFolder'; skipping Defender exclusion setup."
} else {
    if ($DefenderExclusionAccepted -ne '1') {
        throw 'Windows Defender exclusion consent was not provided. KingdomMod installation cannot continue.'
    }
    $script:InstallStage = 'adding Windows Defender exclusion'
    try {
        Add-DefenderExclusion -GameDir $game
    } catch {
        Show-DefenderExclusionManualWarning -GameDir $game -Reason $_.Exception.Message
    }
}

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

if ($script:IsUpgradeInstall -and (Test-GeneratedInteropReady -GameDir $game)) {
    Write-Host 'Existing IL2CPP references found; skipping patched Cpp2IL setup on upgrade.'
} elseif ($script:IsUpgradeInstall -and (Test-PatchedCpp2ILReady -GameDir $game)) {
    Write-Host 'Patched Cpp2IL already installed; skipping Cpp2IL source download/build on upgrade.'
} else {
    $script:InstallStage = 'building and installing patched Cpp2IL'
    Install-PatchedCpp2IL -GameDir $game -SourceRoot $sourceRoot
}
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
