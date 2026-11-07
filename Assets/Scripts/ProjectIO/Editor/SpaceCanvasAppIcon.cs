using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Android;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>
    /// Keeps Android adaptive launcher icons and the APK fallback icon in the source project.
    /// Source PNGs live in Editor/Branding and are referenced by PlayerSettings only.
    /// Configure every build so Android builds through any menu cannot silently use Unity's icon.
    /// </summary>
    public sealed class SpaceCanvasAppIcon : IPreprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        private const string Folder = "Assets/Scripts/ProjectIO/Editor/Branding/";
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android) Configure();
        }

        public static void Configure()
        {
            var legacy = Load("SpaceCanvasIcon.png", false);
            var foreground = Load("SpaceCanvasForeground.png", true);
            var background = Load("SpaceCanvasBackground.png", false);
            // The default icon covers desktop previews and fallback launcher densities.
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { legacy }, IconKind.Any);
            var count = 0;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var icon in icons)
                {
                    if (icon.maxLayerCount > 1)
                    {
                        // Unity Android adaptive icon layer order: background, foreground,
                        // followed by optional monochrome on supported Unity versions.
                        var layers = new Texture2D[icon.maxLayerCount];
                        layers[0] = background;
                        layers[1] = foreground;
                        for (var layer = 2; layer < layers.Length; layer++) layers[layer] = foreground;
                        icon.SetTextures(layers);
                    }
                    else icon.SetTexture(legacy);
                    count++;
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Library");
            File.WriteAllText("Library/SpaceCanvas.AppIcon.txt", "Configured Android icon slots=" + count + "\n" + Folder);
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // Unity 6.6 removed the Legacy/Round PlayerSettings APIs, but still emits
            // a default Unity fallback PNG. APK viewers on Windows often display that PNG.
            // Use the official Gradle callback to supply our fallback in the launcher module.
            var gradleRoot = Directory.GetParent(path)?.FullName;
            if (gradleRoot == null) throw new BuildFailedException("无法定位 Android launcher 工程。");
            var resources = Path.Combine(gradleRoot, "launcher", "src", "main", "res", "mipmap-mdpi");
            Directory.CreateDirectory(resources);
            File.Copy(Path.GetFullPath(Folder + "SpaceCanvasIcon.png"), Path.Combine(resources, "app_icon.png"), true);
        }

        private static Texture2D Load(string name, bool alpha)
        {
            var path = Folder + name;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("缺少 SpaceCanvas 应用图标：" + path);
            if (importer.textureType != TextureImporterType.Default || importer.mipmapEnabled
                || importer.textureCompression != TextureImporterCompression.Uncompressed
                || importer.alphaIsTransparency != alpha || importer.maxTextureSize < 1024)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = alpha;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path)
                ?? throw new InvalidOperationException("无法读取 SpaceCanvas 应用图标：" + path);
        }
    }
}
