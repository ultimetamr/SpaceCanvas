# Restore locally licensed SDKs without redistributing their source.
# Run before opening Unity. Existing SDK directories are never deleted/replaced.
[CmdletBinding()]
param(
    [string]$SdkPath,
    [string]$SpatialAdapterPath,
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$root = [IO.Path]::GetFullPath($ProjectRoot)
if (!(Test-Path -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt'))) {
    throw 'ProjectRoot must point to the SpaceCanvas Unity project.'
}

function Restore-Package([string]$Source, [string]$Relative, [string]$Name, [string]$Version) {
    $target = Join-Path $root $Relative
    if (!(Test-Path -LiteralPath $target)) {
        if ($VerifyOnly -or [string]::IsNullOrWhiteSpace($Source)) {
            throw "Missing $Relative. Supply the licensed SDK source directory; see docs/SETUP.md."
        }
        $sourceRoot = (Resolve-Path -LiteralPath $Source).Path
        $sourceInfo = Get-Content -LiteralPath (Join-Path $sourceRoot 'package.json') -Raw | ConvertFrom-Json
        if ($sourceInfo.name -ne $Name -or $sourceInfo.version -ne $Version) {
            throw "Unexpected package/version at $sourceRoot. Expected $Name $Version."
        }
        Copy-Item -LiteralPath $sourceRoot -Destination $target -Recurse
    }
    $info = Get-Content -LiteralPath (Join-Path $target 'package.json') -Raw | ConvertFrom-Json
    if ($info.name -ne $Name -or $info.version -ne $Version) {
        throw "Unexpected installed package/version at $target. Expected $Name $Version."
    }
    Write-Output "Found $Name $Version"
}

# Patches are computed in memory before writing, so an unsupported source layout
# does not leave half of the compatibility edits applied.
$pending = @{}
function Read-Source([string]$Relative) {
    $path = Join-Path $root $Relative
    if (!(Test-Path -LiteralPath $path)) { throw "Expected SDK source missing: $Relative" }
    return [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
}
function Queue-Source([string]$Relative, [string]$Original, [string]$Updated) {
    if ($Original -ne $Updated) {
        if ($VerifyOnly) { throw "Compatibility patch required: $Relative. Run without -VerifyOnly." }
        $pending[$Relative] = $Updated
    }
}
function Require-Text([string]$Source, [string]$Expected, [string]$Relative) {
    if (!$Source.Contains($Expected)) { throw "Unsupported SDK source layout: $Relative. Missing expected compatibility marker: $Expected" }
}

Restore-Package $SdkPath 'Packages/PICO-Unity-SDK-6.1.1-20260903' 'com.bytedance.pico.xr' '6.1.1'
Restore-Package $SpatialAdapterPath 'Packages/com.bytedance.pico.spatialadapter' 'com.bytedance.pico.spatialadapter' '6.1.0'

$sdk = 'Packages/PICO-Unity-SDK-6.1.1-20260903/'
$adapter = 'Packages/com.bytedance.pico.spatialadapter/Exporter/Editor/'

$file = $sdk + 'Runtime/PICOFeature.cs'
$original = Read-Source $file
$updated = $original
Require-Text $original 'CustomRuntimeLoaderName' $file
if (!(Test-Path -LiteralPath (Join-Path $root ($sdk + 'Runtime/Android/LoaderForUnitySDK.aar')))) {
    throw 'LoaderForUnitySDK.aar is missing. Restore the matching PICO SDK distribution.'
}
$updated = [regex]::Replace($updated, 'CustomRuntimeLoaderName\s*=\s*"[^"]+"', 'CustomRuntimeLoaderName = "LoaderForUnitySDK"')
Queue-Source $file $original $updated

$file = $sdk + 'Enterprise/Sample/VirtualDisplay/OpenXR/VirtualDisplayDemo_OpenXR.cs'
$original = Read-Source $file
$updated = $original
if (!$original.Contains('CompositionLayerManager.TryGetLayerId')) {
    $old = 'surface = OpenXRLayerUtility.GetLayerAndroidSurfaceObject(_overlay.GetInstanceID());'
    Require-Text $original $old $file
    $replacement = @'
#if UNITY_6000_6_OR_NEWER
            if (!Unity.XR.CompositionLayers.Services.CompositionLayerManager.TryGetLayerId(_overlay, out var layerId)) return false;
            surface = OpenXRLayerUtility.GetLayerAndroidSurfaceObject(layerId);
#else
            surface = OpenXRLayerUtility.GetLayerAndroidSurfaceObject(_overlay.GetInstanceID());
#endif
'@
    $updated = $updated.Replace($old, $replacement.Replace("`r`n", "`n"))
}
Queue-Source $file $original $updated

$file = $adapter + 'Exporter/SpatialAdapterPrefabExporter.cs'
$original = Read-Source $file
$updated = $original
if (!$original.Contains('SpatialAdapterUnity6Ids.Get(prefab)')) {
    $old = 'idComponent.PrefabIdentifier = prefab.GetInstanceID();'
    Require-Text $original $old $file
    $replacement = @'
#if UNITY_6000_6_OR_NEWER
            idComponent.PrefabIdentifier = SpatialAdapterUnity6Ids.Get(prefab);
#else
            idComponent.PrefabIdentifier = prefab.GetInstanceID();
#endif
'@
    $updated = $updated.Replace($old, $replacement.Replace("`r`n", "`n"))
}
Queue-Source $file $original $updated

$file = $adapter + 'Exporter/SpatialAdapterShaderGraphExporter.cs'
$original = Read-Source $file
$updated = $original
if (!$original.Contains('SpatialAdapterUnity6Ids.Get(shader)')) {
    $old = 'int shaderId = shader.GetInstanceID();'
    Require-Text $original $old $file
    $replacement = @'
#if UNITY_6000_6_OR_NEWER
            int shaderId = SpatialAdapterUnity6Ids.Get(shader);
#else
            int shaderId = shader.GetInstanceID();
#endif
'@
    $updated = $updated.Replace($old, $replacement.Replace("`r`n", "`n"))
}
Queue-Source $file $original $updated

$file = $adapter + 'Settings/SpatialAdapterRuntimeSettings.cs'
$original = Read-Source $file
$updated = $original
if (!$original.Contains('private static bool HasReferenceId(')) {
    Require-Text $original 'property.objectReferenceInstanceIDValue != 0' $file
    Require-Text $original 'property.objectReferenceInstanceIDValue = 0;' $file
    Require-Text $original 'public static bool IsPackageInstalled(' $file
    $updated = $updated.Replace('property.objectReferenceInstanceIDValue != 0', 'HasReferenceId(property)')
    $clear = @'
#if UNITY_6000_6_OR_NEWER
                property.objectReferenceEntityIdValue = EntityId.None;
#else
                property.objectReferenceInstanceIDValue = 0;
#endif
'@
    $updated = $updated.Replace('property.objectReferenceInstanceIDValue = 0;', $clear.Replace("`r`n", "`n"))
    $helper = @'
private static bool HasReferenceId(SerializedProperty property)
        {
#if UNITY_6000_6_OR_NEWER
            return property.objectReferenceEntityIdValue != EntityId.None;
#else
            return property.objectReferenceInstanceIDValue != 0;
#endif
        }

        public static bool IsPackageInstalled(
'@
    $updated = $updated.Replace('public static bool IsPackageInstalled(', $helper.Replace("`r`n", "`n"))
}
Require-Text $updated 'property.objectReferenceEntityIdValue = EntityId.None;' $file
Require-Text $updated 'property.objectReferenceEntityIdValue != EntityId.None' $file
Queue-Source $file $original $updated

# Original SpaceCanvas helper, supplied independently of the proprietary SDK.
$file = $adapter + 'Exporter/SpatialAdapterUnity6Ids.cs'
$helper = @'
#if UNITY_6000_6_OR_NEWER
using System.Collections.Generic;
using UnityEngine;

namespace ByteDance.PICO.SpatialAdapter.Exporter.Editor
{
    // Assign collision-free session tokens; never truncate a 64-bit EntityId.
    internal static class SpatialAdapterUnity6Ids
    {
        private static readonly Dictionary<EntityId, int> Ids = new Dictionary<EntityId, int>();
        private static int nextId;
        internal static int Get(Object asset)
        {
            var entity = asset.GetEntityId();
            if (!Ids.TryGetValue(entity, out var id)) { id = ++nextId; Ids.Add(entity, id); }
            return id;
        }
    }
}
#endif
'@
$helperPath = Join-Path $root $file
if (!(Test-Path -LiteralPath $helperPath)) {
    if ($VerifyOnly) { throw "Missing original compatibility helper: $file" }
    $pending[$file] = $helper.Replace("`r`n", "`n")
} else {
    Require-Text (Read-Source $file) 'Dictionary<EntityId, int>' $file
}

foreach ($relative in $pending.Keys) {
    [IO.File]::WriteAllText((Join-Path $root $relative), $pending[$relative], $utf8)
    Write-Output "Patched $relative"
}
Write-Output ("Dependencies ready. Compatibility files updated: " + $pending.Count)
