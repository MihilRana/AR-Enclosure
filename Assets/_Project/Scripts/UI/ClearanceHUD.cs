using System.Text;
using ArEnclosure.App;
using ArEnclosure.Diagnostics;
using ArEnclosure.Obstacles;
using ArEnclosure.Placement;
using ArEnclosure.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArEnclosure.UI
{
    /// <summary>
    /// Verdict and per-rule readout, plus the obstacle-mode buttons and the
    /// Debug toggle. Owns no state: it reads ClearanceChecker and asks
    /// ObstacleTool to act.
    /// </summary>
    public class ClearanceHUD : MonoBehaviour
    {
        [Header("Logic")]
        [SerializeField] AppModeController m_Modes;
        [SerializeField] ClearanceChecker m_Checker;
        [SerializeField] ObstacleTool m_Tool;
        [SerializeField] ObstacleSet m_Obstacles;
        [SerializeField] ARPlacementController m_Placement;

        [Header("Readout")]
        [Tooltip("Panel (with background) that holds the readout. Hidden in Measure mode.")]
        [SerializeField] GameObject m_Panel;
        [SerializeField] TMP_Text m_Readout;
        [Tooltip("Thin bar on the panel's left edge, coloured by the verdict.")]
        [SerializeField] Image m_VerdictStrip;

        [Header("Obstacle buttons")]
        [SerializeField] Button m_KindButton;
        [SerializeField] TMP_Text m_KindLabel;
        [SerializeField] Button m_UndoButton;
        [SerializeField] Button m_ClearButton;

        [Header("Debug toggle")]
        [SerializeField] Button m_DebugButton;
        [SerializeField] TMP_Text m_DebugLabel;
        [SerializeField] GameObject m_ScaleReadout;
        [SerializeField] PlaneDiagnostics m_PlaneDiagnostics;
        [Tooltip("Background panels behind the debug readouts, shown and hidden with them.")]
        [SerializeField] GameObject[] m_DebugPanels;
        [SerializeField] bool m_ShowDebug = true;

        readonly StringBuilder m_Builder = new StringBuilder();

        void Awake()
        {
            if (m_Modes == null) m_Modes = FindAnyObjectByType<AppModeController>();
            if (m_Checker == null) m_Checker = FindAnyObjectByType<ClearanceChecker>();
            if (m_Tool == null) m_Tool = FindAnyObjectByType<ObstacleTool>();
            if (m_Obstacles == null) m_Obstacles = FindAnyObjectByType<ObstacleSet>();
            if (m_Placement == null) m_Placement = FindAnyObjectByType<ARPlacementController>();
            if (m_PlaneDiagnostics == null) m_PlaneDiagnostics = FindAnyObjectByType<PlaneDiagnostics>();
        }

        void OnEnable()
        {
            if (m_KindButton != null) m_KindButton.onClick.AddListener(OnKind);
            if (m_UndoButton != null) m_UndoButton.onClick.AddListener(OnUndo);
            if (m_ClearButton != null) m_ClearButton.onClick.AddListener(OnClear);
            if (m_DebugButton != null) m_DebugButton.onClick.AddListener(OnDebug);
        }

        void OnDisable()
        {
            if (m_KindButton != null) m_KindButton.onClick.RemoveListener(OnKind);
            if (m_UndoButton != null) m_UndoButton.onClick.RemoveListener(OnUndo);
            if (m_ClearButton != null) m_ClearButton.onClick.RemoveListener(OnClear);
            if (m_DebugButton != null) m_DebugButton.onClick.RemoveListener(OnDebug);
        }

        void OnKind() => m_Tool.ToggleKind();
        void OnUndo() => m_Tool.Undo();
        void OnClear() => m_Tool.ClearAll();
        void OnDebug() => m_ShowDebug = !m_ShowDebug;

        void Update()
        {
            if (m_Modes == null || m_Checker == null) return;

            var mode = m_Modes.Current;
            var obstacleMode = mode == AppMode.Obstacle;

            SetVisible(m_KindButton, obstacleMode);
            SetVisible(m_UndoButton, obstacleMode);
            SetVisible(m_ClearButton, obstacleMode);
            if (m_KindLabel != null && m_Tool != null) m_KindLabel.text = "Kind: " + m_Tool.Kind;

            if (m_ScaleReadout != null && m_ScaleReadout.activeSelf != m_ShowDebug) m_ScaleReadout.SetActive(m_ShowDebug);
            if (m_PlaneDiagnostics != null) m_PlaneDiagnostics.ShowReadout = m_ShowDebug;
            if (m_DebugPanels != null)
                foreach (var panel in m_DebugPanels)
                    if (panel != null && panel.activeSelf != m_ShowDebug) panel.SetActive(m_ShowDebug);
            if (m_DebugLabel != null) m_DebugLabel.text = m_ShowDebug ? "Debug on" : "Debug off";

            var showPanel = mode != AppMode.Measure;
            if (m_Panel != null && m_Panel.activeSelf != showPanel) m_Panel.SetActive(showPanel);
            if (!showPanel || m_Readout == null) return;

            m_Readout.text = BuildText(obstacleMode);
            if (m_VerdictStrip != null) m_VerdictStrip.color = StripColour(m_Checker.Last);
        }

        string BuildText(bool obstacleMode)
        {
            var c = m_Checker.Last;
            m_Builder.Clear();

            if (obstacleMode && m_Tool != null)
            {
                m_Builder.Append("<color=" + UiPalette.PromptHex + ">").Append(m_Tool.Prompt).AppendLine("</color>");
                m_Builder.AppendLine();
            }

            if (c == null || c.Verdict == Verdict.RulesNotLoaded)
            {
                m_Builder.AppendLine("<b><color=" + UiPalette.FailHex + ">RULES NOT LOADED</color></b>");
                if (!string.IsNullOrEmpty(m_Checker.LoadError)) m_Builder.AppendLine(m_Checker.LoadError);
                return m_Builder.ToString();
            }

            if (c.Verdict == Verdict.NotPlaced)
            {
                m_Builder.AppendLine("<b>CLEARANCE CHECK</b>");
                // The placement status carries the floor-confirm prompts, which
                // must be readable with the debug readouts hidden.
                m_Builder.AppendLine(m_Placement != null && !string.IsNullOrEmpty(m_Placement.Status)
                    ? m_Placement.Status
                    : "Place the enclosure to check.");
                return m_Builder.ToString();
            }

            switch (c.Verdict)
            {
                case Verdict.Compliant:
                    m_Builder.Append("<size=44><b><color=" + UiPalette.PassHex + ">COMPLIANT</color></b></size>\n")
                        .Append("<color=" + UiPalette.MutedHex + ">").Append(c.Standard).AppendLine(" checks</color>");
                    break;
                case Verdict.NotCompliant:
                    m_Builder.AppendLine("<size=44><b><color=" + UiPalette.FailHex + ">NOT COMPLIANT</color></b></size>");
                    break;
                default:
                    m_Builder.AppendLine("<size=44><b><color=" + UiPalette.WarnHex + ">INCOMPLETE</color></b></size>");
                    m_Builder.Append(c.BlockingCount).AppendLine(c.BlockingCount == 1
                        ? " required check has no result yet"
                        : " required checks have no result yet");
                    break;
            }

            if (c.NotEnteredCount > 0)
                m_Builder.Append(c.NotEnteredCount).AppendLine(c.NotEnteredCount == 1
                    ? " manufacturer gap not entered"
                    : " manufacturer gaps not entered");
            if (c.WarningCount > 0)
                m_Builder.Append(c.WarningCount).AppendLine(c.WarningCount == 1 ? " advisory warning" : " advisory warnings");

            m_Builder.Append("Obstacles ").Append(m_Obstacles != null ? m_Obstacles.Count : 0)
                .Append("   Door leaf ")
                .AppendLine(m_Checker.DoorLeafWidthM.HasValue ? m_Checker.DoorLeafWidthM.Value.ToString("F3") + " m" : "not found");
            m_Builder.Append("Conditions on ").Append(m_Checker.Conditions.On.Count)
                .Append("   Ceiling ")
                .AppendLine(m_Checker.CeilingHeightM.HasValue ? m_Checker.CeilingHeightM.Value.ToString("F3") + " m" : "not entered");
            m_Builder.AppendLine();

            foreach (var r in c.Rules)
            {
                m_Builder.Append(DisplayName(r.Rule)).Append("  ").AppendLine(StatusTag(r.Status));
                m_Builder.Append("   ");
                if (r.MeasuredM.HasValue) m_Builder.Append(r.MeasuredM.Value.ToString("F3")).Append(" m");
                else m_Builder.Append("-");
                m_Builder.Append("  min ");
                m_Builder.Append(r.RequiredM.HasValue ? r.RequiredM.Value.ToString("F3") + " m" : "-");
                if (r.Status != RuleStatus.Pass && r.Status != RuleStatus.Fail && !string.IsNullOrEmpty(r.Detail))
                    m_Builder.Append("  <size=22><color=" + UiPalette.MutedHex + ">").Append(r.Detail).Append("</color></size>");
                m_Builder.AppendLine();
            }

            return m_Builder.ToString();
        }

        /// <summary>
        /// The rule's label from rules.json without its unit suffix, since each
        /// line already shows metres. The id is the fallback; CSVs keep the id.
        /// </summary>
        static string DisplayName(RuleDef rule)
        {
            if (string.IsNullOrEmpty(rule.label)) return rule.id;
            return System.Text.RegularExpressions.Regex.Replace(rule.label, @"\s*\(m\)\s*$", "");
        }

        static string StatusTag(RuleStatus status)
        {
            switch (status)
            {
                case RuleStatus.Pass: return "<color=" + UiPalette.PassHex + "><b>PASS</b></color>";
                case RuleStatus.Fail: return "<color=" + UiPalette.FailHex + "><b>FAIL</b></color>";
                case RuleStatus.Warn: return "<color=" + UiPalette.WarnHex + "><b>WARN</b></color>";
                case RuleStatus.NotSet: return "<color=" + UiPalette.WarnHex + ">not set</color>";
                case RuleStatus.NotEntered: return "<color=" + UiPalette.MutedHex + ">not entered</color>";
                case RuleStatus.Unknown: return "<color=" + UiPalette.WarnHex + ">unknown</color>";
                default: return "<color=" + UiPalette.MutedHex + ">n/a</color>";
            }
        }

        /// <summary>Presentation only: the strip mirrors the verdict already shown as text.</summary>
        static Color StripColour(CheckResult c)
        {
            if (c == null) return UiPalette.Muted;
            switch (c.Verdict)
            {
                case Verdict.Compliant: return UiPalette.Pass;
                case Verdict.NotCompliant: return UiPalette.Fail;
                case Verdict.Incomplete: return UiPalette.Warn;
                default: return UiPalette.Muted;
            }
        }

        static void SetVisible(Component c, bool visible)
        {
            if (c != null && c.gameObject.activeSelf != visible) c.gameObject.SetActive(visible);
        }
    }
}
