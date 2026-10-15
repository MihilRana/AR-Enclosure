using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ArEnclosure.Rules
{
    /// <summary>
    /// The user's inputs to the rule engine: switched-on conditions,
    /// manufacturer vent gaps and the ceiling height. Kept in
    /// clearance_inputs.json in persistentDataPath, saved on every change and
    /// loaded at start, so they survive a restart. Reset clears them and
    /// deletes the file. The UI only calls these methods.
    /// </summary>
    public class ClearanceInputs : MonoBehaviour
    {
        [SerializeField] ClearanceChecker m_Checker;

        const string k_FileName = "clearance_inputs.json";

        public event Action Changed;

        public string FilePath { get; private set; }
        public float? CeilingHeightM => m_Checker != null ? m_Checker.CeilingHeightM : null;

        void Awake()
        {
            if (m_Checker == null) m_Checker = FindAnyObjectByType<ClearanceChecker>();
            FilePath = Path.Combine(Application.persistentDataPath, k_FileName);
            Load();
        }

        public bool IsConditionOn(string key) => m_Checker.Conditions.IsOn(key);

        public void SetCondition(string key, bool on)
        {
            m_Checker.Conditions.Set(key, on);
            SaveAndNotify();
        }

        public bool TryGetUserValue(string ruleId, out float metres) => m_Checker.UserValues.TryGetValue(ruleId, out metres);

        /// <summary>Null clears the value, so the rule goes back to "not entered".</summary>
        public void SetUserValue(string ruleId, float? metres)
        {
            if (metres.HasValue) m_Checker.UserValues[ruleId] = metres.Value;
            else m_Checker.UserValues.Remove(ruleId);
            SaveAndNotify();
        }

        public void SetCeilingHeight(float? metres)
        {
            m_Checker.CeilingHeightM = metres;
            SaveAndNotify();
        }

        public void ResetInputs()
        {
            m_Checker.Conditions.Clear();
            m_Checker.UserValues.Clear();
            m_Checker.CeilingHeightM = null;
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (IOException e)
            {
                Debug.LogWarning("Could not delete " + FilePath + ": " + e.Message);
            }
            Changed?.Invoke();
        }

        void Load()
        {
            if (m_Checker == null || !File.Exists(FilePath)) return;
            try
            {
                var data = JsonConvert.DeserializeObject<ClearanceInputsFile>(File.ReadAllText(FilePath));
                if (data == null) return;

                foreach (var key in data.conditions_on) m_Checker.Conditions.Set(key, true);
                foreach (var kv in data.user_values) m_Checker.UserValues[kv.Key] = kv.Value;
                m_Checker.CeilingHeightM = data.ceiling_height_m;
            }
            catch (Exception e)
            {
                // A broken file must not stop the app: start clean and say so.
                Debug.LogWarning("Could not read " + FilePath + ", starting with no inputs: " + e.Message);
            }
        }

        void SaveAndNotify()
        {
            var data = new ClearanceInputsFile
            {
                conditions_on = new System.Collections.Generic.List<string>(m_Checker.Conditions.On),
                user_values = new System.Collections.Generic.Dictionary<string, float>(m_Checker.UserValues),
                ceiling_height_m = m_Checker.CeilingHeightM
            };

            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (IOException e)
            {
                Debug.LogWarning("Could not save " + FilePath + ": " + e.Message);
            }
            Changed?.Invoke();
        }
    }
}
