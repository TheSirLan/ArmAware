#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PackagePath,
    [Parameter(Mandatory)] [string] $SdkToolDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$packageFile = (Resolve-Path -LiteralPath $PackagePath).Path
$identity = Get-Content -LiteralPath "$root/packaging/StoreIdentity.json" -Raw | ConvertFrom-Json
$work = Join-Path $root "artifacts/package-check/$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work -Force | Out-Null
$archive = [IO.Compression.ZipFile]::OpenRead($packageFile)
try {
    $names = @($archive.Entries.FullName)
    foreach ($required in @('AppxManifest.xml', 'AppxBlockMap.xml', 'ArmAware.App.exe', 'ArmAware.Core.dll',
        'App.xbf', 'MainWindow.xbf', 'ArmAware.App.pri', 'resources.pri',
        'Microsoft.Management.Deployment.dll', 'Microsoft.Management.Deployment.winmd')) {
        if ($names -notcontains $required) { throw "Missing package entry: $required" }
    }
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.Package.Identity.Name -ne $identity.Name -or $manifest.Package.Identity.Publisher -ne $identity.Publisher -or
        $manifest.Package.Properties.PublisherDisplayName -ne $identity.PublisherDisplayName -or
        $manifest.Package.Identity.ProcessorArchitecture -ne 'arm64') { throw 'Manifest has an unexpected Store identity or architecture.' }
    $version = [version]$manifest.Package.Identity.Version
    if ($version.Revision -ne 0 -or $version.Major -lt 1) { throw 'Invalid Store package version.' }
    $exeStream = [IO.MemoryStream]::new()
    $compressedExe = $archive.GetEntry('ArmAware.App.exe').Open()
    try { $compressedExe.CopyTo($exeStream) } finally { $compressedExe.Dispose() }
    $binary = [IO.BinaryReader]::new($exeStream)
    try {
        $exeStream.Position = 0x3c
        $peOffset = $binary.ReadInt32()
        $exeStream.Position = $peOffset
        if ($binary.ReadUInt32() -ne 0x4550 -or $binary.ReadUInt16() -ne 0xAA64) { throw 'Packaged EXE is not ARM64.' }
    } finally { $binary.Dispose() }
    foreach ($asset in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
        $name = "PackageAssets/$($asset[0]).png"
        $entry = $archive.GetEntry($name)
        if (!$entry) { throw "Missing package logo: $name" }
        $assetStream = $entry.Open()
        $pngReader = [IO.BinaryReader]::new($assetStream)
        try {
            $header = $pngReader.ReadBytes(24)
            if ($header.Length -ne 24 -or [Convert]::ToHexString($header[0..7]) -ne '89504E470D0A1A0A') { throw "Invalid PNG: $name" }
            $width = [int]$header[16] * 16777216 + [int]$header[17] * 65536 + [int]$header[18] * 256 + [int]$header[19]
            $height = [int]$header[20] * 16777216 + [int]$header[21] * 65536 + [int]$header[22] * 256 + [int]$header[23]
            if ($width -ne $asset[1] -or $height -ne $asset[1]) { throw "Wrong dimensions: $name" }
        } finally { $pngReader.Dispose() }
    }
    foreach ($source in Get-ChildItem -LiteralPath "$root/src/ArmAware.App/Assets" -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath("$root/src/ArmAware.App", $source.FullName).Replace('\', '/')
        $entry = $archive.GetEntry($relative)
        if (!$entry) { throw "Missing original artwork: $relative" }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $source.FullName).Hash) { throw "Original artwork changed: $relative" }
    }
    $pri = Join-Path $work 'resources.pri'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry('resources.pri'), $pri)
} finally { $archive.Dispose() }
$dump = Join-Path $work 'resources.pri.xml'
& (Join-Path $SdkToolDirectory 'makepri.exe') dump /if $pri /of $dump /dt detailed /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Packaged resource index cannot be read.' }
[xml]$resources = Get-Content -LiteralPath $dump -Raw
if ($resources.SelectSingleNode('//ResourceMap').GetAttribute('name') -ne $identity.Name) { throw 'Resource-map identity mismatch.' }
$uris = @($resources.SelectNodes('//NamedResource') | ForEach-Object { $_.GetAttribute('uri') })
foreach ($resource in @('Files/App.xbf', 'Files/MainWindow.xbf', 'Files/Microsoft.UI.Xaml/Themes/themeresources.xbf',
    'Files/PackageAssets/StoreLogo.png', 'Files/PackageAssets/Square44x44Logo.png', 'Files/PackageAssets/Square150x150Logo.png')) {
    if ($uris -notcontains "ms-resource://$($identity.Name)/$resource") { throw "Missing compiled package resource: $resource" }
}
[ordered]@{
    Package = $packageFile
    SHA256 = (Get-FileHash -LiteralPath $packageFile).Hash
    Identity = $identity.Name
    Version = $version.ToString()
    Architecture = 'arm64'
    PayloadAndLogoChecks = 'Passed'
    CompiledThemeAndLogoResources = 'Passed'
    OriginalArtworkDifferences = 0
    InstalledPackageTested = $false
    WackRun = $false
} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $work 'verification.json')
