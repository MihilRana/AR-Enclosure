using System;
using System.Collections.Generic;
using ArEnclosure.App;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ArEnclosure.Measurement
{
    public enum MeasureKind
    {
        Distance,
        Perimeter
    }

    public struct MeasurementResult
    {
        public int RunId;
        public MeasureKind Kind;
        public Vector3[] Points;
        public float Metres;
        public float CameraDistance;
        public string TrackingState;
        public bool Logged;
    }

    /// <summary>
    /// Tap-to-measure on detected planes, active only in Measure mode.
    /// Distance: tap A, tap B. Perimeter: tap three or more points, then
    /// CloseLoop. Points are frozen world positions and are not parented to
    /// any plane, so plane merging cannot move a measurement after the tap.
    /// Every result goes to the CSV with a run id that matches the screen.
    /// </summary>
    public class MeasurementTool : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] AppModeController m_Modes;
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] XROrigin m_XROrigin;

        [Header("Visuals")]
        [Tooltip("URP Unlit material asset. Referenced, never Shader.Find, so IL2CPP keeps the shader.")]
        [SerializeField] Material m_MarkerMaterial;
        [SerializeField] float m_MarkerDiameter = 0.015f;
        [SerializeField] float m_LineWidth = 0.004f;

        [Header("Logging")]
        [SerializeField] string m_FileName = "measurements.csv";

        public event Action<MeasurementResult> MeasurementCompleted;

        public MeasureKind Kind { get; private set; } = MeasureKind.Distance;
        public int PointCount => m_Points.Count;
        public bool IsComplete { get; private set; }
        public bool HasResult { get; private set; }
        public MeasurementResult LastResult { get; private set; }
        public string Prompt { get; private set; } = string.Empty;
        public string LogPath => m_Log?.Path;
        public bool IsActive => m_Modes == null || m_Modes.Current == AppMode.Measure;
        public bool CanCloseLoop => Kind == MeasureKind.Perimeter && !IsComplete && m_Points.Count >= 3;

        readonly List<ARRaycastHit> m_Hits = new List<ARRaycastHit>();
        readonly List<Vector3> m_Points = new List<Vector3>();
        readonly List<GameObject> m_Markers = new List<GameObject>();

        MeasurementLog m_Log;
        LineRenderer m_Line;
        bool m_AllPlanesTracking = true;

        void Awake()
        {
            if (m_Modes == null) m_Modes = FindAnyObjectByType<AppModeController>();
            if (m_XROrigin == null) m_XROrigin = FindAnyObjectByType<XROrigin>();
            if (m_RaycastManager == null) m_RaycastManager = FindAnyObjectByType<ARRaycastManager>();
            if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            m_Log = new MeasurementLog(m_FileName);
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
            // Half-finished measurements are dropped when leaving the mode.
            // Finished ones stay on screen to compare against the tape.
            if (mode != AppMode.Measure && !IsComplete) ClearPoints();
        }

        void Update()
        {
            if (!IsActive) return;

            UpdatePrompt();

            if (ARSession.state != ARSessionState.SessionTracking) return;
            if (TryGetTap(out var screenPoint)) HandleTap(screenPoint);
        }

        public void SetKind(MeasureKind kind)
        {
            if (kind == Kind) return;
            Kind = kind;
            ClearPoints();
        }

        public void ToggleKind()
        {
            SetKind(Kind == MeasureKind.Distance ? MeasureKind.Perimeter : MeasureKind.Distance);
        }

        public void Clear()
        {
            ClearPoints();
        }

        public void CloseLoop()
        {
            if (!CanCloseLoop) return;

            var total = 0f;
            for (var i = 0; i < m_Points.Count; i++)
                total += Vector3.Distance(m_Points[i], m_Points[(i + 1) % m_Points.Count]);

            if (m_Line != null) m_Line.loop = true;
            Complete(total);
        }

        void HandleTap(Vector2 screenPoint)
        {
            if (!m_RaycastManager.Raycast(screenPoint, m_Hits, TrackableType.PlaneWithinPolygon))
            {
                Prompt = "No tracked surface under that tap.";
                return;
            }

            // A tap after a finished result starts a fresh measurement.
            if (IsComplete) ClearPoints();

            var hit = m_Hits[0];
            var plane = m_PlaneManager != null ? m_PlaneManager.GetPlane(hit.trackableId) : null;
            if (plane != null && plane.trackingState != TrackingState.Tracking) m_AllPlanesTracking = false;

            AddPoint(hit.pose.position);

            if (Kind == MeasureKind.Distance && m_Points.Count == 2)
                Complete(Vector3.Distance(m_Points[0], m_Points[1]));
        }

        void Complete(float metres)
        {
            IsComplete = true;

            var points = m_Points.ToArray();
            var cameraDistance = CameraDistanceTo(points);
            var tracking = ARSession.state + "|" + (m_AllPlanesTracking ? "PlanesTracking" : "PlaneLimited");
            var runId = m_Log.NextRunId;
            var mode = Kind == MeasureKind.Distance ? "distance" : "perimeter";

            var logged = m_Log.Append(runId, mode, points, metres, cameraDistance, tracking);

            LastResult = new MeasurementResult
            {
                RunId = runId,
                Kind = Kind,
                Points = points,
                Metres = metres,
                CameraDistance = cameraDistance,
                TrackingState = tracking,
                Logged = logged
            };
            HasResult = true;

            MeasurementCompleted?.Invoke(LastResult);
        }

        float CameraDistanceTo(Vector3[] points)
        {
            if (m_XROrigin == null || m_XROrigin.Camera == null || points.Length == 0) return 0f;

            // Distance: midpoint of the segment. Perimeter: centre of the points.
            var centre = Vector3.zero;
            foreach (var p in points) centre += p;
            centre /= points.Length;

            return Vector3.Distance(m_XROrigin.Camera.transform.position, centre);
        }

        void UpdatePrompt()
        {
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                Prompt = "Waiting for tracking (" + ARSession.state + ")";
                return;
            }

            if (IsComplete)
            {
                Prompt = "Tap to start a new measurement.";
                return;
            }

            if (Kind == MeasureKind.Distance)
            {
                Prompt = m_Points.Count == 0 ? "Tap point A." : "Tap point B.";
                return;
            }

            Prompt = m_Points.Count < 3
                ? "Tap corner " + (m_Points.Count + 1) + " (3 or more)."
                : "Tap more corners or press Close loop.";
        }

        void AddPoint(Vector3 point)
        {
            m_Points.Add(point);

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "MeasurePoint" + m_Points.Count;
            var collider = marker.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var markerRenderer = marker.GetComponent<Renderer>();
            if (m_MarkerMaterial != null && markerRenderer != null) markerRenderer.sharedMaterial = m_MarkerMaterial;
            marker.transform.position = point;
            marker.transform.localScale = Vector3.one * m_MarkerDiameter;
            m_Markers.Add(marker);

            EnsureLine();
            m_Line.positionCount = m_Points.Count;
            m_Line.SetPosition(m_Points.Count - 1, point);
        }

        void EnsureLine()
        {
            if (m_Line != null) return;

            var go = new GameObject("MeasureLine");
            m_Line = go.AddComponent<LineRenderer>();
            m_Line.useWorldSpace = true;
            m_Line.widthMultiplier = m_LineWidth;
            m_Line.numCapVertices = 2;
            m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Line.receiveShadows = false;
            if (m_MarkerMaterial != null) m_Line.sharedMaterial = m_MarkerMaterial;
            m_Line.positionCount = 0;
        }

        void ClearPoints()
        {
            foreach (var marker in m_Markers)
                if (marker != null) Destroy(marker);
            m_Markers.Clear();
            m_Points.Clear();

            if (m_Line != null)
            {
                m_Line.loop = false;
                m_Line.positionCount = 0;
            }

            IsComplete = false;
            m_AllPlanesTracking = true;
        }

        bool TryGetTap(out Vector2 screenPoint)
        {
            screenPoint = default;
            var touches = ETouch.activeTouches;
            for (var i = 0; i < touches.Count; i++)
            {
                if (touches[i].phase != InputTouchPhase.Began) continue;
                if (IsOverUI(touches[i].finger.index)) continue;
                screenPoint = touches[i].screenPosition;
                return true;
            }
            return false;
        }

        static bool IsOverUI(int pointerId)
        {
            return EventSystem.current != null &&
                   EventSystem.current.IsPointerOverGameObject(pointerId);
        }
    }
}
