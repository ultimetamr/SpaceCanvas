using UnityEngine;
using UnityEngine.UI;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Analytic rounded rectangle. Shape data travels in vertex UVs, so panels share one
    /// material without overwriting each other's radius, border or hover colour. The shader
    /// softens the silhouette and subpixel borders using their actual screen footprint.
    /// RectTransform and Graphic raycast bounds retain their original dimensions.
    /// </summary>
    public sealed class SpaceCanvasRoundedPanel : Image
    {
        public float radius = 20f;
        public float borderWidth = 1f;
        public Color borderColor = new Color(1, 1, 1, .11f);
        [Range(0, .3f)] public float topLight = .035f;
        private static Material sharedPanelMaterial;
        public override Material defaultMaterial
        {
            get
            {
                if (sharedPanelMaterial == null)
                    sharedPanelMaterial = Resources.Load<Material>("SpaceCanvasMaterials/RoundedPanel");
                return sharedPanelMaterial != null ? sharedPanelMaterial : base.defaultMaterial;
            }
        }
        protected override void OnEnable() { base.OnEnable(); ConfigureVertexChannels(); }
        protected override void OnCanvasHierarchyChanged() { base.OnCanvasHierarchyChanged(); ConfigureVertexChannels(); }
        private void ConfigureVertexChannels()
        {
            // A freshly reparented Graphic can still have an empty cached canvas.
            var owner = canvas != null ? canvas : GetComponentInParent<Canvas>();
            if (owner != null) SpaceCanvasUIClarity.ConfigureCanvas(owner);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            ConfigureVertexChannels();
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            var half = rect.size * .5f;
            var round = Mathf.Clamp(radius, 0, Mathf.Min(half.x, half.y));
            var shape = new Vector4(round, Mathf.Max(0, borderWidth), Mathf.Clamp(topLight, 0, .3f), 0);
            // Allow the shader's coverage to fade outside the silhouette. This is drawing
            // padding only; pointer hit testing still uses the original RectTransform.
            const float padding = 3f;
            var vertex = UIVertex.simpleVert;
            vertex.color = color; vertex.uv1 = shape;
            vertex.uv2 = new Vector4(borderColor.r, borderColor.g, borderColor.b, borderColor.a);
            for (var i = 0; i < 4; i++)
            {
                var x = i < 2 ? rect.xMin - padding : rect.xMax + padding;
                var y = i == 0 || i == 3 ? rect.yMin - padding : rect.yMax + padding;
                vertex.position = new Vector3(x, y, 0);
                vertex.uv0 = new Vector4(x - rect.center.x, y - rect.center.y, half.x, half.y);
                mesh.AddVert(vertex);
            }
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(2, 3, 0);
        }
    }
}
