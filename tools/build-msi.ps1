<#
.SYNOPSIS
  Builds the small KingdomMod MSI with source and setup scripts.
#>
param(
    [string]$Version,
    [string]$MelonLoaderVersion = 'v0.7.3',
    [string]$OutputDir
)

. "$PSScriptRoot\common.ps1"

$ProgressPreference = 'SilentlyContinue'

$root = Get-RepoRoot
$dotnet = Resolve-Dotnet -RequireSdk
$installer = Join-Path $root 'installer'
$artifacts = if ($OutputDir) { $OutputDir } else { Join-Path $root 'artifacts\installer' }
$payload = Join-Path $artifacts 'payload'
$support = Join-Path $payload '.kingdommod-installer'
$sourcePayload = Join-Path $support 'source'
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

Remove-Item -LiteralPath $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $support, $sourcePayload, $generated | Out-Null

$headers = @{ 'User-Agent' = 'kingdommod-msi-builder' }
$assetName = 'MelonLoader.x64.zip'
$downloadUrl = "https://github.com/LavaGang/MelonLoader/releases/download/$MelonLoaderVersion/$assetName"
$zip = Join-Path $support $assetName
Write-Host "Downloading $assetName ($MelonLoaderVersion)..."
Invoke-WebRequest -Uri $downloadUrl -OutFile $zip -Headers $headers

Write-Host 'Recording MelonLoader archive root paths...'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $ownedRoots = $archive.Entries |
        ForEach-Object {
            ($_.FullName -replace '/', '\').Trim('\') -split '\\' | Select-Object -First 1
        } |
        Where-Object { $_ } |
        Sort-Object -Unique
} finally {
    $archive.Dispose()
}
Set-Content -LiteralPath (Join-Path $support 'melonloader-owned-roots.txt') -Value $ownedRoots -Encoding UTF8

Copy-Item -LiteralPath (Join-Path $installer 'scripts\BuildAndInstallKingdomMod.ps1') -Destination $support -Force
Copy-Item -LiteralPath (Join-Path $installer 'scripts\UninstallMelonLoader.ps1') -Destination $support -Force
Set-Content -LiteralPath (Join-Path $support 'kingdommod-msi.txt') -Value "KingdomMod $Version installer support files." -Encoding UTF8

Write-Host 'Copying source payload for on-machine build...'
$sourceFiles = @()
if (Test-Path (Join-Path $root '.git')) {
    $sourceFiles = & git -C $root ls-files
} else {
    $sourceFiles = Get-ChildItem -LiteralPath $root -Recurse -File |
        ForEach-Object { [System.IO.Path]::GetRelativePath($root, $_.FullName) }
}

$sourceFiles = $sourceFiles | Where-Object {
    $_ -notmatch '^(dist|installer|\.github|docs|packs)/' -and
    $_ -notmatch '(^|/)(bin|obj)/' -and
    $_ -notmatch '\.dll$' -and
    $_ -notmatch '^\.(gitignore|gitattributes)$' -and
    $_ -notmatch '^LICENSE$'
}

foreach ($rel in $sourceFiles) {
    $src = Join-Path $root $rel
    if (-not (Test-Path -LiteralPath $src)) { continue }
    $dst = Join-Path $sourcePayload ($rel -replace '/', '\')
    New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst -Force
}

foreach ($requiredSource in @('KingdomMod.sln','Directory.Build.props','src\KingdomMod.Loader\KingdomMod.Loader.csproj','src\KingdomMod.Api\KingdomMod.Api.csproj')) {
    if (-not (Test-Path (Join-Path $sourcePayload $requiredSource))) {
        throw "Source payload is missing '$requiredSource'."
    }
}

function Convert-ToWixId {
    param([Parameter(Mandatory=$true)][string]$Text)
    $id = [regex]::Replace($Text, '[^A-Za-z0-9_]', '_')
    if ($id -notmatch '^[A-Za-z_]') { $id = "_$id" }
    if ($id.Length -gt 60) {
        $md5 = [System.Security.Cryptography.MD5]::Create()
        try {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
            $hash = -join (($md5.ComputeHash($bytes) | Select-Object -First 4) | ForEach-Object { $_.ToString('x2') })
        } finally {
            $md5.Dispose()
        }
        $id = $id.Substring(0, 51) + '_' + $hash
    }
    return $id
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory=$true)][string]$Root,
        [Parameter(Mandatory=$true)][string]$Path
    )

    $rootFull = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\') + '\'
    $pathFull = (Resolve-Path -LiteralPath $Path).Path
    if (-not $pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$pathFull' is not under '$rootFull'."
    }
    return $pathFull.Substring($rootFull.Length)
}

function Add-WixDirectoryTree {
    param(
        [Parameter(Mandatory=$true)][System.Collections.Generic.List[string]]$Fragments,
        [Parameter(Mandatory=$true)][string]$Root,
        [Parameter(Mandatory=$true)][string]$Directory,
        [Parameter(Mandatory=$true)][int]$Indent
    )

    foreach ($child in Get-ChildItem -LiteralPath $Directory -Directory | Sort-Object Name) {
        $relative = Get-RelativePath -Root $Root -Path $child.FullName
        $id = Convert-ToWixId "dir_$relative"
        $pad = ' ' * $Indent
        $Fragments.Add("$pad<Directory Id=""$id"" Name=""$($child.Name)"">")
        Add-WixDirectoryTree -Fragments $Fragments -Root $Root -Directory $child.FullName -Indent ($Indent + 2)
        $Fragments.Add("$pad</Directory>")
    }
}

$componentRefs = New-Object System.Collections.Generic.List[string]
$fragments = New-Object System.Collections.Generic.List[string]
$fragments.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$fragments.Add('  <Fragment>')
$fragments.Add('    <DirectoryRef Id="INSTALLFOLDER">')
$fragments.Add('      <Directory Id="InstallerSupportFolder" Name=".kingdommod-installer">')
Add-WixDirectoryTree -Fragments $fragments -Root $support -Directory $support -Indent 8
$fragments.Add('      </Directory>')
$fragments.Add('    </DirectoryRef>')
$fragments.Add('  </Fragment>')
$fragments.Add('  <Fragment>')
$fragments.Add('    <DirectoryRef Id="InstallerSupportFolder">')
foreach ($file in Get-ChildItem -LiteralPath $support -File -Recurse | Sort-Object FullName) {
    $relative = Get-RelativePath -Root $support -Path $file.FullName
    $dirId = 'InstallerSupportFolder'
    $parentRel = Split-Path $relative -Parent
    if ($parentRel) {
        $parts = $parentRel -split '[\\/]'
        $currentPath = $support
        foreach ($part in $parts) {
            $currentPath = Join-Path $currentPath $part
            $dirId = Convert-ToWixId "dir_$(Get-RelativePath -Root $support -Path $currentPath)"
        }
    }

    $id = Convert-ToWixId "cmp_support_$relative"
    $fileId = Convert-ToWixId "fil_support_$relative"
    if ($relative -eq 'BuildAndInstallKingdomMod.ps1') { $fileId = 'BuildAndInstallScript' }
    if ($relative -eq 'UninstallMelonLoader.ps1') { $fileId = 'UninstallMelonLoaderScript' }
    $source = $file.FullName.Replace('\', '\\')
    $fragments.Add("      <Component Id=""$id"" Directory=""$dirId"" Guid=""*""><File Id=""$fileId"" Source=""$source"" KeyPath=""yes"" /></Component>")
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
