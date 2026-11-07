using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>One undo transaction per slider gesture, including keyboard/navigation changes.</summary>
    public sealed class SpaceCanvasScalarSlider : MonoBehaviour, IPointerUpHandler, IEndDragHandler
    {
        public SpaceCanvasEditableObject target;
        public Slider slider;
        public TextMeshProUGUI label;
        public bool opacity;
        private SpaceCanvasProject before;
        private float initial;
        private static readonly HashSet<SpaceCanvasScalarSlider> active = new HashSet<SpaceCanvasScalarSlider>();

        public void Bind()
        {
            slider.SetValueWithoutNotify(opacity ? target.Alpha : target.Metallic);
            slider.onValueChanged.RemoveAllListeners(); slider.onValueChanged.AddListener(Preview);
            RefreshLabel();
        }
        public void Preview(float value)
        {
            if (target == null) return;
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, opacity ? target.Alpha : target.Metallic)) return;
            if (before == null)
            {
                if (!SceneExporter.TryCaptureCurrentScene(null, out before, out _)) return;
                initial = opacity ? target.Alpha : target.Metallic; active.Add(this);
            }
            var data = target.ToSceneObject();
            if (opacity) data.alpha = value; else data.metallic = value;
            target.Apply(data); RefreshLabel();
        }
        private void RefreshLabel()
        { if (label != null && target != null) label.text = (opacity ? "透明度  " : "金属度  ") + (opacity ? target.Alpha : target.Metallic).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }
        public void Finish()
        {
            if (before != null && target != null && !Mathf.Approximately(initial, opacity ? target.Alpha : target.Metallic))
                SpaceCanvasUndoService.PushSnapshot(before);
            before = null; active.Remove(this);
        }
        public static void FinishAll()
        { foreach (var item in new List<SpaceCanvasScalarSlider>(active)) if (item != null) item.Finish(); }
        public void OnPointerUp(PointerEventData data) { Finish(); }
        public void OnEndDrag(PointerEventData data) { Finish(); }
        private void OnDisable() { Finish(); }
    }
}
