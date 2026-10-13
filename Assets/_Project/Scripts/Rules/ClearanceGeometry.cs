using System.Collections.Generic;
using UnityEngine;

namespace ArEnclosure.Rules
{
    public enum Face
    {
        Front,
        Back,
        Left,
        Right,
        Top
    }

    public enum ObstacleKind
    {
        Wall,
        Enclosure
    }

    public struct ObstacleLine
    {
        public int Id;
        public ObstacleKind Kind;
        public Vector3 A;
        public Vector3 B;
    }

    public struct FaceGap
    {
        public bool Found;
        public float Metres;
        public int ObstacleId;
        public Vector3 FacePoint;
        public Vector3 ObstaclePoint;
    }

    /// <summary>
    /// Horizontal gap from a face of the panel's oriented box to obstacle
    /// lines marked on the floor.
    ///
    /// Each face looks only along the strip of floor straight out from it,
    /// as wide as the face. An obstacle line is cut to that strip, and the
    /// nearest remaining point gives the gap. A wall off to one side does not
    /// count against the front, which is how a gangway is measured.
    /// </summary>
    public class ClearanceGeometry
    {
        readonly Transform m_Panel;
        readonly Bounds m_Local;
        readonly Vector3 m_FrontLocal;

        public ClearanceGeometry(Transform panel, Bounds localBounds, Vector3 frontLocal)
        {
            m_Panel = panel;
            m_Local = localBounds;
            m_FrontLocal = new Vector3(frontLocal.x, 0f, frontLocal.z).normalized;
        }

        public Vector3 OutwardLocal(Face face)
        {
            var front = m_FrontLocal;
            var right = Vector3.Cross(Vector3.up, front);
            switch (face)
            {
                case Face.Front: return front;
                case Face.Back: return -front;
                case Face.Right: return right;
                case Face.Left: return -right;
                default: return Vector3.up;
            }
        }

        /// <summary>
        /// Nearest obstacle of an accepted kind in front of a face. The top
        /// face has no floor-line obstacle, so it is never found.
        /// </summary>
        public FaceGap Measure(Face face, IReadOnlyList<ObstacleLine> obstacles, System.Func<ObstacleKind, bool> accept)
        {
            var best = new FaceGap { Found = false, Metres = float.PositiveInfinity };
            if (face == Face.Top || obstacles == null) return best;

            var n = OutwardLocal(face);
            var l = Vector3.Cross(Vector3.up, n);
            Extent(n, out _, out var faceOffset);
            Extent(l, out var uMin, out var uMax);
            var dCentre = Vector3.Dot(m_Local.center, n);

            foreach (var o in obstacles)
            {
                if (accept != null && !accept(o.Kind)) continue;

                var a = m_Panel.InverseTransformPoint(o.A);
                var b = m_Panel.InverseTransformPoint(o.B);
                var ua = Vector3.Dot(a, l);
                var ub = Vector3.Dot(b, l);
                var da = Vector3.Dot(a, n);
                var db = Vector3.Dot(b, n);

                // Keep the part of the line inside the face's strip and on
                // the outward side of the panel's centre.
                var t0 = 0f;
                var t1 = 1f;
                if (!ClipAbove(ua, ub, uMin, ref t0, ref t1)) continue;
                if (!ClipBelow(ua, ub, uMax, ref t0, ref t1)) continue;
                if (!ClipAbove(da, db, dCentre, ref t0, ref t1)) continue;

                // Distance is linear along the line, so the nearest point is
                // at one end of the clipped part.
                var d0 = Mathf.Lerp(da, db, t0);
                var d1 = Mathf.Lerp(da, db, t1);
                var t = d0 <= d1 ? t0 : t1;
                var d = Mathf.Min(d0, d1);

                // Negative means the line cuts into the panel. Report 0.
                var gap = Mathf.Max(0f, d - faceOffset);
                if (gap >= best.Metres) continue;

                var u = Mathf.Lerp(ua, ub, t);
                var local = Vector3.Lerp(a, b, t);
                best = new FaceGap
                {
                    Found = true,
                    Metres = gap,
                    ObstacleId = o.Id,
                    ObstaclePoint = m_Panel.TransformPoint(local),
                    FacePoint = m_Panel.TransformPoint(l * u + n * faceOffset + Vector3.up * local.y)
                };
            }

            return best;
        }

        /// <summary>Range of the panel's box along a local axis.</summary>
        void Extent(Vector3 axis, out float min, out float max)
        {
            min = float.PositiveInfinity;
            max = float.NegativeInfinity;
            var c = m_Local.center;
            var e = m_Local.extents;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                var v = Vector3.Dot(corner, axis);
                min = Mathf.Min(min, v);
                max = Mathf.Max(max, v);
            }
        }

        /// <summary>Restrict t so that lerp(va, vb, t) is at least limit.</summary>
        static bool ClipAbove(float va, float vb, float limit, ref float t0, ref float t1)
        {
            var dv = vb - va;
            if (Mathf.Abs(dv) < 1e-6f) return va >= limit && t0 <= t1;
            var t = (limit - va) / dv;
            if (dv > 0f) t0 = Mathf.Max(t0, t);
            else t1 = Mathf.Min(t1, t);
            return t0 <= t1;
        }

        /// <summary>Restrict t so that lerp(va, vb, t) is at most limit.</summary>
        static bool ClipBelow(float va, float vb, float limit, ref float t0, ref float t1)
        {
            return ClipAbove(-va, -vb, -limit, ref t0, ref t1);
        }

        /// <summary>
        /// Widest door leaf along the front face, from mesh parts under the
        /// named group. Parts narrower than the minimum (hinges, locks) are
        /// skipped. Returns false when no leaf is found: the caller must then
        /// show "not set", never a guess.
        /// </summary>
        public static bool TryWidestDoorLeaf(GameObject panel, string groupName, float minWidth, Vector3 frontLocal, out float width)
        {
            width = 0f;
            if (panel == null || string.IsNullOrEmpty(groupName)) return false;

            var group = FindDeep(panel.transform, groupName);
            if (group == null) return false;

            var front = new Vector3(frontLocal.x, 0f, frontLocal.z).normalized;
            var lateral = Vector3.Cross(Vector3.up, front);
            var toRoot = panel.transform.worldToLocalMatrix;
            var found = false;

            foreach (var filter in group.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                var m = toRoot * filter.transform.localToWorldMatrix;
                var c = mesh.bounds.center;
                var e = mesh.bounds.extents;
                var min = float.PositiveInfinity;
                var max = float.NegativeInfinity;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        c.x + ((i & 1) == 0 ? -e.x : e.x),
                        c.y + ((i & 2) == 0 ? -e.y : e.y),
                        c.z + ((i & 4) == 0 ? -e.z : e.z));
                    var v = Vector3.Dot(m.MultiplyPoint3x4(corner), lateral);
                    min = Mathf.Min(min, v);
                    max = Mathf.Max(max, v);
                }

                var w = max - min;
                if (w < minWidth) continue;
                width = Mathf.Max(width, w);
                found = true;
            }

            return found;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        public static bool TryParseAxis(string text, out Vector3 axis)
        {
            axis = Vector3.forward;
            switch ((text ?? "").Trim().ToUpperInvariant())
            {
                case "+Z": axis = Vector3.forward; return true;
                case "-Z": axis = Vector3.back; return true;
                case "+X": axis = Vector3.right; return true;
                case "-X": axis = Vector3.left; return true;
                default: return false;
            }
        }
    }
}
