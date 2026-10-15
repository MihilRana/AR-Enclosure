using System.Collections.Generic;
using System.Globalization;
using ArEnclosure.App;
using ArEnclosure.Rules;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArEnclosure.UI
{
    /// <summary>
    /// The Inputs panel. Its rows are built from rules.json once it has
    /// loaded: one toggle per applies_if condition, one number field per
    /// user_input rule, and a ceiling height field when a rule checks the top
    /// face. Adding a condition or a manufacturer value to the file adds a row
    /// with no code change. Owns no state: every change goes to
    /// ClearanceInputs, which saves it.
    /// </summary>
    public class ClearanceInputsHUD : MonoBehaviour
    {
        [SerializeField] AppModeController m_Modes;
        [SerializeField] ClearanceChecker m_Checker;
        [SerializeField] ClearanceInputs m_Inputs;

        [Header("Panel")]
        [Tooltip("Shown in Obstacle mode. Opens and closes the panel.")]
        [SerializeField] Button m_OpenButton;
        [SerializeField] GameObject m_Panel;
        [Tooltip("Rows are added here, top to bottom.")]
        [SerializeField] RectTransform m_Content;
        [SerializeField] Button m_ResetButton;
        [SerializeField] Button m_DoneButton;
        [SerializeField] TMP_Text m_Message;

        [Header("Row templates (inactive in the scene)")]
        [Tooltip("A Button with a child TMP_Text named Label.")]
        [SerializeField] Button m_ToggleTemplate;
        [Tooltip("A row with a child TMP_Text named Label and a child TMP_InputField named Field.")]
        [SerializeField] GameObject m_FieldTemplate;

        [Header("Limits")]
        [SerializeField] float m_MaxMetres = 10f;

        const string k_CeilingId = "__ceiling_height_m";

        struct ToggleRow
        {
            public string Key;
            public string Label;
            public TMP_Text Text;
        }

        struct FieldRow
        {
            public string Id;
            public TMP_InputField Field;
        }

        readonly List<ToggleRow> m_Toggles = new List<ToggleRow>();
        readonly List<FieldRow> m_Fields = new List<FieldRow>();
        bool m_Built;

        void Awake()
        {
            if (m_Modes == null) m_Modes = FindAnyObjectByType<AppModeController>();
            if (m_Checker == null) m_Checker = FindAnyObjectByType<ClearanceChecker>();
            if (m_Inputs == null) m_Inputs = FindAnyObjectByType<ClearanceInputs>();
            if (m_Panel != null) m_Panel.SetActive(false);
        }

        void OnEnable()
        {
            if (m_OpenButton != null) m_OpenButton.onClick.AddListener(TogglePanel);
            if (m_DoneButton != null) m_DoneButton.onClick.AddListener(ClosePanel);
            if (m_ResetButton != null) m_ResetButton.onClick.AddListener(OnReset);
            if (m_Inputs != null) m_Inputs.Changed += RefreshValues;
        }

        void OnDisable()
        {
            if (m_OpenButton != null) m_OpenButton.onClick.RemoveListener(TogglePanel);
            if (m_DoneButton != null) m_DoneButton.onClick.RemoveListener(ClosePanel);
            if (m_ResetButton != null) m_ResetButton.onClick.RemoveListener(OnReset);
            if (m_Inputs != null) m_Inputs.Changed -= RefreshValues;
        }

        void Update()
        {
            var obstacleMode = m_Modes != null && m_Modes.Current == AppMode.Obstacle;
            if (m_OpenButton != null && m_OpenButton.gameObject.activeSelf != obstacleMode)
                m_OpenButton.gameObject.SetActive(obstacleMode);
            if (!obstacleMode && m_Panel != null && m_Panel.activeSelf) m_Panel.SetActive(false);

            if (!m_Built && m_Checker != null && m_Checker.IsLoaded) Build();
        }

        void TogglePanel()
        {
            if (m_Panel == null) return;
            m_Panel.SetActive(!m_Panel.activeSelf);
            if (m_Panel.activeSelf)
            {
                if (!m_Built && m_Checker != null && m_Checker.IsLoaded) Build();
                RefreshValues();
                SetMessage(m_Checker != null && m_Checker.IsLoaded ? "" : "Rules not loaded yet.");
            }
        }

        void ClosePanel()
        {
            if (m_Panel != null) m_Panel.SetActive(false);
        }

        void OnReset()
        {
            m_Inputs.ResetInputs();
            SetMessage("Inputs cleared.");
        }

        void Build()
        {
            m_Built = true;
            var rules = m_Checker.Rules.rules;

            // One toggle per distinct applies_if entry, in file order.
            var seen = new HashSet<string>();
            foreach (var rule in rules)
            {
                if (rule.applies_if == null) continue;
                foreach (var kv in rule.applies_if)
                {
                    var key = ConditionState.Key(kv.Key, kv.Value);
                    if (!seen.Add(key)) continue;
                    AddToggle(key, FriendlyCondition(kv.Key, kv.Value));
                }
            }

            // One number field per manufacturer value, and the ceiling when a
            // rule checks the top face.
            var needsCeiling = false;
            foreach (var rule in rules)
            {
                if (rule.face == "top") needsCeiling = true;
                if (rule.value_source != "user_input") continue;
                AddField(rule.id, rule.id.Replace('_', ' ') + " (" + rule.face + "), m");
            }
            if (needsCeiling) AddField(k_CeilingId, "Ceiling height above floor, m");

            RefreshValues();
        }

        void AddToggle(string key, string label)
        {
            var button = Instantiate(m_ToggleTemplate, m_Content);
            button.gameObject.SetActive(true);
            button.name = "Toggle_" + key;
            var text = button.transform.Find("Label").GetComponent<TMP_Text>();
            var row = new ToggleRow { Key = key, Label = label, Text = text };
            m_Toggles.Add(row);
            button.onClick.AddListener(() => m_Inputs.SetCondition(key, !m_Inputs.IsConditionOn(key)));
        }

        void AddField(string id, string label)
        {
            var go = Instantiate(m_FieldTemplate, m_Content);
            go.SetActive(true);
            go.name = "Field_" + id;
            go.transform.Find("Label").GetComponent<TMP_Text>().text = label;
            var field = go.transform.Find("Field").GetComponent<TMP_InputField>();
            field.contentType = TMP_InputField.ContentType.DecimalNumber;
            m_Fields.Add(new FieldRow { Id = id, Field = field });
            field.onEndEdit.AddListener(text => OnFieldEdited(id, field, text));
        }

        void OnFieldEdited(string id, TMP_InputField field, string text)
        {
            float? value = null;
            var trimmed = (text ?? "").Trim().Replace(',', '.');
            if (trimmed.Length > 0)
            {
                if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                    parsed < 0f || parsed > m_MaxMetres)
                {
                    SetMessage("Enter metres between 0 and " + m_MaxMetres.ToString("F0") + ", or leave empty.");
                    RefreshValues();
                    return;
                }
                value = parsed;
            }

            if (id == k_CeilingId) m_Inputs.SetCeilingHeight(value);
            else m_Inputs.SetUserValue(id, value);
            SetMessage(value.HasValue ? "Saved." : "Cleared, now not entered.");
        }

        void RefreshValues()
        {
            if (m_Inputs == null) return;

            foreach (var row in m_Toggles)
                row.Text.text = row.Label + ":  " + (m_Inputs.IsConditionOn(row.Key) ? "<b>ON</b>" : "off");

            foreach (var row in m_Fields)
            {
                float? value = null;
                if (row.Id == k_CeilingId) value = m_Inputs.CeilingHeightM;
                else if (m_Inputs.TryGetUserValue(row.Id, out var v)) value = v;

                var text = value.HasValue ? value.Value.ToString("0.000", CultureInfo.InvariantCulture) : "";
                if (row.Field.text != text) row.Field.SetTextWithoutNotify(text);
            }
        }

        void SetMessage(string text)
        {
            if (m_Message != null) m_Message.text = text;
        }

        /// <summary>
        /// Readable text for an applies_if entry. Known keys get a sentence,
        /// with the value taken from the file; anything else is shown as is.
        /// </summary>
        static string FriendlyCondition(string key, JToken value)
        {
            switch (key)
            {
                case "area":
                    return "Area is " + value.ToString().Replace('_', ' ');
                case "doors_lock_open":
                    return value.Type == JTokenType.Boolean && (bool)value ? "Doors lock open" : "Doors do not lock open";
                case "gangway_length_gt_m":
                    return "Gangway longer than " + ((double)value).ToString("0.###", CultureInfo.InvariantCulture) + " m";
                default:
                    return key.Replace('_', ' ') + " = " + value;
            }
        }
    }
}
