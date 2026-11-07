using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Shared MR visual tokens. All dimensions are UI pixels; docks use 0.001 metres/pixel.</summary>
    public static class SpaceCanvasPresentationStyle
    {
        public static readonly Color Surface = new Color(.073f,.080f,.090f,.98f);
        public static readonly Color Card = new Color(.12f,.13f,.145f,1f);
        public static readonly Color Ink = new Color(.94f,.93f,.90f,1f);
        public static readonly Color Muted = new Color(.68f,.70f,.73f,1f);
        public static readonly Color Accent = new Color(.84f,.72f,.50f,1f);
        public static readonly Color Paper = new Color(.89f,.87f,.82f,1f);
        public static readonly Color DarkInk = new Color(.14f,.16f,.18f,1f);
        public static readonly Color Selected = new Color(.27f,.245f,.195f,1f);
        public static void Place(RectTransform rect, Vector2 center, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
            rect.anchoredPosition = center; rect.sizeDelta = size;
        }
        public static void Dock(GameObject panel, Vector3 pixelPosition, Vector2 size, float yaw = 0, int order = 100)
        {
            Place((RectTransform)panel.transform, Vector2.zero, size);
            panel.transform.localPosition = pixelPosition; panel.transform.localRotation = Quaternion.Euler(0,yaw,0);
            // Unity's Editor can return a missing native component wrapper. Use Unity's
            // null comparison rather than C# ?? before adding a nested Canvas.
            var canvas = panel.GetComponent<Canvas>();
            if (canvas == null) canvas = panel.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = order;
            SpaceCanvasUIClarity.ConfigureCanvas(canvas);
            if (panel.GetComponent<GraphicRaycaster>() == null) panel.AddComponent<GraphicRaycaster>();
            var image = panel.GetComponent<Image>(); if (image != null) image.color = Surface;
        }
        public static void ButtonStyle(Button button)
        {
            // SpaceCanvasButtonFeedback owns the complete tint. Selectable's ColorTint
            // also animates CanvasRenderer tint and otherwise multiplies the same highlight.
            button.transition = Selectable.Transition.None;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white; colors.pressedColor = Color.white; colors.fadeDuration = .12f;
            colors.disabledColor = new Color(1,1,1,.28f);
            button.colors = colors;
            if (button.GetComponent<SpaceCanvasButtonFeedback>() == null) button.gameObject.AddComponent<SpaceCanvasButtonFeedback>();
        }
        public static TextMeshProUGUI Label(Transform parent, string name, string value, Vector2 position, Vector2 size,
            int fontSize = 22, bool muted = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent,false);
            Place((RectTransform)go.transform,position,size);
            var text = go.GetComponent<TextMeshProUGUI>(); text.font = SpaceCanvasTypography.Font; text.text = value;
            text.fontSize = fontSize; text.color = muted ? Muted : Ink; text.raycastTarget = false;
            // CJK line metrics exceed glyph height. Ellipsis can hide a whole short label
            // vertically in compact buttons; allow its line box to extend inside the control.
            text.overflowMode = TextOverflowModes.Overflow; SpaceCanvasUnityCompatibility.SetTextWrapping(text, false);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            SpaceCanvasUIClarity.ConfigureText(text);
            return text;
        }
        public static Button Button(Transform parent, string name, string title, Vector2 position, Vector2 size, UnityAction action)
        {
            var go = new GameObject(name,typeof(RectTransform),typeof(SpaceCanvasRoundedPanel),typeof(Button));
            go.transform.SetParent(parent,false); Place((RectTransform)go.transform,position,size);
            var image = go.GetComponent<Image>(); image.color = Card;
            var button = go.GetComponent<Button>(); button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(action);
            // CJK ascenders/descenders need a taller line box than Latin glyphs at the same size.
            // Keep horizontal padding, but allow the centered line to use the button's inner height.
            var text = Label(go.transform,"Caption",title,Vector2.zero,new Vector2(size.x-16,size.y-8),22);
            text.alignment = TextAlignmentOptions.Center; ButtonStyle(button); return button;
        }

        /// <summary>Update both the saved editor visual and the runtime pointer feedback baseline.</summary>
        public static void Tone(Button button, Color fill, bool darkText = false)
        {
            button.targetGraphic.color = fill;
            var feedback = button.GetComponent<SpaceCanvasButtonFeedback>();
            if (feedback != null) feedback.SetIdleColor(fill);
            foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>(true)) label.color = darkText ? DarkInk : Ink;
        }

        public static SpaceCanvasRoundedPanel SurfacePanel(Transform parent, string name, Vector2 position, Vector2 size, Color fill, float radius = 20)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(SpaceCanvasRoundedPanel));
            go.transform.SetParent(parent,false); Place((RectTransform)go.transform, position, size);
            var panel = go.GetComponent<SpaceCanvasRoundedPanel>(); panel.color = fill; panel.radius = radius;
            panel.raycastTarget = false; return panel;
        }

        public static void Rule(Transform parent, Vector2 position, float width)
        {
            var panel = SurfacePanel(parent,"Divider",position,new Vector2(width,1),new Color(1,1,1,.10f),0);
            panel.borderWidth = 0; panel.topLight = 0;
        }
    }
}
