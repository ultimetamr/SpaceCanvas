using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    public sealed partial class SpaceCanvasWorkspaceUI
    {
        private void BuildInspectorTabs()
        {
            for (var i = 0; i < 2; i++)
            {
                var appearance = i == 1;
                var button = SpaceCanvasPresentationStyle.Button(propertyBody, appearance ? "外观页" : "变换页", appearance ? "外观与配色" : "位置与尺寸",
                    new Vector2(i == 0 ? -88 : 88, 141), new Vector2(166, 40), () =>
                    { FinishPropertyEdit(); editSession?.EndGesture(false); appearanceVisible = appearance; ShowProperties(); });
                button.GetComponentInChildren<TMPro.TextMeshProUGUI>().fontSize = 18;
                SpaceCanvasPresentationStyle.Tone(button, appearance == appearanceVisible ? SpaceCanvasPresentationStyle.Selected : SpaceCanvasPresentationStyle.Card);
            }
        }

        private void BuildAppearancePresets()
        {
            var names = new[] { "原始配色", "暖沙", "鼠尾草", "深海" };
            var colors = new[] { "#FFFFFF", "#E8D4B8", "#A9C6AD", "#879EBD" };
            for (var i = 0; i < names.Length; i++)
            {
                var index = i;
                var button = SpaceCanvasPresentationStyle.Button(propertyBody, "配色 " + names[i], names[i],
                    new Vector2(i % 2 == 0 ? -88 : 88, 32 - i / 2 * 49), new Vector2(166, 40),
                    () => ApplyAppearance(colors[index], selected == null ? 1 : selected.Alpha, selected == null ? 0 : selected.Metallic));
                button.GetComponentInChildren<TMPro.TextMeshProUGUI>().fontSize = 18;
                ColorUtility.TryParseHtmlString(colors[i], out var color);
                SpaceCanvasPresentationStyle.Tone(button, color, true);
            }
        }

        /// <summary>Uses the existing format fields and material tint path; source textures and material slots survive.</summary>
        public bool ApplyAppearance(string color, float alpha, float metallic)
        {
            if (selected == null || !ColorUtility.TryParseHtmlString(color, out var parsed)
                || float.IsNaN(alpha) || float.IsInfinity(alpha) || float.IsNaN(metallic) || float.IsInfinity(metallic)) return false;
            try
            {
                FinishPropertyEdit(); editSession?.EndGesture(false);
                color = "#" + ColorUtility.ToHtmlStringRGBA(parsed); alpha = Mathf.Clamp01(alpha); metallic = Mathf.Clamp01(metallic);
                if (ColorUtility.TryParseHtmlString(selected.ColorHex, out var old) && old == parsed
                    && Mathf.Approximately(selected.Alpha, alpha) && Mathf.Approximately(selected.Metallic, metallic)) return true;
                Snapshot(); var data = selected.ToSceneObject(); data.colorHex = color; data.alpha = alpha; data.metallic = metallic;
                selected.Apply(data); ObjectEdited?.Invoke(selected); ShowProperties(); return true;
            }
            catch (Exception ex) { if (status != null) status.text = "配色失败：" + ex.Message; return false; }
        }

        private void BuildObjectActions(float y)
        {
            var copy = SpaceCanvasPresentationStyle.Button(propertyBody, "复制物体", "复制物体", new Vector2(-88,y), new Vector2(166,42), () => DuplicateSelected());
            copy.GetComponentInChildren<TMPro.TextMeshProUGUI>().fontSize = 18;
            var remove = SpaceCanvasPresentationStyle.Button(propertyBody, "删除物体", "删除物体", new Vector2(88,y), new Vector2(166,42), DeleteSelected);
            remove.GetComponentInChildren<TMPro.TextMeshProUGUI>().fontSize = 18;
            remove.GetComponentInChildren<TMPro.TextMeshProUGUI>().color = new Color(.91f,.57f,.49f);
        }

        /// <summary>
        /// Duplicates the selected editable subtree through its portable data, remapping every ID.
        /// Creation happens under inactive staging; partial failure leaves the original scene and undo stack intact.
        /// Group membership is independent and is deliberately not copied into unrelated existing groups.
        /// </summary>
        public bool DuplicateSelected()
        {
            if (selected == null) return false;
            GameObject staging = null;
            try
            {
                FinishPropertyEdit(); editSession?.EndGesture(false); placement?.Cancel();
                if (!SceneExporter.TryCaptureCurrentScene(null, out var before, out var error)) throw new InvalidOperationException(error);
                var originals = selected.GetComponentsInChildren<SpaceCanvasEditableObject>(true);
                var ids = new Dictionary<string,string>();
                foreach (var item in originals) ids.Add(item.ObjectId, Guid.NewGuid().ToString("N"));
                staging = new GameObject("Duplicate staging"); staging.SetActive(false);
                var created = new Dictionary<string,SpaceCanvasEditableObject>();
                foreach (var original in originals)
                {
                    var data = original.ToSceneObject(); data.id = ids[original.ObjectId];
                    data.parentId = ids.TryGetValue(data.parentId, out var newParent) ? newParent : "";
                    var go = SpaceCanvasAssetLibrary.CreateInstance(data.assetId, staging.transform, out _);
                    var item = go.GetComponent<SpaceCanvasEditableObject>(); item.Apply(data); created.Add(original.ObjectId,item);
                }
                foreach (var original in originals)
                {
                    var parent = original.transform.parent == null ? null : original.transform.parent.GetComponentInParent<SpaceCanvasEditableObject>();
                    if (parent != null && created.TryGetValue(parent.ObjectId, out var clonedParent)) created[original.ObjectId].transform.SetParent(clonedParent.transform,false);
                }
                var copied = created[selected.ObjectId];
                copied.transform.SetParent(selected.transform.parent,false);
                copied.transform.localPosition += Vector3.right * .25f;
                copied.gameObject.SetActive(true);
                SpaceCanvasUndoService.PushSnapshot(before);
                selected = copied; RefreshObjects(); AssetAdded?.Invoke(copied);
                if (status != null) status.text = "已复制物体 · 可拖动手柄继续调整";
                return true;
            }
            catch (Exception ex) { if (status != null) status.text = "复制失败：" + ex.Message; return false; }
            finally { if (staging != null) Dispose(staging); }
        }
    }
}
