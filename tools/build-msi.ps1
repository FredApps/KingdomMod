<#
.SYNOPSIS
  Builds the KingdomMod MSI from the committed dist/ payload.
#>
param(
    [string]$Version,
    [string]$MelonLoaderVersion = 'v0.7.3',
    [string]$OutputDir
)

. "$PSScriptRoot\common.ps1"

$root = Get-RepoRoot
$dotnet = Resolve-Dotnet -RequireSdk
$dist = Join-Path $root 'dist'
$installer = Join-Path $root 'installer'
$artifacts = if ($OutputDir) { $OutputDir } else { Join-Path $root 'artifacts\installer' }
$payload = Join-Path $artifacts 'payload'
$support = Join-Path $payload '.kingdommod-installer'
$generated = Join-Path $artifacts 'generated'

if (-not $Version) {
    $tag = $env:GITHUB_REF_NAME
    if (-not $tag) {
        try { $tag = (& git -C $root describe --tags --exact-match 2>$null) } catch {}
    }
    if ($tag -and $tag.StartsWith('v')) { $Version = $tag.Substring(1) }
}
if (-not $Version) { $Version = '0.1.0' }
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw "MSI version must be numeric, for example 0.1.0. Got '$Version'."
}

$required = @(
    'KingdomMod.Loader.dll',
    'KingdomMod.Api.dll',
    'KingdomMod.Examples.AnyMount.dll',
    'KingdomMod.Examples.AnyTrees.dll',
    'KingdomMod.Examples.BalanceExtras.dll',
    'KingdomMod.Examples.BalanceTweaks.dll',
    'KingdomMod.Examples.ChallengeDumper.dll',
    'KingdomMod.Examples.GameplayTweaks.dll',
    'KingdomMod.Examples.HudOverlay.dll',
    'KingdomMod.Examples.ReskinPack.dll',
    'KingdomMod.Examples.SandboxConsole.dll',
    'KingdomMod.Examples.SpeedHotkeys.dll',
    'KingdomMod.Examples.SpeedTweaks.dll'
)
foreach ($name in $required) {
    if (-not (Test-Path (Join-Path $dist $name))) {
        throw "Missing dist payload file: $name. Run tools/prepare-release.ps1 locally and commit dist/."
    }
}

Remove-Item -LiteralPath $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $support, $generated | Out-Null

$headers = @{ 'User-Agent' = 'kingdommod-msi-builder' }
$assetName = 'MelonLoader.x64.zip'
$downloadUrl = "https://github.com/LavaGang/MelonLoader/releases/download/$MelonLoaderVersion/$assetName"
$zip = Join-Path $support $assetName
Write-Host "Downloading $assetName ($MelonLoaderVersion)..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $zip -Headers $headers

Copy-Item -LiteralPath (Join-Path $installer 'scripts\InstallMelonLoader.ps1') -Destination $support -Force
Copy-Item -LiteralPath (Join-Path $installer 'scripts\UninstallMelonLoader.ps1') -Destination $support -Force
Set-Content -LiteralPath (Join-Path $support 'kingdommod-msi.txt') -Value "KingdomMod $Version installer support files." -Encoding UTF8

$modsPayload = Join-Path $payload 'Mods'
New-Item -ItemType Directory -Force -Path $modsPayload | Out-Null
Get-ChildItem -LiteralPath $dist -File -Filter 'KingdomMod*.dll' |
    Copy-Item -Destination $modsPayload -Force

function Convert-ToWixId {
    param([Parameter(Mandatory=$true)][string]$Text)
    $id = [regex]::Replace($Text, '[^A-Za-z0-9_]', '_')
    if ($id -notmatch '^[A-Za-z_]') { $id = "_$id" }
    return $id
}

$componentRefs = New-Object System.Collections.Generic.List[string]
$fragments = New-Object System.Collections.Generic.List[string]
$fragments.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$fragments.Add('  <Fragment>')
$fragments.Add('    <DirectoryRef Id="INSTALLFOLDER">')
$fragments.Add('      <Directory Id="ModsFolder" Name="Mods" />')
$fragments.Add('      <Directory Id="InstallerSupportFolder" Name=".kingdommod-installer" />')
$fragments.Add('    </DirectoryRef>')
$fragments.Add('  </Fragment>')
$fragments.Add('  <Fragment>')
$fragments.Add('    <DirectoryRef Id="ModsFolder">')
foreach ($file in Get-ChildItem -LiteralPath $modsPayload -File | Sort-Object Name) {
    $id = Convert-ToWixId "cmp_mod_$($file.Name)"
    $fileId = Convert-ToWixId "fil_mod_$($file.Name)"
    $source = $file.FullName.Replace('\', '\\')
    $fragments.Add("      <Component Id=""$id"" Guid=""*""><File Id=""$fileId"" Source=""$source"" KeyPath=""yes"" /></Component>")
    $componentRefs.Add($id)
}
$fragments.Add('    </DirectoryRef>')
$fragments.Add('    <DirectoryRef Id="InstallerSupportFolder">')
foreach ($file in Get-ChildItem -LiteralPath $support -File | Sort-Object Name) {
    $id = Convert-ToWixId "cmp_support_$($file.Name)"
    $fileId = Convert-ToWixId "fil_support_$($file.Name)"
    if ($file.Name -eq 'InstallMelonLoader.ps1') { $fileId = 'InstallMelonLoaderScript' }
    if ($file.Name -eq 'UninstallMelonLoader.ps1') { $fileId = 'UninstallMelonLoaderScript' }
    $source = $file.FullName.Replace('\', '\\')
    $fragments.Add("      <Component Id=""$id"" Guid=""*""><File Id=""$fileId"" Source=""$source"" KeyPath=""yes"" /></Component>")
    $componentRefs.Add($id)
}
$fragments.Add('    </DirectoryRef>')
$fragments.Add('  </Fragment>')
$fragments.Add('  <Fragment>')
$fragments.Add('    <ComponentGroup Id="KingdomModPayload">')
foreach ($id in $componentRefs) {
    $fragments.Add("      <ComponentRef Id=""$id"" />")
}
$fragments.Add('    </ComponentGroup>')
$fragments.Add('  </Fragment>')
$fragments.Add('</Wix>')

$filesWxs = Join-Path $generated 'Payload.wxs'
Set-Content -LiteralPath $filesWxs -Value ($fragments -join "`r`n") -Encoding UTF8

$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
    Write-Host 'Installing local WiX tool...'
    & $dotnet tool install --tool-path (Join-Path $artifacts '.tools') wix --version 5.*
    if ($LASTEXITCODE -ne 0) { throw 'Failed to install WiX tool.' }
    $wixExe = Join-Path $artifacts '.tools\wix.exe'
} else {
    $wixExe = $wix.Source
}

Write-Host 'Ensuring WiX UI extension is available...'
& $wixExe extension add WixToolset.UI.wixext/5.0.2 --global
if ($LASTEXITCODE -ne 0) { throw 'Failed to install WiX UI extension.' }

$out = Join-Path $artifacts "KingdomMod-$Version-x64.msi"
Write-Host "Building MSI $out..."
& $wixExe build `
    (Join-Path $installer 'Product.wxs') `
    $filesWxs `
    -ext WixToolset.UI.wixext `
    -d "ProductVersion=$Version" `
    -d "InstallerDir=$installer" `
    -arch x64 `
    -out $out
if ($LASTEXITCODE -ne 0) { throw 'WiX build failed.' }

Write-Host "MSI built: $out"
