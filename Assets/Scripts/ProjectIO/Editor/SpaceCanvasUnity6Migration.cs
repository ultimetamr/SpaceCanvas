using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>显式的一次性升级入口；只更新官方 TMP 必需资源，保留工程的字体配置。</summary>
    public static class SpaceCanvasUnity6Migration
    {
        public static void UpgradeTextResources()
        {
#if UNITY_6000_6_OR_NEWER
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            if (package == null) throw new InvalidOperationException("无法定位 Unity UGUI/TMP 包。");
            var resourcePath = Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(resourcePath)) throw new FileNotFoundException("未找到官方 TMP 必需资源。", resourcePath);
            var settings = TMP_Settings.instance;
            var settingsPath = settings == null ? null : AssetDatabase.GetAssetPath(settings);
            var previous = settings == null ? null : EditorJsonUtility.ToJson(settings);
            UnityEditor.AssetPackage.Package.Import(resourcePath, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!string.IsNullOrEmpty(previous))
            {
                settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(settingsPath);
                if (settings == null) throw new InvalidOperationException("TMP 升级后无法读取原工程字体配置。");
                EditorJsonUtility.FromJsonOverwrite(previous, settings);
                // 与官方 TMP 导入完成回调相同：资源包本身不写版本，完成后显式标记。
                typeof(TMP_Settings).GetMethod("SetAssetVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(settings, null);
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/Unity6-TextResources.txt", "PASS: 官方 TMP 必需资源已升级，保留原字体配置。\n");
            Debug.Log("SpaceCanvas Unity 6：TMP 必需资源升级完成。");
#endif
        }
    }
}
