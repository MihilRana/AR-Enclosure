using System.Collections.Generic;
using ArEnclosure.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArEnclosure.Placement
{
    /// <summary>
    /// Finds one floor and asks the user to confirm it before anything is
    /// placed.
    ///
    /// The candidate is the largest usable plane of at least the minimum
    /// area, offered after a short tracking warm-up once its own height has
    /// held steady. Other planes at different heights only produce a warning:
    /// on a reflective floor a mirrored plane can sit below the real floor for
    /// the whole session and never merge, so a blocking check would never let
    /// the user confirm. Once offered, the offer stays until the candidate
    /// merges, moves beyond the band, or a much larger plane appears.
    ///
    /// The user checks the offer with a floor ring. It is dropped once where
    /// the centre of the screen meets the candidate's height, then stays fixed
    /// in the world. Walking or moving the phone sideways shows the truth: at
    /// the right height the ring stays on the same tile, at a wrong height it
    /// slides across the tiles (parallax). Move ring here re-drops it at the
    /// current screen centre. Plane outlines are not drawn during the offer,
    /// because they reach past walls and make a correct floor look wrong.
    ///
    /// Confirming locks the floor and stops plane finding; existing planes
    /// keep tracking, so drift is still measured. Rescan skips the offered
    /// plane until ARCore changes it. Reset turns finding back on.
    /// </summary>
    public class FloorConfirm : MonoBehaviour
    {
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] FloorLock m_FloorLock;
        [SerializeField] XROrigin m_XROrigin;

        [Header("Offer rules")]
        [Tooltip("No floor is offered before this much continuous tracking.")]
        [SerializeField] float m_MinTrackingSeconds = 8f;
        [Tooltip("Smaller planes are never offered as the floor.")]
        [SerializeField] float m_MinCandidateArea = 1.0f;
        [Tooltip("The candidate's own height must stay within the band this long.")]
        [SerializeField] float m_HoldSeconds = 2f;
        [Tooltip("Once offered, a plane this many times larger replaces the offer.")]
        [SerializeField] float m_LargerFactor = 1.5f;

        [Header("Floor ring")]
        [FormerlySerializedAs("m_OutlineMaterial")]
        [SerializeField] Material m_RingMaterial;
        [FormerlySerializedAs("m_OutlineColour")]
        [SerializeField] Color m_RingColour = new Color(0.2f, 0.9f, 0.35f, 1f);
        [FormerlySerializedAs("m_OutlineWidth")]
        [SerializeField] float m_RingWidth = 0.012f;
        [SerializeField] float m_RingRadius = 0.2f;
        [Tooltip("The ring is hidden where the screen centre meets the floor further than this.")]
        [SerializeField] float m_RingMaxDistance = 4f;

        struct Rejected
        {
            public TrackableId Id;
            public float Height;
        }

        const int k_RingSegments = 48;
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

        public bool IsConfirmed => m_FloorLock != null && m_FloorLock.HasLock;
        /// <summary>A floor is waiting for Use this floor or Rescan.</summary>
        public bool HasOffer { get; private set; }
        public bool PlaneFindingOn => m_PlaneManager != null && m_PlaneManager.requestedDetectionMode != PlaneDetectionMode.None;
        public string Status { get; private set; } = "Scanning for the floor.";
        /// <summary>Advice about other surfaces near the offered floor, or empty.</summary>
        public string Warning { get; private set; } = string.Empty;

        readonly List<Rejected> m_Rejected = new List<Rejected>();
        PlaneDetectionMode m_SearchMode = PlaneDetectionMode.Horizontal;
        ARPlane m_Candidate;
        float m_HeldSince;
        float m_ReferenceHeight;
        float m_OfferedHeight;
        float m_TrackingSince = -1f;
        LineRenderer m_Ring;
        LineRenderer m_Cross;
        MaterialPropertyBlock m_Block;
        bool m_RingPlaced;
        Vector3 m_RingPoint;

        void Awake()
        {
            if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            if (m_FloorLock == null) m_FloorLock = FindAnyObjectByType<FloorLock>();
            if (m_XROrigin == null) m_XROrigin = FindAnyObjectByType<XROrigin>();
            m_Block = new MaterialPropertyBlock();

            // Remember the mode set in the inspector so Reset restores exactly it.
            if (m_PlaneManager != null && m_PlaneManager.requestedDetectionMode != PlaneDetectionMode.None)
                m_SearchMode = m_PlaneManager.requestedDetectionMode;
        }

        void Update()
        {
            if (m_PlaneManager == null || m_FloorLock == null) return;

            if (ARSession.state == ARSessionState.SessionTracking)
            {
                if (m_TrackingSince < 0f) m_TrackingSince = Time.time;
            }
            else
            {
                m_TrackingSince = -1f;
            }

            if (IsConfirmed)
            {
                HasOffer = false;
                Warning = string.Empty;
                return;
            }

            ForgetChangedRejections();

            if (HasOffer) KeepOrReplaceOffer();
            else SearchForOffer();

            if (HasOffer)
            {
                UpdateWarning();
                Status = "Floor found (" + PlaneMetrics.Area(m_Candidate).ToString("F1") + " m2).\n" +
                         (m_RingPlaced
                             ? "Move the phone sideways: the ring must stay on the same tile. If it slides, press Rescan."
                             : "Point at open floor to drop the ring.") +
                         (Warning.Length > 0 ? "\n" + Warning : "") +
                         "\nUse this floor?";
            }

            m_FloorLock.HideAllPlanes = HasOffer;
        }

        void LateUpdate()
        {
            // After the camera pose has been updated this frame, so the ring
            // does not lag behind the phone.
            UpdateRing();
        }

        /// <summary>Lock the offered floor and stop ARCore finding more planes.</summary>
        public void Confirm()
        {
            if (!HasOffer || m_Candidate == null) return;

            m_FloorLock.Lock(m_Candidate, m_Candidate.transform.position.y);
            m_FloorLock.HideAllPlanes = false;
            m_PlaneManager.requestedDetectionMode = PlaneDetectionMode.None;

            HasOffer = false;
            m_Candidate = null;
            Warning = string.Empty;
            Status = "Floor confirmed. Tap the floor to place.";
        }

        /// <summary>Re-drop the ring at the current screen centre.</summary>
        public void MoveRingHere()
        {
            m_RingPlaced = false;
        }

        /// <summary>
        /// Skip the offered floor until ARCore changes it: it merges into
        /// another plane, disappears, or its height moves beyond the band.
        /// </summary>
        public void Rescan()
        {
            if (m_Candidate != null)
                m_Rejected.Add(new Rejected { Id = m_Candidate.trackableId, Height = m_Candidate.transform.position.y });

            Withdraw();
            Status = "Rescanning. Keep moving the phone over the floor.";
        }

        /// <summary>Called on Reset: forget everything and look again.</summary>
        public void ResetSearch()
        {
            m_Rejected.Clear();
            Withdraw();
            if (m_PlaneManager != null) m_PlaneManager.requestedDetectionMode = m_SearchMode;
            Status = "Scanning for the floor.";
        }

        /// <summary>
        /// While an offer stands it only changes for a real reason, so the
        /// buttons do not flicker as other planes come and go.
        /// </summary>
        void KeepOrReplaceOffer()
        {
            var gone = m_Candidate == null || m_PlaneManager.GetPlane(m_Candidate.trackableId) == null ||
                       m_Candidate.subsumedBy != null;
            var moved = !gone && Mathf.Abs(m_Candidate.transform.position.y - m_OfferedHeight) > m_FloorLock.HeightTolerance;
            var larger = !gone && FindLargest(out var largest) && largest != m_Candidate &&
                         PlaneMetrics.Area(largest) >= m_LargerFactor * PlaneMetrics.Area(m_Candidate);

            if (!gone && !moved && !larger) return;

            // Start again: the new or moved plane must hold its height first.
            Withdraw();
            SearchForOffer();
        }

        void SearchForOffer()
        {
            var tracked = m_TrackingSince < 0f ? 0f : Time.time - m_TrackingSince;
            if (tracked < m_MinTrackingSeconds)
            {
                Status = "Scanning for the floor (" + (m_MinTrackingSeconds - tracked).ToString("F0") + " s).";
                return;
            }

            if (!FindLargest(out var best) || PlaneMetrics.Area(best) < m_MinCandidateArea)
            {
                m_Candidate = null;
                Status = m_Rejected.Count > 0
                    ? "Rescanning. Keep moving the phone over the floor."
                    : "Scanning for the floor. Sweep the phone slowly over open floor.";
                return;
            }

            var y = best.transform.position.y;
            if (best != m_Candidate || Mathf.Abs(y - m_ReferenceHeight) > m_FloorLock.HeightTolerance)
            {
                // New plane, or its own height moved: start the hold again.
                m_Candidate = best;
                m_ReferenceHeight = y;
                m_HeldSince = Time.time;
            }

            if (Time.time - m_HeldSince < m_HoldSeconds)
            {
                Status = "Checking the floor.";
                return;
            }

            HasOffer = true;
            m_OfferedHeight = y;
        }

        void UpdateWarning()
        {
            m_FloorLock.FindConflicts(m_Candidate, out var above, out var below);

            Warning = string.Empty;
            if (above.Found)
                Warning += "Surface " + Cm(above.Gap) + " cm above (" + above.Area.ToString("F1") +
                           " m2, table, step or false floor?).";
            if (below.Found)
                Warning += (Warning.Length > 0 ? "\n" : "") + "Surface " + Cm(below.Gap) + " cm below (" +
                           below.Area.ToString("F1") + " m2, likely reflection).";
        }

        static string Cm(float metres) => Mathf.Abs(metres * 100f).ToString("F0");

        bool FindLargest(out ARPlane largest)
        {
            largest = null;
            var bestArea = 0f;
            foreach (var plane in m_PlaneManager.trackables)
            {
                if (!m_FloorLock.IsUsable(plane) || IsRejected(plane.trackableId)) continue;
                var area = PlaneMetrics.Area(plane);
                if (area <= bestArea) continue;
                largest = plane;
                bestArea = area;
            }
            return largest != null;
        }

        void ForgetChangedRejections()
        {
            for (var i = m_Rejected.Count - 1; i >= 0; i--)
            {
                var plane = m_PlaneManager.GetPlane(m_Rejected[i].Id);
                var changed = plane == null ||
                              plane.subsumedBy != null ||
                              Mathf.Abs(plane.transform.position.y - m_Rejected[i].Height) > m_FloorLock.HeightTolerance;
                if (changed) m_Rejected.RemoveAt(i);
            }
        }

        bool IsRejected(TrackableId id)
        {
            foreach (var r in m_Rejected)
                if (r.Id == id) return true;
            return false;
        }

        void Withdraw()
        {
            HasOffer = false;
            m_Candidate = null;
            Warning = string.Empty;
            m_RingPlaced = false;
            if (m_FloorLock != null) m_FloorLock.HideAllPlanes = false;
        }

        /// <summary>
        /// Ring and cross fixed in the world at the candidate's height. Dropped
        /// once where the screen centre meets that height, then left in place,
        /// so moving the phone reveals parallax if the height is wrong. Pure
        /// maths, independent of the plane outline, so it shows exactly the
        /// height the lock would use.
        /// </summary>
        void UpdateRing()
        {
            var active = HasOffer && m_Candidate != null && m_XROrigin != null && m_XROrigin.Camera != null;
            if (!active) m_RingPlaced = false;

            if (active && !m_RingPlaced && TryScreenCentreHit(out var drop))
            {
                m_RingPoint = drop;
                m_RingPlaced = true;
            }

            var show = active && m_RingPlaced;
            EnsureRing();
            m_Ring.enabled = show;
            m_Cross.enabled = show;
            if (!show) return;

            // Fixed position on the floor; only the height follows the candidate,
            // which stays within the band while the offer stands.
            var centre = new Vector3(m_RingPoint.x, m_Candidate.transform.position.y + 0.003f, m_RingPoint.z);
            for (var i = 0; i < k_RingSegments; i++)
            {
                var a = i * Mathf.PI * 2f / k_RingSegments;
                m_Ring.SetPosition(i, centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * m_RingRadius);
            }

            // A small cross marks the exact centre, easy to line up with grout.
            var arm = m_RingRadius * 0.25f;
            m_Cross.SetPosition(0, centre + Vector3.left * arm);
            m_Cross.SetPosition(1, centre + Vector3.right * arm);
            m_Cross.SetPosition(2, centre);
            m_Cross.SetPosition(3, centre + Vector3.forward * arm);
            m_Cross.SetPosition(4, centre + Vector3.back * arm);
        }

        bool TryScreenCentreHit(out Vector3 point)
        {
            point = default;
            var camera = m_XROrigin.Camera;
            var ray = camera.ScreenPointToRay(new Vector2(camera.pixelWidth * 0.5f, camera.pixelHeight * 0.5f));
            if (ray.direction.y > -0.087f) return false;

            var along = (m_Candidate.transform.position.y - ray.origin.y) / ray.direction.y;
            if (along <= 0f) return false;

            point = ray.GetPoint(along);
            return Vector3.Distance(camera.transform.position, point) <= m_RingMaxDistance;
        }

        void EnsureRing()
        {
            if (m_Ring != null) return;
            m_Ring = MakeLine("FloorRing", k_RingSegments, true);
            m_Cross = MakeLine("FloorRingCross", 5, false);
        }

        LineRenderer MakeLine(string name, int points, bool loop)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = points;
            lr.widthMultiplier = m_RingWidth;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (m_RingMaterial != null) lr.sharedMaterial = m_RingMaterial;
            m_Block.Clear();
            m_Block.SetColor(k_BaseColor, m_RingColour);
            lr.SetPropertyBlock(m_Block);
            lr.enabled = false;
            return lr;
        }
    }
}
