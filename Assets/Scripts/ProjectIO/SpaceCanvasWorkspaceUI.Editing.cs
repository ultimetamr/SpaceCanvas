using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SpaceCanvas.ProjectIO
{
    public sealed partial class SpaceCanvasWorkspaceUI
    {
        private int inspectorMode;
        private bool appearanceVisible;
        public void FinishPropertyEdit() { SpaceCanvasScalarSlider.FinishAll(); }
        public void SetPrecisionMode(SpaceCanvasEditTool tool) { inspectorMode = (int)tool; appearanceVisible = false; ShowProperties(); }

        private void BuildPrecisionFields(float offset = 0)
        {
            var modes = new[] { "位置", "旋转", "尺寸" };
            for (var i = 0; i < modes.Length; i++)
            {
                var mode = i;
                var tab = SpaceCanvasPresentationStyle.Button(propertyBody, "参数 " + modes[i], modes[i],
                    new Vector2(-116 + i * 116, 141 + offset), new Vector2(110, 40), () => { if (editSession != null) editSession.SetTool((SpaceCanvasEditTool)mode); else { inspectorMode = mode; ShowProperties(); } });
                SpaceCanvasPresentationStyle.Tone(tab, mode == inspectorMode ? SpaceCanvasPresentationStyle.Selected : SpaceCanvasPresentationStyle.Card);
            }
            var item = selected;
            var values = inspectorMode == 0 ? item.transform.localPosition : inspectorMode == 1 ? item.transform.localEulerAngles : item.transform.localScale;
            for (var i = 0; i < 3; i++)
            {
                var axisIndex = i; var y = 87 - i * 52 + offset;
                var constraint = (SpaceCanvasEditAxis)(i + 1);
                var axisButton = SpaceCanvasPresentationStyle.Button(propertyBody, "约束 " + modes[inspectorMode] + i, new[] { "X", "Y", "Z" }[i],
                    new Vector2(-151, y), new Vector2(40, 44), () =>
                    {
                        if (editSession == null) return;
                        editSession.EndGesture(false);
                        editSession.axis = editSession.axis == constraint ? SpaceCanvasEditAxis.Free : constraint; ShowProperties();
                    });
                SpaceCanvasPresentationStyle.Tone(axisButton, editSession != null && editSession.axis == constraint ? SpaceCanvasPresentationStyle.Selected : SpaceCanvasPresentationStyle.Card);
                SpaceCanvasPresentationStyle.Button(propertyBody, "减少 " + i, "−", new Vector2(-101, y), new Vector2(44, 44), () => StepProperty(item, axisIndex, -1));
                var field = Field(propertyBody, values[i].ToString("0.###", CultureInfo.InvariantCulture), Vector2.zero, Vector2.one);
                SpaceCanvasPresentationStyle.Place((RectTransform)field.transform, new Vector2(-12, y), new Vector2(122, 44));
                field.textComponent.alignment = TextAlignmentOptions.Center;
                var mode = inspectorMode;
                field.onEndEdit.AddListener(value =>
                {
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) SetProperty(item, mode, axisIndex, number);
                    else if (status != null) status.text = "请输入有效数字。";
                });
                SpaceCanvasPresentationStyle.Button(propertyBody, "增加 " + i, "+", new Vector2(78, y), new Vector2(44, 44), () => StepProperty(item, axisIndex, 1));
                SpaceCanvasPresentationStyle.Label(propertyBody, "单位", inspectorMode == 0 ? "m" : inspectorMode == 1 ? "°" : "倍", new Vector2(139, y), new Vector2(58, 32), 15, true);
            }
            var free = SpaceCanvasPresentationStyle.Button(propertyBody, "解除约束", editSession == null || editSession.axis == SpaceCanvasEditAxis.Free ? "自由轴向" : "约束 " + editSession.axis,
                new Vector2(-88, -63 + offset), new Vector2(166, 36), () => { if (editSession != null) { editSession.EndGesture(false); editSession.axis = SpaceCanvasEditAxis.Free; } ShowProperties(); });
            free.GetComponentInChildren<TextMeshProUGUI>().fontSize = 18;
            var snap = SpaceCanvasPresentationStyle.Button(propertyBody, "网格吸附", editSession != null && editSession.snapToGrid ? "吸附 · 开" : "吸附 · 关",
                new Vector2(88, -63 + offset), new Vector2(166, 36), () => { if (editSession != null) { editSession.EndGesture(false); editSession.snapToGrid = !editSession.snapToGrid; } ShowProperties(); });
            snap.GetComponentInChildren<TextMeshProUGUI>().fontSize = 18;
        }

        private void StepProperty(SpaceCanvasEditableObject item, int axis, int direction)
        {
            if (item == null || item != selected) return;
            var vector = inspectorMode == 0 ? item.transform.localPosition : inspectorMode == 1 ? item.transform.localEulerAngles : item.transform.localScale;
            SetProperty(item, inspectorMode, axis, vector[axis] + direction * (inspectorMode == 0 ? .05f : inspectorMode == 1 ? 15f : .1f));
        }

        private void SetProperty(SpaceCanvasEditableObject item, int mode, int axisIndex, float value)
        {
            if (item == null || item != selected || float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 1000000f) return;
            FinishPropertyEdit();
            var vector = mode == 0 ? item.transform.localPosition : mode == 1 ? item.transform.localEulerAngles : item.transform.localScale;
            if (mode == 2) value = Mathf.Clamp(value, .01f, 100f);
            if (Mathf.Approximately(vector[axisIndex], value)) return;
            Snapshot();
            if (mode == 2)
            {
                // A scale step keeps existing proportions; an axis never collapses through zero.
                var ratio = value / Mathf.Max(.0001f, vector[axisIndex]);
                var min = Mathf.Min(vector.x, Mathf.Min(vector.y, vector.z));
                var max = Mathf.Max(vector.x, Mathf.Max(vector.y, vector.z));
                ratio = Mathf.Clamp(ratio, .01f / Mathf.Max(.0001f, min), 100f / Mathf.Max(.0001f, max));
                item.transform.localScale = vector * ratio;
            }
            else { vector[axisIndex] = value; if (mode == 0) item.transform.localPosition = vector; else item.transform.localEulerAngles = vector; }
            ObjectEdited?.Invoke(item); ShowProperties();
        }

        private void BuildScalarSlider(bool opacity, float x, float offset = 0)
        {
            var title = SpaceCanvasPresentationStyle.Label(propertyBody, opacity ? "Opacity title" : "Metallic title", "", new Vector2(x, -151 + offset), new Vector2(166, 24), 14, true);
            var go = new GameObject(opacity ? "透明度滑条" : "金属度滑条", typeof(RectTransform), typeof(Slider), typeof(SpaceCanvasScalarSlider));
            go.transform.SetParent(propertyBody, false);
            SpaceCanvasPresentationStyle.Place((RectTransform)go.transform, new Vector2(x, -184 + offset), new Vector2(166, 42));
            var track = SpaceCanvasPresentationStyle.SurfacePanel(go.transform, "Track", Vector2.zero, new Vector2(148, 6), SpaceCanvasPresentationStyle.Card, 3);
            track.GetComponent<Image>().raycastTarget = false;
            var handleArea = new GameObject("Handle area", typeof(RectTransform)); handleArea.transform.SetParent(go.transform, false);
            var area = (RectTransform)handleArea.transform; area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = new Vector2(14, 0); area.offsetMax = new Vector2(-14, 0);
            var handle = SpaceCanvasPresentationStyle.SurfacePanel(area, "Handle", Vector2.zero, new Vector2(28, 32), SpaceCanvasPresentationStyle.Accent, 8);
            var slider = go.GetComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1; slider.handleRect = (RectTransform)handle.transform; slider.targetGraphic = handle.GetComponent<Image>();
            // Slider stretches the thumb vertically; use an inset instead of adding extra height.
            slider.handleRect.sizeDelta = new Vector2(24, -16);
            // A transparent graphic makes the entire 42 px track a ray target.
            var hit = go.AddComponent<Image>(); hit.color = Color.clear;
            var transaction = go.GetComponent<SpaceCanvasScalarSlider>(); transaction.target = selected; transaction.slider = slider; transaction.label = title; transaction.opacity = opacity; transaction.Bind();
        }
    }
}
