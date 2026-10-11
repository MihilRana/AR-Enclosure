using UnityEngine;

namespace ArEnclosure.Core
{
    /// <summary>
    /// Measures a model without knowing anything about it in advance.
    /// Nothing here is tied to panel-126062, so swapping the test model
    /// changes nothing in code.
    /// </summary>
    public static class ModelBounds
    {
        /// <summary>
        /// Size of the model in its own local space, ignoring where it sits
        /// or how it is rotated. Works on a prefab asset as well as a live
        /// instance, so you can compare authored size against placed size.
        /// </summary>
        public static bool TryGetLocalSize(GameObject root, out Vector3 size)
        {
            size = Vector3.zero;
            if (!TryGetLocalBounds(root, out var bounds)) return false;
            size = bounds.size;
            return true;
        }

        /// <summary>
        /// Box around the model in its own local space. This is the panel's
        /// oriented box: transform it by the root to get faces, base centre
        /// or a hit volume that stays tight however the panel is turned.
        /// </summary>
        public static bool TryGetLocalBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            if (root == null) return false;

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return false;

            var toRoot = root.transform.worldToLocalMatrix;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var found = false;

            foreach (var filter in filters)
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var toLocal = toRoot * filter.transform.localToWorldMatrix;
                var b = mesh.bounds;
                var c = b.center;
                var e = b.extents;

                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        c.x + (((i & 1) == 0) ? -e.x : e.x),
                        c.y + (((i & 2) == 0) ? -e.y : e.y),
                        c.z + (((i & 4) == 0) ? -e.z : e.z));

                    var p = toLocal.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    found = true;
                }
            }

            if (!found) return false;
            bounds.SetMinMax(min, max);
            return true;
        }

        /// <summary>
        /// True size of a placed instance in metres, rotation independent.
        /// Local size times world scale, so a stray scale anywhere up the
        /// hierarchy shows up here.
        /// </summary>
        public static bool TryGetWorldSize(GameObject instance, out Vector3 size)
        {
            size = Vector3.zero;
            if (!TryGetLocalSize(instance, out var local)) return false;

            var s = instance.transform.lossyScale;
            size = new Vector3(
                local.x * Mathf.Abs(s.x),
                local.y * Mathf.Abs(s.y),
                local.z * Mathf.Abs(s.z));
            return true;
        }

        /// <summary>
        /// Axis aligned world box from the renderers. Used for seating the
        /// model on a surface, not for size reporting, because an AABB grows
        /// when the object is rotated.
        /// </summary>
        public static bool TryGetWorldAabb(GameObject instance, out Bounds bounds)
        {
            bounds = new Bounds();
            if (instance == null) return false;

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            var found = false;

            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                if (!found)
                {
                    bounds = r.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return found;
        }
    }
}
