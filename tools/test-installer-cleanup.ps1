<#
.SYNOPSIS
  Exercises KingdomMod MSI uninstall cleanup against fake game folders.
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$uninstallScript = Join-Path $root 'installer\scripts\UninstallMelonLoader.ps1'
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) "kingdommod-cleanup-tests-$([guid]::NewGuid())"

function New-FakeGame {
    param([Parameter(Mandatory=$true)][string]$Name)

    $game = Join-Path $workRoot $Name
    New-Item -ItemType Directory -Force -Path $game | Out-Null
    Set-Content -LiteralPath (Join-Path $game 'KingdomTwoCrowns.exe') -Value '' -Encoding ASCII
    return $game
}

function Add-SupportPayload {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [switch]$LegacyOwnsMarker
    )

    $support = Join-Path $GameDir '.kingdommod-installer'
    New-Item -ItemType Directory -Force -Path $support | Out-Null
    Set-Content -LiteralPath (Join-Path $support 'melonloader-owned-roots.txt') -Value @('MelonLoader', 'version.dll') -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $support 'UninstallMelonLoader.ps1') -Value '' -Encoding ASCII
    if ($LegacyOwnsMarker) {
        Set-Content -LiteralPath (Join-Path $support 'owns-melonloader') -Value 'legacy owned' -Encoding UTF8
    }
}

function Add-MelonLoader {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    New-Item -ItemType Directory -Force -Path (Join-Path $GameDir 'MelonLoader\net6') | Out-Null
    Set-Content -LiteralPath (Join-Path $GameDir 'MelonLoader\net6\MelonLoader.dll') -Value '' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $GameDir 'version.dll') -Value '' -Encoding ASCII
}

function Add-KingdomModDlls {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    $mods = Join-Path $GameDir 'Mods'
    New-Item -ItemType Directory -Force -Path $mods | Out-Null
    Set-Content -LiteralPath (Join-Path $mods 'KingdomMod.Loader.dll') -Value '' -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $mods 'KingdomMod.Api.dll') -Value '' -Encoding ASCII
}

function Add-InstallerCache {
    param([Parameter(Mandatory=$true)][string]$GameDir)

    New-Item -ItemType Directory -Force -Path (Join-Path $GameDir '.kingdommod-cache\dotnet') | Out-Null
    Set-Content -LiteralPath (Join-Path $GameDir '.kingdommod-cache\dotnet\dotnet.exe') -Value '' -Encoding ASCII
}

function Set-TestRegistry {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$GameDir,
        [bool]$OwnsMelonLoader,
        [bool]$DefenderExclusionAdded
    )

    New-Item -Path $Path -Force | Out-Null
    New-ItemProperty -Path $Path -Name 'InstallFolder' -Value $GameDir -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $Path -Name 'OwnsMelonLoader' -Value ([int]$OwnsMelonLoader) -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $Path -Name 'DefenderExclusionAdded' -Value ([int]$DefenderExclusionAdded) -PropertyType DWord -Force | Out-Null
}

function Invoke-Cleanup {
    param(
        [Parameter(Mandatory=$true)][string]$GameDir,
        [Parameter(Mandatory=$true)][string]$RegistryPath
    )

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uninstallScript `
        -GameDir $GameDir `
        -InstallerRegistryPath $RegistryPath `
        -SkipDefenderExclusionRemoval
    if ($LASTEXITCODE -ne 0) { throw "Uninstall cleanup failed for $GameDir" }
}

function Assert-Exists {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Expected path to exist: $Path" }
}

function Assert-NotExists {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (Test-Path -LiteralPath $Path) { throw "Expected path to be removed: $Path" }
}

function Invoke-Scenario {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][scriptblock]$Arrange,
        [Parameter(Mandatory=$true)][scriptblock]$Assert
    )

    $game = New-FakeGame -Name $Name
    $reg = "HKCU:\Software\KingdomMod-CleanupTest-$Name-$([guid]::NewGuid())"
    try {
        & $Arrange $game $reg
        Invoke-Cleanup -GameDir $game -RegistryPath $reg
        & $Assert $game $reg
        Write-Host "[PASS] $Name"
    } finally {
        Remove-Item -LiteralPath $reg -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

    Invoke-Scenario -Name 'owned-removes-everything-owned' -Arrange {
        param($game, $reg)
        Add-SupportPayload -GameDir $game
        Add-MelonLoader -GameDir $game
        Add-KingdomModDlls -GameDir $game
        Add-InstallerCache -GameDir $game
        Set-TestRegistry -Path $reg -GameDir $game -OwnsMelonLoader $true -DefenderExclusionAdded $true
    } -Assert {
        param($game, $reg)
        Assert-NotExists (Join-Path $game 'MelonLoader')
        Assert-NotExists (Join-Path $game 'version.dll')
        Assert-NotExists (Join-Path $game '.kingdommod-installer')
        Assert-NotExists (Join-Path $game '.kingdommod-cache')
        Assert-NotExists (Join-Path $game 'Mods')
        Assert-NotExists $reg
    }

    Invoke-Scenario -Name 'user-owned-preserves-melonloader' -Arrange {
        param($game, $reg)
        Add-SupportPayload -GameDir $game
        Add-MelonLoader -GameDir $game
        Add-KingdomModDlls -GameDir $game
        Add-InstallerCache -GameDir $game
        Set-TestRegistry -Path $reg -GameDir $game -OwnsMelonLoader $false -DefenderExclusionAdded $false
    } -Assert {
        param($game, $reg)
        Assert-Exists (Join-Path $game 'MelonLoader')
        Assert-Exists (Join-Path $game 'version.dll')
        Assert-NotExists (Join-Path $game '.kingdommod-installer')
        Assert-NotExists (Join-Path $game '.kingdommod-cache')
        Assert-NotExists (Join-Path $game 'Mods')
        Assert-NotExists $reg
    }

    Invoke-Scenario -Name 'owned-preserves-foreign-mod-content' -Arrange {
        param($game, $reg)
        Add-SupportPayload -GameDir $game
        Add-MelonLoader -GameDir $game
        Add-KingdomModDlls -GameDir $game
        Set-Content -LiteralPath (Join-Path $game 'Mods\OtherMod.dll') -Value '' -Encoding ASCII
        New-Item -ItemType Directory -Force -Path (Join-Path $game 'Mods\BalanceTweaks\pack') | Out-Null
        Set-Content -LiteralPath (Join-Path $game 'Mods\BalanceTweaks\pack\kingdommod.pack.json') -Value '{}' -Encoding ASCII
        New-Item -ItemType Directory -Force -Path (Join-Path $game 'Plugins') | Out-Null
        Set-Content -LiteralPath (Join-Path $game 'Plugins\OtherPlugin.dll') -Value '' -Encoding ASCII
        New-Item -ItemType Directory -Force -Path (Join-Path $game 'UserLibs') | Out-Null
        Set-Content -LiteralPath (Join-Path $game 'UserLibs\OtherLibrary.dll') -Value '' -Encoding ASCII
        Set-TestRegistry -Path $reg -GameDir $game -OwnsMelonLoader $true -DefenderExclusionAdded $false
    } -Assert {
        param($game, $reg)
        Assert-Exists (Join-Path $game 'MelonLoader')
        Assert-Exists (Join-Path $game 'version.dll')
        Assert-Exists (Join-Path $game 'Mods\OtherMod.dll')
        Assert-Exists (Join-Path $game 'Mods\BalanceTweaks\pack\kingdommod.pack.json')
        Assert-Exists (Join-Path $game 'Plugins\OtherPlugin.dll')
        Assert-Exists (Join-Path $game 'UserLibs\OtherLibrary.dll')
        Assert-NotExists (Join-Path $game 'Mods\KingdomMod.Loader.dll')
        Assert-NotExists (Join-Path $game '.kingdommod-installer')
        Assert-NotExists $reg
    }

    Invoke-Scenario -Name 'legacy-marker-migrates-ownership' -Arrange {
        param($game, $reg)
        Add-SupportPayload -GameDir $game -LegacyOwnsMarker
        Add-MelonLoader -GameDir $game
        Add-KingdomModDlls -GameDir $game
    } -Assert {
        param($game, $reg)
        Assert-NotExists (Join-Path $game 'MelonLoader')
        Assert-NotExists (Join-Path $game 'version.dll')
        Assert-NotExists (Join-Path $game '.kingdommod-installer')
    }

    Invoke-Scenario -Name 'missing-support-and-cache-is-ok' -Arrange {
        param($game, $reg)
        Add-KingdomModDlls -GameDir $game
        Set-TestRegistry -Path $reg -GameDir $game -OwnsMelonLoader $false -DefenderExclusionAdded $false
    } -Assert {
        param($game, $reg)
        Assert-NotExists (Join-Path $game 'Mods')
        Assert-NotExists (Join-Path $game '.kingdommod-installer')
        Assert-NotExists (Join-Path $game '.kingdommod-cache')
        Assert-NotExists $reg
    }
} finally {
    Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Installer cleanup harness passed.'
