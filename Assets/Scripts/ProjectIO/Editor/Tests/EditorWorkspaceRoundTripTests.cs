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
    /// <summary>覆盖真正的菜单宿主路径，验证头显 JSON → 工作台 → 磁盘 Scene → 启动注册。</summary>
    public sealed class EditorWorkspaceRoundTripTests
    {
        private Scene scene, previousScene;
        private SpaceCanvasWorkspace workspace;
        private GameObject anchor, sentinel;
        private Transform previousContent;
        private SpaceCanvasProjectRoot previousProject;
        private string scenePath;
        private string[] originalMaterials;

        [SetUp] public void Setup()
        {
            previousScene = SceneManager.GetActiveScene();
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            previousContent = SpaceCanvasRuntime.ContentRoot;
            previousProject = SpaceCanvasRuntime.CurrentProjectRoot;
            originalMaterials = AssetDatabase.FindAssets("t:Material", new[] { "Assets/SpaceCanvasImportedMaterials" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            anchor = new GameObject("RoundTrip workbench anchor");
            anchor.transform.SetPositionAndRotation(new Vector3(2, 1.3f, -4), Quaternion.Euler(0, 37, 0));
            anchor.transform.localScale = Vector3.one * .83f;
            var manager = new GameObject("RoundTrip workspace"); manager.transform.SetParent(anchor.transform, false);
            workspace = manager.AddComponent<SpaceCanvasWorkspace>();
            workspace.contentRoot = new GameObject("Editable Content").transform;
            workspace.contentRoot.SetParent(manager.transform, false);
            workspace.contentRoot.localPosition = new Vector3(.1f, -.2f, .3f);
            workspace.contentRoot.localScale = Vector3.one * .27f;
            sentinel = new GameObject("Camera or UI must survive"); sentinel.transform.SetParent(anchor.transform, false);
            SpaceCanvasRuntime.ContentRoot = workspace.contentRoot;
            SpaceCanvasRuntime.SetCurrentProjectRoot(null);
            Assert.That(SceneImporter.ImportProject(new SpaceCanvasProject { projectName = "before", objects = { new SceneObject { id = "old" } } }, out var error, false), Is.True, error);
        }

        [TearDown] public void Cleanup()
        {
            Undo.ClearAll();
            if (anchor != null) Object.DestroyImmediate(anchor);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (!string.IsNullOrEmpty(scenePath)) AssetDatabase.DeleteAsset(scenePath);
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/SpaceCanvasImportedMaterials" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (originalMaterials != null && !originalMaterials.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
            SpaceCanvasRuntime.ContentRoot = previousContent;
            SpaceCanvasRuntime.SetCurrentProjectRoot(previousProject);
        }

        [Test] public void MenuHostReplacesEditableContentAndPreservesWorkbenchFrame()
        {
            var project = Fixture();
            var beforeMatrix = workspace.contentRoot.localToWorldMatrix;
            SpaceCanvasEditorImporter.ImportForCurrentScene(SpaceCanvasEditorImporter.Parse(ProjectSerializer.Serialize(project)));
            Assert.That(sentinel, Is.Not.Null);
            Assert.That(workspace.contentRoot.localToWorldMatrix, Is.EqualTo(beforeMatrix));
            Assert.That(workspace.contentRoot.GetComponentsInChildren<SpaceCanvasProjectRoot>(true).Length, Is.EqualTo(1));
            Assert.That(SpaceCanvasRuntime.CurrentProjectRoot.transform.parent, Is.EqualTo(workspace.contentRoot));
            AssertSnapshot(project);
            var parent = SpaceCanvasRuntime.GetEditableObjects().Single(item => item.ObjectId == "parent");
            var child = SpaceCanvasRuntime.GetEditableObjects().Single(item => item.ObjectId == "child");
            Assert.That(child.transform.parent, Is.EqualTo(parent.transform));
            Near(parent.transform.position, workspace.contentRoot.TransformPoint(Vector(project.objects[1].localPosition)));
            Near(parent.transform.lossyScale, Vector3.Scale(workspace.contentRoot.lossyScale, Vector(project.objects[1].localScale)));
        }

        [Test] public void OneUnityUndoRestoresOriginalProject()
        {
            SpaceCanvasEditorAssetBridge.ImportToWorkspace(SpaceCanvasEditorImporter.Parse(ProjectSerializer.Serialize(Fixture())));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(workspace.contentRoot.GetComponentsInChildren<SpaceCanvasEditableObject>(true).Select(item => item.ObjectId), Is.EquivalentTo(new[] { "old" }));
            Assert.That(sentinel, Is.Not.Null);
            Assert.That(SceneExporter.TryCaptureCurrentScene(null, out var restored, out var error), Is.True, error);
            Assert.That(restored.projectName, Is.EqualTo("before"));
        }

        [Test] public void SceneReloadAndStartupKeepAllTransformsGroupsAndMaterialColors()
        {
            var project = Fixture();
            SpaceCanvasEditorAssetBridge.ImportToWorkspace(SpaceCanvasEditorImporter.Parse(ProjectSerializer.Serialize(project)));
            var expectedColors = SpaceCanvasRuntime.GetEditableObjects().Single(item => item.ObjectId == "parent")
                .GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Select(m => m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color).ToArray();
            scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/SpaceCanvasRoundTripTemp.unity");
            Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
            var loaded = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var restored = loaded.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<SpaceCanvasWorkspace>(true)).Single();
                // Unity 6 禁止 EditMode SendMessage 派发 Awake；直接调用同一生命周期方法。
                typeof(SpaceCanvasWorkspace).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(restored, null);
                foreach (var item in SpaceCanvasRuntime.GetEditableObjects())
                    typeof(SpaceCanvasEditableObject).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(item, null);
                AssertSnapshot(project);
                var actualColors = SpaceCanvasRuntime.GetEditableObjects().Single(item => item.ObjectId == "parent")
                    .GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                    .Select(m => m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color).ToArray();
                Assert.That(actualColors.Length, Is.EqualTo(expectedColors.Length));
                for (var i = 0; i < expectedColors.Length; i++)
                    Assert.That(Vector4.Distance(actualColors[i], expectedColors[i]), Is.LessThan(.0001f), "重载后不可重复叠加染色");
            }
            finally { EditorSceneManager.CloseScene(loaded, true); if (scene.IsValid() && scene.isLoaded) SceneManager.SetActiveScene(scene); }
        }

        [Test] public void BuildPreparationSavesEditedWorkspaceScene()
        {
            scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/SpaceCanvasRoundTripTemp.unity");
            Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
            var project = Fixture();
            SpaceCanvasEditorAssetBridge.ImportToWorkspace(SpaceCanvasEditorImporter.Parse(ProjectSerializer.Serialize(project)));
            Assert.That(scene.isDirty, Is.True);
            SpaceCanvasEditorAssetBridge.SaveWorkspaceSceneForBuild(scenePath);
            Assert.That(scene.isDirty, Is.False);
            var loaded = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var restored = loaded.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<SpaceCanvasWorkspace>(true)).Single();
                SpaceCanvasRuntime.ContentRoot = restored.contentRoot;
                SpaceCanvasRuntime.SetCurrentProjectRoot(restored.contentRoot.GetComponentInChildren<SpaceCanvasProjectRoot>());
                AssertSnapshot(project);
            }
            finally { EditorSceneManager.CloseScene(loaded, true); if (scene.IsValid() && scene.isLoaded) SceneManager.SetActiveScene(scene); }
        }

        [Test] public void InvalidHierarchyDoesNotDestroyExistingScene()
        {
            var project = Fixture(); project.objects[0].parentId = "child";
            Assert.Throws<FormatException>(() => SpaceCanvasEditorAssetBridge.ImportToWorkspace(SpaceCanvasEditorImporter.Parse(ProjectSerializer.Serialize(project))));
            Assert.That(workspace.contentRoot.GetComponentsInChildren<SpaceCanvasEditableObject>(true).Single().ObjectId, Is.EqualTo("old"));
        }

        [Test] public void LatestDownloadedHeadsetSaveSurvivesWorkspaceImportAndCapture()
        {
            var directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "DeviceProjects");
            if (!Directory.Exists(directory)) Assert.Ignore("没有可用于真机参数比对的下载存档。");
            var entry = SpaceCanvasEditorImporter.ProjectFileVersion.ReadSorted(Directory.GetFiles(directory, "*.spacecanvas", SearchOption.AllDirectories))
                .FirstOrDefault(item => item.IsValid);
            if (entry == null) Assert.Ignore("没有合法的头显存档。");
            var expected = ProjectSerializer.Deserialize(File.ReadAllText(entry.FilePath));
            SpaceCanvasEditorImporter.ImportForCurrentScene(entry.Project);
            AssertSnapshot(expected);
        }

        private static SpaceCanvasProject Fixture()
        {
            var q = Quaternion.Euler(0, 63, 0);
            return new SpaceCanvasProject
            {
                projectName = "PICO adjusted scene", createTime = 123,
                settings = new SceneSettings { unitSystem = SceneUnitSystem.Millimeter, gridEnabled = false, gridSize = .125f },
                objects =
                {
                    new SceneObject { id = "child", assetId = "primitive.cube", parentId = "parent", localPosition = new[] {.3f, .2f, -.8f}, localScale = new[] {.22f, .6f, .15f} },
                    new SceneObject { id = "parent", assetId = "furniture.armchair", localPosition = new[] {-2.4f, .7f, 3.1f}, localRotation = new[] {q.x, q.y, q.z, q.w}, localScale = new[] {1.23f, .75f, 1.4f}, colorHex = "#729083", alpha = .8f }
                },
                groups = { new SceneGroup { groupId = "g", groupName = "头显组合", childIds = { "parent", "child" } } }
            };
        }

        private static void AssertSnapshot(SpaceCanvasProject project)
        {
            Assert.That(SceneExporter.TryCaptureCurrentScene(null, out var actual, out var error), Is.True, error);
            Assert.That(actual.projectName, Is.EqualTo(project.projectName));
            Assert.That(actual.createTime, Is.EqualTo(project.createTime));
            Assert.That(actual.objects.Count, Is.EqualTo(project.objects.Count));
            foreach (var expected in project.objects)
            {
                var item = actual.objects.Single(o => o.id == expected.id);
                Assert.That(item.assetId, Is.EqualTo(expected.assetId)); Assert.That(item.parentId, Is.EqualTo(expected.parentId));
                Assert.That(item.localPosition, Is.EqualTo(expected.localPosition).Within(.00001f));
                Assert.That(item.localRotation, Is.EqualTo(expected.localRotation).Within(.00001f));
                Assert.That(item.localScale, Is.EqualTo(expected.localScale).Within(.00001f));
            }
            Assert.That(actual.settings.unitSystem, Is.EqualTo(project.settings.unitSystem));
            Assert.That(actual.settings.gridEnabled, Is.EqualTo(project.settings.gridEnabled));
            Assert.That(actual.settings.gridSize, Is.EqualTo(project.settings.gridSize));
            Assert.That(actual.groups.Count, Is.EqualTo(project.groups.Count));
            foreach (var expected in project.groups)
            {
                var group = actual.groups.Single(g => g.groupId == expected.groupId);
                Assert.That(group.groupName, Is.EqualTo(expected.groupName));
                Assert.That(group.childIds, Is.EquivalentTo(expected.childIds));
            }
        }
        private static Vector3 Vector(float[] value) => new Vector3(value[0], value[1], value[2]);
        private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f));
    }
}
