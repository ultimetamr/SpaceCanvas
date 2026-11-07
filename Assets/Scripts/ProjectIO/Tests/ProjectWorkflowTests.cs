using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Tests
{
    public sealed class ProjectWorkflowTests
    {
        private Transform originalContent;
        private SpaceCanvasProjectRoot originalProject;
        private GameObject container;

        [SetUp]
        public void SetUp()
        {
            originalContent = SpaceCanvasRuntime.ContentRoot;
            originalProject = SpaceCanvasRuntime.CurrentProjectRoot;
            container = new GameObject("ProjectIO_TestContainer");
            SpaceCanvasRuntime.ContentRoot = container.transform;
            SpaceCanvasRuntime.SetCurrentProjectRoot(null);
            SpaceCanvasUndoService.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(container);
            SpaceCanvasRuntime.ContentRoot = originalContent;
            SpaceCanvasRuntime.SetCurrentProjectRoot(originalProject);
            SpaceCanvasUndoService.Clear();
        }

        [Test]
        public void EveryFieldSurvivesSerialization()
        {
            var project = new SpaceCanvasProject { projectName = "中文工程", createTime = 123, updateTime = 456 };
            project.settings = new SceneSettings { unitSystem = SceneUnitSystem.Millimeter, gridEnabled = false, gridSize = .125f };
            project.objects.Add(new SceneObject { id = "p", assetId = "furniture.sofa", localPosition = new[] { 1.25f, -2f, .5f }, localRotation = new[] { 0f, .6f, 0f, .8f }, localScale = new[] { 2f, 3f, 4f }, colorHex = "#287FAC", alpha = .42f, metallic = .87f, colliderEnabled = false });
            project.objects.Add(new SceneObject { id = "child", parentId = "p", assetId = "decor.book" });
            project.groups.Add(new SceneGroup { groupId = "g", groupName = "组合", childIds = new System.Collections.Generic.List<string> { "p", "child" } });
            var json = ProjectSerializer.Serialize(project);
            Assert.That(JToken.DeepEquals(JToken.Parse(json), JToken.Parse(ProjectSerializer.Serialize(ProjectSerializer.Deserialize(json)))), Is.True);
        }

        [TestCase("livingroom_demo.spacecanvas")]
        [TestCase("booth_demo.spacecanvas")]
        [TestCase("desk_demo.spacecanvas")]
        public void OfficialExampleImportsAndExportsWithoutParameterLoss(string file)
        {
            var project = ProjectSerializer.Deserialize(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "ExampleProjects", file)));
            Assert.That(SceneImporter.ImportProject(project, out var error, false), Is.True, error);
            Assert.That(SceneExporter.TryCaptureCurrentScene(null, out var exported, out error), Is.True, error);
            Assert.That(exported.projectName, Is.EqualTo(project.projectName));
            Assert.That(exported.createTime, Is.EqualTo(project.createTime));
            CollectionAssert.AreEqual(project.objects.Select(x => x.id).OrderBy(x => x), exported.objects.Select(x => x.id).OrderBy(x => x));
            foreach (var expected in project.objects)
            {
                var actual = exported.objects.Single(x => x.id == expected.id);
                Assert.That(actual.assetId, Is.EqualTo(expected.assetId)); Assert.That(actual.parentId, Is.EqualTo(expected.parentId));
                Assert.That(actual.localPosition, Is.EqualTo(expected.localPosition).Within(.00001f));
                Assert.That(actual.localRotation, Is.EqualTo(expected.localRotation).Within(.00001f));
                Assert.That(actual.localScale, Is.EqualTo(expected.localScale).Within(.00001f));
                Assert.That(actual.colorHex, Is.EqualTo(expected.colorHex)); Assert.That(actual.alpha, Is.EqualTo(expected.alpha));
                Assert.That(actual.metallic, Is.EqualTo(expected.metallic)); Assert.That(actual.colliderEnabled, Is.EqualTo(expected.colliderEnabled));
            }
            Assert.That(JToken.DeepEquals(JToken.FromObject(project.settings), JToken.FromObject(exported.settings)), Is.True);
            Assert.That(JToken.DeepEquals(JToken.FromObject(project.groups), JToken.FromObject(exported.groups)), Is.True);
            Assert.That(SceneImporter.ImportProject(exported, out error, false), Is.True, error);
            Assert.That(SpaceCanvasRuntime.GetEditableObjects().Count, Is.EqualTo(project.objects.Count));
        }

        [Test]
        public void AuthoredPartMaterialsSurviveExportAndReimport()
        {
            var project = new SpaceCanvasProject { projectName = "Part materials" };
            project.objects.Add(new SceneObject { id = "chair", assetId = "furniture.chair" });
            project.objects.Add(new SceneObject { id = "monitor", assetId = "office.monitor", colorHex = "#88CCDD", metallic = .3f });
            Assert.That(SceneImporter.ImportProject(project, out var error, false), Is.True, error);
            var materials = SpaceCanvasRuntime.GetEditableObjects().ToDictionary(x => x.ObjectId, MaterialParameters);
            Assert.That(SceneExporter.TryCaptureCurrentScene(null, out var exported, out error), Is.True, error);
            Assert.That(SceneImporter.ImportProject(ProjectSerializer.Deserialize(ProjectSerializer.Serialize(exported)), out error, false), Is.True, error);
            foreach (var editable in SpaceCanvasRuntime.GetEditableObjects())
                Assert.That(MaterialParameters(editable), Is.EqualTo(materials[editable.ObjectId]).Within(.00001f), editable.AssetId);
        }

        private static float[][] MaterialParameters(SpaceCanvasEditableObject editable)
        {
            return editable.GetComponentsInChildren<Renderer>(true).SelectMany(x => x.sharedMaterials).Select(material => {
                var color = material.GetColor("_BaseColor");
                return new[] { color.r, color.g, color.b, color.a, material.GetFloat("_Metallic"), material.GetFloat("_Smoothness") };
            }).ToArray();
        }

        [Test]
        public void HierarchyAndWholeImportUndoArePreserved()
        {
            var before = new SpaceCanvasProject { projectName = "Before" };
            before.objects.Add(new SceneObject { id = "old" });
            Assert.IsTrue(SceneImporter.ImportProject(before, out var error, false), error);
            var next = new SpaceCanvasProject { projectName = "Next" };
            next.objects.Add(new SceneObject { id = "parent" });
            next.objects.Add(new SceneObject { id = "child", parentId = "parent", localPosition = new[] { 1f, 2f, 3f } });
            Assert.IsTrue(SceneImporter.ImportProject(next, out error, true), error);
            var child = SpaceCanvasRuntime.GetEditableObjects().Single(x => x.ObjectId == "child");
            Assert.That(child.transform.parent.GetComponent<SpaceCanvasEditableObject>().ObjectId, Is.EqualTo("parent"));
            Assert.That(child.transform.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out error), error);
            Assert.That(SpaceCanvasRuntime.CurrentProjectRoot.projectName, Is.EqualTo("Before"));
            Assert.That(SpaceCanvasRuntime.GetEditableObjects().Single().ObjectId, Is.EqualTo("old"));
        }

        [Test]
        public void UndoKeepsNewestTwentyInCorrectOrder()
        {
            for (var i = 0; i < 25; i++) SpaceCanvasUndoService.PushSnapshot(new SpaceCanvasProject { projectName = i.ToString() });
            Assert.That(SpaceCanvasUndoService.Count, Is.EqualTo(20));
            for (var i = 24; i >= 5; i--)
            {
                Assert.IsTrue(SpaceCanvasUndoService.UndoLast(out var error), error);
                Assert.That(SpaceCanvasRuntime.CurrentProjectRoot.projectName, Is.EqualTo(i.ToString()));
            }
        }

        [Test]
        public void InvalidHierarchyLeavesExistingSceneIntact()
        {
            Assert.IsTrue(SceneImporter.ImportProject(new SpaceCanvasProject { projectName = "Keep" }, out _, false));
            var invalid = new SpaceCanvasProject();
            invalid.objects.Add(new SceneObject { id = "a", parentId = "b" }); invalid.objects.Add(new SceneObject { id = "b", parentId = "a" });
            Assert.IsFalse(SceneImporter.ImportProject(invalid, out var error, true));
            StringAssert.Contains("循环", error);
            Assert.That(SpaceCanvasRuntime.CurrentProjectRoot.projectName, Is.EqualTo("Keep"));
            Assert.That(SpaceCanvasUndoService.Count, Is.Zero);
        }

        [Test]
        public void DuplicateIdsAndNonFiniteValuesAreRejected()
        {
            var project = new SpaceCanvasProject(); project.objects.Add(new SceneObject { id = "x" }); project.objects.Add(new SceneObject { id = "x" });
            Assert.Throws<SpaceCanvasFormatException>(() => ProjectValidator.Validate(project));
            project.objects.RemoveAt(1); project.objects[0].localPosition[0] = float.NaN;
            Assert.Throws<SpaceCanvasFormatException>(() => ProjectValidator.Validate(project));
        }

        [Test]
        public void DefaultsAndBrokenJsonAreHandled()
        {
            var project = ProjectSerializer.Deserialize("{}");
            Assert.NotNull(project.settings); Assert.NotNull(project.objects); Assert.That(project.formatVersion, Is.EqualTo("1.0"));
            Assert.Throws<SpaceCanvasFormatException>(() => ProjectSerializer.Deserialize("{ broken"));
        }

        [Test]
        public void ExportNeverOverwritesExistingFile()
        {
            var name = "ProjectIO_Test_" + Guid.NewGuid().ToString("N");
            string first = null, second = null;
            try
            {
                Assert.IsTrue(SceneExporter.ExportCurrentScene(name, out first, out var error), error);
                Assert.IsTrue(SceneExporter.ExportCurrentScene(name, out second, out error), error);
                Assert.That(first, Is.Not.EqualTo(second)); Assert.That(File.Exists(first), Is.True);
            }
            finally { if (first != null && File.Exists(first)) File.Delete(first); if (second != null && File.Exists(second)) File.Delete(second); }
        }
    }
}
