using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Geometry shared by placement, selection and asset auditing. Bounds exclude nested editable objects.</summary>
    public static class SpaceCanvasGeometry
    {
        public static Bounds LocalBounds(SpaceCanvasEditableObject item)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var found = false;
            foreach (var mesh in item.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.sharedMesh == null || mesh.GetComponentInParent<SpaceCanvasEditableObject>(true) != item) continue;
                var local = mesh.sharedMesh.bounds;
                var matrix = item.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                for (var i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = matrix.MultiplyPoint3x4(corner);
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return found ? bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        public static Vector3 GroundedPosition(Vector3 contactInParent, Bounds localBounds, Vector3 scale)
        {
            // Factories have different pivots. Ground the visual bottom, not the root transform.
            var bottom = Mathf.Min(localBounds.min.y * scale.y, localBounds.max.y * scale.y);
            return contactInParent - Vector3.up * bottom;
        }
    }
}
