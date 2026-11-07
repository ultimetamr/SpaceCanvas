using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SpaceCanvas.ProjectIO.Tests
{
    public sealed class ProjectVersionTests
    {
        private string directory;
        [SetUp] public void Setup() { directory = Path.Combine(Path.GetTempPath(), "SpaceCanvasVersions-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private string Write(string name, long time, long precise = 0)
        {
            var path = Path.Combine(directory, name + ".spacecanvas");
            File.WriteAllText(path, ProjectSerializer.Serialize(new SpaceCanvasProject { projectName = name, updateTime = time, savedAtUtcMilliseconds = precise }));
            return path;
        }

        [Test] public void CatalogUsesJsonTimeThenMillisecondsAndKeepsCorruptFilesLast()
        {
            var older = Write("older", 1700000000);
            var latest = Write("latest", 1700000001, 1700000001999);
            var sameSecond = Write("same-second", 1700000001, 1700000001001);
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(1));
            File.SetLastWriteTimeUtc(latest, DateTime.UtcNow.AddDays(-1));
            var corrupt = Path.Combine(directory, "corrupt.spacecanvas"); File.WriteAllText(corrupt, "{broken");
            var entries = SpaceCanvasProjectCatalog.ReadEntries(new[] { older, corrupt, sameSecond, latest });
            CollectionAssert.AreEqual(new[] { latest, sameSecond, older, corrupt }, entries.Select(x => x.FilePath));
            Assert.That(entries.Count(x => x.IsLatest), Is.EqualTo(1));
            Assert.IsTrue(entries[0].IsLatest); Assert.IsFalse(entries[0].UsesFileTime);
            Assert.IsFalse(entries[3].IsValid); Assert.IsNotEmpty(entries[3].Error);
        }

        [Test] public void LegacyMissingSaveTimeUsesClearlyLabelledFileTime()
        {
            var path = Path.Combine(directory, "legacy.spacecanvas"); File.WriteAllText(path, "{\"objects\":[]}");
            var version = SpaceCanvasProjectEntry.Read(path);
            Assert.IsTrue(version.IsValid); Assert.IsTrue(version.UsesFileTime);
            StringAssert.Contains("文件时间", version.TimeLabel);
        }

        [Test] public void ReadSnapshotAndFingerprintRemainStableWhenFileIsReplaced()
        {
            var path = Write("original", 1700000000);
            var first = SpaceCanvasProjectEntry.Read(path);
            File.WriteAllText(path, ProjectSerializer.Serialize(new SpaceCanvasProject { projectName = "replacement" }));
            var second = SpaceCanvasProjectEntry.Read(path);
            Assert.That(first.Project.projectName, Is.EqualTo("original"));
            Assert.That(second.Project.projectName, Is.EqualTo("replacement"));
            Assert.That(first.VersionId, Is.Not.EqualTo(second.VersionId));
            Assert.That(first.VersionId.Length, Is.EqualTo(8));
        }

        [Test] public void OptionalPreviewAndPreciseTimeRoundTripAndOldSavesStayCompatible()
        {
            var project = new SpaceCanvasProject { updateTime = 1700000000, savedAtUtcMilliseconds = 1700000000123,
                preview = new SpaceCanvasProjectPreview { captureTime = 1700000000, pngBase64 = "test-payload" } };
            var read = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
            Assert.That(read.savedAtUtcMilliseconds, Is.EqualTo(project.savedAtUtcMilliseconds));
            Assert.That(read.preview.encoding, Is.EqualTo("png-base64")); Assert.That(read.preview.pngBase64, Is.EqualTo("test-payload"));
            Assert.That(read.preview.captureTime, Is.EqualTo(project.preview.captureTime));
            Assert.IsNull(ProjectSerializer.Deserialize("{\"objects\":[]}").preview);
            var legacy = ProjectSerializer.Serialize(new SpaceCanvasProject());
            StringAssert.DoesNotContain("savedAtUtcMilliseconds", legacy); StringAssert.DoesNotContain("preview", legacy);
        }

        [Test] public void ThumbnailRejectsMalformedOrOversizedPayloadWithoutBlockingData()
        {
            Assert.IsNull(SpaceCanvasProjectThumbnail.Decode(new SpaceCanvasProjectPreview { pngBase64 = "not base64!" }));
            Assert.IsNull(SpaceCanvasProjectThumbnail.Decode(new SpaceCanvasProjectPreview { pngBase64 = new string('A', 400000) }));
            var header = new byte[33];
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            Array.Copy(signature, header, 8); header[11] = 13; header[12] = 73; header[13] = 72; header[14] = 68; header[15] = 82;
            header[19] = 1; header[23] = 1;
            Assert.IsTrue(SpaceCanvasProjectThumbnail.HasSafePngHeader(header));
            header[16] = 127; Assert.IsFalse(SpaceCanvasProjectThumbnail.HasSafePngHeader(header));
        }

        [Test] public void SaveMetadataDoesNotMarkLayoutDirtyOrBreakContentComparison()
        {
            var original = new SpaceCanvasProject { projectName = "same", createTime = 1, updateTime = 2 };
            var saved = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(original));
            saved.updateTime = 3; saved.savedAtUtcMilliseconds = 3001;
            saved.preview = new SpaceCanvasProjectPreview { captureTime = 3, pngBase64 = "generated-image" };
            Assert.That(SpaceCanvasRecoveryStore.Fingerprint(saved), Is.EqualTo(SpaceCanvasRecoveryStore.Fingerprint(original)));
            Assert.IsTrue(SpaceCanvasProjectFormatUtilities.AreEquivalent(original, saved, true, out var difference), difference);
            Assert.IsFalse(SpaceCanvasProjectFormatUtilities.AreEquivalent(original, saved, false, out _));
        }

        [Test] public void CaptureProducesActualModelImageWithoutChangingWorkspaceOrActiveTarget()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("需要图形设备");
            var oldContent = SpaceCanvasRuntime.ContentRoot;
            var oldProject = SpaceCanvasRuntime.CurrentProjectRoot;
            var host = new GameObject("Thumbnail test workspace");
            var activeTarget = RenderTexture.GetTemporary(16, 16);
            var previousTarget = RenderTexture.active;
            Texture2D decoded = null;
            try
            {
                SpaceCanvasRuntime.ContentRoot = host.transform;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(host.transform);
                cube.AddComponent<SpaceCanvasEditableObject>();
                var renderer = cube.GetComponent<Renderer>(); renderer.sharedMaterial = SpaceCanvasAssetLibrary.DefaultMaterial;
                var matrix = cube.transform.localToWorldMatrix; var material = renderer.sharedMaterial;
                var objectCount = host.GetComponentsInChildren<Transform>(true).Length;
                RenderTexture.active = activeTarget;
                var preview = SpaceCanvasProjectThumbnail.Capture(1700000000);
                Assert.IsNotNull(preview); Assert.That(preview.captureTime, Is.EqualTo(1700000000));
                Assert.AreSame(activeTarget, RenderTexture.active);
                Assert.AreEqual(matrix, cube.transform.localToWorldMatrix); Assert.AreSame(material, renderer.sharedMaterial);
                Assert.That(host.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objectCount));
                decoded = SpaceCanvasProjectThumbnail.Decode(preview); Assert.IsNotNull(decoded);
                // Decode uses a non-readable texture by design; read the encoded bytes into a separate readable texture for visual-content assertion.
                var readable = new Texture2D(2, 2);
                try
                {
                    Assert.IsTrue(readable.LoadImage(Convert.FromBase64String(preview.pngBase64)));
                    Assert.That(readable.width, Is.EqualTo(SpaceCanvasProjectThumbnail.Width));
                    var pixels = readable.GetPixels32(); var corner = pixels[0];
                    Assert.That(pixels.Count(x => Math.Abs(x.r-corner.r)+Math.Abs(x.g-corner.g)+Math.Abs(x.b-corner.b)>24), Is.GreaterThan(300), "预览不能只有纯色背景");
                }
                finally { Object.DestroyImmediate(readable); }
                Assert.IsFalse(Object.FindObjectsOfType<Camera>().Any(x => x.name == "Preview camera"));
            }
            finally
            {
                RenderTexture.active = previousTarget; RenderTexture.ReleaseTemporary(activeTarget);
                if (decoded != null) Object.DestroyImmediate(decoded);
                Object.DestroyImmediate(host); SpaceCanvasRuntime.ContentRoot = oldContent; SpaceCanvasRuntime.SetCurrentProjectRoot(oldProject);
            }
        }

        [Test] public void ConfirmationImportsDisplayedSnapshotEvenAfterSourceFileChanges()
        {
            var oldContent = SpaceCanvasRuntime.ContentRoot; var oldProject = SpaceCanvasRuntime.CurrentProjectRoot;
            var host = new GameObject("Version confirmation test");
            var path = Path.Combine(SpaceCanvasProjectCatalog.LocalProjectsDirectory, "VersionTest-" + Guid.NewGuid().ToString("N") + ".spacecanvas");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                SpaceCanvasRuntime.ContentRoot = host.transform; SpaceCanvasRuntime.SetCurrentProjectRoot(null);
                host.SetActive(false);
                var ui = host.AddComponent<SpaceCanvasProjectIOUI>(); ui.createRuntimeToolbar = false;
                host.SetActive(true);
                var project = new SpaceCanvasProject { projectName = "confirmed-version", updateTime = 1700000000 };
                project.objects.Add(new SceneObject { id = "confirmed-object", localPosition = new[] { 1f, 2f, 3f } });
                File.WriteAllText(path, ProjectSerializer.Serialize(project));
                ui.ImportProjectFile(path);
                Assert.That(SpaceCanvasRuntime.GetEditableObjects().Count, Is.EqualTo(0), "选择和确认前不能导入");
                File.WriteAllText(path, "{broken-after-confirmation-card}");
                ui.ConfirmImport();
                Assert.That(SpaceCanvasRuntime.CurrentProjectRoot.projectName, Is.EqualTo("confirmed-version"));
                var restored = SpaceCanvasRuntime.GetEditableObjects().Single();
                Assert.That(restored.ObjectId, Is.EqualTo("confirmed-object"));
                Assert.That(restored.transform.localPosition, Is.EqualTo(new Vector3(1,2,3)));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                Object.DestroyImmediate(host); SpaceCanvasUndoService.Clear();
                SpaceCanvasRuntime.ContentRoot = oldContent; SpaceCanvasRuntime.SetCurrentProjectRoot(oldProject);
            }
        }
    }
}
