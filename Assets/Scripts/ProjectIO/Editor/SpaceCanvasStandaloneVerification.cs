#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Portable verification harness. Copy this and SpaceCanvasEditorImporter.cs into an empty
/// project's Assets/Editor, copy the three JSON examples to StreamingAssets/ExampleProjects,
/// then run -batchmode -executeMethod SpaceCanvasStandaloneVerification.Run -quit.
/// Requires no SpaceCanvas runtime, PICO, Newtonsoft, or Unity Test Framework.
/// </summary>
public static class SpaceCanvasStandaloneVerification
{
    public static void Run()
    {
        var report = new StringBuilder();
        report.AppendLine("Independent Unity restoration verification");
        report.AppendLine("Unity=" + Application.unityVersion);
        report.AppendLine("Utc=" + DateTime.UtcNow.ToString("O"));
        var pico = AppDomain.CurrentDomain.GetAssemblies().Any(x => x.GetName().Name.IndexOf("PICO", StringComparison.OrdinalIgnoreCase) >= 0);
        report.AppendLine("PICO assemblies loaded=" + pico);
        try
        {
            var directory = Path.Combine(Application.streamingAssetsPath, "ExampleProjects");
            var files = Directory.GetFiles(directory, "*_demo.spacecanvas");
            if (files.Length != 3) throw new Exception("Expected exactly 3 official example projects.");
            foreach (var file in files)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var data = SpaceCanvasEditorImporter.Parse(File.ReadAllText(file));
                SpaceCanvasEditorImporter.ImportIntoScene(data);
                var root = GameObject.Find(data.projectName);
                if (root == null) throw new Exception("Missing project root.");
                var transforms = root.GetComponentsInChildren<Transform>(true);
                foreach (var expected in data.objects)
                {
                    var item = transforms.Single(x => x.name.EndsWith("[" + expected.id + "]", StringComparison.Ordinal));
                    Near(item.localPosition, new Vector3(expected.localPosition[0], expected.localPosition[1], expected.localPosition[2]), expected.id + " position");
                    Near(item.localScale, new Vector3(expected.localScale[0], expected.localScale[1], expected.localScale[2]), expected.id + " scale");
                    var q = new Quaternion(expected.localRotation[0], expected.localRotation[1], expected.localRotation[2], expected.localRotation[3]);
                    if (Quaternion.Angle(item.localRotation, q) > .01f) throw new Exception(expected.id + " rotation mismatch.");
                    if (string.IsNullOrEmpty(expected.parentId)) { if (item.parent != root.transform) throw new Exception("Root hierarchy mismatch."); }
                    else if (!item.parent.name.EndsWith("[" + expected.parentId + "]", StringComparison.Ordinal)) throw new Exception("Parent hierarchy mismatch.");
                    var renderer = item.GetComponentInChildren<Renderer>();
                    if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null) throw new Exception("Missing PBR material.");
                    if (ColorUtility.TryParseHtmlString(expected.colorHex, out var color))
                    {
                        color.a = expected.alpha;
                        var actual = renderer.sharedMaterial.HasProperty("_BaseColor") ? renderer.sharedMaterial.GetColor("_BaseColor") : renderer.sharedMaterial.color;
                        if (Mathf.Abs(actual.r-color.r)+Mathf.Abs(actual.g-color.g)+Mathf.Abs(actual.b-color.b)+Mathf.Abs(actual.a-color.a) > .001f) throw new Exception(expected.id + " color mismatch.");
                    }
                    if (Mathf.Abs(renderer.sharedMaterial.GetFloat("_Metallic") - expected.metallic) > .001f) throw new Exception("Metallic mismatch.");
                    if (item.GetComponentsInChildren<Collider>().Any(x => x.enabled != expected.colliderEnabled)) throw new Exception("Collider mismatch.");
                }
                report.AppendLine("PASS " + Path.GetFileName(file) + " objects=" + data.objects.Count + " hierarchy/transform/PBR/colliders");
            }
            report.AppendLine("PASS all examples restored with the portable editor script.");
        }
        catch (Exception ex) { report.AppendLine("FAIL " + ex); throw; }
        finally { Directory.CreateDirectory("Library"); File.WriteAllText("Library/SpaceCanvas.StandaloneValidation.txt", report.ToString(), new UTF8Encoding(false)); }
    }
    private static void Near(Vector3 actual, Vector3 expected, string label)
    { if ((actual-expected).sqrMagnitude > .00000001f) throw new Exception(label + " mismatch."); }
}
#endif
