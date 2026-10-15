using System.Collections;
using System.Collections.Generic;
using System.Text;
using ArEnclosure.Core;
using ArEnclosure.Obstacles;
using ArEnclosure.Placement;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace ArEnclosure.Rules
{
    /// <summary>
    /// Runs the rule engine against the placed panel and the marked
    /// obstacles. Rules come from StreamingAssets/rules.json and the model's
    /// front axis and door group from StreamingAssets/model_profile.json.
    /// Nothing is hardcoded: if either file fails to load, the verdict says so.
    ///
    /// Checks run every frame (eight rules against a few lines is cheap), are
    /// drawn as gap lines, and are logged to rule_checks.csv whenever the
    /// outcome changes and no drag or twist is in progress.
    /// </summary>
    public class ClearanceChecker : MonoBehaviour
    {
        [SerializeField] ARPlacementController m_Placement;
        [SerializeField] PlacementManipulator m_Manipulator;
        [SerializeField] ObstacleSet m_Obstacles;
        [SerializeField] FloorLock m_FloorLock;

        [Header("Gap lines")]
        [SerializeField] Material m_GapLineMaterial;
        [SerializeField] float m_GapLineWidth = 0.008f;
        [SerializeField] Color m_PassColour = new Color(0.2f, 0.85f, 0.3f, 1f);
        [SerializeField] Color m_FailColour = new Color(0.95f, 0.2f, 0.2f, 1f);
        [SerializeField] Color m_WarnColour = new Color(1f, 0.75f, 0.1f, 1f);
        [SerializeField] Color m_NeutralColour = new Color(0.9f, 0.9f, 0.9f, 1f);

        const string k_RulesFile = "rules.json";
        const string k_ProfileFile = "model_profile.json";
        const string k_LogFile = "rule_checks.csv";
        const string k_LogHeader = "check_id,timestamp,verdict,standard,rule_id,level,face,value_source,status," +
                                   "measured_m,required_m,detail,obstacle_count,panel_base_x,panel_base_y,panel_base_z," +
                                   "panel_yaw_deg,floor_drift_m,conditions_on,ceiling_height_m";

        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Face[] k_FloorFaces = { Face.Front, Face.Back, Face.Left, Face.Right };

        public RuleFile Rules { get; private set; }
        public ModelProfile Profile { get; private set; }
        public string LoadError { get; private set; }
        public bool IsLoaded => Rules != null && Profile != null && LoadError == null;
        public CheckResult Last { get; private set; }
        public float? DoorLeafWidthM { get; private set; }
        public ConditionState Conditions { get; } = new ConditionState();
        public Dictionary<string, float> UserValues { get; } = new Dictionary<string, float>();
        /// <summary>Ceiling height above the floor, entered by the user. Null when not entered.</summary>
        public float? CeilingHeightM { get; set; }
        public string LogPath => m_Log?.Path;

        CsvLog m_Log;
        Vector3 m_FrontLocal = Vector3.forward;
        GameObject m_GeometryOwner;
        ClearanceGeometry m_Geometry;
        Bounds m_LocalBounds;
        readonly LineRenderer[] m_GapLines = new LineRenderer[4];
        MaterialPropertyBlock m_Block;
        string m_LastSignature;
        string m_SessionStamp;
        int m_CheckCount;

        void Awake()
        {
            if (m_Placement == null) m_Placement = FindAnyObjectByType<ARPlacementController>();
            if (m_Manipulator == null) m_Manipulator = FindAnyObjectByType<PlacementManipulator>();
            if (m_Obstacles == null) m_Obstacles = FindAnyObjectByType<ObstacleSet>();
            if (m_FloorLock == null) m_FloorLock = FindAnyObjectByType<FloorLock>();
            m_Log = new CsvLog(k_LogFile, k_LogHeader);
            m_Block = new MaterialPropertyBlock();
            m_SessionStamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CsvLog.Ci);
        }

        IEnumerator Start()
        {
            string rulesText = null, profileText = null, error = null;
            yield return ReadStreamingAsset(k_RulesFile, t => rulesText = t, e => error = e);
            if (error == null) yield return ReadStreamingAsset(k_ProfileFile, t => profileText = t, e => error = e);

            if (error == null)
            {
                try
                {
                    Rules = JsonConvert.DeserializeObject<RuleFile>(rulesText);
                    Profile = JsonConvert.DeserializeObject<ModelProfile>(profileText);
                    if (Rules == null || Rules.rules == null || Rules.rules.Count == 0) error = k_RulesFile + " has no rules";
                    else if (Profile == null || !ClearanceGeometry.TryParseAxis(Profile.front_axis, out m_FrontLocal))
                        error = k_ProfileFile + ": front_axis must be +Z, -Z, +X or -X";
                }
                catch (JsonException e)
                {
                    error = "JSON error: " + e.Message;
                }
            }

            LoadError = error;
            if (error != null) Debug.LogError("Clearance rules not loaded: " + error);
        }

        static IEnumerator ReadStreamingAsset(string fileName, System.Action<string> done, System.Action<string> fail)
        {
            // On Android StreamingAssets sits inside the APK, so it has to be
            // read as a URL. In the Editor it is a plain path.
            var path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
            if (!path.Contains("://")) path = "file://" + path;

            using (var request = UnityWebRequest.Get(path))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) done(request.downloadHandler.text);
                else fail(fileName + ": " + request.error);
            }
        }

        void Update()
        {
            var instance = m_Placement != null ? m_Placement.PlacedInstance : null;

            if (!IsLoaded)
            {
                Last = new CheckResult { Verdict = Verdict.RulesNotLoaded };
                HideGapLines();
                return;
            }

            if (instance == null)
            {
                m_GeometryOwner = null;
                m_Geometry = null;
                DoorLeafWidthM = null;
                Last = RuleEngine.Evaluate(Rules, null, null, null, Conditions, UserValues);
                HideGapLines();
                m_LastSignature = null;
                return;
            }

            if (m_GeometryOwner != instance) BuildGeometry(instance);
            if (m_Geometry == null)
            {
                HideGapLines();
                return;
            }

            Last = RuleEngine.Evaluate(Rules, m_Geometry, m_Obstacles != null ? m_Obstacles.Lines : null,
                DoorLeafWidthM, Conditions, UserValues, CeilingHeightM);

            DrawGapLines();

            var gestureActive = m_Manipulator != null && m_Manipulator.CurrentGesture != ManipulationKind.None;
            if (!gestureActive) LogIfChanged(instance);
        }

        void BuildGeometry(GameObject instance)
        {
            m_GeometryOwner = instance;
            m_Geometry = null;
            DoorLeafWidthM = null;
            if (!ModelBounds.TryGetLocalBounds(instance, out m_LocalBounds)) return;

            m_Geometry = new ClearanceGeometry(instance.transform, m_LocalBounds, m_FrontLocal);

            if (ClearanceGeometry.TryWidestDoorLeaf(instance, Profile.door_group, (float)Profile.door_leaf_min_width_m,
                    m_FrontLocal, out var width))
                DoorLeafWidthM = width;
        }

        void DrawGapLines()
        {
            for (var i = 0; i < k_FloorFaces.Length; i++)
            {
                var face = k_FloorFaces[i];
                var gap = m_Geometry.Measure(face, m_Obstacles != null ? m_Obstacles.Lines : null, null);
                var line = EnsureGapLine(i);
                if (!gap.Found)
                {
                    line.enabled = false;
                    continue;
                }

                line.enabled = true;
                var lift = Vector3.up * 0.01f;
                line.SetPosition(0, gap.FacePoint + lift);
                line.SetPosition(1, gap.ObstaclePoint + lift);

                m_Block.Clear();
                m_Block.SetColor(k_BaseColor, FaceColour(face));
                line.SetPropertyBlock(m_Block);
            }
        }

        Color FaceColour(Face face)
        {
            var colour = m_NeutralColour;
            var rank = 0;
            foreach (var r in Last.Rules)
            {
                if (r.Faces == null || System.Array.IndexOf(r.Faces, face) < 0) continue;
                var thisRank = r.Status == RuleStatus.Fail ? 3 : r.Status == RuleStatus.Warn ? 2 : r.Status == RuleStatus.Pass ? 1 : 0;
                if (thisRank <= rank) continue;
                rank = thisRank;
                colour = thisRank == 3 ? m_FailColour : thisRank == 2 ? m_WarnColour : m_PassColour;
            }
            return colour;
        }

        LineRenderer EnsureGapLine(int index)
        {
            if (m_GapLines[index] != null) return m_GapLines[index];

            var go = new GameObject("GapLine_" + k_FloorFaces[index]);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = m_GapLineWidth;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (m_GapLineMaterial != null) lr.sharedMaterial = m_GapLineMaterial;
            m_GapLines[index] = lr;
            return lr;
        }

        /// <summary>Switched-on conditions joined with ';' (empty when none).</summary>
        string ConditionsText()
        {
            var on = new List<string>(Conditions.On);
            on.Sort(System.StringComparer.Ordinal);
            return string.Join(";", on);
        }

        void HideGapLines()
        {
            foreach (var line in m_GapLines)
                if (line != null) line.enabled = false;
        }

        void LogIfChanged(GameObject instance)
        {
            var t = instance.transform;
            var baseCentre = t.TransformPoint(new Vector3(m_LocalBounds.center.x, m_LocalBounds.min.y, m_LocalBounds.center.z));
            var yaw = t.eulerAngles.y;
            var obstacleCount = m_Obstacles != null ? m_Obstacles.Count : 0;

            // Rounded so tracking jitter does not flood the log.
            var sig = new StringBuilder();
            sig.Append(Last.Verdict).Append('|').Append(obstacleCount).Append('|')
               .Append(Mathf.RoundToInt(baseCentre.x * 100f)).Append(',').Append(Mathf.RoundToInt(baseCentre.z * 100f))
               .Append('|').Append(Mathf.RoundToInt(yaw));
            sig.Append('|').Append(ConditionsText()).Append('|').Append(CsvLog.Num(CeilingHeightM, "F3"));
            foreach (var kv in UserValues) sig.Append('|').Append(kv.Key).Append('=').Append(kv.Value.ToString("F3", CsvLog.Ci));
            foreach (var r in Last.Rules)
                sig.Append('|').Append(r.Status).Append(':').Append(r.MeasuredM.HasValue ? Mathf.RoundToInt(r.MeasuredM.Value * 1000f) : -1);

            var signature = sig.ToString();
            if (signature == m_LastSignature) return;
            m_LastSignature = signature;

            m_CheckCount++;
            var ci = CsvLog.Ci;
            var checkId = m_SessionStamp + "-" + m_CheckCount;
            var now = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", ci);
            var drift = m_FloorLock != null && m_FloorLock.HasLock ? (float?)m_FloorLock.Drift : null;

            var rows = new List<string>();
            foreach (var r in Last.Rules)
            {
                rows.Add(string.Join(",", new[]
                {
                    checkId, now, Last.Verdict.ToString(), CsvLog.Clean(Last.Standard),
                    CsvLog.Clean(r.Rule.id), CsvLog.Clean(r.Rule.level), CsvLog.Clean(r.Rule.face), CsvLog.Clean(r.Rule.value_source),
                    r.Status.ToString(), CsvLog.Num(r.MeasuredM), CsvLog.Num(r.RequiredM), CsvLog.Clean(r.Detail),
                    obstacleCount.ToString(ci),
                    baseCentre.x.ToString("F4", ci), baseCentre.y.ToString("F4", ci), baseCentre.z.ToString("F4", ci),
                    yaw.ToString("F1", ci), CsvLog.Num(drift),
                    CsvLog.Clean(ConditionsText()), CsvLog.Num(CeilingHeightM, "F3")
                }));
            }
            m_Log.AppendRows(rows);
        }
    }
}
