using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ArEnclosure.Core;
using ArEnclosure.Placement;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ArEnclosure.Diagnostics
{
    /// <summary>
    /// Shows what ARCore thinks the floor is: every plane with its height,
    /// size, area, tracking state and whether it has been merged into
    /// another, plus which plane the last raycast actually used. Heights are
    /// given relative to the lowest plane, so a false upper floor stands out.
    /// Raycasts are also written to CSV for later analysis.
    /// </summary>
    public class PlaneDiagnostics : MonoBehaviour
    {
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] FloorLock m_FloorLock;
        [SerializeField] TMP_Text m_Readout;
        [SerializeField] bool m_ShowReadout = true;

        [Header("Logging")]
        [SerializeField] bool m_WriteCsv = true;

        // Constants, not serialized fields: a serialized name kept the old
        // file in use after the columns changed, mixing two headers in one CSV.
        const string k_FileName = "plane_diagnostics_v2.csv";
        const string k_Header = "timestamp,source,hit_count,chosen_id,chosen_y,lowest_plane_y," +
                                "chosen_above_lowest_m,plane_count,lock_id,lock_y,lock_current_y,lock_drift_m,hits,planes";

        static readonly CultureInfo k_Ci = CultureInfo.InvariantCulture;

        readonly StringBuilder m_Builder = new StringBuilder();
        readonly List<ARPlane> m_Planes = new List<ARPlane>();
        string m_CsvPath;
        TrackableId m_LastDragChoice = TrackableId.invalidId;
        int m_LastDragHitCount = -1;

        void Awake()
        {
            if (m_PlaneManager == null) m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            if (m_FloorLock == null) m_FloorLock = FindAnyObjectByType<FloorLock>();
            m_CsvPath = Path.Combine(Application.persistentDataPath, k_FileName);
            SetAsideIfHeaderChanged();
        }

        /// <summary>Lets the HUD's Debug toggle hide the readout. Logging carries on.</summary>
        public bool ShowReadout
        {
            get => m_ShowReadout;
            set => m_ShowReadout = value;
        }

        void OnEnable() => RaycastProbe.Recorded += OnRecorded;
        void OnDisable() => RaycastProbe.Recorded -= OnRecorded;

        void Update()
        {
            if (m_Readout == null) return;
            if (m_Readout.gameObject.activeSelf != m_ShowReadout) m_Readout.gameObject.SetActive(m_ShowReadout);
            if (!m_ShowReadout) return;

            CollectPlanes();
            var lowest = LowestY();

            m_Builder.Clear();
            m_Builder.Append("PLANES ").Append(m_Planes.Count);
            if (m_Planes.Count > 0) m_Builder.Append("   lowest y ").Append(lowest.ToString("F3")).Append(" m");
            m_Builder.AppendLine();
            m_Builder.AppendLine("  id    above   size m       area   state  merged     use");

            foreach (var plane in m_Planes)
            {
                m_Builder.Append("  ").Append(RaycastProbe.ShortId(plane.trackableId))
                    .Append("  ").Append(Signed(plane.transform.position.y - lowest))
                    .Append("  ").Append((plane.size.x).ToString("F2")).Append('x').Append(plane.size.y.ToString("F2"))
                    .Append("  ").Append(PlaneMetrics.Area(plane).ToString("F2").PadLeft(5))
                    .Append("  ").Append(StateShort(plane))
                    .Append("  ").Append(plane.subsumedBy != null ? "into " + RaycastProbe.ShortId(plane.subsumedBy.trackableId) : "-")
                    .Append("  ").Append(UseLabel(plane))
                    .Append(plane.alignment == PlaneAlignment.HorizontalUp ? "" : "  " + plane.alignment)
                    .AppendLine();
            }

            AppendLock(lowest);
            AppendLastHit(lowest);
            m_Readout.text = m_Builder.ToString();
        }

        void AppendLastHit(float lowest)
        {
            m_Builder.AppendLine();
            if (!RaycastProbe.HasRecord)
            {
                m_Builder.AppendLine("LAST HIT  none yet");
                return;
            }

            var r = RaycastProbe.Last;
            m_Builder.Append("LAST HIT  ").Append(r.Source).Append(", ")
                .Append((Time.time - r.Time).ToString("F1")).Append(" s ago, ")
                .Append(r.Hits.Length).AppendLine(" hits (nearest first)");

            for (var i = 0; i < r.Hits.Length && i < 6; i++)
            {
                var h = r.Hits[i];
                m_Builder.Append(i == r.Chosen ? "  > " : "    ")
                    .Append(RaycastProbe.ShortId(h.Id))
                    .Append("  above ").Append(Signed(h.Y - lowest))
                    .Append("  ray ").Append(h.Distance.ToString("F2")).Append(" m")
                    .AppendLine(i == r.Chosen ? "  USED" : "");
            }

            if (r.Chosen < 0) m_Builder.AppendLine("  no hit used");
        }

        void OnRecorded(ProbeRecord r)
        {
            if (!m_WriteCsv) return;

            // Drag raycasts every frame. Only log when the winning plane or
            // the hit count changes, which is exactly when a jump happens.
            if (r.Source.StartsWith("drag"))
            {
                var choice = r.Chosen >= 0 ? r.Hits[r.Chosen].Id : TrackableId.invalidId;
                if (choice == m_LastDragChoice && r.Hits.Length == m_LastDragHitCount) return;
                m_LastDragChoice = choice;
                m_LastDragHitCount = r.Hits.Length;
            }

            CollectPlanes();
            var lowest = LowestY();
            AppendCsv(r, lowest);
        }

        void AppendCsv(ProbeRecord r, float lowest)
        {
            try
            {
                var newFile = !File.Exists(m_CsvPath);
                using (var writer = new StreamWriter(m_CsvPath, true))
                {
                    if (newFile)
                    {
                        writer.WriteLine(k_Header);
                    }

                    var chosen = r.Chosen >= 0 ? r.Hits[r.Chosen] : default;
                    var hits = new StringBuilder();
                    for (var i = 0; i < r.Hits.Length; i++)
                    {
                        if (i > 0) hits.Append('|');
                        hits.Append(r.Hits[i].Id).Append(';')
                            .Append(r.Hits[i].Y.ToString("F4", k_Ci)).Append(';')
                            .Append(r.Hits[i].Distance.ToString("F3", k_Ci));
                    }

                    var planes = new StringBuilder();
                    for (var i = 0; i < m_Planes.Count; i++)
                    {
                        var p = m_Planes[i];
                        if (i > 0) planes.Append('|');
                        planes.Append(p.trackableId).Append(';')
                            .Append(p.transform.position.y.ToString("F4", k_Ci)).Append(';')
                            .Append(p.size.x.ToString("F2", k_Ci)).Append('x').Append(p.size.y.ToString("F2", k_Ci)).Append(';')
                            .Append(PlaneMetrics.Area(p).ToString("F3", k_Ci)).Append(';')
                            .Append(p.trackingState).Append(';')
                            .Append(p.alignment).Append(';')
                            .Append(p.subsumedBy != null ? p.subsumedBy.trackableId.ToString() : "-");
                    }

                    writer.WriteLine(string.Join(",", new[]
                    {
                        System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", k_Ci),
                        r.Source,
                        r.Hits.Length.ToString(k_Ci),
                        r.Chosen >= 0 ? chosen.Id.ToString() : "-",
                        r.Chosen >= 0 ? chosen.Y.ToString("F4", k_Ci) : "",
                        m_Planes.Count > 0 ? lowest.ToString("F4", k_Ci) : "",
                        r.Chosen >= 0 && m_Planes.Count > 0 ? (chosen.Y - lowest).ToString("F4", k_Ci) : "",
                        m_Planes.Count.ToString(k_Ci),
                        HasLock ? m_FloorLock.CurrentId.ToString() : "-",
                        HasLock ? m_FloorLock.LockedHeight.ToString("F4", k_Ci) : "",
                        HasLock ? m_FloorLock.CurrentHeight.ToString("F4", k_Ci) : "",
                        HasLock ? m_FloorLock.Drift.ToString("F4", k_Ci) : "",
                        hits.ToString(),
                        planes.ToString()
                    }));
                }
            }
            catch (IOException e)
            {
                Debug.LogWarning("Plane diagnostics write failed: " + e.Message);
            }
        }

        /// <summary>
        /// If the file on disk was written with different columns, rename it
        /// and start fresh, so one CSV never holds two layouts.
        /// </summary>
        void SetAsideIfHeaderChanged()
        {
            try
            {
                if (!File.Exists(m_CsvPath)) return;
                string first;
                using (var reader = new StreamReader(m_CsvPath)) first = reader.ReadLine();
                if (first == k_Header) return;

                var stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", k_Ci);
                File.Move(m_CsvPath, Path.Combine(Application.persistentDataPath, "plane_diagnostics_v2_old_" + stamp + ".csv"));
            }
            catch (IOException e)
            {
                Debug.LogWarning("Plane diagnostics header check failed: " + e.Message);
            }
        }

        void CollectPlanes()
        {
            m_Planes.Clear();
            if (m_PlaneManager == null) return;
            foreach (var plane in m_PlaneManager.trackables) m_Planes.Add(plane);
            m_Planes.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));
        }

        float LowestY()
        {
            return m_Planes.Count > 0 ? m_Planes[0].transform.position.y : 0f;
        }

        bool HasLock => m_FloorLock != null && m_FloorLock.HasLock;

        string UseLabel(ARPlane plane)
        {
            if (m_FloorLock == null) return "";
            if (HasLock && plane.trackableId == m_FloorLock.CurrentId) return "LOCKED";
            var label = m_FloorLock.UsabilityLabel(plane);
            if (label == "ok" && HasLock &&
                Mathf.Abs(plane.transform.position.y - m_FloorLock.CurrentHeight) > m_FloorLock.HeightTolerance)
                return "off-floor";
            return label;
        }

        void AppendLock(float lowest)
        {
            m_Builder.AppendLine();
            if (m_FloorLock == null) return;
            if (m_PlaneManager != null)
                m_Builder.Append("PLANE FINDING  ").AppendLine(m_PlaneManager.requestedDetectionMode == PlaneDetectionMode.None ? "off (floor confirmed)" : m_PlaneManager.requestedDetectionMode.ToString());
            if (!m_FloorLock.HasLock)
            {
                m_Builder.AppendLine("LOCK  none, set on placement");
                return;
            }

            var followed = m_FloorLock.CurrentId != m_FloorLock.LockedId
                ? " (merged from " + RaycastProbe.ShortId(m_FloorLock.LockedId) + ")"
                : "";
            m_Builder.Append("LOCK  ").Append(RaycastProbe.ShortId(m_FloorLock.CurrentId)).Append(followed)
                .Append("  y ").Append(m_FloorLock.LockedHeight.ToString("F3"))
                .Append("  band +/-").Append((m_FloorLock.HeightTolerance * 100f).ToString("F0")).AppendLine(" cm of floor now");
            m_Builder.Append("  floor now ").Append(m_FloorLock.CurrentHeight.ToString("F3"))
                .Append(", drift ").Append((m_FloorLock.Drift * 100f).ToString("+0.0;-0.0")).Append(" cm")
                .Append(m_FloorLock.CurrentPlaneTracked ? "" : ", plane lost")
                .AppendLine(m_FloorLock.HasDrifted ? "  ! FLOOR MOVED" : "");
        }

        static string StateShort(ARPlane plane)
        {
            switch (plane.trackingState)
            {
                case TrackingState.Tracking: return "trk ";
                case TrackingState.Limited: return "LIM ";
                default: return "none";
            }
        }

        static string Signed(float metres)
        {
            return (metres >= 0f ? "+" : "") + metres.ToString("F3") + "m";
        }
    }
}
