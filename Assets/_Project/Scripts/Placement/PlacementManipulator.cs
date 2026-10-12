using System;
using System.Collections.Generic;
using ArEnclosure.App;
using ArEnclosure.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ArEnclosure.Placement
{
    public enum ManipulationKind
    {
        None,
        Drag,
        Rotate
    }

    public struct ManipulationReport
    {
        public ManipulationKind Kind;
        public float MovedMetres;
        public float TurnedDegrees;
        public Vector3 FinalPosition;
        public float FinalYaw;
        public bool Reanchored;
    }

    /// <summary>
    /// Moves and turns the placed panel. One finger that starts on the panel
    /// drags it across horizontal planes. Two fingers twist it about world up,
    /// pivoting on the centre of its base so it turns on the spot.
    ///
    /// Scale is never touched. Pinch distance is read nowhere. True scale is
    /// the first objective, so the user must not be able to resize the model.
    ///
    /// The anchor is dropped while a finger is down, because an anchored
    /// object is pulled back to its anchor pose, and a fresh world anchor is
    /// added on release. The anchoring model is the same as at placement.
    /// </summary>
    public class PlacementManipulator : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] ARPlacementController m_Placement;
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] XROrigin m_XROrigin;
        [Tooltip("Optional. When set, gestures only work in Move mode.")]
        [SerializeField] AppModeController m_Modes;
        [Tooltip("Optional. Restricts drag to the floor locked at placement.")]
        [SerializeField] FloorLock m_FloorLock;

        [Header("Gestures")]
        [Tooltip("A drag starts if the finger lands within this distance of the panel box.")]
        [SerializeField] float m_GrabMarginMetres = 0.25f;
        [Tooltip("Plane hits further than this are ignored while dragging.")]
        [SerializeField] float m_MaximumDragDistance = 6f;

        enum Gesture
        {
            None,
            Drag,
            Rotate,
            WaitForRelease
        }

        public event Action<ManipulationReport> ManipulationEnded;

        /// <summary>
        /// Second switch next to the mode check. Other tools can turn
        /// manipulation off without owning the app mode.
        /// </summary>
        public bool ManipulationEnabled { get; set; } = true;

        public bool IsActive =>
            ManipulationEnabled && (m_Modes == null || m_Modes.Current == AppMode.Move);

        public ManipulationKind CurrentGesture =>
            m_Gesture == Gesture.Drag ? ManipulationKind.Drag :
            m_Gesture == Gesture.Rotate ? ManipulationKind.Rotate : ManipulationKind.None;

        readonly List<ARRaycastHit> m_Hits = new List<ARRaycastHit>();

        Gesture m_Gesture;
        ManipulationKind m_KindThisGesture;
        int m_PlacedFrame = -1;

        GameObject m_BoundsOwner;
        Bounds m_LocalBounds;

        bool m_HadAnchor;
        Vector3 m_StartPosition;
        float m_StartYaw;

        int m_DragFinger = -1;
        Vector3 m_GrabOffset;
        bool m_HasGrabOffset;

        int m_TwistFingerA = -1;
        int m_TwistFingerB = -1;
        float m_LastTwistAngle;

        bool m_HasReferencePose;
        Vector3 m_ReferenceLocalPosition;
        Quaternion m_ReferenceLocalRotation;

        void Awake()
        {
            if (m_Placement == null) m_Placement = FindAnyObjectByType<ARPlacementController>();
            if (m_XROrigin == null) m_XROrigin = FindAnyObjectByType<XROrigin>();
            if (m_RaycastManager == null) m_RaycastManager = FindAnyObjectByType<ARRaycastManager>();
            if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            if (m_FloorLock == null) m_FloorLock = FindAnyObjectByType<FloorLock>();
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            if (m_Placement != null)
            {
                m_Placement.Placed += OnPlaced;
                m_Placement.Cleared += OnCleared;
            }
        }

        void OnDisable()
        {
            if (m_Gesture == Gesture.Drag || m_Gesture == Gesture.Rotate) EndGesture();
            m_Gesture = Gesture.None;

            if (m_Placement != null)
            {
                m_Placement.Placed -= OnPlaced;
                m_Placement.Cleared -= OnCleared;
            }
            EnhancedTouchSupport.Disable();
        }

        void OnPlaced(PlacementReport report)
        {
            // The tap that placed the panel must not also start a drag.
            m_PlacedFrame = Time.frameCount;
        }

        void OnCleared()
        {
            // The instance is gone, so there is nothing to re-anchor.
            m_Gesture = Gesture.None;
            m_BoundsOwner = null;
        }

        void Update()
        {
            UiTouchFilter.Refresh();
            var instance = m_Placement != null ? m_Placement.PlacedInstance : null;

            if (instance == null)
            {
                m_Gesture = Gesture.None;
                return;
            }

            var down = CountDown();

            if (!IsActive)
            {
                // Mode switched mid gesture: drop it cleanly and re-anchor.
                if (m_Gesture == Gesture.Drag || m_Gesture == Gesture.Rotate)
                {
                    EndGesture();
                    m_Gesture = Gesture.WaitForRelease;
                }
                if (m_Gesture == Gesture.WaitForRelease && down == 0) m_Gesture = Gesture.None;
                return;
            }

            if (!EnsureBounds(instance)) return;

            switch (m_Gesture)
            {
                case Gesture.None:
                    TryBeginGesture(instance, down);
                    break;

                case Gesture.Drag:
                    if (down == 0)
                    {
                        EndGesture();
                        m_Gesture = Gesture.None;
                    }
                    else if (down >= 2)
                    {
                        SwitchToRotate();
                    }
                    else if (TryGetFinger(m_DragFinger, out var dragTouch))
                    {
                        UpdateDrag(instance, dragTouch.screenPosition);
                    }
                    else
                    {
                        EndGesture();
                        m_Gesture = Gesture.WaitForRelease;
                    }
                    break;

                case Gesture.Rotate:
                    if (down >= 2)
                    {
                        UpdateRotate(instance);
                    }
                    else
                    {
                        // Lifting one finger ends the turn. The other finger
                        // must not suddenly become a drag.
                        EndGesture();
                        m_Gesture = down == 0 ? Gesture.None : Gesture.WaitForRelease;
                    }
                    break;

                case Gesture.WaitForRelease:
                    if (down == 0) m_Gesture = Gesture.None;
                    break;
            }
        }

        void TryBeginGesture(GameObject instance, int down)
        {
            if (down == 0 || Time.frameCount <= m_PlacedFrame) return;

            var touches = ETouch.activeTouches;
            for (var i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (touch.phase != InputTouchPhase.Began) continue;
                if (UiTouchFilter.IsBlocked(touch)) continue;

                if (down >= 2)
                {
                    BeginGesture(instance, ManipulationKind.Rotate);
                    SwitchToRotate();
                    return;
                }

                if (TouchHitsPanel(instance, touch.screenPosition))
                {
                    BeginGesture(instance, ManipulationKind.Drag);
                    m_Gesture = Gesture.Drag;
                    m_DragFinger = touch.finger.index;
                    m_HasGrabOffset = false;
                    UpdateDrag(instance, touch.screenPosition);
                    return;
                }
            }
        }

        void BeginGesture(GameObject instance, ManipulationKind kind)
        {
            m_KindThisGesture = kind;
            m_StartPosition = instance.transform.position;
            m_StartYaw = instance.transform.eulerAngles.y;

            var anchor = instance.GetComponent<ARAnchor>();
            m_HadAnchor = anchor != null;
            if (anchor != null) Destroy(anchor);

            var reference = m_Placement.ReferenceInstance;
            m_HasReferencePose = reference != null;
            if (m_HasReferencePose)
            {
                var t = instance.transform;
                m_ReferenceLocalPosition = t.InverseTransformPoint(reference.transform.position);
                m_ReferenceLocalRotation = Quaternion.Inverse(t.rotation) * reference.transform.rotation;
            }
        }

        void SwitchToRotate()
        {
            if (m_KindThisGesture == ManipulationKind.Drag) m_KindThisGesture = ManipulationKind.Rotate;
            m_Gesture = Gesture.Rotate;
            m_TwistFingerA = -1;
            m_TwistFingerB = -1;
        }

        void EndGesture()
        {
            var instance = m_Placement != null ? m_Placement.PlacedInstance : null;
            if (instance == null) return;

            var reanchored = false;
            if (m_HadAnchor && instance.GetComponent<ARAnchor>() == null)
            {
                instance.AddComponent<ARAnchor>();
                reanchored = true;
            }

            var t = instance.transform;
            var moved = t.position - m_StartPosition;
            moved.y = 0f;

            ManipulationEnded?.Invoke(new ManipulationReport
            {
                Kind = m_KindThisGesture,
                MovedMetres = moved.magnitude,
                TurnedDegrees = Mathf.DeltaAngle(m_StartYaw, t.eulerAngles.y),
                FinalPosition = t.position,
                FinalYaw = t.eulerAngles.y,
                Reanchored = reanchored
            });

            m_DragFinger = -1;
            m_TwistFingerA = -1;
            m_TwistFingerB = -1;
        }

        void UpdateDrag(GameObject instance, Vector2 screenPoint)
        {
            if (!TryRaycastHorizontal(screenPoint, out var hitPoint)) return;

            var t = instance.transform;

            if (!m_HasGrabOffset)
            {
                // Keep the panel where it is relative to the finger, so it
                // does not jump to centre itself under the touch.
                m_GrabOffset = t.position - hitPoint;
                m_GrabOffset.y = 0f;
                m_HasGrabOffset = true;
            }

            // Seat the base on the plane under the finger, the same way
            // placement does, using the oriented box rather than the AABB.
            var baseY = t.TransformPoint(BaseCentreLocal()).y;
            var heightAboveBase = t.position.y - baseY;

            var target = hitPoint + m_GrabOffset;
            target.y = hitPoint.y + heightAboveBase;

            t.position = target;
            FollowReference(instance);
        }

        void UpdateRotate(GameObject instance)
        {
            if (!TryGetFinger(m_TwistFingerA, out var a) || !TryGetFinger(m_TwistFingerB, out var b))
            {
                // Pick the first two fingers down and take a fresh baseline.
                if (!TryGetFirstTwo(out a, out b)) return;
                m_TwistFingerA = a.finger.index;
                m_TwistFingerB = b.finger.index;
                m_LastTwistAngle = TwistAngle(a, b);
                return;
            }

            var angle = TwistAngle(a, b);
            var delta = Mathf.DeltaAngle(m_LastTwistAngle, angle);
            m_LastTwistAngle = angle;
            if (Mathf.Abs(delta) < 0.01f) return;

            // Screen angles grow anticlockwise. Unity yaw grows clockwise seen
            // from above, so the sign flips to make the panel follow the fingers.
            var t = instance.transform;
            var pivot = t.TransformPoint(BaseCentreLocal());
            t.RotateAround(pivot, Vector3.up, -delta);
            FollowReference(instance);
        }

        static float TwistAngle(ETouch a, ETouch b)
        {
            var d = b.screenPosition - a.screenPosition;
            return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        void FollowReference(GameObject instance)
        {
            if (!m_HasReferencePose) return;
            var reference = m_Placement.ReferenceInstance;
            if (reference == null) return;

            var t = instance.transform;
            reference.transform.SetPositionAndRotation(
                t.TransformPoint(m_ReferenceLocalPosition),
                t.rotation * m_ReferenceLocalRotation);
        }

        bool TouchHitsPanel(GameObject instance, Vector2 screenPoint)
        {
            if (m_XROrigin == null || m_XROrigin.Camera == null) return false;

            var ray = m_XROrigin.Camera.ScreenPointToRay(screenPoint);
            var t = instance.transform;
            var localRay = new Ray(t.InverseTransformPoint(ray.origin), t.InverseTransformDirection(ray.direction));

            var box = m_LocalBounds;
            box.Expand(m_GrabMarginMetres * 2f);
            return box.IntersectRay(localRay);
        }

        bool TryRaycastHorizontal(Vector2 screenPoint, out Vector3 point)
        {
            point = default;
            if (m_RaycastManager == null) return false;
            if (!m_RaycastManager.Raycast(screenPoint, m_Hits, TrackableType.PlaneWithinPolygon))
            {
                RaycastProbe.Record("drag", m_Hits, -1);
                return false;
            }

            // With a floor lock, only the locked floor can carry the panel, so a
            // nearer false plane above it no longer wins.
            if (m_FloorLock != null)
            {
                var picked = m_FloorLock.TryPickHit(m_Hits, m_MaximumDragDistance, out var index, out _);
                RaycastProbe.Record("drag", m_Hits, index);
                if (picked) point = m_Hits[index].pose.position;
                return picked;
            }

            for (var i = 0; i < m_Hits.Count; i++)
            {
                var hit = m_Hits[i];
                if (hit.distance > m_MaximumDragDistance) continue;

                var plane = m_PlaneManager != null ? m_PlaneManager.GetPlane(hit.trackableId) : null;
                if (plane != null && plane.alignment != PlaneAlignment.HorizontalUp) continue;

                RaycastProbe.Record("drag", m_Hits, i);
                point = hit.pose.position;
                return true;
            }

            RaycastProbe.Record("drag", m_Hits, -1);
            return false;
        }

        bool EnsureBounds(GameObject instance)
        {
            if (m_BoundsOwner == instance) return true;
            if (!ModelBounds.TryGetLocalBounds(instance, out m_LocalBounds)) return false;
            m_BoundsOwner = instance;
            return true;
        }

        Vector3 BaseCentreLocal()
        {
            var c = m_LocalBounds.center;
            return new Vector3(c.x, m_LocalBounds.min.y, c.z);
        }

        static int CountDown()
        {
            var count = 0;
            var touches = ETouch.activeTouches;
            for (var i = 0; i < touches.Count; i++)
                if (IsLive(touches[i])) count++;
            return count;
        }

        static bool TryGetFinger(int fingerIndex, out ETouch touch)
        {
            touch = default;
            if (fingerIndex < 0) return false;

            var touches = ETouch.activeTouches;
            for (var i = 0; i < touches.Count; i++)
            {
                if (touches[i].finger.index != fingerIndex || !IsLive(touches[i])) continue;
                touch = touches[i];
                return true;
            }
            return false;
        }

        static bool TryGetFirstTwo(out ETouch a, out ETouch b)
        {
            a = default;
            b = default;
            var found = 0;

            var touches = ETouch.activeTouches;
            for (var i = 0; i < touches.Count && found < 2; i++)
            {
                if (!IsLive(touches[i])) continue;
                if (found == 0) a = touches[i];
                else b = touches[i];
                found++;
            }
            return found == 2;
        }

        /// <summary>Down, and did not begin over UI.</summary>
        static bool IsLive(ETouch touch)
        {
            return touch.isInProgress && !UiTouchFilter.IsBlocked(touch);
        }
    }
}
