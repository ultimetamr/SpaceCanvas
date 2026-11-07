using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Editor.Tests
{
    /// <summary>独立脚本的版本选择逻辑也必须与运行时一致，且无需业务程序集来读取快照。</summary>
    public sealed class EditorProjectVersionTests
    {
        private string directory;
        [SetUp] public void Setup() { directory = Path.Combine(Path.GetTempPath(), "SpaceCanvasEditorVersions-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); }
        [TearDown] public void Cleanup() { Directory.Delete(directory, true); }

        [Test] public void EditorUsesSaveTimeRatherThanCopiedFileTimeAndRetainsSnapshot()
        {
            var older = Path.Combine(directory, "older.spacecanvas"); var newer = Path.Combine(directory, "newer.spacecanvas");
            var corrupt = Path.Combine(directory, "corrupt.spacecanvas");
            File.WriteAllText(older, "{\"projectName\":\"old\",\"updateTime\":1700000000,\"objects\":[]}");
            File.WriteAllText(newer, "{\"projectName\":\"new\",\"updateTime\":1700000001,\"savedAtUtcMilliseconds\":1700000001999,\"objects\":[]}");
            File.WriteAllText(corrupt, "{broken");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(1));
            var entries = SpaceCanvasEditorImporter.ProjectFileVersion.ReadSorted(new[] { older, corrupt, newer });
            Assert.That(entries[0].FilePath, Is.EqualTo(newer)); Assert.IsFalse(entries[0].UsesFileTime);
            Assert.That(entries[0].SortTimeMilliseconds, Is.EqualTo(1700000001999)); Assert.IsFalse(entries[2].IsValid);
            var runtime = SpaceCanvasProjectEntry.Read(newer);
            Assert.That(entries[0].VersionId, Is.EqualTo(runtime.VersionId));
            File.WriteAllText(newer, "{\"projectName\":\"changed\",\"objects\":[]}");
            Assert.That(entries[0].Project.projectName, Is.EqualTo("new"));
            Assert.That(SpaceCanvasEditorImporter.ProjectFileVersion.Read(newer).VersionId, Is.Not.EqualTo(entries[0].VersionId));
        }

        [Test] public void MissingOrInvalidThumbnailDoesNotInvalidateEditorVersion()
        {
            var file = Path.Combine(directory, "preview.spacecanvas");
            File.WriteAllText(file, "{\"objects\":[],\"preview\":{\"encoding\":\"png-base64\",\"pngBase64\":\"bad data!\"}}");
            var entry = SpaceCanvasEditorImporter.ProjectFileVersion.Read(file);
            Assert.IsTrue(entry.IsValid); Assert.IsNull(entry.DecodePreview());
        }

        [Test] public void EmbeddedPngSurvivesStandaloneEditorReader()
        {
            var texture = new Texture2D(4, 3); Texture2D decoded = null;
            try
            {
                texture.SetPixel(0, 0, Color.red); texture.Apply();
                var project = new SpaceCanvasProject { preview = new SpaceCanvasProjectPreview { pngBase64 = Convert.ToBase64String(texture.EncodeToPNG()) } };
                var file = Path.Combine(directory, "preview.spacecanvas"); File.WriteAllText(file, ProjectSerializer.Serialize(project));
                var entry = SpaceCanvasEditorImporter.ProjectFileVersion.Read(file);
                decoded = entry.DecodePreview(); Assert.IsNotNull(decoded);
                Assert.That(decoded.width, Is.EqualTo(4)); Assert.That(decoded.height, Is.EqualTo(3));
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded); }
        }
    }
}
