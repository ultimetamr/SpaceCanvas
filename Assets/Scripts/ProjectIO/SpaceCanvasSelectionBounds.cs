using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>独立线框显示当前选择，不修改物体材质或写入工程颜色。</summary>
    public sealed class SpaceCanvasSelectionBounds : MonoBehaviour
    {
        public SpaceCanvasEditSession session;
        private LineRenderer line;
        private Material material;
        private SpaceCanvasEditableObject cached;
        private Bounds localBounds;
        private readonly Vector3[] corners = new Vector3[8];
        private static readonly int[] Route = {0,1,3,2,0,4,5,1,5,7,3,7,6,2,6,4};
        private readonly LineRenderer[] axes = new LineRenderer[3];
        private readonly Material[] axisMaterials = new Material[3];
        private void Awake()
        {
            var child = new GameObject("Selection Bounds"); child.transform.SetParent(transform, false);
            line = child.AddComponent<LineRenderer>(); line.useWorldSpace = true;
            line.widthMultiplier = .0025f; line.positionCount = 16;
            // Reuse a serialized, Android-included unlit resource instead of Shader.Find:
            // an unreferenced URP Unlit shader may be stripped from player builds.
            var template = Resources.Load<Material>("SpaceCanvasMaterials/OpenXRPointer");
            if (template != null)
            {
                material = new Material(template);
                material.color = new Color(.25f, .95f, 1f); line.sharedMaterial = material;
            }
            line.enabled = false;
            for (var i = 0; i < 3; i++)
            {
                var axis = new GameObject("Axis " + i); axis.transform.SetParent(transform, false);
                axes[i] = axis.AddComponent<LineRenderer>(); axes[i].useWorldSpace = true; axes[i].positionCount = 2; axes[i].enabled = false;
                if (template != null)
                {
                    axisMaterials[i] = new Material(template); axisMaterials[i].color = i == 0 ? new Color(.95f,.32f,.28f) : i == 1 ? new Color(.38f,.88f,.42f) : new Color(.35f,.64f,1);
                    axes[i].sharedMaterial = axisMaterials[i];
                }
            }
        }
        private void LateUpdate()
        {
            var item = session == null ? null : session.Selected;
            line.enabled = item != null && item.gameObject.activeInHierarchy;
            foreach (var axis in axes) axis.enabled = line.enabled && (session == null || session.Handles == null);
            if (!line.enabled) { cached = null; return; }
            if (cached != item) { cached = item; localBounds = SpaceCanvasGeometry.LocalBounds(item); }
            for (var i = 0; i < corners.Length; i++) corners[i] = item.transform.TransformPoint(localBounds.center + Vector3.Scale(localBounds.extents + Vector3.one * .02f,
                new Vector3((i&1)==0 ? -1 : 1,(i&2)==0 ? -1 : 1,(i&4)==0 ? -1 : 1)));
            for (var i = 0; i < Route.Length; i++) line.SetPosition(i, corners[Route[i]]);
            var center = item.transform.TransformPoint(localBounds.center);
            for (var i = 0; i < axes.Length; i++)
            {
                var direction = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
                if (item.transform.parent != null) direction = item.transform.parent.TransformDirection(direction).normalized;
                axes[i].SetPosition(0,center); axes[i].SetPosition(1,center + direction * .075f);
                axes[i].widthMultiplier = session.axis == (SpaceCanvasEditAxis)(i+1) ? .003f : .0014f;
            }
        }
        private void OnDestroy() { if (material != null) Destroy(material); foreach(var owned in axisMaterials) if(owned != null) Destroy(owned); }
    }
}
