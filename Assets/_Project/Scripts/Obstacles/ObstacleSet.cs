using System;
using System.Collections.Generic;
using ArEnclosure.Core;
using ArEnclosure.Rules;
using UnityEngine;

namespace ArEnclosure.Obstacles
{
    /// <summary>
    /// The marked obstacles: walls or facing enclosures, each a line on the
    /// floor along its base. Points are frozen world positions, not parented to
    /// any plane, so ARCore merging or moving planes cannot shift them.
    /// </summary>
    public class ObstacleSet : MonoBehaviour
    {
        [Header("Visuals")]
        [Tooltip("Opaque URP Unlit material for the floor line.")]
        [SerializeField] Material m_LineMaterial;
        [Tooltip("Transparent URP Unlit material for the fence above the line.")]
        [SerializeField] Material m_FenceMaterial;
        [SerializeField] float m_LineWidth = 0.012f;
        [SerializeField] float m_FenceHeight = 1.0f;
        [SerializeField] Color m_WallColour = new Color(1f, 0.55f, 0f, 1f);
        [SerializeField] Color m_EnclosureColour = new Color(0.25f, 0.7f, 1f, 1f);
        [SerializeField] float m_FenceAlpha = 0.25f;

        const string k_FileName = "obstacles.csv";
        const string k_Header = "timestamp,event,obstacle_id,kind,ax,ay,az,bx,by,bz,length_m,count_after";

        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");

        public event Action Changed;

        public IReadOnlyList<ObstacleLine> Lines => m_Lines;
        public int Count => m_Lines.Count;
        public string LogPath => m_Log?.Path;

        readonly List<ObstacleLine> m_Lines = new List<ObstacleLine>();
        readonly List<GameObject> m_Visuals = new List<GameObject>();
        CsvLog m_Log;
        int m_NextId = 1;
        MaterialPropertyBlock m_Block;

        void Awake()
        {
            m_Log = new CsvLog(k_FileName, k_Header);
            m_Block = new MaterialPropertyBlock();
        }

        public void Add(ObstacleKind kind, Vector3 a, Vector3 b)
        {
            var line = new ObstacleLine { Id = m_NextId++, Kind = kind, A = a, B = b };
            m_Lines.Add(line);
            m_Visuals.Add(BuildVisual(line));
            Log("add", line);
            Changed?.Invoke();
        }

        public void RemoveLast()
        {
            if (m_Lines.Count == 0) return;
            var last = m_Lines.Count - 1;
            var line = m_Lines[last];
            m_Lines.RemoveAt(last);
            if (m_Visuals[last] != null) Destroy(m_Visuals[last]);
            m_Visuals.RemoveAt(last);
            Log("undo", line);
            Changed?.Invoke();
        }

        public void Clear()
        {
            if (m_Lines.Count == 0) return;
            foreach (var v in m_Visuals) if (v != null) Destroy(v);
            m_Visuals.Clear();
            m_Lines.Clear();
            Log("clear", default);
            Changed?.Invoke();
        }

        GameObject BuildVisual(ObstacleLine line)
        {
            var colour = line.Kind == ObstacleKind.Wall ? m_WallColour : m_EnclosureColour;
            var root = new GameObject("Obstacle" + line.Id + "_" + line.Kind);

            var lineGo = new GameObject("FloorLine");
            lineGo.transform.SetParent(root.transform, false);
            var lr = lineGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, line.A + Vector3.up * 0.003f);
            lr.SetPosition(1, line.B + Vector3.up * 0.003f);
            lr.widthMultiplier = m_LineWidth;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (m_LineMaterial != null) lr.sharedMaterial = m_LineMaterial;
            Tint(lr, colour);

            var along = line.B - line.A;
            along.y = 0f;
            if (along.sqrMagnitude > 1e-6f && m_FenceMaterial != null)
            {
                var fence = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fence.name = "Fence";
                var collider = fence.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                fence.transform.SetParent(root.transform, false);

                var floorY = Mathf.Min(line.A.y, line.B.y);
                var mid = (line.A + line.B) * 0.5f;
                fence.transform.SetPositionAndRotation(
                    new Vector3(mid.x, floorY + m_FenceHeight * 0.5f, mid.z),
                    Quaternion.LookRotation(along.normalized, Vector3.up));
                fence.transform.localScale = new Vector3(0.01f, m_FenceHeight, along.magnitude);

                var fr = fence.GetComponent<Renderer>();
                fr.sharedMaterial = m_FenceMaterial;
                fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fr.receiveShadows = false;
                Tint(fr, new Color(colour.r, colour.g, colour.b, m_FenceAlpha));
            }

            return root;
        }

        void Tint(Renderer r, Color colour)
        {
            m_Block.Clear();
            m_Block.SetColor(k_BaseColor, colour);
            r.SetPropertyBlock(m_Block);
        }

        void Log(string evt, ObstacleLine line)
        {
            var ci = CsvLog.Ci;
            var has = evt != "clear";
            m_Log.Append(new[]
            {
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", ci),
                evt,
                has ? line.Id.ToString(ci) : "",
                has ? line.Kind.ToString() : "",
                has ? line.A.x.ToString("F4", ci) : "", has ? line.A.y.ToString("F4", ci) : "", has ? line.A.z.ToString("F4", ci) : "",
                has ? line.B.x.ToString("F4", ci) : "", has ? line.B.y.ToString("F4", ci) : "", has ? line.B.z.ToString("F4", ci) : "",
                has ? Vector3.Distance(line.A, line.B).ToString("F4", ci) : "",
                m_Lines.Count.ToString(ci)
            });
        }
    }
}
