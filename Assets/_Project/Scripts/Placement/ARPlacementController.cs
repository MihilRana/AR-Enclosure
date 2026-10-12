using System;
using System.Collections;
using System.Collections.Generic;
using ArEnclosure.App;
using ArEnclosure.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

namespace ArEnclosure.Placement
{
    public struct PlacementReport
    {
        public GameObject Instance;
        public Vector3 HitPosition;
        public float DistanceAtPlacement;
        public PlaneAlignment Alignment;
        public int TrackedPlaneCount;
        public float WarmUpSeconds;
    }

    /// <summary>
    /// Places the enclosure once on a detected plane, then stops listening.
    /// The placement gate exists on purpose. Placing during the first second
    /// of tracking is the main cause of the model landing at the wrong depth.
    /// </summary>
    public class ARPlacementController : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] XROrigin m_XROrigin;

        [Header("Content")]
        [SerializeField] GameObject m_ModelPrefab;
        [Tooltip("Drops a 1.000 m rod beside the model so you can hold a tape measure against it.")]
        [SerializeField] bool m_SpawnMetreReference = true;
        [SerializeField] float m_ReferenceSideOffset = 0.7f;

        [Header("Placement gate")]
        [Tooltip("Seconds of continuous tracking required before a tap counts.")]
        [SerializeField] float m_MinimumTrackingSeconds = 4f;
        [SerializeField] int m_MinimumTrackedPlanes = 1;
        [Tooltip("Refuse taps further away than this. Far planes carry the most depth error.")]
        [SerializeField] float m_MaximumPlacementDistance = 3.5f;
        [Tooltip("Optional. Picks usable floor planes, blocks placement while the floor is unsettled, and is locked on placement.")]
        [SerializeField] FloorLock m_FloorLock;
        [Tooltip("How long a refused-tap message stays on screen.")]
        [SerializeField] float m_NoticeSeconds = 2.5f;

        [Header("Seating and orientation")]
        [SerializeField] bool m_SeatModelOnSurface = true;
        [Tooltip("Use if the model faces the wrong way after the Blender axis fix.")]
        [SerializeField] float m_YawOffsetDegrees;

        [Header("Anchoring")]
        [SerializeField] bool m_AttachAnchor = true;

        [SerializeField] Material m_ReferenceMaterial;

        [Tooltip("Optional. When set, taps only place the panel in Move mode.")]
        [SerializeField] AppModeController m_Modes;

        public event Action<PlacementReport> Placed;
        public event Action Cleared;

        readonly List<ARRaycastHit> m_Hits = new List<ARRaycastHit>();
        GameObject m_Instance;
        GameObject m_Reference;
        float m_TrackingStartedAt = -1f;
        string m_Notice;
        float m_NoticeUntil = -1f;
        bool m_Reseating;

        public GameObject ModelPrefab => m_ModelPrefab;
        public GameObject PlacedInstance => m_Instance;
        public GameObject ReferenceInstance => m_Reference;
        public bool HasPlaced => m_Instance != null;
        public string Status { get; private set; } = "Starting session";
        public float WarmUpSeconds => m_TrackingStartedAt < 0f ? 0f : Time.time - m_TrackingStartedAt;
        public int TrackedPlaneCount => m_PlaneManager == null ? 0 : m_PlaneManager.trackables.count;

        void Awake()
        {
            if (m_XROrigin == null) m_XROrigin = FindAnyObjectByType<XROrigin>();
            if (m_RaycastManager == null) m_RaycastManager = FindAnyObjectByType<ARRaycastManager>();
            if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            if (m_FloorLock == null) m_FloorLock = FindAnyObjectByType<FloorLock>();

            //if (m_PlaneManager != null)
            //{
            //    m_PlaneManager.requestedDetectionMode =
            //        PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            //}
            // commented to avoid overriding DetectionMode
        }

        void OnEnable()
        {
#if ENABLE_INPUT_SYSTEM
            EnhancedTouchSupport.Enable();
#endif
        }

        void OnDisable()
        {
#if ENABLE_INPUT_SYSTEM
            EnhancedTouchSupport.Disable();
#endif
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            UiTouchFilter.Refresh();
#endif
            UpdateWarmUp();

            if (m_Instance != null)
            {
                if (Time.time < m_NoticeUntil)
                    Status = m_Notice;
                else if (m_FloorLock != null && m_FloorLock.HasDrifted)
                    Status = "Placed. Floor estimate moved " + (m_FloorLock.Drift * 100f).ToString("+0.0;-0.0") +
                             " cm. Press Re-seat on floor.";
                else
                    Status = "Placed. Hit Reset to try again.";
                return;
            }

            if (!ReadyToPlace(out var blockedReason))
            {
                Status = blockedReason;
                return;
            }

            // Other tools own taps in their own modes, so a measurement tap
            // must not also drop the panel.
            if (m_Modes != null && m_Modes.Current != AppMode.Move)
            {
                Status = "Switch to Move mode to place.";
                return;
            }

            // A refused tap used to be overwritten on the next frame, so its
            // reason was never readable. Hold it for a moment.
            Status = Time.time < m_NoticeUntil ? m_Notice : "Ready. Tap a surface.";

            if (TryGetTap(out var screenPoint))
                TryPlace(screenPoint);
        }

        void UpdateWarmUp()
        {
            if (ARSession.state == ARSessionState.SessionTracking)
            {
                if (m_TrackingStartedAt < 0f) m_TrackingStartedAt = Time.time;
            }
            else
            {
                m_TrackingStartedAt = -1f;
            }
        }

        bool ReadyToPlace(out string reason)
        {
            if (m_ModelPrefab == null)
            {
                reason = "No model prefab assigned.";
                return false;
            }

            if (ARSession.state != ARSessionState.SessionTracking)
            {
                reason = "Waiting for tracking (" + ARSession.state + ")";
                return false;
            }

            if (TrackedPlaneCount < m_MinimumTrackedPlanes)
            {
                reason = "Move the phone around, no surface found yet.";
                return false;
            }

            if (WarmUpSeconds < m_MinimumTrackingSeconds)
            {
                var left = m_MinimumTrackingSeconds - WarmUpSeconds;
                reason = "Settling tracking, " + left.ToString("F1") + " s left.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        bool TryGetTap(out Vector2 screenPoint)
        {
            screenPoint = default;

#if ENABLE_INPUT_SYSTEM
            var touches = ETouch.activeTouches;
            for (var i = 0; i < touches.Count; i++)
            {
                if (touches[i].phase != InputTouchPhase.Began) continue;
                if (UiTouchFilter.IsBlocked(touches[i])) continue;
                screenPoint = touches[i].screenPosition;
                return true;
            }
#else
            for (var i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Began) continue;
                if (UiTouchFilter.IsOverUI(touch.position)) continue;
                screenPoint = touch.position;
                return true;
            }
#endif
            return false;
        }

        void TryPlace(Vector2 screenPoint)
        {
            if (!m_RaycastManager.Raycast(screenPoint, m_Hits, TrackableType.PlaneWithinPolygon))
            {
                RaycastProbe.Record("place", m_Hits, -1);
                Notice("No tracked surface under that tap.");
                return;
            }

            // Distance is checked after picking, so the message names the
            // plane that would actually have been used.
            var chosen = 0;
            if (m_FloorLock != null && !m_FloorLock.TryPickHit(m_Hits, float.PositiveInfinity, out chosen, out _))
            {
                RaycastProbe.Record("place", m_Hits, -1);
                Notice("Surface not reliable yet (small or merged). Keep scanning.");
                return;
            }
            RaycastProbe.Record("place", m_Hits, chosen);

            var hit = m_Hits[chosen];
            var cameraTransform = m_XROrigin.Camera.transform;
            var distance = Vector3.Distance(cameraTransform.position, hit.pose.position);

            if (distance > m_MaximumPlacementDistance)
            {
                Notice("That surface is " + distance.ToString("F1") + " m away. Step within " +
                       m_MaximumPlacementDistance.ToString("F1") + " m.");
                return;
            }

            var plane = m_PlaneManager != null ? m_PlaneManager.GetPlane(hit.trackableId) : null;

            // Two floor estimates at different heights near the tap means
            // ARCore has not decided yet. Either could be the one it keeps,
            // so wait rather than lock onto a guess.
            if (m_FloorLock != null && !m_FloorLock.IsFloorSettled(hit.pose.position, plane, out var gap))
            {
                Notice("Floor still settling (" + (gap * 100f).ToString("F0") + " cm apart), keep scanning.");
                return;
            }
            var alignment = plane != null ? plane.alignment : PlaneAlignment.None;
            var normal = plane != null ? plane.normal : Vector3.up;

            var rotation = BuildRotation(alignment, normal, hit.pose.position, cameraTransform.position);

            m_Instance = Instantiate(m_ModelPrefab, hit.pose.position, rotation);

            // Placement never touches scale. If the readout says the size is
            // wrong, the cause is upstream: import settings, prefab root, or
            // XR Origin. Do not fix it here.
            m_Instance.transform.localScale = m_ModelPrefab.transform.localScale;

            if (m_SeatModelOnSurface)
                SeatOnSurface(m_Instance, hit.pose.position, alignment, normal);

            if (m_AttachAnchor && m_Instance.GetComponent<ARAnchor>() == null)
                m_Instance.AddComponent<ARAnchor>();

            if (m_SpawnMetreReference)
                SpawnMetreReference(m_Instance, hit.pose.position, rotation);

            if (m_FloorLock != null) m_FloorLock.Lock(plane, hit.pose.position.y);

            Status = "Placed.";

            Placed?.Invoke(new PlacementReport
            {
                Instance = m_Instance,
                HitPosition = hit.pose.position,
                DistanceAtPlacement = distance,
                Alignment = alignment,
                TrackedPlaneCount = TrackedPlaneCount,
                WarmUpSeconds = WarmUpSeconds
            });
        }

        /// <summary>True when the locked floor has drifted away from the panel's base.</summary>
        public bool CanReseat => m_Instance != null && m_FloorLock != null && m_FloorLock.HasDrifted && !m_Reseating;

        /// <summary>
        /// Moves the panel straight up or down so its base sits on the locked
        /// floor's current height, then re-anchors. Height only: position on
        /// the floor, yaw and scale are untouched.
        /// </summary>
        public void ReseatOnFloor()
        {
            if (!CanReseat) return;
            StartCoroutine(ReseatRoutine());
        }

        IEnumerator ReseatRoutine()
        {
            m_Reseating = true;

            // Same order the drag uses: drop the anchor, let it go at the end
            // of this frame, then move, then anchor again. An anchor that is
            // still alive would pull the panel back to its old pose.
            var anchor = m_Instance.GetComponent<ARAnchor>();
            var hadAnchor = anchor != null;
            if (anchor != null) Destroy(anchor);
            yield return null;

            if (m_Instance != null && ModelBounds.TryGetLocalBounds(m_Instance, out var local))
            {
                var t = m_Instance.transform;
                var baseY = t.TransformPoint(new Vector3(local.center.x, local.min.y, local.center.z)).y;
                var delta = m_FloorLock.CurrentHeight - baseY;

                t.position += Vector3.up * delta;
                if (m_Reference != null) m_Reference.transform.position += Vector3.up * delta;

                if (hadAnchor && m_Instance.GetComponent<ARAnchor>() == null)
                    m_Instance.AddComponent<ARAnchor>();

                m_FloorLock.Rebase();
                Notice("Re-seated on floor, moved " + (delta * 100f).ToString("+0.0;-0.0") + " cm.");
            }

            m_Reseating = false;
        }

        void Notice(string message)
        {
            m_Notice = message;
            m_NoticeUntil = Time.time + m_NoticeSeconds;
            Status = message;
        }

        Quaternion BuildRotation(PlaneAlignment alignment, Vector3 normal, Vector3 hitPosition, Vector3 cameraPosition)
        {
            Quaternion rotation;

            if (alignment == PlaneAlignment.Vertical)
            {
                // Front of the panel points out of the wall.
                rotation = Quaternion.LookRotation(normal, Vector3.up);
            }
            else
            {
                var toCamera = cameraPosition - hitPosition;
                toCamera.y = 0f;
                rotation = toCamera.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(toCamera.normalized, Vector3.up)
                    : Quaternion.identity;
            }

            return rotation * Quaternion.Euler(0f, m_YawOffsetDegrees, 0f);
        }

        /// <summary>
        /// Pushes the model so it rests on the tapped point rather than being
        /// centred on it. Measured at runtime, so the FBX pivot can sit
        /// anywhere and this still works.
        /// </summary>
        void SeatOnSurface(GameObject instance, Vector3 hitPosition, PlaneAlignment alignment, Vector3 normal)
        {
            if (!ModelBounds.TryGetWorldAabb(instance, out var box)) return;

            if (alignment == PlaneAlignment.Vertical)
            {
                if (!ModelBounds.TryGetWorldSize(instance, out var size)) return;
                var offset = hitPosition - box.center;
                instance.transform.position += offset + normal * (size.z * 0.5f);
                return;
            }

            var shift = new Vector3(
                hitPosition.x - box.center.x,
                hitPosition.y - box.min.y,
                hitPosition.z - box.center.z);

            instance.transform.position += shift;
        }

        void SpawnMetreReference(GameObject instance, Vector3 hitPosition, Quaternion rotation)
        {
            if (!ModelBounds.TryGetWorldSize(instance, out var size)) return;

            m_Reference = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Reference.name = "OneMetreReference";

            var collider = m_Reference.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var rodRenderer = m_Reference.GetComponent<Renderer>();
            if (m_ReferenceMaterial != null && rodRenderer != null)
                rodRenderer.sharedMaterial = m_ReferenceMaterial;

            var side = rotation * Vector3.right;
            m_Reference.transform.position =
                hitPosition + side * (size.x * 0.5f + m_ReferenceSideOffset) + Vector3.up * 0.5f;
            m_Reference.transform.rotation = rotation;
            m_Reference.transform.localScale = new Vector3(0.02f, 1.000f, 0.02f);
        }

        /// <summary>Wire this to a Reset button so you can repeat the test.</summary>
        public void Clear()
        {
            if (m_Instance != null) Destroy(m_Instance);
            if (m_Reference != null) Destroy(m_Reference);
            m_Instance = null;
            m_Reference = null;
            if (m_FloorLock != null) m_FloorLock.Clear();
            Status = "Cleared. Tap a surface.";
            Cleared?.Invoke();
        }
    }
}
