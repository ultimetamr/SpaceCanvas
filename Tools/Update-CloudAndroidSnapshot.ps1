# Refresh the compiled Android application payload after a successful Unity build.
# This does not recompile Unity C# itself. Existing output is never deleted/overwritten.
[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$UnityGradlePath = 'Library/Bee/Android/Prj/IL2CPP/Gradle',
    [string]$VerifiedApkPath = 'Builds/SpaceCanvas-FullSpaceMR.apk',
    [string]$OutputDirectory = 'CloudAndroid'
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$root = [IO.Path]::GetFullPath($ProjectRoot)
$source = [IO.Path]::GetFullPath((Join-Path $root $UnityGradlePath))
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$apkPath = [IO.Path]::GetFullPath((Join-Path $root $VerifiedApkPath))
if (!(Test-Path -LiteralPath $apkPath)) { throw 'A successfully built Unity APK is required to verify the application payload.' }
if (!$output.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must remain inside the project directory.'
}
if (Test-Path -LiteralPath (Join-Path $output 'snapshot.json')) {
    throw 'Snapshot already exists. Rename CloudAndroid to a backup outside the repository, then run this script again; restore app/build.gradle from Git. Existing files were not changed.'
}
if ((Test-Path -LiteralPath (Join-Path $output 'app/src')) -or (Test-Path -LiteralPath (Join-Path $output 'app/libs'))) {
    throw 'Output contains source/runtime files already. Choose a new output directory; no files were changed.'
}
$required = @('unityLibrary/src/main/AndroidManifest.xml', 'unityLibrary/src/main/jniLibs/arm64-v8a/libil2cpp.so',
    'unityLibrary/src/main/assets/bin/Data/globalgamemanagers', 'launcher/src/main/AndroidManifest.xml', 'unityLibrary/libs/unity-classes.jar')
foreach ($relative in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $source $relative))) { throw "Incomplete Unity Android output: $relative" }
}
New-Item -ItemType Directory -Force -Path (Join-Path $output 'app/src/main'), (Join-Path $output 'app/libs') | Out-Null
$destination = Join-Path $output 'app/src/main'
foreach ($folder in @('assets', 'jniLibs', 'res', 'resources')) {
    $sourceFolder = Join-Path $source ('unityLibrary/src/main/' + $folder)
    if (Test-Path -LiteralPath $sourceFolder) { Copy-Item -LiteralPath $sourceFolder -Destination (Join-Path $destination $folder) -Recurse }
}

# Unity strips debug information from some native libraries at APK packaging time.
# Use the exact shipped ELF bytes and check that scene assets match that APK too.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($apkPath)
try {
    foreach ($native in Get-ChildItem -LiteralPath (Join-Path $destination 'jniLibs') -File -Recurse) {
        $relative = $native.FullName.Substring((Join-Path $destination 'jniLibs').Length).TrimStart('\').Replace('\','/')
        $entry = $archive.GetEntry('lib/' + $relative)
        if ($null -eq $entry) { throw "Native library missing from verified APK: $relative" }
        $inputStream = $entry.Open()
        $outputStream = [IO.File]::Create($native.FullName)
        try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
    }
    foreach ($asset in Get-ChildItem -LiteralPath (Join-Path $destination 'assets') -File -Recurse) {
        $relative = $asset.FullName.Substring($destination.Length + 1).Replace('\','/')
        $entry = $archive.GetEntry($relative)
        if ($null -eq $entry) { throw "Asset missing from verified APK: $relative" }
        $stream = $entry.Open()
        $digest = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($digest.ComputeHash($stream)).Replace('-','').ToLower() }
        finally { $stream.Dispose(); $digest.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLower()) {
            throw "Unity output and APK do not match: $relative. Run a successful Unity build before updating the snapshot."
        }
    }
} finally { $archive.Dispose() }

# Copy the Unity application Activity only. The unused SpatialAdapter sample is not needed.
$javaDirectory = Join-Path $destination 'java/com/unity3d/player'
New-Item -ItemType Directory -Force -Path $javaDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'unityLibrary/src/main/java/com/unity3d/player/UnityPlayerActivity.java') -Destination $javaDirectory
Get-ChildItem -LiteralPath (Join-Path $source 'unityLibrary/libs') -File | Where-Object { $_.Extension -in @('.jar', '.aar') } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $output 'app/libs')
}

# Merge Unity's main manifest with the launcher's icon/name/screen support attributes.
$androidNamespace = 'http://schemas.android.com/apk/res/android'
$manifest = New-Object Xml.XmlDocument
$manifest.PreserveWhitespace = $true
$manifest.Load((Join-Path $source 'unityLibrary/src/main/AndroidManifest.xml'))
$launcher = New-Object Xml.XmlDocument
$launcher.Load((Join-Path $source 'launcher/src/main/AndroidManifest.xml'))
$app = $manifest.SelectSingleNode('/manifest/application')
foreach ($attribute in $launcher.SelectSingleNode('/manifest/application').Attributes) {
    $app.SetAttribute($attribute.LocalName, $attribute.NamespaceURI, $attribute.Value) | Out-Null
}
$manifest.DocumentElement.SetAttribute('installLocation', $androidNamespace, 'auto') | Out-Null
foreach ($child in $launcher.DocumentElement.ChildNodes) {
    if ($child.Name -ne 'application' -and $child.NodeType -eq [Xml.XmlNodeType]::Element) {
        $manifest.DocumentElement.AppendChild($manifest.ImportNode($child, $true)) | Out-Null
    }
}
$manifest.Save((Join-Path $destination 'AndroidManifest.xml'))
Get-ChildItem -LiteralPath (Join-Path $source 'launcher/src/main/res') -File -Recurse | ForEach-Object {
    $relative = $_.FullName.Substring((Join-Path $source 'launcher/src/main/res').Length).TrimStart('\')
    $target = Join-Path (Join-Path $destination 'res') $relative
    if (Test-Path -LiteralPath $target) {
        if (!$relative.StartsWith('values') -or $_.Extension -ne '.xml') { throw "Resource collision; merge manually: $relative" }
        $existing = New-Object Xml.XmlDocument
        $existing.Load($target)
        $incoming = New-Object Xml.XmlDocument
        $incoming.Load($_.FullName)
        foreach ($resource in $incoming.DocumentElement.ChildNodes) {
            if ($resource.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
            $duplicate = @($existing.DocumentElement.ChildNodes | Where-Object {
                $_.Name -eq $resource.Name -and $_.GetAttribute('name') -eq $resource.GetAttribute('name')
            })
            if ($duplicate.Count -gt 0) {
                if ($duplicate[0].OuterXml -ne $resource.OuterXml) { throw "Resource value collision: $relative / $($resource.GetAttribute('name'))" }
            } else {
                $existing.DocumentElement.AppendChild($existing.ImportNode($resource, $true)) | Out-Null
            }
        }
        $existing.Save($target)
    } else {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
}

# Remove only the development computer's connection IP. Do not alter gameplay assets.
$boot = Join-Path $destination 'assets/bin/Data/boot.config'
$bootText = [IO.File]::ReadAllText($boot)
$bootText = [regex]::Replace($bootText, '(?m)^player-connection-ip=[^\r\n]*\r?\n?', '')
[IO.File]::WriteAllText($boot, $bootText, $utf8)

$payloadFiles = @(Get-ChildItem -LiteralPath (Join-Path $output 'app/src'), (Join-Path $output 'app/libs') -File -Recurse | Sort-Object FullName | ForEach-Object {
    if ($_.Length -ge 100MB) { throw "File exceeds GitHub's normal per-file limit: $($_.Name)" }
    [ordered]@{path=$_.FullName.Substring($output.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLower(); size=$_.Length}
})
$properties = [IO.File]::ReadAllText((Join-Path $source 'gradle.properties'))
function Get-UnityProperty([string]$Name) {
    $value = [regex]::Match($properties, '(?m)^' + [regex]::Escape($Name) + '=([^\r\n]+)').Groups[1].Value.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing Unity build property: $Name" }
    return $value
}
$editorVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'ProjectSettings/ProjectVersion.txt')), '(?m)^m_EditorVersion: ([^\r\n]+)').Groups[1].Value.Trim()
$metadata = [ordered]@{
    schemaVersion=1; unityVersion=$editorVersion; versionName=(Get-UnityProperty 'unity.versionName'); versionCode=[int](Get-UnityProperty 'unity.versionCode');
    applicationId=(Get-UnityProperty 'unity.applicationId'); architecture=(Get-UnityProperty 'unity.abiFilters');
    sourceCommit=(git -C $root rev-parse HEAD); generatedAt=(Get-Date).ToUniversalTime().ToString('o');
    sourceApkSha256=(Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLower();
    scope='Compiled Unity application snapshot. Gradle does not compile Assets/*.cs. Refresh after Unity changes.';
    files=$payloadFiles
}
[IO.File]::WriteAllText((Join-Path $output 'snapshot.json'), ($metadata | ConvertTo-Json -Depth 6), $utf8)
Write-Output ("Created Unity Android snapshot: " + $payloadFiles.Count + " files in " + $output)
