#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string] $Version = '1.0.1.0',
    [string] $SdkToolDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$packageVersion = [version]$Version
if ($packageVersion.Major -lt 1 -or $packageVersion.Major -gt 65535 -or
    $packageVersion.Minor -gt 65535 -or $packageVersion.Build -gt 65535) {
    throw 'Store versions require major 1..65535, minor/build 0..65535 and revision 0.'
}
$identity = Get-Content -LiteralPath "$root/packaging/StoreIdentity.json" -Raw | ConvertFrom-Json
[xml]$manifest = Get-Content -LiteralPath "$root/packaging/AppxManifest.xml" -Raw
if ($manifest.Package.Identity.Name -ne $identity.Name -or $manifest.Package.Identity.Publisher -ne $identity.Publisher -or
    $manifest.Package.Properties.PublisherDisplayName -ne $identity.PublisherDisplayName -or
    $manifest.Package.Identity.ProcessorArchitecture -ne 'arm64') {
    throw 'Manifest must match the reserved Store identity and ARM64 architecture.'
}
$manifest.Package.Identity.Version = $Version
# Fresh staging avoids stale payloads and never deletes a previous build.
$build = Join-Path $root "artifacts/msix/$Version-$([guid]::NewGuid().ToString('N'))"
$stage = Join-Path $build 'payload'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Push-Location $root
try {
    & dotnet publish src/ArmAware.App -c Release -p:Platform=ARM64 "-p:Version=$Version" -r win-arm64 --self-contained true -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'ARM64 publish failed.' }
    foreach ($file in @('ArmAware.App.exe', 'App.xbf', 'MainWindow.xbf', 'ArmAware.App.pri',
        'Microsoft.Management.Deployment.winmd', 'Microsoft.Management.Deployment.dll', 'Assets/AppLogo.png')) {
        if (!(Test-Path -LiteralPath (Join-Path $stage $file))) { throw "Missing required payload: $file" }
    }
    # Check the entry point really is an ARM64 PE, even on an x64 build runner.
    $stream = [IO.File]::OpenRead((Join-Path $stage 'ArmAware.App.exe'))
    $reader = [IO.BinaryReader]::new($stream)
    try {
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0xAA64) { throw 'Entry point is not ARM64.' }
    } finally { $reader.Dispose() }
    if (!$SdkToolDirectory) {
        $nugetRoot = (& dotnet msbuild src/ArmAware.App/ArmAware.App.csproj -getProperty:NuGetPackageRoot).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the NuGet SDK tools.' }
        $toolArchitecture = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x64' }
        $tool = Get-ChildItem -LiteralPath (Join-Path $nugetRoot 'microsoft.windows.sdk.buildtools') -Filter makeappx.exe -Recurse |
            Where-Object { $_.Directory.Name -eq $toolArchitecture } | Sort-Object FullName -Descending | Select-Object -First 1
        if (!$tool) { throw 'MakeAppx not found; supply -SdkToolDirectory with MakeAppx.exe and MakePri.exe.' }
        $SdkToolDirectory = $tool.Directory.FullName
    }
    foreach ($toolName in @('makeappx.exe', 'makepri.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $SdkToolDirectory $toolName))) { throw "Missing SDK tool: $toolName" }
    }
    $manifest.Save((Join-Path $stage 'AppxManifest.xml'))
    # Scale copies of the source logo for the package; the source file is left unchanged.
    Add-Type -AssemblyName System.Drawing
    $assetDirectory = Join-Path $stage 'PackageAssets'
    New-Item -ItemType Directory -Path $assetDirectory | Out-Null
    $sourceImage = [Drawing.Image]::FromFile((Join-Path $stage 'Assets/AppLogo.png'))
    try {
        foreach ($asset in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
            $size = [int]$asset[1]
            $bitmap = [Drawing.Bitmap]::new($size, $size)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
                $bitmap.Save((Join-Path $assetDirectory "$($asset[0]).png"), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $sourceImage.Dispose() }
    $config = Join-Path $build 'priconfig.xml'
    & (Join-Path $SdkToolDirectory 'makepri.exe') createconfig /dq en-US /cf $config /o
    if ($LASTEXITCODE -ne 0) { throw 'PRI configuration failed.' }
    # The compiled app PRI already contains WinUI's merged theme resources. Import it
    # once, alongside only the new package artwork. Indexing the entire publish folder
    # either duplicates loose XBFs or loses compiled theme resources when PRI import is disabled.
    $priInput = Join-Path $build 'pri-input'
    New-Item -ItemType Directory -Path $priInput | Out-Null
    Copy-Item -LiteralPath (Join-Path $stage 'ArmAware.App.pri') -Destination $priInput
    Copy-Item -LiteralPath $assetDirectory -Destination $priInput -Recurse
    [xml]$priConfig = Get-Content -LiteralPath $config -Raw
    # Ship a single self-contained MSIX, rather than emitting unbundled language/scale PRIs.
    $packagingNode = $priConfig.SelectSingleNode('/resources/packaging')
    $null = $packagingNode.ParentNode.RemoveChild($packagingNode)
    $priConfig.Save($config)
    $packagePri = Join-Path $build 'resources.pri'
    & (Join-Path $SdkToolDirectory 'makepri.exe') new /pr $priInput /cf $config /mn (Join-Path $stage 'AppxManifest.xml') /of $packagePri /o
    if ($LASTEXITCODE -ne 0) { throw 'Package resource indexing failed.' }
    if (@(Get-ChildItem -LiteralPath $build -Filter 'resources*.pri').Count -ne 1) {
        throw 'Resource indexing emitted split PRIs; the single-package payload would be incomplete.'
    }
    Copy-Item -LiteralPath $packagePri -Destination (Join-Path $stage 'resources.pri')
    $priDump = Join-Path $build 'resources.pri.xml'
    & (Join-Path $SdkToolDirectory 'makepri.exe') dump /if $packagePri /of $priDump /dt detailed /o
    if ($LASTEXITCODE -ne 0) { throw 'Cannot validate the compiled package resource map.' }
    [xml]$resources = Get-Content -LiteralPath $priDump -Raw
    $map = $resources.SelectSingleNode('//ResourceMap')
    if ($map.GetAttribute('name') -ne $identity.Name) { throw 'Package resource map has the wrong identity.' }
    $resourceUris = @($resources.SelectNodes('//NamedResource') | ForEach-Object { $_.GetAttribute('uri') })
    foreach ($resource in @('Files/App.xbf', 'Files/MainWindow.xbf', 'Files/Microsoft.UI.Xaml/Themes/themeresources.xbf',
        'Files/PackageAssets/StoreLogo.png', 'Files/PackageAssets/Square44x44Logo.png', 'Files/PackageAssets/Square150x150Logo.png')) {
        if ($resourceUris -notcontains "ms-resource://$($identity.Name)/$resource") { throw "Missing compiled package resource: $resource" }
    }
    $package = Join-Path $build "ArmAware_${Version}_arm64.msix"
    & (Join-Path $SdkToolDirectory 'makeappx.exe') pack /d $stage /p $package /o
    if ($LASTEXITCODE -ne 0) { throw 'MSIX validation/pack failed.' }
    & "$PSScriptRoot/Test-Msix.ps1" -PackagePath $package -SdkToolDirectory $SdkToolDirectory
    [ordered]@{
        Package = $package
        SHA256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
        Identity = $identity.Name
        Publisher = $identity.Publisher
        Architecture = 'arm64'
        Version = $Version
        MakeAppxValidation = 'Passed'
        CompiledThemeAndLogoResources = 'Passed'
        InstalledPackageTested = $false
        WackRun = $false
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $build 'verification.json')
    Get-FileHash -LiteralPath $package -Algorithm SHA256 | Format-List
    Write-Host "Unsigned Store package: $package"
    Write-Host "Staged manifest: $stage/AppxManifest.xml"
    Write-Host 'No certificate or trust settings were changed. Store certification and installed-package testing are separate gates.'
} finally { Pop-Location }
