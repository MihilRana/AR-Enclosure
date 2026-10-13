using System.Collections.Generic;
using ArEnclosure.Core;
using UnityEngine;
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
    /// the user confirm. Once offered, the buttons stay until the candidate
    /// merges, moves beyond the band, or a much larger plane appears.
    ///
    /// Confirming locks the floor and stops plane finding; existing planes
    /// keep tracking, so drift is still measured. Rescan skips the offered
    /// plane until ARCore changes it. Reset turns finding back on.
    /// </summary>
    public class FloorConfirm : MonoBehaviour
    {
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] FloorLock m_FloorLock;

        [Header("Offer rules")]
        [Tooltip("No floor is offered before this much continuous tracking.")]
        [SerializeField] float m_MinTrackingSeconds = 8f;
        [Tooltip("Smaller planes are never offered as the floor.")]
        [SerializeField] float m_MinCandidateArea = 1.0f;
        [Tooltip("The candidate's own height must stay within the band this long.")]
        [SerializeField] float m_HoldSeconds = 2f;
        [Tooltip("Once offered, a plane this many times larger replaces the offer.")]
        [SerializeField] float m_LargerFactor = 1.5f;

        [Header("Outline")]
        [SerializeField] Material m_OutlineMaterial;
        [SerializeField] Color m_OutlineColour = new Color(0.2f, 0.9f, 0.35f, 1f);
        [SerializeField] float m_OutlineWidth = 0.015f;

        struct Rejected
        {
            public TrackableId Id;
            public float Height;
        }

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
        LineRenderer m_Outline;
        MaterialPropertyBlock m_Block;

        void Awake()
        {
            if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            if (m_FloorLock == null) m_FloorLock = FindAnyObjectByType<FloorLock>();
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
                SetOutline(null);
                return;
            }

            ForgetChangedRejections();

            if (HasOffer) KeepOrReplaceOffer();
            else SearchForOffer();

            if (HasOffer)
            {
                UpdateWarning();
                SetOutline(m_Candidate);
                Status = "Floor found (" + PlaneMetrics.Area(m_Candidate).ToString("F1") + " m2). Use this floor?" +
                         (Warning.Length > 0 ? "\n" + Warning : "");
            }
        }

        /// <summary>Lock the offered floor and stop ARCore finding more planes.</summary>
        public void Confirm()
        {
            if (!HasOffer || m_Candidate == null) return;

            m_FloorLock.Lock(m_Candidate, m_Candidate.transform.position.y);
            m_FloorLock.HighlightId = TrackableId.invalidId;
            m_PlaneManager.requestedDetectionMode = PlaneDetectionMode.None;

            HasOffer = false;
            m_Candidate = null;
            Warning = string.Empty;
            SetOutline(null);
            Status = "Floor confirmed. Tap the floor to place.";
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
            m_FloorLock.HighlightId = best.trackableId;
        }

        void UpdateWarning()
        {
            if (!m_FloorLock.TryFindConflict(m_Candidate, out var gap))
            {
                Warning = string.Empty;
                return;
            }

            var cm = Mathf.Abs(gap * 100f).ToString("F0");
            Warning = gap < 0f
                ? "Another surface " + cm + " cm below (likely reflection)."
                : "Another surface " + cm + " cm above (table, step or false floor?).";
        }

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
            m_FloorLock.HighlightId = TrackableId.invalidId;
            SetOutline(null);
        }

        void SetOutline(ARPlane plane)
        {
            if (plane == null)
            {
                if (m_Outline != null) m_Outline.enabled = false;
                return;
            }

            if (m_Outline == null)
            {
                var go = new GameObject("FloorCandidateOutline");
                m_Outline = go.AddComponent<LineRenderer>();
                m_Outline.useWorldSpace = true;
                m_Outline.loop = true;
                m_Outline.widthMultiplier = m_OutlineWidth;
                m_Outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_Outline.receiveShadows = false;
                if (m_OutlineMaterial != null) m_Outline.sharedMaterial = m_OutlineMaterial;
                m_Block.Clear();
                m_Block.SetColor(k_BaseColor, m_OutlineColour);
                m_Outline.SetPropertyBlock(m_Block);
            }

            // Redrawn every frame: the outline keeps growing while finding is on.
            var boundary = plane.boundary;
            if (!boundary.IsCreated || boundary.Length < 3)
            {
                m_Outline.enabled = false;
                return;
            }

            m_Outline.enabled = true;
            m_Outline.positionCount = boundary.Length;
            for (var i = 0; i < boundary.Length; i++)
            {
                var local = new Vector3(boundary[i].x, 0.005f, boundary[i].y);
                m_Outline.SetPosition(i, plane.transform.TransformPoint(local));
            }
        }
    }
}
