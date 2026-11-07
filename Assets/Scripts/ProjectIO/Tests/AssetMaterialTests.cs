using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace SpaceCanvas.ProjectIO.Tests
{
    /// <summary>Use the same factory as palette clicks, rather than testing saved scene materials.</summary>
    public sealed class AssetMaterialTests
    {
        public static IEnumerable AssetIds => SpaceCanvasBuiltinAssets.Ids;

        [TestCaseSource(nameof(AssetIds))]
        public void PaletteCreatedAssetUsesSupportedURPMaterials(string id)
        {
            var instance = SpaceCanvasAssetLibrary.CreateInstance(id, null, out var missing);
            try
            {
                Assert.That(missing, Is.False);
                var editable = instance.GetComponent<SpaceCanvasEditableObject>();
                var bounds = SpaceCanvasGeometry.LocalBounds(editable);
                Assert.That(bounds.size.x, Is.InRange(.001f, 20f), id + " visual width");
                Assert.That(bounds.size.y, Is.InRange(.001f, 20f), id + " visual height");
                Assert.That(bounds.size.z, Is.InRange(.001f, 20f), id + " visual depth");
                Assert.That(instance.GetComponentsInChildren<Collider>(true).Length, Is.GreaterThan(0), id + " selection collider");
                editable.Apply(new SceneObject { id = "material-test", assetId = id, colorHex = "#5599CC", alpha = .6f, metallic = .3f });
                AssertRenderers(instance);
                editable.Apply(new SceneObject { id = "material-test", assetId = id, colorHex = "#FFFFFF" });
                AssertRenderers(instance);
                if (instance.GetComponent<SpaceCanvasCuratedAsset>() == null)
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            Assert.That(material.shader, Is.SameAs(SpaceCanvasAssetLibrary.DefaultMaterial.shader));
            }
            finally { DestroyInstance(instance); }
        }

        [Test]
        public void UnknownAssetUsesTheSameSupportedPBRFallback()
        {
            var instance = SpaceCanvasAssetLibrary.CreateInstance("unregistered.material-test", null, out var missing);
            try { Assert.That(missing, Is.True); AssertRenderers(instance); }
            finally { DestroyInstance(instance); }
        }

        [TestCase("primitive.cube")]
        [TestCase("decor.plant")]
        public void InactiveStagingOwnsClonesAndPreservesSharedResources(string id)
        {
            var root = new GameObject("Inactive staging");
            root.SetActive(false);
            var shared = SpaceCanvasAssetLibrary.DefaultMaterial;
            var originalColor = shared.GetColor("_BaseColor");
            GameObject instance = null;
            try
            {
                instance = SpaceCanvasAssetLibrary.CreateInstance(id, root.transform, out _);
                var sources = new System.Collections.Generic.Dictionary<Renderer, Material[]>();
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    sources.Add(renderer, renderer.sharedMaterials);

                instance.GetComponent<SpaceCanvasEditableObject>().Apply(new SceneObject {
                    assetId = id, colorHex = "#5599CC", alpha = .6f, colliderEnabled = false
                });
                AssertRenderers(instance);
                foreach (var renderer in sources.Keys)
                    for (var i = 0; i < sources[renderer].Length; i++)
                        Assert.That(renderer.sharedMaterials[i], Is.Not.SameAs(sources[renderer][i]), renderer.name);
                foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                    Assert.That(collider.enabled, Is.False, collider.name);
                Assert.That(shared.GetColor("_BaseColor"), Is.EqualTo(originalColor));
            }
            finally
            {
                if (instance != null) DestroyInstance(instance);
                Object.DestroyImmediate(root);
            }
            Assert.That(shared, Is.Not.Null);
            Assert.That(SpaceCanvasAssetLibrary.DefaultMaterial, Is.SameAs(shared));
        }

        [TestCase("furniture.chair")]
        [TestCase("office.monitor")]
        [TestCase("office.keyboard")]
        [TestCase("kitchen.fridge")]
        [TestCase("decor.book")]
        public void NewProceduralAssetRetainsAuthoredPartColorsAfterApply(string id)
        {
            var instance = SpaceCanvasAssetLibrary.CreateInstance(id, null, out _);
            try
            {
                var editable = instance.GetComponent<SpaceCanvasEditableObject>();
                var data = new SceneObject { assetId = id };
                var tag = instance.GetComponent<SpaceCanvasPremiumAssetTag>();
                var script = UnityEditor.MonoScript.FromMonoBehaviour(tag);
                Assert.That(UnityEditor.AssetDatabase.GetAssetPath(script), Does.EndWith("/SpaceCanvasPremiumAssetTag.cs"));
                Assert.That(script.GetClass(), Is.EqualTo(typeof(SpaceCanvasPremiumAssetTag)));
                editable.Apply(data);
                var colors = new System.Collections.Generic.List<Color>();
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials) colors.Add(material.GetColor("_BaseColor"));
                Assert.That(new System.Collections.Generic.HashSet<Color>(colors).Count, Is.GreaterThanOrEqualTo(2), id);
                Assert.That(colors.Exists(c => c.r < .7f && c.g < .7f && c.b < .7f), Is.True, id);

                data.colorHex = "#5599CC"; editable.Apply(data);
                editable.Apply(new SceneObject { assetId = id });
                var index = 0;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    foreach (var material in renderer.sharedMaterials)
                        Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(colors[index++]), id);
            }
            finally { DestroyInstance(instance); }
        }

        private static void AssertRenderers(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            foreach (var renderer in renderers)
                foreach (var material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null, instance.name + "/" + renderer.name);
                    Assert.That(material.shader.isSupported, Is.True, material.name);
                    Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/"), instance.name);
                }
        }

        private static void DestroyInstance(GameObject instance)
        {
            // EditMode doesn't execute runtime material cleanup; release only nonasset clones.
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && !UnityEditor.AssetDatabase.Contains(material)) Object.DestroyImmediate(material);
            Object.DestroyImmediate(instance);
        }
    }
}
