param(
    [Parameter(Mandatory=$true)]
    [string]$GameDir,

    [string]$InstallerRegistryPath = 'HKLM:\Software\KingdomMod',

    [switch]$SkipDefenderExclusionRemoval
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

function Test-Truthy {
    param([object]$Value)

    if ($null -eq $Value) { return $false }
    $normalized = ([string]$Value).Trim().Trim('"')
    return ($normalized -and $normalized -ne '0' -and $normalized -ne '#0' -and $normalized -ine 'false')
}

function Get-RegistryValue {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Name
    )

    try {
        if (-not (Test-Path -LiteralPath $Path)) { return $null }
        $props = Get-ItemProperty -LiteralPath $Path -ErrorAction Stop
        return $props.$Name
    } catch {
        Write-Host "Could not read installer registry value '$Name': $($_.Exception.Message)"
        return $null
    }
}

function Remove-InstallerRegistryState {
    param([Parameter(Mandatory=$true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        Write-Host "Removed KingdomMod installer registry state: $Path"
    } catch {
        Write-Host "Could not remove KingdomMod installer registry state '$Path': $($_.Exception.Message)"
    }
}

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

function Remove-EmptyDirectory {
    param([Parameter(Mandatory=$true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $children = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue)
    if ($children.Count -eq 0) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
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

function Remove-KingdomModDlls {
    param([Parameter(Mandatory=$true)][string]$ModsDir)

    if (-not (Test-Path -LiteralPath $ModsDir)) { return }
    Get-ChildItem -LiteralPath $ModsDir -File -Filter 'KingdomMod*.dll' -ErrorAction SilentlyContinue |
        ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
            Write-Host "Removed KingdomMod DLL: $($_.FullName)"
        }
}

function Remove-InstallerBuildCache {
    param(
        [Parameter(Mandatory=$true)][string]$SupportDir,
        [Parameter(Mandatory=$true)][string]$CacheDir
    )

    Remove-Tree -Path (Join-Path $SupportDir 'dotnet')
    Remove-Tree -Path (Join-Path $SupportDir 'source')
    Remove-Tree -Path $CacheDir
}

function Get-ForeignContent {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    $foreign = [System.Collections.Generic.List[string]]::new()
    foreach ($relative in @('Mods', 'Plugins', 'UserLibs')) {
        $path = Join-Path $GameDir $relative
        if (-not (Test-Path -LiteralPath $path)) { continue }
        Get-ChildItem -LiteralPath $path -Force -ErrorAction SilentlyContinue |
            ForEach-Object {
                $foreign.Add($_.FullName) | Out-Null
            }
    }
    return @($foreign)
}

function Get-OwnedMelonLoaderRoots {
    param([Parameter(Mandatory=$true)][string]$SupportDir)

    $roots = [System.Collections.Generic.List[string]]::new()
    $manifest = Join-Path $SupportDir 'melonloader-owned-roots.txt'
    if (Test-Path -LiteralPath $manifest) {
        Get-Content -LiteralPath $manifest -ErrorAction SilentlyContinue |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object {
                $root = $_.Trim().Trim('\', '/')
                if ($root -and -not [System.IO.Path]::IsPathRooted($root) -and $root -notmatch '(^|[\\/])\.\.([\\/]|$)') {
                    $roots.Add($root.Replace('/', '\')) | Out-Null
                }
            }
    }

    if ($roots.Count -eq 0) {
        $roots.Add('MelonLoader') | Out-Null
        $roots.Add('version.dll') | Out-Null
    }
    return @($roots | Select-Object -Unique)
}

function Remove-OwnedMelonLoader {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][string]$SupportDir
    )

    Write-Host 'Removing MelonLoader installed by KingdomMod MSI...'
    foreach ($root in Get-OwnedMelonLoaderRoots -SupportDir $SupportDir) {
        $path = Join-Path $GameDir $root
        if (Test-Path -LiteralPath $path) {
            Remove-Tree -Path $path
            Write-Host "Removed owned MelonLoader path: $path"
        }
    }
}

function Remove-DefenderExclusionIfOwned {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][bool]$DefenderExclusionAdded
    )

    if (-not $DefenderExclusionAdded) { return }
    if ($SkipDefenderExclusionRemoval) {
        Write-Host 'Skipping Defender exclusion removal for test run.'
        return
    }

    try {
        Remove-MpPreference -ExclusionPath $GameDir -ErrorAction Stop
        Write-Host "Removed Windows Defender exclusion added by KingdomMod: $GameDir"
    } catch {
        Write-Host "Could not remove Windows Defender exclusion automatically: $($_.Exception.Message)"
    }
}

$game = Resolve-GameDir -Path $GameDir
$support = Join-Path $game '.kingdommod-installer'
$cache = Join-Path $game '.kingdommod-cache'
$ownsMarker = Join-Path $support 'owns-melonloader'
$defenderMarker = Join-Path $support 'defender-exclusion-added'
$modsDir = Join-Path $game 'Mods'

$ownsMelonLoader = (Test-Truthy -Value (Get-RegistryValue -Path $InstallerRegistryPath -Name 'OwnsMelonLoader')) -or (Test-Path -LiteralPath $ownsMarker)
$defenderExclusionAdded = (Test-Truthy -Value (Get-RegistryValue -Path $InstallerRegistryPath -Name 'DefenderExclusionAdded')) -or (Test-Path -LiteralPath $defenderMarker)

Stop-GameProcesses -GameDir $game
Remove-KingdomModDlls -ModsDir $modsDir
Remove-InstallerBuildCache -SupportDir $support -CacheDir $cache

$foreignContent = @(Get-ForeignContent -GameDir $game)
if ($ownsMelonLoader -and $foreignContent.Count -eq 0) {
    Remove-OwnedMelonLoader -GameDir $game -SupportDir $support
    Write-Host 'Owned MelonLoader files removed.'
} elseif ($ownsMelonLoader) {
    Write-Host 'Other mod/plugin content was found; leaving MelonLoader in place.'
    $foreignContent | ForEach-Object { Write-Host "  Preserved foreign content: $_" }
} else {
    Write-Host 'KingdomMod MSI did not install MelonLoader; leaving MelonLoader in place.'
}

Remove-EmptyDirectory -Path $modsDir
Remove-DefenderExclusionIfOwned -GameDir $game -DefenderExclusionAdded $defenderExclusionAdded
Remove-Item -LiteralPath $ownsMarker -Force -ErrorAction SilentlyContinue
Remove-Tree -Path $support
Remove-InstallerRegistryState -Path $InstallerRegistryPath
try { Stop-Transcript | Out-Null } catch { }
