<#
.SYNOPSIS
  Builds and installs KingdomMod's patched Cpp2IL into MelonLoader's Il2CppAssemblyGenerator.

.DESCRIPTION
  Kingdom Two Crowns 2.4.0 contains stripped property metadata that crashes the
  Cpp2IL version pulled by MelonLoader during interop generation. This script
  downloads upstream Cpp2IL source, applies KingdomMod's source patch locally,
  builds it, and installs only the local build into the user's game folder.
#>
param(
    [string]$GameDir,
    [string]$Cpp2IlVersion = '2022.1.0-pre-release.21'
)
. "$PSScriptRoot\common.ps1"

$ProgressPreference = 'SilentlyContinue'

$root = Get-RepoRoot
$game = Find-GameDir -Override $GameDir
$srcRoot = Join-Path $root 'build\_tools\Cpp2IL-src'
$source = Join-Path $srcRoot 'Cpp2IL\bin\Release\net8.0'
$pluginSource = Join-Path $srcRoot 'Cpp2IL.Plugin.StrippedCodeRegSupport\bin\Release\net8.0'
$target = Join-Path $game 'MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL'

function Replace-ExactText {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Old,
        [Parameter(Mandatory=$true)][string]$New
    )

    $text = Get-Content -LiteralPath $Path -Raw
    if ($text.Contains($New)) { return }
    if (-not $text.Contains($Old)) {
        throw "Could not apply KingdomMod Cpp2IL patch to '$Path'; expected source text was not found."
    }
    Set-Content -LiteralPath $Path -Value ($text.Replace($Old, $New)) -Encoding UTF8 -NoNewline
}

function Set-TargetFrameworkText {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][string]$Element,
        [Parameter(Mandatory=$true)][string]$Value
    )

    $text = Get-Content -LiteralPath $Path -Raw
    $pattern = "<$Element>[^<]+</$Element>"
    $replacement = "<$Element>$Value</$Element>"
    if ($text -match [regex]::Escape($replacement)) { return }
    if ($text -notmatch $pattern) {
        throw "Could not find <$Element> in '$Path'."
    }
    Set-Content -LiteralPath $Path -Value ([regex]::Replace($text, $pattern, $replacement, 1)) -Encoding UTF8 -NoNewline
}

function Ensure-Cpp2IlSource {
    $project = Join-Path $srcRoot 'Cpp2IL\Cpp2IL.csproj'
    $tools = Join-Path $root 'build\_tools'

    # Keep KingdomMod's Directory.Build.props from leaking into downloaded
    # third-party source. Cpp2IL must build before refs/ exists in the MSI flow.
    $propsBoundary = Join-Path $tools 'Directory.Build.props'
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    Set-Content -LiteralPath $propsBoundary -Value '<Project />' -Encoding UTF8 -NoNewline

    if (Test-Path $project) { return }

    $zip = Join-Path $tools "Cpp2IL-$Cpp2IlVersion.zip"
    $url = "https://github.com/SamboyCoding/Cpp2IL/archive/refs/tags/$Cpp2IlVersion.zip"
    Write-Host "Downloading Cpp2IL source ($Cpp2IlVersion)..."
    Invoke-WebRequest -Uri $url -OutFile $zip -Headers @{ 'User-Agent' = 'kingdommod-cpp2il-builder' }

    $extract = Join-Path $tools "Cpp2IL-$Cpp2IlVersion"
    Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $zip -DestinationPath $tools -Force
    $expanded = Get-ChildItem -LiteralPath $tools -Directory |
        Where-Object { $_.Name -like 'Cpp2IL-*' -and (Test-Path (Join-Path $_.FullName 'Cpp2IL\Cpp2IL.csproj')) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $expanded) {
        throw "Downloaded Cpp2IL archive did not contain the expected source tree."
    }

    Remove-Item -LiteralPath $srcRoot -Recurse -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $expanded.FullName -Destination $srcRoot
}

function Apply-KingdomModCpp2IlPatch {
    Ensure-Cpp2IlSource

    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'Cpp2IL.Core.Tests\Cpp2IL.Core.Tests.csproj') -Element 'TargetFramework' -Value 'net8.0'
    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'Cpp2IL.Plugin.BuildReport\Cpp2IL.Plugin.BuildReport.csproj') -Element 'TargetFramework' -Value 'net8.0'
    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'Cpp2IL.Plugin.ControlFlowGraph\Cpp2IL.Plugin.ControlFlowGraph.csproj') -Element 'TargetFramework' -Value 'net8.0'
    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'Cpp2IL.Plugin.Pdb\Cpp2IL.Plugin.Pdb.csproj') -Element 'TargetFramework' -Value 'net8.0'
    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'Cpp2IL.Plugin.StrippedCodeRegSupport\Cpp2IL.Plugin.StrippedCodeRegSupport.csproj') -Element 'TargetFramework' -Value 'net8.0'
    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'LibCpp2ILTests\LibCpp2ILTests.csproj') -Element 'TargetFramework' -Value 'net8.0'

    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'Cpp2IL.Core\Cpp2IL.Core.csproj') -Element 'TargetFrameworks' -Value 'net8.0;net7.0;net6.0;netstandard2.0'
    Replace-ExactText -Path (Join-Path $srcRoot 'Cpp2IL.Core\Cpp2IL.Core.csproj') -Old '<LangVersion>13</LangVersion>' -New '<LangVersion>preview</LangVersion>'
    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'Cpp2IL\Cpp2IL.csproj') -Element 'TargetFrameworks' -Value 'net8.0;net472'
    Replace-ExactText -Path (Join-Path $srcRoot 'Cpp2IL\Cpp2IL.csproj') -Old @'
        <TargetFrameworks>net8.0;net472</TargetFrameworks>
'@ -New @'
        <TargetFrameworks>net8.0;net472</TargetFrameworks>
        <RollForward>LatestMajor</RollForward>
'@
    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'LibCpp2IL\LibCpp2IL.csproj') -Element 'TargetFrameworks' -Value 'net8.0;net7.0;net6.0;netstandard2.0'
    Set-TargetFrameworkText -Path (Join-Path $srcRoot 'WasmDisassembler\WasmDisassembler.csproj') -Element 'TargetFrameworks' -Value 'net8.0;net7.0;net6.0;netstandard2.0'

    Replace-ExactText -Path (Join-Path $srcRoot 'global.json') -Old @'
    "version": "9.0.0",
    "rollForward": "latestMinor",
'@ -New @'
    "version": "8.0.0",
    "rollForward": "latestMajor",
'@

    Replace-ExactText -Path (Join-Path $srcRoot 'LibCpp2IL\Metadata\Il2CppPropertyDefinition.cs') -Old @'
    public Il2CppTypeReflectionData? PropertyType => LibCpp2IlMain.TheMetadata == null ? null : Getter == null ? Setter!.Parameters![0].Type : Getter!.ReturnType;

    public Il2CppType? RawPropertyType => LibCpp2IlMain.TheMetadata == null ? null : Getter == null ? Setter!.Parameters![0].RawType : Getter!.RawReturnType;

    public bool IsStatic => Getter == null ? Setter!.IsStatic : Getter!.IsStatic;
'@ -New @'
    public Il2CppTypeReflectionData? PropertyType => LibCpp2IlMain.TheMetadata == null ? null : Getter != null ? Getter.ReturnType : Setter?.Parameters?[0].Type;

    public Il2CppType? RawPropertyType => LibCpp2IlMain.TheMetadata == null ? null : Getter != null ? Getter.RawReturnType : Setter?.Parameters?[0].RawType;

    public bool IsStatic => Getter != null ? Getter.IsStatic : Setter?.IsStatic ?? false;
'@

    Replace-ExactText -Path (Join-Path $srcRoot 'Cpp2IL.Core\Utils\AsmResolver\AsmResolverAssemblyPopulator.cs') -Old @'
                foreach (var property in type.Properties)
                    CopyCustomAttributes(property, property.GetExtraData<PropertyDefinition>("AsmResolverProperty")!.CustomAttributes);
'@ -New @'
                foreach (var property in type.Properties)
                {
                    var asmProp = property.GetExtraData<PropertyDefinition>("AsmResolverProperty");
                    if (asmProp == null) continue;
                    CopyCustomAttributes(property, asmProp.CustomAttributes);
                }
'@

    Replace-ExactText -Path (Join-Path $srcRoot 'Cpp2IL.Core\Utils\AsmResolver\AsmResolverAssemblyPopulator.cs') -Old @'
        foreach (var propertyCtx in typeContext.Properties)
        {
            var propertyTypeSig = propertyCtx.ToTypeSignature(importer.TargetModule);
            var propertySignature = propertyCtx.IsStatic
'@ -New @'
        foreach (var propertyCtx in typeContext.Properties)
        {
            if (propertyCtx.Getter == null && propertyCtx.Setter == null)
                continue;

            var propertyTypeSig = propertyCtx.ToTypeSignature(importer.TargetModule);
            if (propertyTypeSig == null)
                continue;
            var propertySignature = propertyCtx.IsStatic
'@
}

Apply-KingdomModCpp2IlPatch

if (-not (Test-Path (Join-Path $source 'Cpp2IL.exe'))) {
    $dotnet = Resolve-Dotnet -RequireSdk
    $project = Join-Path $srcRoot 'Cpp2IL\Cpp2IL.csproj'
    if (-not (Test-Path $project)) {
        throw "Patched Cpp2IL source is missing at '$project'. Restore build/_tools/Cpp2IL-src before installing."
    }

    Write-Host "Building patched Cpp2IL (net8.0)..."
    # -f net8.0 is required: the source also lists net472 which we don't build.
    # SkipRefsCheck=true: refs/ is not yet populated at this point in the MSI flow;
    # Cpp2IL doesn't use the game interop refs so the guard is irrelevant here.
    & $dotnet build $project -c Release -f net8.0 --nologo -p:SkipRefsCheck=true
    if ($LASTEXITCODE -ne 0) { throw "Patched Cpp2IL build failed." }

    $pluginProj = Join-Path $srcRoot 'Cpp2IL.Plugin.StrippedCodeRegSupport\Cpp2IL.Plugin.StrippedCodeRegSupport.csproj'
    if (Test-Path $pluginProj) {
        Write-Host "Building StrippedCodeRegSupport plugin..."
        & $dotnet build $pluginProj -c Release -f net8.0 --nologo -p:SkipRefsCheck=true
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
