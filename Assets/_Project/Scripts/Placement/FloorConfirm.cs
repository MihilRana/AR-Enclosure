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
    /// The candidate is the largest usable plane. It is offered once no other
    /// usable plane near its outline disagrees with its height by more than
    /// the band, and that has held for a short time. Confirming locks the
    /// floor and stops plane finding, so ARCore stops spawning new planes; the
    /// existing ones keep tracking, so drift is still measured. Rescan skips
    /// the offered plane until ARCore changes it. Reset turns finding back on.
    /// </summary>
    public class FloorConfirm : MonoBehaviour
    {
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] FloorLock m_FloorLock;

        [Header("Candidate")]
        [Tooltip("The candidate must stay settled this long before it is offered.")]
        [SerializeField] float m_StableSeconds = 1.5f;
        [Tooltip("A candidate whose height moves more than this restarts the stability timer.")]
        [SerializeField] float m_StableHeight = 0.01f;

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
        /// <summary>A settled floor is waiting for Use this floor or Rescan.</summary>
        public bool HasOffer { get; private set; }
        public bool PlaneFindingOn => m_PlaneManager != null && m_PlaneManager.requestedDetectionMode != PlaneDetectionMode.None;
        public string Status { get; private set; } = "Scanning for the floor.";

        readonly List<Rejected> m_Rejected = new List<Rejected>();
        PlaneDetectionMode m_SearchMode = PlaneDetectionMode.Horizontal;
        ARPlane m_Candidate;
        float m_CandidateSince;
        float m_CandidateHeight;
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

            if (IsConfirmed)
            {
                HasOffer = false;
                SetOutline(null);
                return;
            }

            ForgetChangedRejections();
            UpdateCandidate();
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

            ClearCandidate();
            Status = "Rescanning. Keep moving the phone over the floor.";
        }

        /// <summary>Called on Reset: forget everything and look again.</summary>
        public void ResetSearch()
        {
            m_Rejected.Clear();
            ClearCandidate();
            if (m_PlaneManager != null) m_PlaneManager.requestedDetectionMode = m_SearchMode;
            Status = "Scanning for the floor.";
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

        void UpdateCandidate()
        {
            ARPlane best = null;
            var bestArea = 0f;
            foreach (var plane in m_PlaneManager.trackables)
            {
                if (!m_FloorLock.IsUsable(plane) || IsRejected(plane.trackableId)) continue;
                var area = PlaneMetrics.Area(plane);
                if (area <= bestArea) continue;
                best = plane;
                bestArea = area;
            }

            if (best == null)
            {
                ClearCandidate();
                Status = m_Rejected.Count > 0
                    ? "Rescanning. Keep moving the phone over the floor."
                    : "Scanning for the floor.";
                return;
            }

            var y = best.transform.position.y;
            if (best != m_Candidate || Mathf.Abs(y - m_CandidateHeight) > m_StableHeight)
            {
                // New plane, or the same plane moving: start the clock again.
                m_Candidate = best;
                m_CandidateSince = Time.time;
                m_CandidateHeight = y;
            }

            if (!m_FloorLock.IsPlaneSettled(best, out var gap))
            {
                m_CandidateSince = Time.time;
                SetOffer(false);
                Status = "Floor still settling (" + (gap * 100f).ToString("F0") + " cm apart), keep scanning.";
                return;
            }

            if (Time.time - m_CandidateSince < m_StableSeconds)
            {
                SetOffer(false);
                Status = "Checking the floor.";
                return;
            }

            SetOffer(true);
            Status = "Floor found (" + bestArea.ToString("F1") + " m2). Use this floor?";
        }

        void SetOffer(bool offer)
        {
            HasOffer = offer;
            m_FloorLock.HighlightId = offer && m_Candidate != null ? m_Candidate.trackableId : TrackableId.invalidId;
            SetOutline(offer ? m_Candidate : null);
        }

        void ClearCandidate()
        {
            m_Candidate = null;
            SetOffer(false);
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
