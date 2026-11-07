using UnityEngine;

namespace SpaceCanvas.ProjectIO
{
    /// <summary>Pure world-space geometry for MR handles. No colliders or SDK-specific input.</summary>
    public static class SpaceCanvasHandleMath
    {
        public static bool AxisParameter(Ray ray, Vector3 origin, Vector3 axis, out float parameter)
        {
            parameter = 0;
            if (!Finite(ray.origin) || !Finite(ray.direction) || !Finite(origin) || !Finite(axis)) return false;
            axis.Normalize(); var direction = ray.direction.normalized;
            var dot = Vector3.Dot(direction, axis);
            var denominator = 1f - dot * dot;
            // Nearly end-on axes cannot provide a stable depth estimate. Keep the last pose.
            if (axis.sqrMagnitude < .9f || denominator < .025f) return false;
            var offset = ray.origin - origin;
            var distance = (dot * Vector3.Dot(axis, offset) - Vector3.Dot(direction, offset)) / denominator;
            if (distance < 0) return false;
            parameter = Vector3.Dot(axis, offset + direction * distance);
            return !float.IsNaN(parameter) && !float.IsInfinity(parameter);
        }

        public static bool PlaneDirection(Ray ray, Vector3 center, Vector3 normal, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!Finite(ray.origin) || !Finite(ray.direction) || !Finite(center) || !Finite(normal)) return false;
            if (Mathf.Abs(Vector3.Dot(ray.direction.normalized, normal.normalized)) < .04f) return false;
            if (!new Plane(normal, center).Raycast(ray, out var distance) || distance > 10f) return false;
            direction = ray.GetPoint(distance) - center;
            if (direction.sqrMagnitude < .000025f) return false;
            direction.Normalize(); return true;
        }

        /// <summary>Closest points on a forward ray and a finite line segment, including endpoints.</summary>
        public static float RaySegment(Ray ray, Vector3 a, Vector3 b, out float distance, out Vector3 point)
        {
            var d = ray.direction.normalized; var v = b - a; var w = ray.origin - a;
            var length = v.sqrMagnitude; var dv = Vector3.Dot(d, v);
            var denominator = length - dv * dv;
            var u = length < .0000001f ? 0 : denominator > .0000001f
                ? Mathf.Clamp01((Vector3.Dot(v, w) - dv * Vector3.Dot(d, w)) / denominator)
                : Mathf.Clamp01(Vector3.Dot(v, w) / length);
            point = a + v * u; distance = Mathf.Max(0, Vector3.Dot(point - ray.origin, d));
            if (distance == 0 && length > .0000001f)
            { u = Mathf.Clamp01(Vector3.Dot(v, w) / length); point = a + v * u; }
            return Vector3.Distance(ray.origin + d * distance, point);
        }

        public static bool Finite(Vector3 v) => Number(v.x) && Number(v.y) && Number(v.z);
        private static bool Number(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
