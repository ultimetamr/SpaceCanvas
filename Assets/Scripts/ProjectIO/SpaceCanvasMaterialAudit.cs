using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Development-player smoke check for Android material availability. Creates each asset
    /// under a disabled temporary root, applies the palette appearance path and records the
    /// actual player shader. It never adds content to the workbench or changes undo history.
    /// Release players do not create this component.
    /// </summary>
    public sealed class SpaceCanvasMaterialAudit : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartDevelopmentAudit()
        {
            if (!Debug.isDebugBuild || Application.isEditor) return;
            new GameObject("Development Material Audit").AddComponent<SpaceCanvasMaterialAudit>();
        }

        private IEnumerator Start()
        {
            var root = new GameObject("Disabled material audit content");
            root.transform.SetParent(transform, false); root.SetActive(false);
            var report = new List<string>(); var failures = 0; var slots = 0;
            foreach (var id in SpaceCanvasBuiltinAssets.Ids)
            {
                GameObject instance = null;
                try
                {
                    instance = SpaceCanvasAssetLibrary.CreateInstance(id, root.transform, out var missing);
                    instance.GetComponent<SpaceCanvasEditableObject>().Apply(new SceneObject { assetId = id });
                    var count = 0; var colored = 0; var textured = 0;
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                        {
                            count++; slots++;
                            if (material == null || material.shader == null || !material.shader.isSupported
                                || !material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
                                throw new InvalidOperationException(id + "/" + renderer.name + ": invalid material " + (material == null ? "null" : material.shader.name));
                            if (material.HasProperty("_BaseColor"))
                            {
                                var color = material.GetColor("_BaseColor");
                                if (color.r < .9f || color.g < .9f || color.b < .9f) colored++;
                            }
                            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null) textured++;
                        }
                    if (missing || count == 0) throw new InvalidOperationException(id + ": no renderable asset");
                    if (instance.GetComponent<SpaceCanvasPremiumAssetTag>() != null && colored == 0)
                        throw new InvalidOperationException(id + ": authored part colors missing");
                    report.Add("PASS " + id + " slots=" + count + " colored=" + colored + " textured=" + textured);
                }
                catch (Exception ex) { failures++; report.Add("FAIL " + id + " " + ex.Message); }
                finally
                {
                    // EditableObject owns its material clones. Never destroy materials
                    // collected from renderers here: they may be shared resource assets.
                    if (instance != null)
                    {
                        foreach (var editable in instance.GetComponentsInChildren<SpaceCanvasEditableObject>(true))
                            editable.ReleaseOwnedMaterials();
                        Destroy(instance);
                    }
                }
                yield return null;
            }
            var summary = "[SpaceCanvas MaterialAudit] " + (failures == 0 ? "PASS" : "FAIL")
                + " assets=" + SpaceCanvasBuiltinAssets.Ids.Length + " slots=" + slots + " failures=" + failures;
            report.Insert(0, summary);
            try { File.WriteAllLines(Path.Combine(Application.persistentDataPath, "MaterialAudit.txt"), report); }
            catch (Exception ex) { Debug.LogWarning("Material audit report: " + ex.Message); }
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary + "\n" + string.Join("\n", report));
            Destroy(gameObject);
        }
    }
}
