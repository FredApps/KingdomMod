<#
.SYNOPSIS
  Installs KingdomMod's patched Cpp2IL into MelonLoader's Il2CppAssemblyGenerator.

.DESCRIPTION
  Kingdom Two Crowns 2.4.0 contains stripped property metadata that crashes the
  Cpp2IL version pulled by MelonLoader during interop generation.  The patched
  Cpp2IL source/build under build/_tools/Cpp2IL-src skips those broken property
  entries so Il2CppInterop can emit usable reference assemblies.
#>
param([string]$GameDir)
. "$PSScriptRoot\common.ps1"

$root = Get-RepoRoot
$game = Find-GameDir -Override $GameDir
$srcRoot = Join-Path $root 'build\_tools\Cpp2IL-src'
$source = Join-Path $srcRoot 'Cpp2IL\bin\Release\net8.0'
$pluginSource = Join-Path $srcRoot 'Cpp2IL.Plugin.StrippedCodeRegSupport\bin\Release\net8.0'
$target = Join-Path $game 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL'

if (-not (Test-Path (Join-Path $source 'Cpp2IL.exe'))) {
    $dotnet = Resolve-Dotnet -RequireSdk
    $project = Join-Path $srcRoot 'Cpp2IL\Cpp2IL.csproj'
    if (-not (Test-Path $project)) {
        throw "Patched Cpp2IL source is missing at '$project'. Restore build/_tools/Cpp2IL-src before installing."
    }

    Write-Host "Building patched Cpp2IL (net8.0)..."
    # -f net8.0 is required: the source also lists net472 which we don't build.
    & $dotnet build $project -c Release -f net8.0 --nologo
    if ($LASTEXITCODE -ne 0) { throw "Patched Cpp2IL build failed." }

    $pluginProj = Join-Path $srcRoot 'Cpp2IL.Plugin.StrippedCodeRegSupport\Cpp2IL.Plugin.StrippedCodeRegSupport.csproj'
    if (Test-Path $pluginProj) {
        Write-Host "Building StrippedCodeRegSupport plugin..."
        & $dotnet build $pluginProj -c Release -f net8.0 --nologo
        if ($LASTEXITCODE -ne 0) { throw "Plugin build failed." }
    }
}

if (-not (Test-Path (Join-Path $source 'Cpp2IL.exe'))) {
    throw "Patched Cpp2IL build did not produce '$source\Cpp2IL.exe'."
}

New-Item -ItemType Directory -Force -Path $target | Out-Null

# Preserve the bundled stock Cpp2IL.exe so the patched install can be reverted.
$existing = Join-Path $target 'Cpp2IL.exe'
if ((Test-Path $existing) -and -not (Test-Path (Join-Path $target 'Cpp2IL.original.exe'))) {
    Copy-Item $existing (Join-Path $target 'Cpp2IL.original.exe') -Force
}

Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
if (Test-Path (Join-Path $pluginSource 'Cpp2IL.Plugin.StrippedCodeRegSupport.dll')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $target 'Plugins') | Out-Null
    Copy-Item -Path (Join-Path $pluginSource 'Cpp2IL.Plugin.StrippedCodeRegSupport.dll') `
              -Destination (Join-Path $target 'Plugins') -Force
}

# Force MelonLoader to re-run Cpp2IL with the patched binary next launch.
foreach ($cache in @(
        (Join-Path $target 'cpp2il_out'),
        (Join-Path $game 'MelonLoader\Il2CppAssemblies'),
        (Join-Path $game 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\AssemblyGenerator.cfg'))) {
    if (Test-Path $cache) { Remove-Item $cache -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "Installed patched Cpp2IL to $target"
