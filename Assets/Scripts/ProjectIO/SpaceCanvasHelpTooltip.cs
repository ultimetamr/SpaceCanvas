using UnityEngine;
using UnityEngine.EventSystems;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Ray and mouse share EventSystem hover events; tooltip graphics never intercept input.</summary>
    public sealed class SpaceCanvasHelpTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GameObject label;
        public void OnPointerEnter(PointerEventData eventData) { if (label != null) label.SetActive(true); }
        public void OnPointerExit(PointerEventData eventData) { if (label != null) label.SetActive(false); }
        private void OnDisable() { if (label != null) label.SetActive(false); }
    }
}
