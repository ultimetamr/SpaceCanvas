using System.Collections.Generic;
using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>
    /// Editing selection is independent of physical collision. Disabled colliders keep their
    /// serialized state; an oriented visual bound supplies the editing hit instead.
    /// </summary>
    public static class SpaceCanvasPicking
    {
        public struct Hit
        {
            public SpaceCanvasEditableObject item;
            public Vector3 point;
            public float distance;
        }

        private static readonly List<SpaceCanvasEditableObject> candidates = new List<SpaceCanvasEditableObject>();

        /// <summary>Returns the nearest physical obstacle or editable visual bound, in world meters.</summary>
        public static bool Raycast(Ray ray, float maxDistance, out Hit hit)
        {
            hit = default;
            if (maxDistance <= 0 || ray.direction.sqrMagnitude < .000001f) return false;
            ray = new Ray(ray.origin, ray.direction.normalized);
            var distance = maxDistance;
            var found = Physics.Raycast(ray, out var physical, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            if (found)
            {
                distance = physical.distance;
                hit = new Hit { item = physical.collider.GetComponentInParent<SpaceCanvasEditableObject>(),
                    point = physical.point, distance = distance };
            }
            // Reuse the list: import, undo and EditMode authoring may replace the hierarchy
            // without a runtime OnEnable callback. Mesh bounds remain cached on each item.
            candidates.Clear();
            if (SpaceCanvasRuntime.ContentRoot != null)
                SpaceCanvasRuntime.ContentRoot.GetComponentsInChildren(false, candidates);
            else candidates.AddRange(SpaceCanvasRuntime.GetEditableObjects());
            foreach (var item in candidates)
            {
                if (item == null || item.ColliderEnabled || !item.isActiveAndEnabled
                    || (SpaceCanvasRuntime.ContentRoot != null && !item.transform.IsChildOf(SpaceCanvasRuntime.ContentRoot))) continue;
                var matrix = item.transform.worldToLocalMatrix;
                var direction = matrix.MultiplyVector(ray.direction);
                if (direction.sqrMagnitude < .000001f) continue;
                var localRay = new Ray(matrix.MultiplyPoint3x4(ray.origin), direction.normalized);
                if (!item.PickingBounds.IntersectRay(localRay, out var localDistance)) continue;
                var point = item.transform.TransformPoint(localRay.GetPoint(localDistance));
                var worldDistance = Vector3.Dot(point - ray.origin, ray.direction);
                // A foreground physical object still occludes a disabled-collider object.
                if (worldDistance < 0 || worldDistance > distance) continue;
                distance = worldDistance; found = true;
                hit = new Hit { item = item, point = point, distance = worldDistance };
            }
            return found;
        }
    }
}
