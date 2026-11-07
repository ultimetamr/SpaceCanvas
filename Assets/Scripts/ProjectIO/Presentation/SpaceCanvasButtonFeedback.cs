using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Per-pointer hover/press feedback; supports both hands without hover flicker.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class SpaceCanvasButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        private readonly HashSet<int> hovering = new HashSet<int>();
        private readonly HashSet<int> pressing = new HashSet<int>();
        private Button button;
        private Graphic graphic;
        private Color idle;
        private SpaceCanvasRoundedPanel panel;
        private Color idleBorder;
        private void Awake()
        {
            button = GetComponent<Button>(); graphic = button.targetGraphic;
            if (graphic == null) return;
            button.transition = Selectable.Transition.None;
            graphic.CrossFadeColor(Color.white, 0f, true, true);
            idle = graphic.color;
            panel = graphic.GetComponent<SpaceCanvasRoundedPanel>();
            if (panel != null) idleBorder = panel.borderColor;
        }
        public void OnPointerEnter(PointerEventData e) { hovering.Add(e.pointerId); }
        public void OnPointerExit(PointerEventData e) { hovering.Remove(e.pointerId); pressing.Remove(e.pointerId); }
        public void OnPointerDown(PointerEventData e) { pressing.Add(e.pointerId); }
        public void OnPointerUp(PointerEventData e) { pressing.Remove(e.pointerId); }
        public void SetIdleColor(Color value)
        {
            if (idle == value && graphic != null) return;
            idle = value;
            if (graphic == null) { var control = GetComponent<Button>(); if (control != null) graphic = control.targetGraphic; }
            if (graphic != null && hovering.Count == 0) graphic.color = value;
        }
        private void LateUpdate()
        {
            if (graphic == null) return;
            var interactable = button.IsInteractable();
            var active = interactable && hovering.Count > 0;
            var target = active ? Color.Lerp(idle, SpaceCanvasPresentationStyle.Accent, pressing.Count > 0 ? .30f : .13f) : idle;
            if (!interactable) target.a *= .28f;
            // Stop dirtying UI geometry once a transition has settled.
            var next = Color.Lerp(graphic.color, target, 1 - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            if (Mathf.Abs(next.r-target.r)+Mathf.Abs(next.g-target.g)+Mathf.Abs(next.b-target.b)+Mathf.Abs(next.a-target.a) < .001f) next = target;
            if (graphic.color != next) graphic.color = next;
            if (panel != null)
            {
                var border = active ? new Color(.84f,.72f,.50f,.8f) : idleBorder;
                if (panel.borderColor != border) { panel.borderColor = border; panel.SetVerticesDirty(); }
            }
        }
        private void OnDisable()
        {
            hovering.Clear(); pressing.Clear();
            if (graphic != null) graphic.color = idle;
            if (panel != null) { panel.borderColor = idleBorder; panel.SetVerticesDirty(); }
        }
    }
}
