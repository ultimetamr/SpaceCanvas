using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SpaceCanvas.ProjectIO.Editor.Tests
{
    /// <summary>以运行时工厂为基准比较 36 个素材的网格/子层级/材质，而非只检查导入器自己的参数。</summary>
    public sealed class EditorImportParityTests
    {
        private Scene testScene, previousScene;
        private GameObject runtimeRoot;
        private int[] initialRootIds;
        private Func<SpaceCanvasEditorImporter.ObjectData, Transform, GameObject> previousFactory;
        private readonly System.Collections.Generic.List<string> generatedMaterials = new System.Collections.Generic.List<string>();

        [SetUp] public void Setup()
        {
            previousScene = SceneManager.GetActiveScene();
            // TestRunner 已切到隔离的 Untitled 场景；不能在其未保存时再 NewScene(Additive)。
            testScene = previousScene;
            initialRootIds = testScene.GetRootGameObjects().Select(go => SpaceCanvasUnityCompatibility.ObjectId(go)).ToArray();
            runtimeRoot = new GameObject("Runtime reference");
            previousFactory = SpaceCanvasEditorImporter.CreateObjectOverride;
            SpaceCanvasEditorImporter.CreateObjectOverride = SpaceCanvasEditorAssetBridge.Create;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/SpaceCanvasImportedMaterials" }))
                generatedMaterials.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        [TearDown] public void Cleanup()
        {
            SpaceCanvasEditorImporter.CreateObjectOverride = previousFactory;
            if (runtimeRoot != null) foreach (var item in runtimeRoot.GetComponentsInChildren<SpaceCanvasEditableObject>(true))
            {
                // 运行时参考材质仅用于本次比较；不释放持久资源。
                foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && !AssetDatabase.Contains(material)) Object.DestroyImmediate(material);
            }
            if (testScene.IsValid() && testScene.isLoaded && initialRootIds != null)
                foreach (var go in testScene.GetRootGameObjects())
                    if (!initialRootIds.Contains(SpaceCanvasUnityCompatibility.ObjectId(go))) Object.DestroyImmediate(go);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/SpaceCanvasImportedMaterials" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!generatedMaterials.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
            generatedMaterials.Clear();
        }

        [Test] public void EveryBuiltinUsesSameGeometrySlotsTexturesAndTintAsRuntime()
        {
            foreach (var assetId in SpaceCanvasBuiltinAssets.Ids)
            {
                var data = MakeObject(assetId, assetId);
                var expected = CreateRuntime(data);
                var project = new SpaceCanvasEditorImporter.ProjectData { projectName = "Parity " + assetId };
                project.objects.Add(data);
                SpaceCanvasEditorImporter.ImportIntoScene(project);
                var root = Selection.activeGameObject;
                var actual = root.transform.GetChild(0).gameObject;
                AssertModel(expected, actual, assetId);
                Assert.That(actual.GetComponent<SpaceCanvasEditableObject>(), Is.Null, "不能在 Awake 对烘焙后的外观再次染色");
            }
        }

        [Test] public void NestedMovedObjectsKeepLocalAndProjectRelativeTransforms()
        {
            var project = new SpaceCanvasEditorImporter.ProjectData { projectName = "Hierarchy parity" };
            var child = MakeObject("furniture.armchair", "child"); child.parentId = "parent";
            var parent = MakeObject("furniture.sofa", "parent");
            // 子节点排在父节点前，验证建立层级时不会把局部坐标当世界坐标转换。
            project.objects.Add(child); project.objects.Add(parent);
            SpaceCanvasEditorImporter.ImportIntoScene(project);
            var root = Selection.activeGameObject;
            var p = root.GetComponentsInChildren<Transform>(true).Single(t => t.name.EndsWith("[parent]"));
            var c = root.GetComponentsInChildren<Transform>(true).Single(t => t.name.EndsWith("[child]"));
            Assert.That(c.parent, Is.EqualTo(p));
            Near(c.localPosition, Vector(child.localPosition)); Near(p.localPosition, Vector(parent.localPosition));
            Near(c.position, p.TransformPoint(Vector(child.localPosition)));
            Near(c.localScale, Vector(child.localScale));
            Assert.That(Quaternion.Angle(c.localRotation, Rotation(child.localRotation)), Is.LessThan(.001f));
        }

        [Test] public void DownloadedHeadsetProjectKeepsEverySavedTransform()
        {
            var directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "DeviceProjects");
            var file = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.spacecanvas", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (file == null) Assert.Ignore("未提供头显保存文件；其余测试使用固定数据。");
            var project = SpaceCanvasEditorImporter.Parse(File.ReadAllText(file));
            SpaceCanvasEditorImporter.ImportIntoScene(project);
            var root = Selection.activeGameObject;
            foreach (var data in project.objects)
            {
                var item = root.GetComponentsInChildren<Transform>(true).Single(t => t.name.EndsWith("[" + data.id + "]"));
                Near(item.localPosition, Vector(data.localPosition)); Near(item.localScale, Vector(data.localScale));
                Assert.That(Quaternion.Angle(item.localRotation, Rotation(data.localRotation)), Is.LessThan(.001f), data.id);
            }
        }

        [Test] public void StandaloneFallbackKeepsCuratedMeshesAndAllMaterialSlots()
        {
            SpaceCanvasEditorImporter.CreateObjectOverride = null;
            var data = MakeObject("furniture.armchair", "standalone");
            var expected = CreateRuntime(data);
            var project = new SpaceCanvasEditorImporter.ProjectData(); project.objects.Add(data);
            SpaceCanvasEditorImporter.ImportIntoScene(project);
            AssertModel(expected, Selection.activeGameObject.transform.GetChild(0).gameObject, data.assetId);
        }

        [Test] public void MaterialAssetsSurviveSavingAndReloadingScene()
        {
            var data = MakeObject("furniture.armchair", "persisted");
            var expected = CreateRuntime(data);
            var project = new SpaceCanvasEditorImporter.ProjectData { projectName = "Persisted project" }; project.objects.Add(data);
            SpaceCanvasEditorImporter.ImportIntoScene(project);
            var path = AssetDatabase.GenerateUniqueAssetPath("Assets/SpaceCanvasImportParityTemp.unity");
            Scene loaded = default;
            try
            {
                Assert.That(EditorSceneManager.SaveScene(testScene, path), Is.True);
                loaded = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var root = loaded.GetRootGameObjects().Single(go => go.name == "Persisted project");
                AssertModel(expected, root.transform.GetChild(0).gameObject, data.assetId);
            }
            finally
            {
                if (loaded.IsValid() && loaded.isLoaded) EditorSceneManager.CloseScene(loaded, true);
                AssetDatabase.DeleteAsset(path);
            }
        }

        private GameObject CreateRuntime(SpaceCanvasEditorImporter.ObjectData data)
        {
            var go = SpaceCanvasAssetLibrary.CreateInstance(data.assetId, runtimeRoot.transform, out _);
            go.GetComponent<SpaceCanvasEditableObject>().Apply(new SceneObject
            {
                id = data.id, assetId = data.assetId, localPosition = data.localPosition, localRotation = data.localRotation,
                localScale = data.localScale, colorHex = data.colorHex, alpha = data.alpha, metallic = data.metallic, colliderEnabled = data.colliderEnabled
            });
            return go;
        }

        private static void AssertModel(GameObject expected, GameObject actual, string label)
        {
            var a = expected.GetComponentsInChildren<MeshFilter>(true); var b = actual.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(b.Length, Is.EqualTo(a.Length), label + " mesh count");
            for (var i = 0; i < a.Length; i++)
            {
                Assert.That(b[i].sharedMesh, Is.EqualTo(a[i].sharedMesh), label + " mesh");
                Near(b[i].transform.localPosition, a[i].transform.localPosition);
                Near(b[i].transform.localScale, a[i].transform.localScale);
            }
            var er = expected.GetComponentsInChildren<Renderer>(true); var ar = actual.GetComponentsInChildren<Renderer>(true);
            Assert.That(ar.Length, Is.EqualTo(er.Length), label + " renderers");
            for (var r = 0; r < er.Length; r++)
            {
                var em = er[r].sharedMaterials; var am = ar[r].sharedMaterials;
                Assert.That(am.Length, Is.EqualTo(em.Length), label + " slots");
                for (var m = 0; m < em.Length; m++)
                {
                    Assert.That(am[m].shader, Is.EqualTo(em[m].shader), label + " shader");
                    foreach (var property in new[] { "_Color", "_BaseColor" })
                        if (em[m].HasProperty(property)) Assert.That(Vector4.Distance(am[m].GetColor(property), em[m].GetColor(property)), Is.LessThan(.00001f), label + property);
                    foreach (var property in new[] { "_MainTex", "_BaseMap", "_BumpMap", "_MetallicGlossMap" })
                        if (em[m].HasProperty(property)) Assert.That(am[m].GetTexture(property), Is.EqualTo(em[m].GetTexture(property)), label + property);
                    if (em[m].HasProperty("_Metallic")) Assert.That(am[m].GetFloat("_Metallic"), Is.EqualTo(em[m].GetFloat("_Metallic")), label + " metallic");
                }
            }
            Assert.That(actual.GetComponentsInChildren<Collider>(true).Select(c => c.enabled),
                Is.EqualTo(expected.GetComponentsInChildren<Collider>(true).Select(c => c.enabled)), label + " colliders");
        }

        private static SpaceCanvasEditorImporter.ObjectData MakeObject(string assetId, string id)
        {
            var q = Quaternion.Euler(17, 63, -9);
            return new SpaceCanvasEditorImporter.ObjectData
            {
                id = id, assetId = assetId, localPosition = new[] { -7.695384f, -2.1585803f, -8.296579f },
                localRotation = new[] { q.x, q.y, q.z, q.w }, localScale = new[] { 1.2f, .7f, 1.1f },
                colorHex = "#799781", alpha = .72f, metallic = .38f, colliderEnabled = false
            };
        }
        private static Vector3 Vector(float[] v) => new Vector3(v[0], v[1], v[2]);
        private static Quaternion Rotation(float[] q) => new Quaternion(q[0], q[1], q[2], q[3]);
        private static void Near(Vector3 a, Vector3 b) => Assert.That((a - b).sqrMagnitude, Is.LessThan(1e-8f));
    }
}
