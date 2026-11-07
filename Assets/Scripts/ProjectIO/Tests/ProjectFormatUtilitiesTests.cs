using System.IO;
using NUnit.Framework;

namespace SpaceCanvas.ProjectIO.Tests
{
    public sealed class ProjectFormatUtilitiesTests
    {
        [Test]
        public void EquivalentComparisonIgnoresTimestampsWhenRequested()
        {
            var first = new SpaceCanvasProject { projectName = "A", createTime = 1, updateTime = 2 };
            var second = new SpaceCanvasProject { projectName = "A", createTime = 3, updateTime = 4 };
            Assert.IsTrue(SpaceCanvasProjectFormatUtilities.AreEquivalent(first, second, true, out var difference), difference);
            Assert.IsFalse(SpaceCanvasProjectFormatUtilities.AreEquivalent(first, second, false, out _));
        }

        [Test]
        public void LocalCatalogRejectsPathsOutsideSandbox()
        {
            Assert.IsFalse(SpaceCanvasProjectCatalog.IsInsideLocalDirectory(Path.Combine("..", "outside.spacecanvas")));
        }

        [Test]
        public void FileNameHelpersKeepOpenExtensionPortable()
        {
            Assert.AreEqual("Demo.spacecanvas", SpaceCanvasProjectFormatUtilities.EnsureExtension("Demo"));
            Assert.AreEqual("Demo.spacecanvas", SpaceCanvasProjectFormatUtilities.EnsureExtension("Demo.spacecanvas"));
        }
    }
}
