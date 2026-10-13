using System.Collections.Generic;
using ArEnclosure.Core;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArEnclosure.Placement
{
    public enum HitRejection
    {
        None,
        NoHits,
        TooFar,
        NoUsablePlane,
        OffLockedFloor,
        /// <summary>The ray points at or above the horizon, so it never meets the floor.</summary>
        AboveHorizon
    }

    /// <summary>
    /// Decides which planes count as the floor.
    ///
    /// Before placement: only planes that are unmerged, fully tracked,
    /// horizontal and at least the minimum area are usable, and placement is
    /// refused while two usable planes near the tap disagree on height.
    ///
    /// After placement: the floor plane is locked. Drag and measurement only
    /// accept hits on that plane, or on whatever ARCore merges it into, or on
    /// a usable plane within the height band of the locked floor's current
    /// height. A false upper floor nearer the camera can no longer win.
    ///
    /// Visuals: before the lock only usable planes are drawn. After it only
    /// the locked floor is drawn, so stacked and false planes disappear.
    /// </summary>
    public class FloorLock : MonoBehaviour
    {
        [SerializeField] ARPlaneManager m_PlaneManager;

        [Header("Rules")]
        [Tooltip("Hits within this height of the locked floor count as floor.")]
        [SerializeField] float m_HeightTolerance = 0.03f;
        [Tooltip("Planes smaller than this are ignored and hidden.")]
        [SerializeField] float m_MinimumPlaneArea = 0.25f;
        [Tooltip("Planes within this horizontal distance of a placement tap must agree on height.")]
        [SerializeField] float m_SettleRadius = 1.0f;

        [Header("Visuals")]
        [SerializeField] bool m_HideUnusablePlanes = true;

        public bool HasLock { get; private set; }
        public TrackableId LockedId { get; private set; } = TrackableId.invalidId;
        public TrackableId CurrentId { get; private set; } = TrackableId.invalidId;
        public float LockedHeight { get; private set; }
        public float CurrentHeight { get; private set; }
        public bool CurrentPlaneTracked { get; private set; }
        public float Drift => CurrentHeight - LockedHeight;
        public bool HasDrifted => HasLock && Mathf.Abs(Drift) > m_HeightTolerance;
        public float HeightTolerance => m_HeightTolerance;
        public float MinimumPlaneArea => m_MinimumPlaneArea;
        public float SettleRadius => m_SettleRadius;

        /// <summary>
        /// Before the lock, a floor offered for confirmation. When set, only
        /// this plane is drawn so the user sees exactly what they would accept.
        /// </summary>
        public TrackableId HighlightId { get; set; } = TrackableId.invalidId;

        readonly List<ARPlane> m_Scratch = new List<ARPlane>();

        void Awake()
        {
            if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
        }

        /// <summary>
        /// After the lock: where a screen tap meets the floor, treating the
        /// floor as an endless flat plane at its current height. Plane outlines
        /// stop growing once plane finding is off, so the outline cannot be
        /// used. Refuses rays at or above the horizon (5 degrees margin) and
        /// hits beyond maxDistance, where a small height error becomes a large
        /// horizontal one.
        /// </summary>
        public bool TryRayFloor(Camera camera, Vector2 screenPoint, float maxDistance,
            out Vector3 point, out float distance, out HitRejection rejection)
        {
            point = default;
            distance = 0f;
            rejection = HitRejection.NoHits;
            if (!HasLock || camera == null) return false;

            var ray = camera.ScreenPointToRay(screenPoint);
            if (ray.direction.y > -0.087f)
            {
                rejection = HitRejection.AboveHorizon;
                return false;
            }

            var along = (CurrentHeight - ray.origin.y) / ray.direction.y;
            if (along <= 0f)
            {
                rejection = HitRejection.AboveHorizon;
                return false;
            }

            // The screen ray starts at the near clip plane, not the camera, so
            // measure the distance from the camera itself.
            point = ray.GetPoint(along);
            distance = Vector3.Distance(camera.transform.position, point);
            if (distance > maxDistance)
            {
                rejection = HitRejection.TooFar;
                return false;
            }

            rejection = HitRejection.None;
            return true;
        }

        /// <summary>
        /// A whole plane, not a tap, is settled when no other usable plane
        /// near its outline disagrees with its height by more than the band.
        /// Same rule as the placement-tap check, applied to the plane's edge.
        /// </summary>
        public bool IsPlaneSettled(ARPlane candidate, out float worstGap)
        {
            worstGap = 0f;
            if (candidate == null || m_PlaneManager == null) return false;

            var y = candidate.transform.position.y;
            m_Scratch.Clear();
            foreach (var plane in m_PlaneManager.trackables) m_Scratch.Add(plane);

            foreach (var other in m_Scratch)
            {
                if (other == candidate || !IsUsable(other)) continue;
                var gap = Mathf.Abs(other.transform.position.y - y);
                if (gap <= m_HeightTolerance) continue;
                if (!IsNear(candidate, other)) continue;
                worstGap = Mathf.Max(worstGap, gap);
            }

            return worstGap <= 0f;
        }

        bool IsNear(ARPlane candidate, ARPlane other)
        {
            if (PlaneMetrics.HorizontalDistance(candidate, other.center) <= m_SettleRadius) return true;
            var boundary = other.boundary;
            if (!boundary.IsCreated) return false;
            for (var i = 0; i < boundary.Length; i++)
            {
                var world = other.transform.TransformPoint(new Vector3(boundary[i].x, 0f, boundary[i].y));
                if (PlaneMetrics.HorizontalDistance(candidate, world) <= m_SettleRadius) return true;
            }
            return false;
        }

        void Update()
        {
            if (!HasLock || m_PlaneManager == null) return;

            // Follow the locked plane through merges. A merged plane takes the
            // pose of the plane it merged into, so the end of the chain is the
            // floor ARCore believes in now.
            var plane = m_PlaneManager.GetPlane(CurrentId);
            var guard = 0;
            while (plane != null && plane.subsumedBy != null && guard++ < 16)
                plane = plane.subsumedBy;

            CurrentPlaneTracked = plane != null;
            if (plane == null) return;

            CurrentId = plane.trackableId;
            CurrentHeight = plane.transform.position.y;
        }

        void LateUpdate()
        {
            if (!m_HideUnusablePlanes || m_PlaneManager == null) return;

            // After the plane visualisers have run this frame, so ours wins.
            // Before the lock, every usable plane is drawn so the user can see
            // what is available. After it, only the locked floor is drawn. Its
            // merged members copy its outline exactly, so drawing them too
            // would only stack the same dots again.
            foreach (var plane in m_PlaneManager.trackables)
            {
                bool visible;
                if (HasLock) visible = plane.trackableId == CurrentId;
                else if (HighlightId != TrackableId.invalidId) visible = plane.trackableId == HighlightId;
                else visible = IsUsable(plane);
                var planeRenderer = plane.GetComponent<Renderer>();
                if (planeRenderer != null && planeRenderer.enabled != visible) planeRenderer.enabled = visible;
            }
        }

        /// <summary>Unmerged, fully tracked, facing up and big enough.</summary>
        public bool IsUsable(ARPlane plane)
        {
            return plane != null &&
                   plane.subsumedBy == null &&
                   plane.trackingState == TrackingState.Tracking &&
                   plane.alignment == PlaneAlignment.HorizontalUp &&
                   PlaneMetrics.Area(plane) >= m_MinimumPlaneArea;
        }

        public string UsabilityLabel(ARPlane plane)
        {
            if (plane.subsumedBy != null) return "merged";
            if (plane.trackingState != TrackingState.Tracking) return "limited";
            if (plane.alignment != PlaneAlignment.HorizontalUp) return "not floor";
            if (PlaneMetrics.Area(plane) < m_MinimumPlaneArea) return "small";
            return "ok";
        }

        /// <summary>
        /// Picks the hit drag or measurement should use. Hits arrive nearest
        /// first. Without a lock the first usable plane wins. With a lock only
        /// the locked floor, or a usable plane at its height, can win.
        /// </summary>
        public bool TryPickHit(List<ARRaycastHit> hits, float maxDistance, out int index, out HitRejection rejection)
        {
            index = -1;
            if (hits == null || hits.Count == 0)
            {
                rejection = HitRejection.NoHits;
                return false;
            }

            rejection = HitRejection.TooFar;
            for (var i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                if (hit.distance > maxDistance) continue;

                var plane = m_PlaneManager != null ? m_PlaneManager.GetPlane(hit.trackableId) : null;

                if (HasLock && IsLockedFloor(hit.trackableId))
                {
                    index = i;
                    rejection = HitRejection.None;
                    return true;
                }

                if (!IsUsable(plane))
                {
                    if (rejection == HitRejection.TooFar) rejection = HitRejection.NoUsablePlane;
                    continue;
                }

                // The band sits on the locked floor as it is now, not where it
                // was at lock time, so drift cannot let a false plane back in.
                if (HasLock && Mathf.Abs(hit.pose.position.y - CurrentHeight) > m_HeightTolerance)
                {
                    rejection = HitRejection.OffLockedFloor;
                    continue;
                }

                index = i;
                rejection = HitRejection.None;
                return true;
            }

            return false;
        }

        /// <summary>
        /// False while two usable planes near the tap disagree on the floor
        /// height. ARCore has not settled, and either could be the one it
        /// keeps, so placing now would lock onto a guess.
        /// </summary>
        public bool IsFloorSettled(Vector3 hitPoint, ARPlane hitPlane, out float worstGap)
        {
            worstGap = 0f;
            if (m_PlaneManager == null) return true;

            m_Scratch.Clear();
            foreach (var plane in m_PlaneManager.trackables) m_Scratch.Add(plane);

            foreach (var plane in m_Scratch)
            {
                if (plane == hitPlane || !IsUsable(plane)) continue;
                if (PlaneMetrics.HorizontalDistance(plane, hitPoint) > m_SettleRadius) continue;

                var gap = Mathf.Abs(plane.transform.position.y - hitPoint.y);
                if (gap > m_HeightTolerance) worstGap = Mathf.Max(worstGap, gap);
            }

            return worstGap <= 0f;
        }

        public void Lock(ARPlane plane, float height)
        {
            HasLock = true;
            LockedId = plane != null ? plane.trackableId : TrackableId.invalidId;
            CurrentId = LockedId;
            LockedHeight = height;
            CurrentHeight = height;
            CurrentPlaneTracked = plane != null;
        }

        /// <summary>
        /// Accept the floor's current height as the new reference. Called after
        /// the panel has been re-seated onto it, which clears the drift.
        /// </summary>
        public void Rebase()
        {
            if (!HasLock) return;
            LockedHeight = CurrentHeight;
        }

        public void Clear()
        {
            HighlightId = TrackableId.invalidId;
            HasLock = false;
            LockedId = TrackableId.invalidId;
            CurrentId = TrackableId.invalidId;
        }

        bool IsLockedFloor(TrackableId id)
        {
            if (id == CurrentId || id == LockedId) return true;

            // A hit on a plane that has since been merged into the floor.
            var plane = m_PlaneManager != null ? m_PlaneManager.GetPlane(id) : null;
            var guard = 0;
            while (plane != null && plane.subsumedBy != null && guard++ < 16)
            {
                plane = plane.subsumedBy;
                if (plane.trackableId == CurrentId) return true;
            }
            return false;
        }
    }
}
