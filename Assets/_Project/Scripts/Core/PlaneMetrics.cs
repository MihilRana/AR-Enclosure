using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ArEnclosure.Core
{
    /// <summary>Plane geometry helpers shared by the floor lock and diagnostics.</summary>
    public static class PlaneMetrics
    {
        /// <summary>Area of the plane's boundary polygon in square metres.</summary>
        public static float Area(ARPlane plane)
        {
            var boundary = plane.boundary;
            if (!boundary.IsCreated || boundary.Length < 3) return 0f;

            var twice = 0f;
            for (var i = 0; i < boundary.Length; i++)
            {
                var a = boundary[i];
                var b = boundary[(i + 1) % boundary.Length];
                twice += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(twice) * 0.5f;
        }

        /// <summary>
        /// Horizontal distance from a world point to the plane's polygon,
        /// zero when the point is inside it. Height is ignored on purpose:
        /// this answers "is this plane under or beside the tap".
        /// </summary>
        public static float HorizontalDistance(ARPlane plane, Vector3 worldPoint)
        {
            var boundary = plane.boundary;
            if (!boundary.IsCreated || boundary.Length < 3) return float.PositiveInfinity;

            var local = plane.transform.InverseTransformPoint(worldPoint);
            var p = new Vector2(local.x, local.z);

            var inside = false;
            var nearest = float.PositiveInfinity;
            for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
            {
                var a = boundary[i];
                var b = boundary[j];

                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;

                nearest = Mathf.Min(nearest, DistanceToSegment(p, a, b));
            }

            return inside ? 0f : nearest;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-8f) return Vector2.Distance(p, a);
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
