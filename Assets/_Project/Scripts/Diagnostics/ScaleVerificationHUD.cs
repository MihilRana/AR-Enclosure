using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ArEnclosure.Core;
using ArEnclosure.Placement;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ArEnclosure.Diagnostics
{
    /// <summary>
    /// Answers one question on screen: is the panel the size it is supposed
    /// to be, and if it looks wrong, is that a scale fault or a depth fault?
    /// Those two failures look identical through a phone camera, so the
    /// numbers have to separate them.
    /// </summary>
    public class ScaleVerificationHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ARPlacementController m_Placement;
        [SerializeField] XROrigin m_XROrigin;
        [SerializeField] TMP_Text m_Readout;

        [Header("Tolerance")]
        [SerializeField] float m_ToleranceMillimetres = 5f;

        [Header("Logging")]
        [SerializeField] bool m_WriteCsv = true;
        [SerializeField] string m_FileName = "scale_verification.csv";

        struct Run
        {
            public Vector3 Placed;
            public float Distance;
            public float WarmUp;
            public int Planes;
            public string Alignment;
        }

        readonly List<Run> m_Runs = new List<Run>();
        readonly StringBuilder m_Builder = new StringBuilder();

        Vector3 m_AuthoredSize;
        bool m_HasAuthoredSize;
        Vector3 m_PlacedSize;
        bool m_HasPlacedSize;
        float m_PlacementDistance;
        string m_CsvPath;

        void Awake()
        {
            if (m_Placement == null) m_Placement = FindAnyObjectByType<ARPlacementController>();
            if (m_XROrigin == null) m_XROrigin = FindAnyObjectByType<XROrigin>();
            m_CsvPath = Path.Combine(Application.persistentDataPath, m_FileName);
        }

        void Start()
        {
            // Measured off the prefab, not typed in. Change the model and the
            // reference figure changes with it.
            m_HasAuthoredSize = ModelBounds.TryGetLocalSize(m_Placement.ModelPrefab, out var local);
            if (m_HasAuthoredSize)
            {
                var s = m_Placement.ModelPrefab.transform.localScale;
                m_AuthoredSize = new Vector3(local.x * s.x, local.y * s.y, local.z * s.z);
            }
        }

        void OnEnable()
        {
            if (m_Placement == null) return;
            m_Placement.Placed += OnPlaced;
            m_Placement.Cleared += OnCleared;
        }

        void OnDisable()
        {
            if (m_Placement == null) return;
            m_Placement.Placed -= OnPlaced;
            m_Placement.Cleared -= OnCleared;
        }

        void OnPlaced(PlacementReport report)
        {
            m_HasPlacedSize = ModelBounds.TryGetWorldSize(report.Instance, out m_PlacedSize);
            m_PlacementDistance = report.DistanceAtPlacement;
            if (!m_HasPlacedSize) return;

            var run = new Run
            {
                Placed = m_PlacedSize,
                Distance = report.DistanceAtPlacement,
                WarmUp = report.WarmUpSeconds,
                Planes = report.TrackedPlaneCount,
                Alignment = report.Alignment.ToString()
            };

            m_Runs.Add(run);
            if (m_WriteCsv) AppendCsv(run);
        }

        void OnCleared()
        {
            m_HasPlacedSize = false;
        }

        void Update()
        {
            if (m_Readout == null) return;

            m_Builder.Clear();
            m_Builder.AppendLine("SESSION");
            m_Builder.AppendLine("  State      " + ARSession.state);
            m_Builder.AppendLine("  Planes     " + m_Placement.TrackedPlaneCount);
            m_Builder.AppendLine("  Tracking   " + m_Placement.WarmUpSeconds.ToString("F1") + " s");
            m_Builder.AppendLine("  " + m_Placement.Status);
            m_Builder.AppendLine();

            AppendRigLines();
            AppendSizeLines();
            AppendRunLines();

            m_Readout.text = m_Builder.ToString();
        }

        void AppendRigLines()
        {
            var scale = m_XROrigin != null ? m_XROrigin.transform.lossyScale : Vector3.one;
            var yOffset = m_XROrigin != null ? m_XROrigin.CameraYOffset : 0f;

            m_Builder.AppendLine("RIG");
            m_Builder.AppendLine("  XR Origin scale  " + scale.x.ToString("F3"));
            m_Builder.AppendLine("  Camera Y offset  " + yOffset.ToString("F3") + " m");

            if (Mathf.Abs(scale.x - 1f) > 0.001f)
                m_Builder.AppendLine("  ! Origin scale is not 1, all content is scaled");

            // ARCore always runs Device tracking origin mode, so a non zero
            // offset shifts the camera and every trackable with it.
            if (Mathf.Abs(yOffset) > 0.001f)
                m_Builder.AppendLine("  ! Set Camera Y Offset to 0 for ARCore");

            m_Builder.AppendLine();
        }

        void AppendSizeLines()
        {
            m_Builder.AppendLine("SIZE");

            if (!m_HasAuthoredSize)
            {
                m_Builder.AppendLine("  Could not measure the prefab");
                m_Builder.AppendLine();
                return;
            }

            m_Builder.AppendLine("  Authored  " + Fmt(m_AuthoredSize));

            if (!m_HasPlacedSize)
            {
                m_Builder.AppendLine("  Placed    waiting");
                m_Builder.AppendLine();
                return;
            }

            m_Builder.AppendLine("  Placed    " + Fmt(m_PlacedSize));

            var deltaMm = (m_PlacedSize - m_AuthoredSize) * 1000f;
            var worst = Mathf.Max(Mathf.Abs(deltaMm.x), Mathf.Abs(deltaMm.y), Mathf.Abs(deltaMm.z));
            var ratio = m_AuthoredSize.y > 0.0001f ? m_PlacedSize.y / m_AuthoredSize.y : 0f;

            m_Builder.AppendLine("  Ratio     " + ratio.ToString("F4"));
            m_Builder.AppendLine("  Worst gap " + worst.ToString("F1") + " mm");

            if (worst <= m_ToleranceMillimetres)
            {
                m_Builder.AppendLine("  PASS geometry is true scale");
                m_Builder.AppendLine("  So any size change you see is depth,");
                m_Builder.AppendLine("  not scale. Check the distance below.");
            }
            else
            {
                m_Builder.AppendLine("  FAIL something scaled the model");
                m_Builder.AppendLine("  Look at import Scale Factor, prefab");
                m_Builder.AppendLine("  root scale, then XR Origin scale.");
            }

            m_Builder.AppendLine();
        }

        void AppendRunLines()
        {
            m_Builder.AppendLine("RUNS  " + m_Runs.Count);
            if (m_Runs.Count == 0)
            {
                m_Builder.AppendLine("  Place, reset, place again to build data");
                return;
            }

            var minH = float.MaxValue;
            var maxH = float.MinValue;
            var minD = float.MaxValue;
            var maxD = float.MinValue;

            foreach (var run in m_Runs)
            {
                minH = Mathf.Min(minH, run.Placed.y);
                maxH = Mathf.Max(maxH, run.Placed.y);
                minD = Mathf.Min(minD, run.Distance);
                maxD = Mathf.Max(maxD, run.Distance);
            }

            m_Builder.AppendLine("  Height spread   " + ((maxH - minH) * 1000f).ToString("F1") + " mm");
            m_Builder.AppendLine("  Distance range  " + minD.ToString("F2") + " to " + maxD.ToString("F2") + " m");
            m_Builder.AppendLine("  Last placed at  " + m_PlacementDistance.ToString("F2") + " m");

            if (LiveDistance(out var live))
                m_Builder.AppendLine("  You are now     " + live.ToString("F2") + " m away");

            if (m_WriteCsv)
                m_Builder.AppendLine("  CSV " + m_CsvPath);
        }

        bool LiveDistance(out float distance)
        {
            distance = 0f;
            var instance = m_Placement.PlacedInstance;
            if (instance == null || m_XROrigin == null || m_XROrigin.Camera == null) return false;
            if (!ModelBounds.TryGetWorldAabb(instance, out var box)) return false;
            distance = Vector3.Distance(m_XROrigin.Camera.transform.position, box.center);
            return true;
        }

        static string Fmt(Vector3 v)
        {
            return v.x.ToString("F3") + " x " + v.z.ToString("F3") + " x " + v.y.ToString("F3") + " m";
        }

        void AppendCsv(Run run)
        {
            try
            {
                var newFile = !File.Exists(m_CsvPath);
                using (var writer = new StreamWriter(m_CsvPath, true))
                {
                    if (newFile)
                    {
                        writer.WriteLine("timestamp,run,device,authored_h,placed_w,placed_d,placed_h," +
                                         "ratio,origin_scale,cam_y_offset,distance_m,warmup_s,planes,alignment");
                    }

                    var ci = CultureInfo.InvariantCulture;
                    var scale = m_XROrigin != null ? m_XROrigin.transform.lossyScale.x : 1f;
                    var yOffset = m_XROrigin != null ? m_XROrigin.CameraYOffset : 0f;
                    var ratio = m_AuthoredSize.y > 0.0001f ? run.Placed.y / m_AuthoredSize.y : 0f;

                    writer.WriteLine(string.Join(",", new[]
                    {
                        System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci),
                        m_Runs.Count.ToString(ci),
                        SystemInfo.deviceModel,
                        m_AuthoredSize.y.ToString("F4", ci),
                        run.Placed.x.ToString("F4", ci),
                        run.Placed.z.ToString("F4", ci),
                        run.Placed.y.ToString("F4", ci),
                        ratio.ToString("F4", ci),
                        scale.ToString("F4", ci),
                        yOffset.ToString("F4", ci),
                        run.Distance.ToString("F3", ci),
                        run.WarmUp.ToString("F2", ci),
                        run.Planes.ToString(ci),
                        run.Alignment
                    }));
                }
            }
            catch (IOException e)
            {
                Debug.LogWarning("Scale log write failed: " + e.Message);
            }
        }
    }
}
