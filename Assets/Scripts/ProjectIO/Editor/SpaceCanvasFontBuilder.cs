using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SpaceCanvas.ProjectIO.Editor
{
    // Editor-only font asset maintenance utilities.
    /// <summary>
    /// 生成应用内中文 SDF 字库。
    /// 只预置项目实际使用的字符和一组常用字符，避免把完整 CJK 区域写入资源导致
    /// 数百 MB 的字库文件。运行时仍保留动态补字能力，用户输入的新字符会按需加入图集。
    /// </summary>
    public static class SpaceCanvasFontBuilder
    {
        public const string Path = "Assets/Resources/SpaceCanvasFonts/SpaceCanvasChineseSDF.asset";

        /// <summary>Warm the new UI labels without replacing the existing font GUID.</summary>
        public static void EnsurePresentationCharacters()
        {
            EnsureFont();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Path);
            var characters="全部基础家居厨房办公展会装饰上一页下一页专注编辑展开面板回正空间原型坊素材库添加可替换模型";
            foreach(var file in Directory.GetFiles("Assets/Scripts/ProjectIO","*.cs",SearchOption.AllDirectories)) characters+=File.ReadAllText(file);
            characters=new string(characters.Distinct().Where(c=>!char.IsControl(c)).ToArray());
            font.TryAddCharacters(characters, out string missing);
            foreach (var atlas in font.atlasTextures)
                if (atlas != null && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
        }

        public static void EnsureFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Path);
            // 旧版本曾把完整 CJK 区域写入资源，超过 64 MB 时自动重建为有限字库。
            // 这样不需要用户手动删除资源，也能在刷新 MR 布局时完成迁移。
            var absolutePath = System.IO.Path.Combine(Application.dataPath, "Resources/SpaceCanvasFonts/SpaceCanvasChineseSDF.asset");
            if (existing != null && File.Exists(absolutePath) && new FileInfo(absolutePath).Length <= 64L * 1024L * 1024L) return;
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(Path);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            const string essentials = "Packages/com.unity.textmeshpro/Package Resources/TMP Essential Resources.unitypackage";
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
            {
                AssetDatabase.ImportPackage(essentials, false);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
                    throw new InvalidOperationException("TMP Essential Resources 正在导入，导入完成后请再次刷新 MR 布局。");
            }
            var source = Resources.Load<Font>("SpaceCanvasFonts/NotoSansSC-Regular");
            if (source == null) throw new InvalidOperationException("缺少 NotoSansSC 中文源字体。");
            // 2048 图集足以覆盖本项目 UI 和示例文本；动态模式用于处理用户自定义工程名。
            var font = TMP_FontAsset.CreateFontAsset(source, 40, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "SpaceCanvasChineseSDF";
            var characters = string.Concat(Enumerable.Range(32, 95).Select(c => (char)c));
            // 覆盖项目脚本、格式文档和内置示例中真实出现的字符。
            foreach (var root in new[] { "Assets/Scripts/ProjectIO", "Assets/StreamingAssets/SpaceCanvasDocs", "Assets/StreamingAssets/ExampleProjects" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    var extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (extension == ".cs" || extension == ".md" || extension == ".json" || extension == ".spacecanvas")
                        characters += File.ReadAllText(file);
                }
            }
            // 常用中文和标点，覆盖新手直接输入的常见工程名，同时保持资源体积可控。
            characters += "空间原型坊素材库属性面板保存工程导入工程关于开源撤销移动旋转缩放删除客厅展位工位办公桌椅柜门窗墙地板沙发茶几电视灯植物展架海报台面电脑键盘鼠标显示器文件打印机会议桌椅子模型场景示例本地官方名称项目默认未知对象成功失败警告错误确认取消返回加载创建更新时间单位米厘米毫米网格开启关闭颜色透明度金属碰撞体分组编辑导出导入设置帮助版本格式规范自由修改二次开发窗口平面空间共享混合现实PICO";
            characters += "°·–—…（）【】《》：，。！？；“”‘’、·→←↑↓+-*/=_#%&@()[]{}<>\"'";
            characters = new string(characters.Distinct().Where(c=>!char.IsControl(c)).ToArray());
            font.TryAddCharacters(characters, out string missing);
            AssetDatabase.CreateAsset(font, Path);
            foreach (var atlas in font.atlasTextures)
            {
                atlas.name = "SpaceCanvasChineseAtlas";
                AssetDatabase.AddObjectToAsset(atlas, font);
            }
            font.material.name = "SpaceCanvasChineseSDF Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("中文字库不含部分源字符（已排除控制字符），数量=" + missing.Length);
        }
    }
}
