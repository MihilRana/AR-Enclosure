using System.Collections.Generic;
using ArEnclosure.App;
using ArEnclosure.Core;
using ArEnclosure.Placement;
using ArEnclosure.Rules;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ArEnclosure.Obstacles
{
    /// <summary>
    /// Obstacle mode: tap two points on the floor along the base of a wall,
    /// or along the front base of a facing enclosure. The two taps become one
    /// obstacle line. Uses the locked floor, so false and reflected planes
    /// cannot place a point at the wrong height.
    /// </summary>
    public class ObstacleTool : MonoBehaviour
    {
        [SerializeField] AppModeController m_Modes;
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] FloorLock m_FloorLock;
        [SerializeField] ObstacleSet m_Obstacles;

        [Header("Marking")]
        [SerializeField] float m_MinimumLength = 0.10f;
        [SerializeField] float m_MaximumDistance = 3.5f;
        [SerializeField] Material m_MarkerMaterial;
        [SerializeField] float m_MarkerDiameter = 0.02f;

        public ObstacleKind Kind { get; private set; } = ObstacleKind.Wall;
        public bool HasPendingPoint => m_Pending != null;
        public string Prompt { get; private set; } = string.Empty;
        public bool IsActive => m_Modes != null && m_Modes.Current == AppMode.Obstacle;

        readonly List<ARRaycastHit> m_Hits = new List<ARRaycastHit>();
        GameObject m_Pending;
        Vector3 m_PendingPoint;
        float m_NoticeUntil = -1f;

        void Awake()
        {
            if (m_Modes == null) m_Modes = FindAnyObjectByType<AppModeController>();
            if (m_RaycastManager == null) m_RaycastManager = FindAnyObjectByType<ARRaycastManager>();
            if (m_FloorLock == null) m_FloorLock = FindAnyObjectByType<FloorLock>();
            if (m_Obstacles == null) m_Obstacles = FindAnyObjectByType<ObstacleSet>();
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            if (m_Modes != null) m_Modes.ModeChanged += OnModeChanged;
        }

        void OnDisable()
        {
            if (m_Modes != null) m_Modes.ModeChanged -= OnModeChanged;
            EnhancedTouchSupport.Disable();
        }

        void OnModeChanged(AppMode mode)
        {
            if (mode != AppMode.Obstacle) CancelPending();
        }

        public void ToggleKind()
        {
            Kind = Kind == ObstacleKind.Wall ? ObstacleKind.Enclosure : ObstacleKind.Wall;
        }

        /// <summary>Drops a half-marked line first, otherwise the last line.</summary>
        public void Undo()
        {
            if (m_Pending != null) CancelPending();
            else m_Obstacles.RemoveLast();
        }

        public void ClearAll()
        {
            CancelPending();
            m_Obstacles.Clear();
        }

        void Update()
        {
            UiTouchFilter.Refresh();
            if (!IsActive || m_Obstacles == null) return;

            UpdatePrompt();
            if (ARSession.state != ARSessionState.SessionTracking) return;
            if (TryGetTap(out var screenPoint)) HandleTap(screenPoint);
        }

        void HandleTap(Vector2 screenPoint)
        {
            if (!m_RaycastManager.Raycast(screenPoint, m_Hits, TrackableType.PlaneWithinPolygon))
            {
                RaycastProbe.Record("obstacle", m_Hits, -1);
                Notice("No floor under that tap.");
                return;
            }

            var chosen = 0;
            var rejection = HitRejection.None;
            if (m_FloorLock != null && !m_FloorLock.TryPickHit(m_Hits, m_MaximumDistance, out chosen, out rejection))
            {
                RaycastProbe.Record("obstacle", m_Hits, -1);
                Notice(rejection == HitRejection.OffLockedFloor ? "Tap the floor, at the base of the wall."
                    : rejection == HitRejection.TooFar ? "Step within " + m_MaximumDistance.ToString("F1") + " m."
                    : "Surface not reliable yet (small or merged).");
                return;
            }
            RaycastProbe.Record("obstacle", m_Hits, chosen);

            var point = m_Hits[chosen].pose.position;

            if (m_Pending == null)
            {
                m_PendingPoint = point;
                m_Pending = MakeMarker(point);
                return;
            }

            var length = Vector3.Distance(new Vector3(point.x, 0f, point.z), new Vector3(m_PendingPoint.x, 0f, m_PendingPoint.z));
            if (length < m_MinimumLength)
            {
                Notice("Too short. Tap further along the wall.");
                return;
            }

            m_Obstacles.Add(Kind, m_PendingPoint, point);
            CancelPending();
            Notice(Kind + " marked, " + length.ToString("F2") + " m long.");
        }

        void UpdatePrompt()
        {
            if (Time.time < m_NoticeUntil) return;

            if (ARSession.state != ARSessionState.SessionTracking)
                Prompt = "Waiting for tracking (" + ARSession.state + ")";
            else if (m_Pending == null)
                Prompt = Kind == ObstacleKind.Wall
                    ? "Tap the floor at the base of a wall."
                    : "Tap the floor at the front base of the other enclosure.";
            else
                Prompt = "Tap the other end of the line.";
        }

        void Notice(string message)
        {
            Prompt = message;
            m_NoticeUntil = Time.time + 2.5f;
        }

        GameObject MakeMarker(Vector3 point)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "ObstaclePointA";
            var collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var r = marker.GetComponent<Renderer>();
            if (m_MarkerMaterial != null && r != null) r.sharedMaterial = m_MarkerMaterial;
            marker.transform.position = point;
            marker.transform.localScale = Vector3.one * m_MarkerDiameter;
            return marker;
        }

        void CancelPending()
        {
            if (m_Pending != null) Destroy(m_Pending);
            m_Pending = null;
        }

        bool TryGetTap(out Vector2 screenPoint)
        {
            screenPoint = default;
            var touches = ETouch.activeTouches;
            for (var i = 0; i < touches.Count; i++)
            {
                if (touches[i].phase != InputTouchPhase.Began) continue;
                if (UiTouchFilter.IsBlocked(touches[i])) continue;
                screenPoint = touches[i].screenPosition;
                return true;
            }
            return false;
        }
    }
}
