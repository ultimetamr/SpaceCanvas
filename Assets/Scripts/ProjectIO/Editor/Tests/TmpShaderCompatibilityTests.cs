using NUnit.Framework;
using UnityEditor.Build;

namespace SpaceCanvas.ProjectIO.Editor.Tests
{
    public sealed class TmpShaderCompatibilityTests
    {
        [Test] public void InstalledShadersMatchTheInstalledMeshWriter()
        {
            Assert.DoesNotThrow(SpaceCanvasTmpShaderCompatibility.ValidateInstalledShaders);
        }

        [Test] public void LegacyShaderIsRejectedForPackedUvMesh()
        {
            var error = Assert.Throws<BuildFailedException>(() => SpaceCanvasTmpShaderCompatibility.ValidateSource(
                "scale *= abs(input.texcoord1.y) * _GradientScale;", true));
            StringAssert.Contains("UV0.w", error.Message);
        }

        [Test] public void MatchingOfficialVertexContractsAreAccepted()
        {
            Assert.DoesNotThrow(() => SpaceCanvasTmpShaderCompatibility.ValidateSource("scale *= abs(input.texcoord0.w);", true));
            Assert.DoesNotThrow(() => SpaceCanvasTmpShaderCompatibility.ValidateSource("scale *= abs(input.texcoord1.y);", false));
        }
    }
}
