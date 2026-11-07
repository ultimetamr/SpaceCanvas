using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor
{
    /// <summary>
    /// 将带授权记录的 Poly Haven FBX 转为移动端 URP Prefab。仅编辑器运行，无运行时下载依赖。
    /// 材质映射来自同模型的 glTF 元数据；将 glTF 的 B 金属/G 粗糙打包为 Unity R 金属/A 光滑度。
    /// 源资产独立于 Resources，只有最终 prefab 引用的网格/贴图进入构建。
    /// </summary>
    [InitializeOnLoad]
    public static class SpaceCanvasCuratedAssetBuilder
    {
        private const string Art = "Assets/SpaceCanvasArt/PolyHaven/";
        private const string Prefabs = "Assets/Resources/SpaceCanvasCurated/";
        private const string Request = "Library/SpaceCanvas.CuratedAssets.request";
        private const string Report = "Library/SpaceCanvas.CuratedAssets.txt";

        static SpaceCanvasCuratedAssetBuilder() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Move(Request, Request + "." + DateTime.UtcNow.Ticks + ".started");
            try { Build(); }
            catch (Exception ex) { File.WriteAllText(Report, "FAIL\n" + ex); Debug.LogException(ex); }
        }

#if SPACECANVAS_DEVELOPER_MENUS
        [MenuItem("Tools/SpaceCanvas/素材/接入真实家具并更新场景")]
#endif
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出播放模式。");
            Directory.CreateDirectory(Prefabs);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var catalog = JArray.Parse(File.ReadAllText(Art + "catalog.json"));
            var report = new List<string>();
            foreach (var entry in catalog)
            {
                var id = (string)entry["assetId"];
                var source = (string)entry["sourceId"];
                var folder = Art + source + "/";
                var modelPath = folder + source + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
                if (importer == null) throw new FileNotFoundException("缺少真实模型", modelPath);
                importer.importAnimation = false;
                importer.isReadable = false;
                importer.meshCompression = ModelImporterMeshCompression.Low;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                var metadata = JObject.Parse(File.ReadAllText(folder + "source.gltf.txt"));
                var materials = BuildMaterials(folder, metadata);
                var root = new GameObject(SpaceCanvasBuiltinAssets.DisplayName(id));
                try
                {
                    var marker = root.AddComponent<SpaceCanvasCuratedAsset>(); marker.sourceId = source;
                    var child = UnityEngine.Object.Instantiate(model, root.transform);
                    child.name = "真实模型 " + source;
                    foreach (var renderer in child.GetComponentsInChildren<Renderer>(true))
                    {
                        var slots = renderer.sharedMaterials;
                        for (var i = 0; i < slots.Length; i++)
                        {
                            var name = slots[i] == null ? "" : slots[i].name.Replace(" (Instance)", "");
                            if (!materials.TryGetValue(name, out var material))
                            {
                                if (materials.Count == 1) material = materials.Values.First();
                                else throw new InvalidOperationException(source + " 材质槽无法映射：" + name + "，可用：" + string.Join(",", materials.Keys));
                            }
                            slots[i] = material;
                        }
                        renderer.sharedMaterials = slots;
                    }
                    var bounds = BoundsOf(root);
                    var axis = (int)entry["axis"];
                    var scale = (float)entry["extent"] / Mathf.Max(.0001f, bounds.size[axis]);
                    child.transform.localScale *= scale;
                    bounds = BoundsOf(root);
                    child.transform.localPosition += new Vector3(-bounds.center.x, (float)entry["bottom"] - bounds.min.y, -bounds.center.z);
                    bounds = BoundsOf(root);
                    var collider = root.AddComponent<BoxCollider>(); collider.center = bounds.center; collider.size = bounds.size;
                    PrefabUtility.SaveAsPrefabAsset(root, Prefabs + id + ".prefab");
                    var triangles = child.GetComponentsInChildren<MeshFilter>(true).Sum(x => x.sharedMesh == null ? 0L : Enumerable.Range(0, x.sharedMesh.subMeshCount).Sum(i => (long)x.sharedMesh.GetIndexCount(i) / 3));
                    report.Add(id + " <- " + source + " triangles=" + triangles + " bounds=" + bounds.size.ToString("F3"));
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            // This is the actual current scene migration, not only a prefab rebuild.
            SpaceCanvasSceneBootstrap.UpgradeCurrentSceneAssets();
            var workspace = UnityEngine.Object.FindObjectOfType<SpaceCanvasWorkspace>();
            var count = workspace == null ? 0 : workspace.contentRoot.GetComponentsInChildren<SpaceCanvasCuratedAsset>(true).Length;
            report.Insert(0, "PASS Prefabs=" + catalog.Count + " SceneCurated=" + count);
            File.WriteAllLines(Report, report);
            Debug.Log("SpaceCanvas 真实素材接入完成：" + catalog.Count + " 个素材映射，当前场景 " + count + " 个真实模型。");
            RenderPreview();
        }

        private static Dictionary<string, Material> BuildMaterials(string folder, JObject gltf)
        {
            var result = new Dictionary<string, Material>();
            Directory.CreateDirectory(folder + "Materials");
            foreach (var token in (JArray)gltf["materials"])
            {
                var name = (string)token["name"];
                var pbr = token["pbrMetallicRoughness"];
                var albedoPath = TexturePath(folder, gltf, pbr?["baseColorTexture"]);
                var normalPath = TexturePath(folder, gltf, token["normalTexture"]);
                var packedSource = TexturePath(folder, gltf, pbr?["metallicRoughnessTexture"]);
                ConfigureTexture(albedoPath, false, true);
                ConfigureTexture(normalPath, true, false);
                var materialPath = folder + "Materials/" + name + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (mat == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    if (shader == null) throw new InvalidOperationException("缺少 PBR Shader。");
                    mat = new Material(shader); AssetDatabase.CreateAsset(mat, materialPath);
                }
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
                mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
                var transparent = (string)token["alphaMode"] == "BLEND";
                var color = new Color(1, 1, 1, transparent ? .08f : 1);
                mat.SetColor("_BaseColor", color); mat.SetColor("_Color", color);
                mat.SetFloat("_Cull", (bool?)token["doubleSided"] == true ? 0 : 2);
                mat.SetFloat("_Metallic", (float?)pbr?["metallicFactor"] ?? 1);
                mat.SetFloat("_Smoothness", .45f);
                if (!string.IsNullOrEmpty(normalPath))
                {
                    mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
                    mat.SetFloat("_BumpScale", .65f); mat.EnableKeyword("_NORMALMAP");
                }
                if (!string.IsNullOrEmpty(packedSource))
                {
                    var packed = PackMetallicSmoothness(packedSource, folder + "Materials/" + name + "_metallicSmoothness.png", (float?)pbr?["metallicFactor"] ?? 1);
                    mat.SetTexture("_MetallicGlossMap", packed); mat.SetFloat("_Smoothness", 1); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                if (transparent)
                {
                    mat.SetFloat("_Surface", 1); mat.SetFloat("_ZWrite", 0);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 3000;
                }
                EditorUtility.SetDirty(mat);
                result.Add(name, mat);
            }
            return result;
        }

        private static string TexturePath(string folder, JObject gltf, JToken slot)
        {
            if (slot == null) return null;
            var texture = gltf["textures"][(int)slot["index"]];
            return folder + (string)gltf["images"][(int)texture["source"]]["uri"];
        }

        private static void ConfigureTexture(string path, bool normal, bool srgb)
        {
            if (string.IsNullOrEmpty(path)) return;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("缺少贴图", path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb; importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true; importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Compressed;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android); importer.SaveAndReimport();
        }

        private static Texture2D PackMetallicSmoothness(string source, string output, float metallicFactor)
        {
            // Decode source bytes without changing the source importer or keeping runtime read access.
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(source))) throw new InvalidDataException("无法读取贴图 " + source);
                var pixels = image.GetPixels32();
                for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)(pixels[i].b * metallicFactor), 0, 0, (byte)(255 - pixels[i].g));
                image.SetPixels32(pixels); image.Apply(); File.WriteAllBytes(output, image.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTexture(output, false, false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(output);
        }

        private static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidDataException("模型没有 Renderer：" + root.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void RenderPreview()
        {
            // Calls the existing OpenXR editor preview without taking a runtime assembly dependency.
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("SpaceCanvas.ProjectIO.XR.Editor.SpaceCanvasFullSpaceBuilder")).FirstOrDefault(x => x != null);
            type?.GetMethod("RenderPresentationPreview")?.Invoke(null, null);
        }
    }
}
