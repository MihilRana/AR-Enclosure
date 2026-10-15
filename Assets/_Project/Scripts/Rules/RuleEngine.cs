using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ArEnclosure.Rules
{
    public enum RuleStatus
    {
        Pass,
        Fail,
        /// <summary>Advisory rule below its value. Never affects the verdict.</summary>
        Warn,
        /// <summary>Required value missing (standard null, or model geometry not found).</summary>
        NotSet,
        /// <summary>User-supplied value not entered yet. Skipped.</summary>
        NotEntered,
        /// <summary>Value known, but no obstacle marked on that face.</summary>
        Unknown,
        /// <summary>applies_if conditions are off.</summary>
        NotApplicable
    }

    public enum Verdict
    {
        RulesNotLoaded,
        NotPlaced,
        Compliant,
        NotCompliant,
        Incomplete
    }

    public class RuleResult
    {
        public RuleDef Rule;
        public RuleStatus Status;
        public float? MeasuredM;
        public float? RequiredM;
        public string Detail;
        public FaceGap Gap;
        public Face[] Faces;
    }

    public class CheckResult
    {
        public Verdict Verdict;
        public string Standard;
        public readonly List<RuleResult> Rules = new List<RuleResult>();
        public int NotEnteredCount;
        public int WarningCount;
        public int BlockingCount;
    }

    /// <summary>
    /// Conditions a user has switched on. Each is one applies_if entry from
    /// rules.json written as "key=value", for example "area=closed_restricted"
    /// or "gangway_length_gt_m=10" (the user confirms the gangway is longer
    /// than 10 m). Nothing is named in code, so a new condition in the file is
    /// a data change only. Everything is off by default, so a rule with
    /// applies_if does not run until every one of its conditions is switched on.
    /// </summary>
    public class ConditionState
    {
        readonly HashSet<string> m_On = new HashSet<string>();

        public IReadOnlyCollection<string> On => m_On;

        public static string Key(string name, JToken value)
        {
            var text = value == null ? "" :
                value.Type == JTokenType.Boolean ? ((bool)value ? "true" : "false") :
                value.Type == JTokenType.Float || value.Type == JTokenType.Integer
                    ? ((double)value).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    : value.ToString();
            return name + "=" + text;
        }

        public bool IsOn(string key) => m_On.Contains(key);

        public void Set(string key, bool on)
        {
            if (on) m_On.Add(key);
            else m_On.Remove(key);
        }

        public void Clear() => m_On.Clear();

        public bool Satisfies(Dictionary<string, JToken> appliesIf, out string missing)
        {
            missing = null;
            if (appliesIf == null) return true;

            foreach (var kv in appliesIf)
            {
                if (m_On.Contains(Key(kv.Key, kv.Value))) continue;
                missing = kv.Key;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Evaluates rules.json against the placed panel and the marked obstacles.
    /// No clearance value lives in code: required values come from min_m,
    /// from the model's door geometry, or from user input.
    /// </summary>
    public static class RuleEngine
    {
        public static CheckResult Evaluate(
            RuleFile rules,
            ClearanceGeometry geometry,
            IReadOnlyList<ObstacleLine> obstacles,
            float? doorLeafWidthM,
            ConditionState conditions,
            IReadOnlyDictionary<string, float> userValues,
            float? ceilingHeightM = null)
        {
            var result = new CheckResult { Standard = rules?.standard ?? "" };

            if (rules == null || rules.rules == null)
            {
                result.Verdict = Verdict.RulesNotLoaded;
                return result;
            }

            if (geometry == null)
            {
                result.Verdict = Verdict.NotPlaced;
                return result;
            }

            var anyFail = false;
            var anyIncomplete = false;

            foreach (var rule in rules.rules)
            {
                var r = EvaluateRule(rule, geometry, obstacles, doorLeafWidthM, conditions, userValues, ceilingHeightM);
                result.Rules.Add(r);

                var required = rule.level == "required";
                switch (r.Status)
                {
                    case RuleStatus.Fail:
                        if (required) anyFail = true;
                        break;
                    case RuleStatus.Warn:
                        result.WarningCount++;
                        break;
                    case RuleStatus.NotEntered:
                        result.NotEnteredCount++;
                        break;
                    case RuleStatus.NotSet:
                    case RuleStatus.Unknown:
                        if (required)
                        {
                            anyIncomplete = true;
                            result.BlockingCount++;
                        }
                        break;
                }
            }

            result.Verdict = anyFail ? Verdict.NotCompliant
                : anyIncomplete ? Verdict.Incomplete
                : Verdict.Compliant;
            return result;
        }

        static RuleResult EvaluateRule(
            RuleDef rule,
            ClearanceGeometry geometry,
            IReadOnlyList<ObstacleLine> obstacles,
            float? doorLeafWidthM,
            ConditionState conditions,
            IReadOnlyDictionary<string, float> userValues,
            float? ceilingHeightM)
        {
            var r = new RuleResult { Rule = rule, Faces = FacesFor(rule.face) };

            string missing = null;
            if (conditions == null || !conditions.Satisfies(rule.applies_if, out missing))
            {
                r.Status = RuleStatus.NotApplicable;
                r.Detail = "condition off: " + (missing ?? "?");
                return r;
            }

            // Required value, by value_source.
            switch (rule.value_source)
            {
                case "standard":
                    r.RequiredM = rule.min_m.HasValue ? (float?)rule.min_m.Value : null;
                    break;
                case "model_door_geometry":
                    r.RequiredM = doorLeafWidthM;
                    if (!r.RequiredM.HasValue) r.Detail = "door parts not identified";
                    break;
                case "user_input":
                    if (userValues != null && userValues.TryGetValue(rule.id, out var entered)) r.RequiredM = entered;
                    else if (rule.min_m.HasValue) r.RequiredM = (float)rule.min_m.Value;
                    if (!r.RequiredM.HasValue)
                    {
                        r.Status = RuleStatus.NotEntered;
                        r.Detail = "manufacturer value not entered";
                        return r;
                    }
                    break;
                default:
                    r.Detail = "unknown value_source " + rule.value_source;
                    break;
            }

            if (!r.RequiredM.HasValue)
            {
                r.Status = RuleStatus.NotSet;
                if (r.Detail == null) r.Detail = "min_m not set";
                return r;
            }

            if (r.Faces == null)
            {
                r.Status = RuleStatus.NotSet;
                r.Detail = "unknown face " + rule.face;
                return r;
            }

            // Measured gap: nearest accepted obstacle over the rule's faces.
            var accept = AcceptFor(rule.obstacle);
            var gap = new FaceGap { Found = false, Metres = float.PositiveInfinity };
            foreach (var face in r.Faces)
            {
                // The ceiling is not marked as an obstacle line. The user enters
                // its height above the floor, and the top gap is that height
                // minus the panel's own height from the model's bounds.
                var g = face == Face.Top
                    ? (ceilingHeightM.HasValue
                        ? new FaceGap { Found = true, Metres = Mathf.Max(0f, ceilingHeightM.Value - geometry.PanelHeight) }
                        : new FaceGap { Found = false, Metres = float.PositiveInfinity })
                    : geometry.Measure(face, obstacles, accept);
                if (g.Found && g.Metres < gap.Metres) gap = g;
            }

            if (!gap.Found)
            {
                r.Status = RuleStatus.Unknown;
                r.Detail = r.Faces[0] == Face.Top ? "ceiling height not entered" : "no obstacle marked on this face";
                return r;
            }

            var measured = gap.Metres;

            // A door fixed open at 90 degrees sticks out by its leaf width, so
            // the passing room starts at the door edge, not the panel face.
            if (rule.reference == "door_edge_open_90")
            {
                if (!doorLeafWidthM.HasValue)
                {
                    r.Status = RuleStatus.NotSet;
                    r.Detail = "door parts not identified";
                    return r;
                }
                measured -= doorLeafWidthM.Value;
            }

            r.Gap = gap;
            r.MeasuredM = measured;

            // Compared in whole millimetres, the resolution the HUD shows.
            var ok = Mathf.Round(measured * 1000f) >= Mathf.Round(r.RequiredM.Value * 1000f);
            r.Status = ok ? RuleStatus.Pass : rule.level == "advisory" ? RuleStatus.Warn : RuleStatus.Fail;
            return r;
        }

        static Face[] FacesFor(string face)
        {
            switch (face)
            {
                case "front": return new[] { Face.Front };
                case "back": return new[] { Face.Back };
                case "side": return new[] { Face.Left, Face.Right };
                case "top": return new[] { Face.Top };
                default: return null;
            }
        }

        static System.Func<ObstacleKind, bool> AcceptFor(string obstacle)
        {
            switch (obstacle)
            {
                case "wall": return k => k == ObstacleKind.Wall;
                case "enclosure": return k => k == ObstacleKind.Enclosure;
                default: return null;
            }
        }
    }
}
