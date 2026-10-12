using System.Text;
using ArEnclosure.App;
using ArEnclosure.Measurement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArEnclosure.UI
{
    /// <summary>
    /// Buttons and readout for the measurement tool. It owns no state: the
    /// mode lives in AppModeController and the measurement in
    /// MeasurementTool. Buttons only request changes, the labels only read.
    /// </summary>
    public class MeasurementHUD : MonoBehaviour
    {
        [Header("Logic")]
        [SerializeField] AppModeController m_Modes;
        [SerializeField] MeasurementTool m_Tool;

        [Header("Buttons")]
        [SerializeField] Button m_ModeButton;
        [SerializeField] TMP_Text m_ModeLabel;
        [SerializeField] Button m_KindButton;
        [SerializeField] TMP_Text m_KindLabel;
        [SerializeField] Button m_CloseLoopButton;
        [SerializeField] Button m_ClearButton;

        [Header("Readout")]
        [SerializeField] TMP_Text m_Result;

        readonly StringBuilder m_Builder = new StringBuilder();

        void Awake()
        {
            if (m_Modes == null) m_Modes = FindAnyObjectByType<AppModeController>();
            if (m_Tool == null) m_Tool = FindAnyObjectByType<MeasurementTool>();
        }

        void OnEnable()
        {
            if (m_ModeButton != null) m_ModeButton.onClick.AddListener(OnModePressed);
            if (m_KindButton != null) m_KindButton.onClick.AddListener(OnKindPressed);
            if (m_CloseLoopButton != null) m_CloseLoopButton.onClick.AddListener(OnCloseLoopPressed);
            if (m_ClearButton != null) m_ClearButton.onClick.AddListener(OnClearPressed);
        }

        void OnDisable()
        {
            if (m_ModeButton != null) m_ModeButton.onClick.RemoveListener(OnModePressed);
            if (m_KindButton != null) m_KindButton.onClick.RemoveListener(OnKindPressed);
            if (m_CloseLoopButton != null) m_CloseLoopButton.onClick.RemoveListener(OnCloseLoopPressed);
            if (m_ClearButton != null) m_ClearButton.onClick.RemoveListener(OnClearPressed);
        }

        void OnModePressed()
        {
            // Obstacle mode joins this cycle in 3.3.
            var next = m_Modes.Current == AppMode.Measure ? AppMode.Move : AppMode.Measure;
            m_Modes.RequestMode(next);
        }

        void OnKindPressed() => m_Tool.ToggleKind();
        void OnCloseLoopPressed() => m_Tool.CloseLoop();
        void OnClearPressed() => m_Tool.Clear();

        void Update()
        {
            if (m_Modes == null || m_Tool == null) return;

            var measuring = m_Modes.Current == AppMode.Measure;

            if (m_ModeLabel != null) m_ModeLabel.text = "Mode: " + m_Modes.Current;

            SetVisible(m_KindButton, measuring);
            SetVisible(m_ClearButton, measuring);
            SetVisible(m_CloseLoopButton, measuring && m_Tool.CanCloseLoop);
            if (m_KindLabel != null) m_KindLabel.text = m_Tool.Kind.ToString();

            if (m_Result == null) return;

            m_Builder.Clear();

            if (measuring)
            {
                m_Builder.AppendLine(m_Tool.Prompt);
                if (m_Tool.Kind == MeasureKind.Perimeter && !m_Tool.IsComplete && m_Tool.PointCount > 0)
                    m_Builder.AppendLine(m_Tool.PointCount + " points");
            }

            if (m_Tool.HasResult)
            {
                var r = m_Tool.LastResult;
                m_Builder.AppendLine();
                m_Builder.AppendLine("<size=150%><b>Run #" + r.RunId + "</b></size>");
                m_Builder.AppendLine(r.Kind == MeasureKind.Distance ? "Distance" : "Perimeter, " + r.Points.Length + " pts");
                m_Builder.AppendLine("<size=150%><b>" + r.Metres.ToString("F3") + " m</b></size>");
                m_Builder.AppendLine("Camera " + r.CameraDistance.ToString("F2") + " m away");
                m_Builder.AppendLine(r.Logged ? "Saved to CSV" : "<color=#ff6060>CSV write failed</color>");
            }

            m_Result.text = m_Builder.ToString();
        }

        static void SetVisible(Component c, bool visible)
        {
            if (c != null && c.gameObject.activeSelf != visible) c.gameObject.SetActive(visible);
        }
    }
}
