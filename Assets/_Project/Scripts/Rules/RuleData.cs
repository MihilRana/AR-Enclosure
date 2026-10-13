using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.Scripting;

namespace ArEnclosure.Rules
{
    // Field names match rules.json and model_profile.json exactly, so the
    // files stay the single source of truth. link.xml keeps this namespace
    // from being stripped by IL2CPP.

    [Preserve]
    public class RuleFile
    {
        public string standard;
        public string edition;
        public string @case;
        public string measured_with;
        public string national_deviations;
        public List<RuleDef> rules = new List<RuleDef>();
    }

    [Preserve]
    public class RuleDef
    {
        public string id;
        public string face;
        /// <summary>Null means not set. Never treated as zero.</summary>
        public double? min_m;
        public string source;
        public string value_source;
        public bool configurable;
        public string reference;
        public string door_state;
        public string obstacle;
        public Dictionary<string, JToken> applies_if = new Dictionary<string, JToken>();
        public string level;
        public string note;
    }

    [Preserve]
    public class ModelProfile
    {
        public string model_id;
        public string description;
        public string front_axis;
        public string door_group;
        public double door_leaf_min_width_m;
    }
}
