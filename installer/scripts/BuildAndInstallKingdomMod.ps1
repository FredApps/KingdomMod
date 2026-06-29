param(
    [Parameter(Mandatory=$true)]
    [string]$GameDir,

    [int]$LaunchTimeoutSec = 900
)

$ErrorActionPreference = 'Stop'

$logPath = Join-Path ([System.IO.Path]::GetTempPath()) 'KingdomModMsi-BuildAndInstall.log'
try { Start-Transcript -Path $logPath -Append | Out-Null } catch { }
trap {
    Write-Host "KingdomMod MSI install failed: $($_.Exception.Message)"
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

function Resolve-GameDir {
    param([Parameter(Mandatory=$true)][string]$Path)

    $trimmed = $Path.Trim().Trim('"')
    $resolved = (Resolve-Path -LiteralPath $trimmed).Path
    return $resolved.TrimEnd('\')
}

function Resolve-Dotnet {
    param([Parameter(Mandatory=$true)][string]$SupportDir)

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

    throw 'No .NET SDK found. The bundled SDK could not be used; rerun the MSI or check the KingdomMod installer log.'
}

function Install-BundledDotnetSdk {
    param([Parameter(Mandatory=$true)][string]$SupportDir)

    $dotnetExe = Join-Path $SupportDir 'dotnet\dotnet.exe'
    if (Test-Path $dotnetExe) { return }

    $sdkZip = Get-ChildItem -LiteralPath $SupportDir -File -Filter 'dotnet-sdk-*-win-x64.zip' -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -First 1
    if (-not $sdkZip) {
        throw "Bundled .NET SDK payload is missing from '$SupportDir'."
    }

    Write-Host "Extracting bundled .NET SDK: $($sdkZip.Name)..."
    $target = Join-Path $SupportDir 'dotnet'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Expand-Archive -LiteralPath $sdkZip.FullName -DestinationPath $target -Force
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
        [Parameter(Mandatory=$true)][string]$SupportDir
    )

    $source = Join-Path $SupportDir 'patched-cpp2il\Cpp2IL'
    $plugin = Join-Path $SupportDir 'patched-cpp2il\Plugins'
    if (-not (Test-Path (Join-Path $source 'Cpp2IL.exe'))) {
        throw "Bundled patched Cpp2IL is missing at '$source'."
    }

    $target = Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL'
    New-Item -ItemType Directory -Force -Path $target | Out-Null

    $existing = Join-Path $target 'Cpp2IL.exe'
    if ((Test-Path $existing) -and -not (Test-Path (Join-Path $target 'Cpp2IL.original.exe'))) {
        Copy-Item -LiteralPath $existing -Destination (Join-Path $target 'Cpp2IL.original.exe') -Force
    }

    Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
    if (Test-Path $plugin) {
        New-Item -ItemType Directory -Force -Path (Join-Path $target 'Plugins') | Out-Null
        Copy-Item -Path (Join-Path $plugin '*') -Destination (Join-Path $target 'Plugins') -Recurse -Force
    }

    foreach ($cache in @(
        (Join-Path $target 'cpp2il_out'),
        (Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'),
        (Join-Path $GameDir 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\AssemblyGenerator.cfg'))) {
        if (Test-Path $cache) { Remove-Item -LiteralPath $cache -Recurse -Force -ErrorAction SilentlyContinue }
    }
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
        Write-Host 'Launching Kingdom Two Crowns once to generate IL2CPP references...'
        $proc = Start-Process -FilePath $exe -PassThru
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
$exe = Join-Path $game 'KingdomTwoCrowns.exe'
if (-not (Test-Path $exe)) {
    throw "The selected folder is not a Kingdom Two Crowns install: $game"
}

$support = Join-Path $game '.kingdommod-installer'
$sourceRoot = Join-Path $support 'source'
$zip = Join-Path $support 'MelonLoader.x64.zip'
if (-not (Test-Path $zip)) {
    throw "Missing bundled MelonLoader payload: $zip"
}
if (-not (Test-Path (Join-Path $sourceRoot 'KingdomMod.sln'))) {
    throw "Missing bundled KingdomMod source payload: $sourceRoot"
}
Install-BundledDotnetSdk -SupportDir $support

$mlDir = Join-Path $game 'MelonLoader'
$versionDll = Join-Path $game 'version.dll'
$ownsMarker = Join-Path $support 'owns-melonloader'

if ((Test-Path $mlDir) -or (Test-Path $versionDll)) {
    Write-Host 'MelonLoader already exists; KingdomMod will leave it owned by the user.'
    Remove-Item -LiteralPath $ownsMarker -Force -ErrorAction SilentlyContinue
} else {
    Write-Host 'Installing bundled MelonLoader...'
    Expand-Archive -LiteralPath $zip -DestinationPath $game -Force
    Set-Content -LiteralPath $ownsMarker -Value "KingdomMod MSI installed MelonLoader on $(Get-Date -Format o)" -Encoding UTF8
}

Install-PatchedCpp2IL -GameDir $game -SupportDir $support
Generate-Refs -GameDir $game -SourceRoot $sourceRoot -LaunchTimeoutSec $LaunchTimeoutSec

$dotnet = Resolve-Dotnet -SupportDir $support
Write-Host 'Building KingdomMod DLLs on this machine...'
& $dotnet build (Join-Path $sourceRoot 'KingdomMod.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'KingdomMod build failed.' }

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
    Copy-Item -LiteralPath $dll -Destination $modsDir -Force
}

Write-Host "KingdomMod built and installed. DLLs copied: $($dlls.Count)"
try { Stop-Transcript | Out-Null } catch { }
